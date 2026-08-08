using LilAgents.Platform;

namespace LilAgents.Agents;

/// <summary>
/// OpenCode CLI. One process per turn via <c>opencode run &lt;message&gt; --format json</c>.
/// </summary>
public sealed class OpenCodeSession : ProcessAgentSession
{
    private ResolvedCli? _cli;
    private string _currentResponseText = "";

    protected override AgentProviderKind Provider => AgentProviderKind.OpenCode;

    public override void Start()
    {
        ResolveBinaryAsync(cli =>
        {
            _cli = cli;
            IsRunning = true;
            RaiseSessionReady();
        });
    }

    public override void Send(string message)
    {
        if (!IsRunning || _cli is null) return;

        IsBusy = true;
        _currentResponseText = "";
        History.Add(new AgentMessage(MessageRole.User, message));
        LineBuffer.Clear();

        LaunchProcess(_cli, ["run", message, "--format", "json"]);
    }

    protected override void OnProcessTerminated()
    {
        if (!string.IsNullOrEmpty(_currentResponseText))
        {
            History.Add(new AgentMessage(MessageRole.Assistant, _currentResponseText));
        }

        CompleteTurnIfBusy();
    }

    protected override void ParseLine(string line)
    {
        var json = JsonHelpers.TryParseObject(line);
        if (json is null) return;

        switch (json.String("type"))
        {
            case "text":
            {
                var text = json.Object("part").String("text");
                if (!string.IsNullOrEmpty(text))
                {
                    _currentResponseText += text;
                    RaiseText(text);
                }
                break;
            }

            case "step_start":
                IsBusy = true;
                break;

            case "result":
                IsBusy = false;
                RaiseTurnComplete();
                break;

            case "assistant.tool_call":
            {
                var part = json.Object("part");
                var toolName = part.String("name") ?? "Tool";
                var input = part.Object("arguments");
                History.Add(new AgentMessage(MessageRole.ToolUse, toolName));
                RaiseToolUse(toolName, input);
                break;
            }

            case "assistant.tool_result":
            {
                var part = json.Object("part");
                var output = part.String("result") ?? "";
                var isError = part.String("status") == "error";
                var summary = JsonHelpers.Summarize(output);
                History.Add(new AgentMessage(MessageRole.ToolResult, isError ? $"ERROR: {summary}" : summary));
                RaiseToolResult(summary, isError);
                break;
            }
        }
    }
}
