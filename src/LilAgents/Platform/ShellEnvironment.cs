using System.Diagnostics;
using Microsoft.Win32;

namespace LilAgents.Platform;

/// <summary>
/// Locates AI CLI binaries and builds the environment they are spawned with.
///
/// The macOS build shells out to <c>zsh -l -i -c env</c> because GUI apps on macOS do
/// not inherit the login shell's PATH. Windows has no login-shell concept, but it has
/// the mirror-image problem: a process inherits the PATH that existed when it started,
/// so a CLI installed after the app launched is invisible. This class therefore unions
/// the process PATH with the live registry PATH and a set of known install locations.
/// </summary>
public static class ShellEnvironment
{
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _cachedEnvironment;
    private static readonly Dictionary<string, ResolvedCli?> BinaryCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Executable file extensions, in the order Windows itself prefers them.</summary>
    private static readonly string[] Extensions = [".exe", ".cmd", ".bat", ".com", ".ps1", ""];

    // MARK: - Environment

    public static IReadOnlyDictionary<string, string> Resolve()
    {
        lock (Gate)
        {
            if (_cachedEnvironment is not null) return _cachedEnvironment;

            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key is string key && entry.Value is string value) env[key] = value;
            }

            env["PATH"] = string.Join(Path.PathSeparator, BuildSearchPath());
            _cachedEnvironment = env;
            return env;
        }
    }

    public static void InvalidateCache()
    {
        lock (Gate)
        {
            _cachedEnvironment = null;
            BinaryCache.Clear();
        }
    }

    /// <summary>
    /// Applies the resolved environment to a process, minus the markers that make a
    /// spawned Claude Code refuse to start because it thinks it is nested inside
    /// another session. Same reasoning as the macOS build.
    /// </summary>
    public static void ApplyEnvironment(ProcessStartInfo startInfo, IEnumerable<string>? extraPaths = null)
    {
        startInfo.Environment.Clear();
        foreach (var (key, value) in Resolve()) startInfo.Environment[key] = value;

        if (extraPaths is not null)
        {
            // ProcessStartInfo.Environment is IDictionary<string, string?>, so a present
            // key can still carry null.
            var current = (startInfo.Environment.TryGetValue("PATH", out var existing) ? existing : "") ?? "";
            var additions = extraPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Where(p => !current.Contains(p, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (additions.Count > 0)
            {
                startInfo.Environment["PATH"] =
                    string.Join(Path.PathSeparator, additions) + Path.PathSeparator + current;
            }
        }

        startInfo.Environment["TERM"] = "dumb";
        startInfo.Environment.Remove("CLAUDECODE");
        startInfo.Environment.Remove("CLAUDE_CODE_ENTRYPOINT");
    }

    // MARK: - Search path

    public static IReadOnlyList<string> BuildSearchPath()
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            var trimmed = dir.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (trimmed.Length == 0) return;
            if (seen.Add(trimmed)) ordered.Add(trimmed);
        }

        void AddAll(string? pathValue)
        {
            if (string.IsNullOrWhiteSpace(pathValue)) return;
            foreach (var part in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                Add(Environment.ExpandEnvironmentVariables(part));
            }
        }

        AddAll(Environment.GetEnvironmentVariable("PATH"));
        AddAll(ReadRegistryPath(Registry.CurrentUser, @"Environment"));
        AddAll(ReadRegistryPath(Registry.LocalMachine,
            @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"));

        foreach (var known in KnownInstallDirectories()) Add(known);

        return ordered;
    }

    /// <summary>
    /// Where the four supported CLIs actually land on a normal Windows machine.
    /// npm global installs go to %APPDATA%\npm; Claude Code's native installer uses
    /// %USERPROFILE%\.local\bin; winget shims live under %LOCALAPPDATA%\Microsoft\WinGet.
    /// </summary>
    public static IReadOnlyList<string> KnownInstallDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        var candidates = new List<string>();

        void Add(params string[] parts)
        {
            if (parts.Any(string.IsNullOrEmpty)) return;
            candidates.Add(Path.Combine(parts));
        }

        Add(home, ".local", "bin");
        Add(home, ".claude", "local", "bin");
        Add(home, ".bun", "bin");
        Add(home, ".deno", "bin");
        Add(appData, "npm");
        Add(appData, "npm", "node_modules", ".bin");
        Add(home, ".npm-global", "bin");
        Add(localAppData, "Microsoft", "WinGet", "Links");
        Add(localAppData, "Programs", "nodejs");
        Add(localAppData, "pnpm");
        Add(localAppData, "Yarn", "bin");
        Add(programFiles, "nodejs");

        return candidates;
    }

    private static string? ReadRegistryPath(RegistryKey root, string subKeyPath)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyPath);
            // DoNotExpandEnvironmentNames keeps %USERPROFILE% intact so we can expand it
            // ourselves; without it the value comes back already expanded, which is fine
            // too, hence the ExpandEnvironmentVariables call on the way out.
            return key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        catch
        {
            return null;
        }
    }

    // MARK: - Binary resolution

    /// <summary>
    /// Finds a CLI by bare name (e.g. "claude"), trying each executable extension in
    /// PATHEXT-like order. Results are cached; call <see cref="InvalidateCache"/> after
    /// the user installs something.
    /// </summary>
    public static ResolvedCli? FindBinary(string name)
    {
        lock (Gate)
        {
            if (BinaryCache.TryGetValue(name, out var cached)) return cached;
        }

        ResolvedCli? found = null;
        foreach (var directory in BuildSearchPath())
        {
            foreach (var extension in Extensions)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory, name + extension);
                }
                catch (ArgumentException)
                {
                    continue; // Malformed PATH entry.
                }

                if (!File.Exists(candidate)) continue;
                // An extensionless match is only useful if it is a real executable;
                // npm also drops a bash script with no extension next to the .cmd shim,
                // and Windows cannot run that one.
                if (extension.Length == 0) continue;

                found = ResolvedCli.Classify(candidate);
                break;
            }
            if (found is not null) break;
        }

        lock (Gate)
        {
            BinaryCache[name] = found;
        }
        return found;
    }
}
