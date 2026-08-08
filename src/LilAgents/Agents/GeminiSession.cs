using System.Text.Json.Nodes;
using LilAgents.Platform;

namespace LilAgents.Agents;

/// <summary>
/// Google Gemini CLI. Emits JSONL on some versions and plain text on others, so parsing
/// tries JSON first and streams raw lines when no JSON has ever been seen.
/// </summary>
public sealed class GeminiSession : ProcessAgentSession
{
    private ResolvedCli? _cli;
    private bool _isFirstTurn = true;
    private bool _sawJsonLine;
    private string _currentResponseText = "";
    private string _collectedText = "";

    protected override AgentProviderKind Provider => AgentProviderKind.Gemini;

    protected override IEnumerable<string> ExtraPaths
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return [Path.Combine(home, ".npm-global", "bin"), Path.Combine(home, ".local", "bin")];
        }
    }

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
        _collectedText = "";
        History.Add(new AgentMessage(MessageRole.User, message));
        LineBuffer.Clear();

        List<string> arguments = _isFirstTurn
            ? ["--yolo", "-p", message]
            : ["--yolo", "--resume", "latest", "-p", message];

        if (LaunchProcess(_cli, arguments) is not null) _isFirstTurn = false;
    }

    protected override void OnProcessTerminated()
    {
        var text = _collectedText.Trim();
        if (!string.IsNullOrEmpty(text) && IsBusy)
        {
            var alreadyStreamed = History.Count > 0 && History[^1].Role == MessageRole.Assistant;
            if (!alreadyStreamed && string.IsNullOrEmpty(_currentResponseText))
            {
                History.Add(new AgentMessage(MessageRole.Assistant, text));
                RaiseText(text);
            }
        }

        if (!string.IsNullOrEmpty(_currentResponseText) &&
            (History.Count == 0 || History[^1].Role != MessageRole.Assistant))
        {
            History.Add(new AgentMessage(MessageRole.Assistant, _currentResponseText));
        }

        CompleteTurnIfBusy();
    }

    /// <summary>
    /// Gemini writes spinners and status glyphs to stderr. Surfacing those as errors
    /// would fill the terminal with noise, so only real messages get through.
    /// </summary>
    protected override void HandleStandardError(string chunk)
    {
        var trimmed = chunk.Trim();
        if (trimmed.Length == 0) return;
        if (IsProgressNoise(trimmed)) return;
        if (chunk.Contains("Keychain initialization encountered an error")) return;
        RaiseError(chunk);
    }

    internal static bool IsProgressNoise(string trimmed)
    {
        if (trimmed.Length == 0) return true;
        // Braille spinner frames plus the CLI's status prefixes.
        const string noisePrefixes = "✓→◆⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏";
        return noisePrefixes.Contains(trimmed[0]);
    }

    protected override void ParseLine(string line)
    {
        var json = JsonHelpers.TryParseObject(line);
        if (json is not null)
        {
            _sawJsonLine = true;
            _collectedText += line + "\n";
            HandleJsonEvent(json);
            return;
        }

        _collectedText += line + "\n";

        // Plain-text mode: stream each line straight through as assistant output.
        if (!_sawJsonLine)
        {
            var text = line + "\n";
            _currentResponseText += text;
            RaiseText(text);
        }
    }

    private void HandleJsonEvent(JsonObject json)
    {
        var type = json.String("type") ?? json.String("event") ?? "";
        var data = json.Object("data") ?? json;

        switch (type)
        {
            case "content":
            case "text":
            case "delta":
            case "message":
            {
                var text = data.String("text") ?? data.String("content") ?? json.String("text") ?? "";
                if (string.IsNullOrEmpty(text)) break;

                if (json.String("role") == "assistant" && json.String("content") is { } content)
                {
                    if (json.Bool("delta") == true)
                    {
                        _currentResponseText += content;
                        RaiseText(content);
                    }
                    else if (string.IsNullOrEmpty(_currentResponseText))
                    {
                        _currentResponseText = content;
                        RaiseText(content);
                    }
                }
                else
                {
                    RaiseText(text);
                }
                break;
            }

            case "tool_call":
            case "function_call":
            case "tool_use":
            {
                var toolName = data.String("name") ?? json.String("tool_name") ?? "Tool";
                if (toolName == "activate_skill") break; // Internal skill chatter.

                var input = data.Object("input") ?? data.Object("arguments") ?? json.Object("parameters");
                History.Add(new AgentMessage(MessageRole.ToolUse,
                    $"{toolName}: {FormatToolSummary(toolName, input)}"));
                RaiseToolUse(toolName, input);
                break;
            }

            case "tool_result":
            case "function_result":
            {
                var output = data.String("output") ?? data.String("result") ?? json.String("output") ?? "";
                var isError = data.Bool("is_error") ?? json.String("status") == "error";
                var summary = JsonHelpers.Summarize(output);
                History.Add(new AgentMessage(MessageRole.ToolResult, isError ? $"ERROR: {summary}" : summary));
                RaiseToolResult(summary, isError);
                break;
            }

            case "done":
            case "end":
            case "complete":
            case "turn_end":
            case "result":
            {
                if (!IsBusy) break;
                IsBusy = false;

                var result = json.String("result") ?? data.String("text");
                if (!string.IsNullOrEmpty(result))
                {
                    History.Add(new AgentMessage(MessageRole.Assistant, result));
                }
                else if (!string.IsNullOrEmpty(_currentResponseText))
                {
                    History.Add(new AgentMessage(MessageRole.Assistant, _currentResponseText));
                }

                RaiseTurnComplete();
                break;
            }

            case "error":
            {
                var message = data.String("message") ?? data.String("error") ?? "Unknown Gemini error";
                RaiseError(message, recordInHistory: true);
                break;
            }

            default:
            {
                var text = json.String("text") ?? json.String("content");
                if (!string.IsNullOrEmpty(text))
                {
                    _currentResponseText += text;
                    RaiseText(text);
                }
                break;
            }
        }
    }

    private static string FormatToolSummary(string toolName, JsonObject? input) => toolName switch
    {
        "run_shell_command" => input.String("command") ?? "",
        "read_file" => input.String("file_path") ?? "",
        "replace" or "write_file" => input.String("file_path") ?? "",
        "glob" => input.String("pattern") ?? "",
        "grep_search" => input.String("pattern") ?? "",
        _ => string.Join(", ", input.ToDictionary().Keys.Order().Take(3))
    };
}
