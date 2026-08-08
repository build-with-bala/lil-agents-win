using System.Text.RegularExpressions;

namespace LilAgents.Platform;

public enum CliKind
{
    /// <summary>A real PE executable — launch it directly.</summary>
    Executable,

    /// <summary>An npm shim we decoded into "node.exe &lt;script.js&gt;".</summary>
    NodeScript,

    /// <summary>A .cmd/.bat we could not decode — must go through cmd.exe.</summary>
    CommandShim,

    /// <summary>A PowerShell script shim.</summary>
    PowerShellScript
}

/// <summary>
/// A CLI binary, classified by how Windows is able to start it.
///
/// This distinction has no macOS counterpart and is the single biggest porting hazard:
/// <c>npm install -g @google/gemini-cli</c> does not produce <c>gemini.exe</c>. It produces
/// <c>%APPDATA%\npm\gemini.cmd</c> (plus a .ps1 and an extensionless bash script), and
/// CreateProcess — which is what <c>Process.Start</c> with UseShellExecute=false calls —
/// cannot execute a .cmd at all. It fails with "not a valid application for this OS platform".
///
/// Rather than route everything through cmd.exe and inherit its metacharacter parsing,
/// we read the shim and recover the underlying <c>node.exe &lt;script.js&gt;</c> invocation,
/// which can then be started directly with argument-array semantics and no shell in the
/// middle. cmd.exe remains only as a last-resort fallback.
/// </summary>
public sealed record ResolvedCli(string Path, CliKind Kind, string? Interpreter = null, string? Script = null)
{
    /// <summary>Matches the script path an npm .cmd shim hands to node.</summary>
    private static readonly Regex NodeModulesScript = new(
        @"%~?dp0%?\\?(?<rel>node_modules\\[^""]+?\.(?:js|mjs|cjs))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ResolvedCli Classify(string path)
    {
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".exe" or ".com" => new ResolvedCli(path, CliKind.Executable),
            ".cmd" or ".bat" => ClassifyShim(path),
            ".ps1" => new ResolvedCli(path, CliKind.PowerShellScript),
            _ => new ResolvedCli(path, CliKind.Executable)
        };
    }

    private static ResolvedCli ClassifyShim(string path)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) return new ResolvedCli(path, CliKind.CommandShim);

            var content = File.ReadAllText(path);
            var match = NodeModulesScript.Match(content);
            if (!match.Success) return new ResolvedCli(path, CliKind.CommandShim);

            var script = System.IO.Path.Combine(directory, match.Groups["rel"].Value);
            if (!File.Exists(script)) return new ResolvedCli(path, CliKind.CommandShim);

            var node = FindNode(directory);
            if (node is null) return new ResolvedCli(path, CliKind.CommandShim);

            return new ResolvedCli(path, CliKind.NodeScript, node, script);
        }
        catch (IOException)
        {
            return new ResolvedCli(path, CliKind.CommandShim);
        }
        catch (UnauthorizedAccessException)
        {
            return new ResolvedCli(path, CliKind.CommandShim);
        }
    }

    /// <summary>
    /// npm shims prefer a node.exe sitting next to them; otherwise they fall back to
    /// whichever node is on PATH. Mirror that order.
    /// </summary>
    private static string? FindNode(string shimDirectory)
    {
        var sibling = System.IO.Path.Combine(shimDirectory, "node.exe");
        if (File.Exists(sibling)) return sibling;

        foreach (var directory in ShellEnvironment.BuildSearchPath())
        {
            string candidate;
            try
            {
                candidate = System.IO.Path.Combine(directory, "node.exe");
            }
            catch (ArgumentException)
            {
                continue;
            }
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
