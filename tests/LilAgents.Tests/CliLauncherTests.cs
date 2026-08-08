using LilAgents.Platform;
using Xunit;

namespace LilAgents.Tests;

/// <summary>
/// Argument quoting for the cmd.exe fallback. A prompt is arbitrary user text, so a
/// mistake here is command injection into the user's own shell, not just a formatting bug.
/// </summary>
public class CliLauncherTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("has space", "\"has space\"")]
    [InlineData("has\ttab", "\"has\ttab\"")]
    [InlineData("", "\"\"")]
    public void QuotesOnlyWhenNecessary(string input, string expected)
    {
        Assert.Equal(expected, CliLauncher.QuoteArgument(input));
    }

    [Fact]
    public void EscapesEmbeddedQuotes()
    {
        Assert.Equal("\"say \\\"hi\\\"\"", CliLauncher.QuoteArgument("say \"hi\""));
    }

    [Fact]
    public void LeavesABackslashTerminatedPathAloneWhenItNeedsNoQuoting()
    {
        // No whitespace and no quote, so there is nothing to protect it from.
        Assert.Equal(@"C:\path\", CliLauncher.QuoteArgument(@"C:\path\"));
    }

    [Fact]
    public void DoublesTrailingBackslashesSoTheyDoNotEscapeTheClosingQuote()
    {
        // The space forces quoting; the final backslash then sits against the closing
        // quote and must be doubled or CommandLineToArgvW reads it as an escape.
        Assert.Equal("\"C:\\Program Files\\\\\"", CliLauncher.QuoteArgument(@"C:\Program Files\"));
    }

    [Fact]
    public void DoublesBackslashesThatPrecedeAnEmbeddedQuote()
    {
        Assert.Equal("\"a\\\\\\\"b\"", CliLauncher.QuoteArgument("a\\\"b"));
    }

    [Fact]
    public void PreservesInteriorBackslashesUntouched()
    {
        Assert.Equal("\"C:\\Program Files\\node\"", CliLauncher.QuoteArgument("C:\\Program Files\\node"));
    }

    [Theory]
    [InlineData("&")]
    [InlineData("|")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("^")]
    [InlineData("(")]
    [InlineData(")")]
    public void EscapesCmdMetacharactersOutsideQuotes(string metacharacter)
    {
        var escaped = CliLauncher.EscapeCmdMetacharacters(metacharacter);
        Assert.Equal("^" + metacharacter, escaped);
    }

    [Fact]
    public void LeavesMetacharactersInsideQuotesAlone()
    {
        // Inside a quoted span cmd does not treat & as a separator, and a stray ^ would
        // land in the argument as a literal character.
        Assert.Equal("\"a & b\"", CliLauncher.EscapeCmdMetacharacters("\"a & b\""));
    }

    [Fact]
    public void WrapsTheWholeCommandForCmdSlashC()
    {
        var command = CliLauncher.BuildCmdCommand(@"C:\npm\gemini.cmd", ["--yolo", "-p", "hello world"]);

        Assert.StartsWith("\"", command);
        Assert.EndsWith("\"", command);
        Assert.Contains(@"C:\npm\gemini.cmd", command);
        Assert.Contains("\"hello world\"", command);
    }

    [Fact]
    public void NeutralisesAShellOperatorSmuggledIntoAPrompt()
    {
        var command = CliLauncher.BuildCmdCommand(@"C:\npm\gemini.cmd", ["-p", "list files & del /q *"]);

        // The whole prompt is a single quoted argument, so cmd sees no command separator.
        Assert.Contains("\"list files & del /q *\"", command);
        Assert.DoesNotContain("* \"", command);
    }

    [Fact]
    public void ClassifiesExecutablesDirectly()
    {
        var resolved = ResolvedCli.Classify(@"C:\Users\me\.local\bin\claude.exe");
        Assert.Equal(CliKind.Executable, resolved.Kind);
    }

    [Fact]
    public void ClassifiesPowerShellShims()
    {
        var resolved = ResolvedCli.Classify(@"C:\npm\gemini.ps1");
        Assert.Equal(CliKind.PowerShellScript, resolved.Kind);
    }

    [Fact]
    public void FallsBackToCommandShimWhenTheCmdFileCannotBeRead()
    {
        var resolved = ResolvedCli.Classify(@"C:\does-not-exist\gemini.cmd");
        Assert.Equal(CliKind.CommandShim, resolved.Kind);
    }

    [Fact]
    public void DecodesAnNpmShimIntoADirectNodeInvocation()
    {
        // Reproduces the shim npm actually writes on Windows, including the node.exe
        // sitting beside it, and checks we recover "node.exe <script.js>" from it.
        var directory = Path.Combine(Path.GetTempPath(), "lil-agents-shim-" + Guid.NewGuid().ToString("N"));
        var scriptDirectory = Path.Combine(directory, "node_modules", "@google", "gemini-cli", "dist");
        System.IO.Directory.CreateDirectory(scriptDirectory);

        try
        {
            var scriptPath = Path.Combine(scriptDirectory, "index.js");
            File.WriteAllText(scriptPath, "// entry point");
            File.WriteAllText(Path.Combine(directory, "node.exe"), "");

            var shimPath = Path.Combine(directory, "gemini.cmd");
            File.WriteAllText(shimPath,
                """
                @ECHO off
                SETLOCAL
                CALL :find_dp0
                IF EXIST "%dp0%\node.exe" (
                  SET "_prog=%dp0%\node.exe"
                ) ELSE (
                  SET "_prog=node"
                  SET PATHEXT=%PATHEXT:;.JS;=;%
                )
                endLocal & goto #_undefined_# 2>NUL || title %COMSPEC% & "%_prog%"  "%dp0%\node_modules\@google\gemini-cli\dist\index.js" %*
                """);

            var resolved = ResolvedCli.Classify(shimPath);

            Assert.Equal(CliKind.NodeScript, resolved.Kind);
            Assert.Equal(Path.Combine(directory, "node.exe"), resolved.Interpreter);
            Assert.Equal(scriptPath, resolved.Script);
        }
        finally
        {
            try
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
