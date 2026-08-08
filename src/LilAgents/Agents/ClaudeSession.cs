using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LilAgents.Agents;

/// <summary>
/// Claude Code. The only provider held open across turns: it speaks stream-json on both
/// stdin and stdout, so one long-lived process carries the whole conversation.
/// </summary>
public sealed class ClaudeSession : ProcessAgentSession
{
    private Process? _process;
    private StreamWriter? _input;
    private readonly List<string> _pendingMessages = [];
    private string _currentResponseText = "";

    protected override AgentProviderKind Provider => AgentProviderKind.Claude;

    public override void Start()
    {
        ResolveBinaryAsync(cli =>
        {
            var process = LaunchProcess(cli,
            [
                "-p",
                "--output-format", "stream-json",
                "--input-format", "stream-json",
                "--verbose",
                "--dangerously-skip-permissions"
            ], redirectInput: true);

            if (process is null) return;

            _process = process;
            _input = process.StandardInput;
            IsRunning = true;

            var pending = _pendingMessages.ToList();
            _pendingMessages.Clear();
            foreach (var message in pending) WriteMessage(message);
        });
    }

    public override void Send(string message)
    {
        if (!IsRunning || _input is null)
        {
            // The CLI is still being located; replay once the process is up.
            _pendingMessages.Add(message);
            return;
        }

        WriteMessage(message);
    }

    private void WriteMessage(string message)
    {
        IsBusy = true;
        _currentResponseText = "";
        History.Add(new AgentMessage(MessageRole.User, message));

        var payload = new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = message
            }
        };

        try
        {
            _input!.Write(payload.ToJsonString(new JsonSerializerOptions()) + "\n");
            _input.Flush();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            IsBusy = false;
            RaiseError("Lost the connection to the Claude CLI.", recordInHistory: true);
        }
    }

    public override void Terminate()
    {
        try
        {
            _input?.Dispose();
        }
        catch (IOException)
        {
        }

        _input = null;
        _process = null;
        _pendingMessages.Clear();
        base.Terminate();
    }

    protected override void OnProcessTerminated()
    {
        IsRunning = false;
        IsBusy = false;
        RaiseProcessExit();
    }

    // MARK: - stream-json parsing

    protected override void ParseLine(string line)
    {
        var json = JsonHelpers.TryParseObject(line);
        if (json is null) return;

        switch (json.String("type"))
        {
            case "system":
                if (json.String("subtype") == "init") RaiseSessionReady();
                break;

            case "assistant":
                ParseAssistant(json);
                break;

            case "user":
                ParseToolResult(json);
                break;

            case "result":
                ParseResult(json);
                break;
        }
    }

    private void ParseAssistant(JsonObject json)
    {
        var content = json.Object("message").Array("content");
        if (content is null) return;

        foreach (var node in content)
        {
            if (node is not JsonObject block) continue;

            switch (block.String("type"))
            {
                case "text":
                    var text = block.String("text");
                    if (!string.IsNullOrEmpty(text))
                    {
                        _currentResponseText += text;
                        RaiseText(text);
                    }
                    break;

                case "tool_use":
                    var toolName = block.String("name") ?? "Tool";
                    var input = block.Object("input");
                    History.Add(new AgentMessage(MessageRole.ToolUse,
                        $"{toolName}: {FormatToolSummary(toolName, input)}"));
                    RaiseToolUse(toolName, input);
                    break;
            }
        }
    }

    private void ParseToolResult(JsonObject json)
    {
        var content = json.Object("message").Array("content");
        if (content is null) return;

        foreach (var node in content)
        {
            if (node is not JsonObject block) continue;
            if (block.String("type") != "tool_result") continue;

            var isError = block.Bool("is_error") ?? false;
            var summary = "";

            if (json.Object("tool_use_result") is { } resultInfo)
            {
                if (resultInfo.String("type") == "text" && resultInfo.Object("file") is { } file)
                {
                    var path = file.String("filePath");
                    var lines = file.Int("totalLines") ?? 0;
                    if (!string.IsNullOrEmpty(path)) summary = $"{path} ({lines} lines)";
                }
            }
            else if (json.String("tool_use_result") is { } resultText)
            {
                summary = JsonHelpers.Summarize(resultText);
            }

            if (string.IsNullOrEmpty(summary)) summary = JsonHelpers.Summarize(block.String("content"));

            History.Add(new AgentMessage(MessageRole.ToolResult, isError ? $"ERROR: {summary}" : summary));
            RaiseToolResult(summary, isError);
        }
    }

    private void ParseResult(JsonObject json)
    {
        IsBusy = false;

        var result = json.String("result");
        var finalText = !string.IsNullOrEmpty(result) ? result : _currentResponseText;

        if (!string.IsNullOrEmpty(finalText))
        {
            History.Add(new AgentMessage(MessageRole.Assistant, finalText));
        }

        _currentResponseText = "";
        RaiseTurnComplete();
    }

    private static string FormatToolSummary(string toolName, JsonObject? input) => toolName switch
    {
        "Bash" => input.String("command") ?? "",
        "Read" => input.String("file_path") ?? "",
        "Edit" or "Write" => input.String("file_path") ?? "",
        "Glob" => input.String("pattern") ?? "",
        "Grep" => input.String("pattern") ?? "",
        _ => input.String("description") ?? string.Join(", ", input.ToDictionary().Keys.Order().Take(3))
    };
}
