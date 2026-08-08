using LilAgents.Platform;

namespace LilAgents.Agents;

/// <summary>
/// GitHub Copilot CLI. One process per turn, with <c>--continue</c> from the second turn
/// onward. Falls back to plain-text collection if the first response is not JSON, since
/// older builds ignore --output-format.
/// </summary>
public sealed class CopilotSession : ProcessAgentSession
{
    private ResolvedCli? _cli;
    private bool _isFirstTurn = true;
    private bool _useJsonOutput = true;
    private string _collectedPlainText = "";

    protected override AgentProviderKind Provider => AgentProviderKind.Copilot;

    protected override IEnumerable<string> ExtraPaths =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npm-global", "bin")
    ];

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
        History.Add(new AgentMessage(MessageRole.User, message));
        LineBuffer.Clear();
        _collectedPlainText = "";

        var arguments = new List<string>();
        if (!_isFirstTurn) arguments.Add("--continue");
        arguments.Add("-p");
        arguments.Add(message);

        if (_useJsonOutput)
        {
            arguments.Add("--output-format");
            arguments.Add("json");
        }
        else
        {
            arguments.Add("-s");
        }

        arguments.Add("--allow-all");

        if (LaunchProcess(_cli, arguments) is not null) _isFirstTurn = false;
    }

    protected override void OnProcessTerminated()
    {
        if (!_useJsonOutput)
        {
            var text = _collectedPlainText.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                History.Add(new AgentMessage(MessageRole.Assistant, text));
                RaiseText(text);
            }
        }

        CompleteTurnIfBusy();
    }

    protected override void ParseLine(string line)
    {
        var json = JsonHelpers.TryParseObject(line);

        if (json is null)
        {
            // Non-JSON on the very first exchange means this build does not honour
            // --output-format; degrade to plain text for the rest of the session.
            if (History.Count <= 1)
            {
                _useJsonOutput = false;
                var text = line.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    History.Add(new AgentMessage(MessageRole.Assistant, text));
                    RaiseText(text);
                }
            }
            else if (!_useJsonOutput)
            {
                _collectedPlainText += line + "\n";
            }
            return;
        }

        // Ephemeral frames are progress noise, except for the streaming deltas.
        if (json.Bool("ephemeral") == true)
        {
            if (json.String("type") == "assistant.message_delta")
            {
                var delta = json.Object("data").String("deltaContent");
                if (!string.IsNullOrEmpty(delta)) RaiseText(delta);
            }
            return;
        }

        var data = json.Object("data");

        switch (json.String("type"))
        {
            case "assistant.message":
            {
                var content = data.String("content") ?? "";
                if (!string.IsNullOrEmpty(content))
                {
                    History.Add(new AgentMessage(MessageRole.Assistant, content));
                }
                break;
            }

            case "assistant.turn_end":
            case "result":
                IsBusy = false;
                RaiseTurnComplete();
                break;

            case "assistant.tool_call":
            {
                var toolName = data.String("name") ?? data.String("tool") ?? "Tool";
                var input = data.Object("input") ?? data.Object("arguments");
                var command = input.String("command") ?? "";
                var displayName = string.IsNullOrEmpty(command) ? toolName : "Bash";
                var summary = string.IsNullOrEmpty(command) ? toolName : command;
                History.Add(new AgentMessage(MessageRole.ToolUse, $"{displayName}: {summary}"));
                RaiseToolUse(displayName, input);
                break;
            }

            case "assistant.tool_result":
            {
                var output = data.String("output") ?? data.String("result") ?? "";
                var isError = data.Bool("is_error") ?? data.String("status") == "error";
                var summary = JsonHelpers.Summarize(output);
                History.Add(new AgentMessage(MessageRole.ToolResult, isError ? $"ERROR: {summary}" : summary));
                RaiseToolResult(summary, isError);
                break;
            }

            case "error":
            {
                var message = data.String("message") ?? data.String("error") ?? "Unknown error";
                RaiseError(message, recordInHistory: true);
                break;
            }
        }
    }
}
