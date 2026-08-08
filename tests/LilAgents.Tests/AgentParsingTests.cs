using LilAgents.Agents;
using Xunit;

namespace LilAgents.Tests;

/// <summary>
/// Provider event parsing, driven by feeding real stream shapes through the sessions and
/// asserting on the events and history they produce.
/// </summary>
public class AgentParsingTests
{
    /// <summary>Pushes lines through a session's parser without launching a process.</summary>
    private static void Feed(ProcessAgentSession session, params string[] lines)
    {
        var method = typeof(ProcessAgentSession).GetMethod("ParseLine",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        foreach (var line in lines) method.Invoke(session, [line]);
    }

    [Fact]
    public void ClaudeStreamsAssistantTextAndCompletesTheTurn()
    {
        var session = new ClaudeSession();
        var text = "";
        var completed = false;
        session.TextReceived += chunk => text += chunk;
        session.TurnCompleted += () => completed = true;

        Feed(session,
            """{"type":"system","subtype":"init"}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Hello "}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"world"}]}}""",
            """{"type":"result","result":"Hello world"}""");

        Assert.Equal("Hello world", text);
        Assert.True(completed);
        Assert.Contains(session.History, m => m.Role == MessageRole.Assistant && m.Text == "Hello world");
    }

    [Fact]
    public void ClaudeRecordsToolUseWithASummary()
    {
        var session = new ClaudeSession();
        string? toolName = null;

        session.ToolUsed += (name, _) => toolName = name;

        Feed(session,
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"ls -la"}}]}}""");

        Assert.Equal("Bash", toolName);
        Assert.Contains(session.History, m => m.Role == MessageRole.ToolUse && m.Text == "Bash: ls -la");
    }

    [Fact]
    public void ClaudeIgnoresMalformedLinesInsteadOfThrowing()
    {
        var session = new ClaudeSession();
        Feed(session, "not json at all", "", "{ broken", """{"type":"result","result":"ok"}""");
        Assert.Contains(session.History, m => m.Text == "ok");
    }

    [Fact]
    public void CodexSurfacesAgentMessagesAndCommandResults()
    {
        var session = new CodexSession();
        var text = "";
        var completed = false;
        session.TextReceived += chunk => text += chunk;
        session.TurnCompleted += () => completed = true;

        Feed(session,
            """{"type":"thread.started"}""",
            """{"type":"item.started","item":{"type":"command_execution","command":"pytest"}}""",
            """{"type":"item.completed","item":{"type":"command_execution","command":"pytest","status":"completed"}}""",
            """{"type":"item.completed","item":{"type":"agent_message","text":"All tests pass."}}""",
            """{"type":"turn.completed"}""");

        Assert.Equal("All tests pass.", text);
        Assert.True(completed);
    }

    [Fact]
    public void CodexMarksFailedCommandsAsErrors()
    {
        var session = new CodexSession();
        bool? sawError = null;
        session.ToolResultReceived += (_, isError) => sawError = isError;

        Feed(session,
            """{"type":"item.completed","item":{"type":"command_execution","command":"pytest","status":"failed"}}""");

        Assert.True(sawError);
    }

    [Fact]
    public void CodexPromptCarriesPriorTurnsForwardVerbatim()
    {
        var history = new List<AgentMessage>
        {
            new(MessageRole.User, "what is 2+2"),
            new(MessageRole.Assistant, "4")
        };

        var prompt = CodexSession.BuildExecPrompt(history, "and 3+3?");

        Assert.Contains("User: what is 2+2", prompt);
        Assert.Contains("Assistant: 4", prompt);
        Assert.Contains("User (follow-up): and 3+3?", prompt);
    }

    [Fact]
    public void CodexPromptIsJustTheMessageOnTheFirstTurn()
    {
        Assert.Equal("hello", CodexSession.BuildExecPrompt([], "hello"));
    }

    [Fact]
    public void CopilotStreamsDeltasFromEphemeralFrames()
    {
        var session = new CopilotSession();
        var text = "";
        session.TextReceived += chunk => text += chunk;

        Feed(session,
            """{"ephemeral":true,"type":"assistant.message_delta","data":{"deltaContent":"par"}}""",
            """{"ephemeral":true,"type":"assistant.message_delta","data":{"deltaContent":"tial"}}""",
            """{"type":"assistant.turn_end"}""");

        Assert.Equal("partial", text);
    }

    [Fact]
    public void GeminiFallsBackToPlainTextWhenNoJsonEverArrives()
    {
        var session = new GeminiSession();
        var text = "";
        session.TextReceived += chunk => text += chunk;

        Feed(session, "Just a plain answer.", "Second line.");

        Assert.Contains("Just a plain answer.", text);
        Assert.Contains("Second line.", text);
    }

    [Theory]
    [InlineData("⠋ loading", true)]
    [InlineData("✓ done", true)]
    [InlineData("", true)]
    [InlineData("Error: quota exceeded", false)]
    public void GeminiFiltersSpinnerNoiseOffStandardError(string line, bool expectedNoise)
    {
        Assert.Equal(expectedNoise, GeminiSession.IsProgressNoise(line));
    }

    [Fact]
    public void OpenCodeAccumulatesTextParts()
    {
        var session = new OpenCodeSession();
        var text = "";
        session.TextReceived += chunk => text += chunk;

        Feed(session,
            """{"type":"step_start"}""",
            """{"type":"text","part":{"text":"chunk one "}}""",
            """{"type":"text","part":{"text":"chunk two"}}""",
            """{"type":"result"}""");

        Assert.Equal("chunk one chunk two", text);
    }

    [Fact]
    public void ToolResultSummariesAreCollapsedAndCapped()
    {
        var summary = JsonHelpers.Summarize("line one\nline two\r\nline three", limit: 20);
        Assert.DoesNotContain('\n', summary);
        Assert.True(summary.Length <= 20);
    }
}
