using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace LilAgents.Agents;

/// <summary>
/// Connects to a self-hosted OpenClaw gateway over WebSocket (protocol v3).
///
/// The only provider with no local process — which makes it the most portable piece of
/// the whole app. ClientWebSocket stands in for URLSessionWebSocketTask; the frame
/// protocol and Ed25519 handshake are unchanged from the macOS build.
/// </summary>
public sealed class OpenClawSession : IAgentSession
{
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cancellation;
    private readonly OpenClawDeviceIdentity _device = OpenClawDeviceIdentity.LoadOrCreate();
    private readonly string _sessionKey;
    private string? _pendingNonce;
    private int _nextRequestId;

    public bool IsRunning { get; private set; }
    public bool IsBusy { get; private set; }
    public List<AgentMessage> History { get; } = [];

    public event Action<string>? TextReceived;
    public event Action<string>? ErrorReceived;
    public event Action<string, IReadOnlyDictionary<string, object?>>? ToolUsed;
    public event Action<string, bool>? ToolResultReceived;
    public event Action? SessionReady;
    public event Action? TurnCompleted;
    public event Action? ProcessExited;

    public OpenClawSession()
    {
        _sessionKey = $"{Settings.Current.OpenClawSessionPrefix}:{Guid.NewGuid().ToString().ToLowerInvariant()}";
    }

    // MARK: - Lifecycle

    public void Start()
    {
        var url = Settings.Current.OpenClawGatewayUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            Fail($"Invalid gateway URL: {url}\n\n{AgentProviderKind.OpenClaw.InstallInstructions()}");
            return;
        }

        _cancellation = new CancellationTokenSource();
        _socket = new ClientWebSocket();
        IsRunning = true;

