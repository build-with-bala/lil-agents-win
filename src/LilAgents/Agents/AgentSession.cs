namespace LilAgents.Agents;

public enum MessageRole { User, Assistant, Error, ToolUse, ToolResult }

public readonly record struct AgentMessage(MessageRole Role, string Text);

/// <summary>
/// Contract every provider implements. Mirrors the macOS AgentSession protocol so the
/// walker/terminal layer above it is provider-agnostic.
///
/// All events are raised on the UI thread — implementations marshal via the dispatcher
/// so subscribers never have to.
/// </summary>
public interface IAgentSession
{
    bool IsRunning { get; }
    bool IsBusy { get; }
    List<AgentMessage> History { get; }

    event Action<string>? TextReceived;
    event Action<string>? ErrorReceived;
    event Action<string, IReadOnlyDictionary<string, object?>>? ToolUsed;
    event Action<string, bool>? ToolResultReceived;
    event Action? SessionReady;
    event Action? TurnCompleted;
    event Action? ProcessExited;

    void Start();
    void Send(string message);
    void Terminate();
}
