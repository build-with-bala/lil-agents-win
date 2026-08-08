using System.Text;
using LilAgents.Platform;

namespace LilAgents.Agents;

/// <summary>
/// OpenAI Codex. Current versions expose only <c>codex exec &lt;PROMPT&gt;</c> with no
/// resume, so each turn is a fresh process and the prior conversation is replayed into
/// the prompt — the same workaround the macOS build uses.
/// </summary>
public sealed class CodexSession : ProcessAgentSession
{
    private ResolvedCli? _cli;

    protected override AgentProviderKind Provider => AgentProviderKind.Codex;

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
        LineBuffer.Clear();

        var prompt = BuildExecPrompt(History, message);
        History.Add(new AgentMessage(MessageRole.User, message));

        LaunchProcess(_cli, ["exec", "--json", "--full-auto", "--skip-git-repo-check", prompt]);
    }

    protected override void OnProcessTerminated() => CompleteTurnIfBusy();

    // MARK: - Prompt assembly

    /// <summary>
    /// Flattens history into a single prompt. Takes the messages recorded *before* the
    /// new one, matching the macOS build's <c>priorMessages: history.dropLast()</c>.
    /// </summary>
    internal static string BuildExecPrompt(IReadOnlyList<AgentMessage> priorMessages, string latestUserMessage)
    {
        if (priorMessages.Count == 0) return latestUserMessage;

        var parts = new List<string>();
        foreach (var message in priorMessages)
        {
            var prefix = message.Role switch
            {
                MessageRole.User => "User",
                MessageRole.Assistant => "Assistant",
                MessageRole.ToolUse => "Tool",
                MessageRole.ToolResult => "Tool result",
                _ => "Error"
            };
            parts.Add($"{prefix}: {message.Text}");
        }

        var builder = new StringBuilder();
        builder.AppendLine("Conversation so far (for context; respond only to the follow-up):");
        builder.AppendLine();
        builder.AppendLine(string.Join("\n\n", parts));
        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine();
        builder.Append($"User (follow-up): {latestUserMessage}");
        return builder.ToString();
    }

    // MARK: - JSONL parsing

    protected override void ParseLine(string line)
    {
        var json = JsonHelpers.TryParseObject(line);
        if (json is null) return;

        switch (json.String("type"))
        {
            case "item.started":
            {
                var item = json.Object("item");
                if (item.String("type") == "command_execution")
                {
                    var command = item.String("command") ?? "";
                    History.Add(new AgentMessage(MessageRole.ToolUse, $"Bash: {command}"));
                    RaiseToolUse("Bash", item);
                }
                break;
            }

            case "item.completed":
                ParseCompletedItem(json);
                break;

            case "turn.completed":
                IsBusy = false;
                RaiseTurnComplete();
                break;

            case "turn.failed":
            {
                IsBusy = false;
                var message = json.String("message") ?? "Turn failed";
                RaiseError(message, recordInHistory: true);
                RaiseTurnComplete();
                break;
            }

            case "error":
            {
                var message = json.String("message") ?? json.String("error") ?? "Unknown error";
                RaiseError(message, recordInHistory: true);
                break;
            }
        }
    }

    private void ParseCompletedItem(System.Text.Json.Nodes.JsonObject json)
    {
        var item = json.Object("item");
        if (item is null) return;

        switch (item.String("type"))
        {
            case "agent_message":
            {
                var text = item.String("text") ?? "";
                if (!string.IsNullOrEmpty(text))
                {
                    History.Add(new AgentMessage(MessageRole.Assistant, text));
                    RaiseText(text);
                }
                break;
            }

            case "command_execution":
            {
                var status = item.String("status") ?? "";
                var command = item.String("command") ?? "";
                var isError = status == "failed";
                var summary = string.IsNullOrEmpty(command) ? status : JsonHelpers.Summarize(command);
                History.Add(new AgentMessage(MessageRole.ToolResult, isError ? $"ERROR: {summary}" : summary));
                RaiseToolResult(summary, isError);
                break;
            }

            case "file_change":
            {
                var path = item.String("file") ?? item.String("path") ?? "file";
                History.Add(new AgentMessage(MessageRole.ToolUse, $"FileChange: {path}"));
                RaiseToolUse("FileChange", item);
                History.Add(new AgentMessage(MessageRole.ToolResult, path));
                RaiseToolResult(path, false);
                break;
            }
        }
    }
}