        _ = ConnectAndReceiveAsync(uri, _cancellation.Token);
    }

    public void Send(string message)
    {
        if (!IsRunning) return;

        IsBusy = true;
        History.Add(new AgentMessage(MessageRole.User, message));

        var parameters = new JsonObject
        {
            ["sessionKey"] = _sessionKey,
            ["message"] = message,
            ["idempotencyKey"] = Guid.NewGuid().ToString()
        };

        if (!string.IsNullOrEmpty(Settings.Current.OpenClawAgentId))
        {
            parameters["agentId"] = Settings.Current.OpenClawAgentId;
        }

        _ = SendRequestAsync("chat.send", parameters);
    }

    public void Terminate()
    {
        IsRunning = false;
        IsBusy = false;

        var socket = _socket;
        _socket = null;
        _cancellation?.Cancel();
        _cancellation = null;

        if (socket is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception exception) when (exception is WebSocketException or OperationCanceledException
                                                      or ObjectDisposedException)
                {
                }
                finally
                {
                    socket.Dispose();
                }
            });
        }

        UiDispatcher.Invoke(() => ProcessExited?.Invoke());
    }

    // MARK: - Transport

    private async Task ConnectAndReceiveAsync(Uri uri, CancellationToken token)
    {
        try
        {
            await _socket!.ConnectAsync(uri, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException
                                              or ObjectDisposedException)
        {
            UiDispatcher.Invoke(() =>
            {
                IsRunning = false;
                IsBusy = false;
                Fail($"Could not reach the OpenClaw gateway at {uri}.\n\n{exception.Message}");
                ProcessExited?.Invoke();
            });
            return;
        }

        await ReceiveLoopAsync(token).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        var accumulated = new StringBuilder();

        try
        {
            while (_socket is { State: WebSocketState.Open } && !token.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token)
                    .ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close) break;

                accumulated.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                // A gateway frame can span several receives; only parse once complete.
                if (!result.EndOfMessage) continue;

                var text = accumulated.ToString();
                accumulated.Clear();
                UiDispatcher.Invoke(() => HandleFrame(text));
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException)
        {
            UiDispatcher.Invoke(() =>
            {
                IsRunning = false;
                IsBusy = false;
                ErrorReceived?.Invoke($"Connection lost: {exception.Message}");
                ProcessExited?.Invoke();
            });
            return;
        }

        UiDispatcher.Invoke(() =>
        {
            IsRunning = false;
            IsBusy = false;
            ProcessExited?.Invoke();
        });
    }

    private async Task SendRequestAsync(string method, JsonObject parameters)
    {
        var socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open) return;

        _nextRequestId++;
        var frame = new JsonObject
        {
            ["type"] = "req",
            ["id"] = $"lil-{_nextRequestId}",
            ["method"] = method,
            ["params"] = parameters
        };

        var bytes = Encoding.UTF8.GetBytes(frame.ToJsonString());

        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException
                                              or OperationCanceledException)
        {
            UiDispatcher.Invoke(() => ErrorReceived?.Invoke($"Send error: {exception.Message}"));
        }
    }

    // MARK: - Frame dispatch

    private void HandleFrame(string text)
    {
        var json = JsonHelpers.TryParseObject(text);
        if (json is null) return;

        switch (json.String("type"))
        {
            case "event":
                HandleEvent(json);
                break;
            case "res":
                HandleResponse(json);
                break;
        }
    }

    private void HandleEvent(JsonObject json)
    {
        var payload = json.Object("payload") ?? new JsonObject();

        switch (json.String("event"))
        {
            case "connect.challenge":
                _pendingNonce = payload.String("nonce");
                _ = SendConnectRequestAsync();
                break;

            case "chat":
                HandleChatEvent(payload);
                break;
        }
    }

    private void HandleChatEvent(JsonObject payload)
    {
        switch (payload.String("state"))
        {
            case "delta":
            {
                var content = payload.Object("message").Array("content");
                if (content is null) return;

                foreach (var node in content)
                {
                    if (node is not JsonObject block) continue;

                    switch (block.String("type"))
                    {
                        case "text":
                            var text = block.String("text");
                            if (!string.IsNullOrEmpty(text)) TextReceived?.Invoke(text);
                            break;

                        case "tool_use":
                        {
                            var name = block.String("name") ?? "Tool";
                            var input = block.Object("input");
                            History.Add(new AgentMessage(MessageRole.ToolUse,
                                $"{name}: {ToolSummary(name, input)}"));
                            ToolUsed?.Invoke(name, input.ToDictionary());
                            break;
                        }

                        case "tool_result":
                        {
                            var isError = block.Bool("is_error") ?? false;
                            var summary = JsonHelpers.Summarize(block.String("text"));
                            History.Add(new AgentMessage(MessageRole.ToolResult,
                                isError ? $"ERROR: {summary}" : summary));
                            ToolResultReceived?.Invoke(summary, isError);
                            break;
                        }
                    }
                }
                break;
            }

            case "final":
            {
                IsBusy = false;
                var content = payload.Object("message").Array("content");
                if (content is not null)
                {
                    var builder = new StringBuilder();
                    foreach (var node in content)
                    {
                        if (node is JsonObject block && block.String("type") == "text")
                        {
                            builder.Append(block.String("text"));
                        }
                    }
                    if (builder.Length > 0)
                    {
                        History.Add(new AgentMessage(MessageRole.Assistant, builder.ToString()));
                    }
                }
                TurnCompleted?.Invoke();
                break;
            }

            case "error":
                IsBusy = false;
                Fail(payload.String("errorMessage") ?? "Chat error");
                TurnCompleted?.Invoke();
                break;

            case "aborted":
                IsBusy = false;
                TurnCompleted?.Invoke();
                break;
        }
    }

    private void HandleResponse(JsonObject json)
    {
        var ok = json.Bool("ok") ?? false;
        var payload = json.Object("payload");

        if (ok && payload.String("type") == "hello-ok")
        {
            SessionReady?.Invoke();
            return;
        }

        if (ok) return;

        var error = json.Object("error");
        var message = error.String("message") ?? "Unknown error";
        var code = error.String("code") ?? "";

        if (code is "auth_required" or "auth_failed")
        {
            ErrorReceived?.Invoke(
                "Authentication failed. Set your gateway token under Provider > Advanced Settings.\n\n" + message);
        }
        else
        {
            ErrorReceived?.Invoke($"Gateway error: {message}");
        }
    }

    // MARK: - Handshake

    private Task SendConnectRequestAsync()
    {
        const string role = "operator";
        string[] scopes = ["operator.read", "operator.write"];
        var signedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var token = Settings.Current.OpenClawAuthToken;

        var payload = _device.BuildAuthPayload("cli", "cli", role, scopes, signedAtMs, token, _pendingNonce);
        var signature = _device.Sign(payload);

        var deviceNode = new JsonObject
        {
            ["id"] = _device.DeviceId,
            ["publicKey"] = _device.PublicKeyBase64Url,
            ["signature"] = signature,
            ["signedAt"] = signedAtMs
        };
        if (_pendingNonce is not null) deviceNode["nonce"] = _pendingNonce;

        var parameters = new JsonObject
        {
            ["minProtocol"] = 3,
            ["maxProtocol"] = 3,
            ["client"] = new JsonObject
            {
                ["id"] = "cli",
                ["version"] = "1.0.0",
                ["platform"] = "windows",
                ["mode"] = "cli"
            },
            ["role"] = role,
            ["scopes"] = new JsonArray(scopes.Select(s => (JsonNode)s!).ToArray()),
            ["device"] = deviceNode
        };

        if (!string.IsNullOrEmpty(token))
        {
            parameters["auth"] = new JsonObject { ["token"] = token };
        }

        return SendRequestAsync("connect", parameters);
    }

    // MARK: - Helpers

    private void Fail(string message)
    {
        History.Add(new AgentMessage(MessageRole.Error, message));
        ErrorReceived?.Invoke(message);
    }

    private static string ToolSummary(string name, JsonObject? input) => name switch
    {
        "Bash" => input.String("command") ?? "",
        "Read" => input.String("file_path") ?? "",
        "Edit" or "Write" => input.String("file_path") ?? "",
        "Glob" => input.String("pattern") ?? "",
        "Grep" => input.String("pattern") ?? "",
        _ => input.String("description") ?? string.Join(", ", input.ToDictionary().Keys.Order().Take(3))
    };
}
