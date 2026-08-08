using System.Diagnostics;
using System.Text;

namespace LilAgents.Platform;

/// <summary>
/// Turns a <see cref="ResolvedCli"/> plus an argument list into a ready-to-start
/// <see cref="ProcessStartInfo"/>, with UTF-8 forced on both output streams.
/// </summary>
public static class CliLauncher
{
    /// <summary>
    /// UTF-8 without a BOM. Windows consoles default to a legacy OEM code page, and the
    /// agent CLIs stream NDJSON that routinely carries non-ASCII (smart quotes, emoji,
    /// box drawing). Without this the JSON decodes into mojibake and the parser drops
    /// whole events — a failure mode that does not exist on macOS, where UTF-8 is the
    /// default everywhere.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static ProcessStartInfo CreateStartInfo(
        ResolvedCli cli,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IEnumerable<string>? extraPaths = null,
        bool redirectInput = false)
    {
        var args = arguments.ToList();

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            WorkingDirectory = workingDirectory
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (redirectInput) startInfo.StandardInputEncoding = Utf8NoBom;

        switch (cli.Kind)
        {
            case CliKind.NodeScript:
                startInfo.FileName = cli.Interpreter!;
                startInfo.ArgumentList.Add(cli.Script!);
                foreach (var arg in args) startInfo.ArgumentList.Add(arg);
                break;

            case CliKind.PowerShellScript:
                startInfo.FileName = "powershell.exe";
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-NonInteractive");
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-File");
                startInfo.ArgumentList.Add(cli.Path);
                foreach (var arg in args) startInfo.ArgumentList.Add(arg);
                break;

            case CliKind.CommandShim:
                startInfo.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
                // /d skips AutoRun registry commands, /s makes cmd strip exactly the outer
                // quote pair and treat the remainder verbatim, /c runs and exits.
                startInfo.Arguments = "/d /s /c " + BuildCmdCommand(cli.Path, args);
                break;

            default:
                startInfo.FileName = cli.Path;
                foreach (var arg in args) startInfo.ArgumentList.Add(arg);
                break;
        }

        ShellEnvironment.ApplyEnvironment(startInfo, extraPaths);
        return startInfo;
    }

    // MARK: - cmd.exe command construction

    /// <summary>
    /// Builds the quoted command for <c>cmd.exe /d /s /c</c>.
    ///
    /// Caveat worth knowing: cmd expands %VAR% even inside double quotes, and there is no
    /// escape that reliably suppresses it in this position. A prompt containing literal
    /// %TEXT% may therefore be substituted. This path is a fallback only — every npm-installed
    /// CLI resolves to <see cref="CliKind.NodeScript"/>, which bypasses cmd entirely.
    /// </summary>
    internal static string BuildCmdCommand(string executable, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        builder.Append('"');
        builder.Append(QuoteArgument(executable));
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            builder.Append(EscapeCmdMetacharacters(QuoteArgument(argument)));
        }
        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>
    /// Applies the CommandLineToArgvW quoting rules: backslashes are only special when
    /// they immediately precede a quote, in which case they must be doubled.
    /// </summary>
    internal static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return argument;
        }

        var builder = new StringBuilder();
        builder.Append('"');

        for (var i = 0; i < argument.Length; i++)
        {
            var backslashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                backslashes++;
                i++;
            }

            if (i == argument.Length)
            {
                // Trailing backslashes precede the closing quote, so they are doubled.
                builder.Append('\\', backslashes * 2);
                break;
            }

            if (argument[i] == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
            }
            else
            {
                builder.Append('\\', backslashes);
                builder.Append(argument[i]);
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>
    /// Neutralises cmd's own metacharacters outside of quoted spans, so a prompt like
    /// <c>build &amp; deploy</c> cannot become a second command.
    /// </summary>
    internal static string EscapeCmdMetacharacters(string value)
    {
        const string metacharacters = "^&|<>()!";
        var builder = new StringBuilder(value.Length);
        var inQuotes = false;

        foreach (var character in value)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                builder.Append(character);
                continue;
            }

            if (!inQuotes && metacharacters.Contains(character)) builder.Append('^');
            builder.Append(character);
        }

        return builder.ToString();
    }
}
