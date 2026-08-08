using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using LilAgents.Platform;

namespace LilAgents.Agents;

/// <summary>
/// Shared plumbing for the five providers that are driven by spawning a CLI process:
/// binary resolution, UTF-8 stream pumping, line assembly, teardown and UI-thread
/// marshalling. Subclasses supply the arguments and the per-provider event parsing.
/// </summary>
public abstract class ProcessAgentSession : IAgentSession
{
    private Process? _process;
    private CancellationTokenSource? _pumpCancellation;
    protected readonly NdjsonLineBuffer LineBuffer = new();

    public bool IsRunning { get; protected set; }
    public bool IsBusy { get; protected set; }
    public List<AgentMessage> History { get; } = [];

    public event Action<string>? TextReceived;
    public event Action<string>? ErrorReceived;
    public event Action<string, IReadOnlyDictionary<string, object?>>? ToolUsed;
    public event Action<string, bool>? ToolResultReceived;
    public event Action? SessionReady;
    public event Action? TurnCompleted;
    public event Action? ProcessExited;

    // MARK: - Subclass contract

    protected abstract AgentProviderKind Provider { get; }

    /// <summary>Extra PATH entries this provider needs on top of the standard set.</summary>
    protected virtual IEnumerable<string> ExtraPaths => [];

    /// <summary>Handles one complete line of stdout.</summary>
    protected abstract void ParseLine(string line);

    /// <summary>Called after the process exits, before the turn is force-completed.</summary>
    protected virtual void OnProcessTerminated() { }

    // MARK: - Lifecycle

    public abstract void Start();

    public abstract void Send(string message);

    public virtual void Terminate()
    {
        _pumpCancellation?.Cancel();
        _pumpCancellation = null;

        var process = _process;
        _process = null;

        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    // Kill the whole tree: the npm shims and node wrappers spawn children
                    // that would otherwise survive and keep holding the model session.
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException
                                                  or System.ComponentModel.Win32Exception
                                                  or NotSupportedException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        IsRunning = false;
        IsBusy = false;
    }

    // MARK: - Binary resolution

    /// <summary>
    /// Resolves this provider's CLI off the UI thread, then hands it back on the UI
    /// thread. Reports a Windows-appropriate install hint when it is missing.
    /// </summary>
    protected void ResolveBinaryAsync(Action<ResolvedCli> onResolved)
    {
        Task.Run(() => ShellEnvironment.FindBinary(Provider.BinaryName()))
            .ContinueWith(task =>
            {
                UiDispatcher.Invoke(() =>
                {
                    var resolved = task.IsCompletedSuccessfully ? task.Result : null;
                    if (resolved is null)
                    {
                        RaiseError($"{Provider.DisplayName()} CLI not found.\n\n{Provider.InstallInstructions()}",
                            recordInHistory: true);
                        return;
                    }
                    onResolved(resolved);
                });
            }, TaskScheduler.Default);
    }

    // MARK: - Process launch

    protected Process? LaunchProcess(ResolvedCli cli, IEnumerable<string> arguments, bool redirectInput = false)
    {
        var startInfo = CliLauncher.CreateStartInfo(cli, arguments, extraPaths: ExtraPaths, redirectInput: redirectInput);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += (_, _) => UiDispatcher.Invoke(HandleProcessExited);

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                              or InvalidOperationException
                                              or PlatformNotSupportedException)
        {
            process.Dispose();
            IsBusy = false;
            RaiseError($"Failed to launch {Provider.DisplayName()} CLI.\n\n" +
                       $"{Provider.InstallInstructions()}\n\nError: {exception.Message}",
                recordInHistory: true);
            return null;
        }

        _process = process;
        _pumpCancellation = new CancellationTokenSource();
        var token = _pumpCancellation.Token;

        _ = PumpAsync(process.StandardOutput, chunk => UiDispatcher.Invoke(() => ProcessOutput(chunk)), token);
        _ = PumpAsync(process.StandardError, chunk => UiDispatcher.Invoke(() => HandleStandardError(chunk)), token);

        return process;
    }

    /// <summary>
    /// Reads raw bytes and decodes incrementally.
    ///
    /// Deliberately not Process.OutputDataReceived: that only surfaces whole lines, and
    /// providers which stream partial assistant text without newlines would appear frozen
    /// until the turn ended. A stateful <see cref="Decoder"/> is required because a UTF-8
    /// sequence can straddle a read boundary.
    /// </summary>
    private static async Task PumpAsync(StreamReader reader, Action<string> onChunk, CancellationToken token)
    {
        var decoder = new UTF8Encoding(false).GetDecoder();
        var bytes = new byte[8192];
        var chars = new char[8192 * 2];

        try
        {
            while (!token.IsCancellationRequested)
            {
                var read = await reader.BaseStream.ReadAsync(bytes.AsMemory(0, bytes.Length), token)
                    .ConfigureAwait(false);
                if (read == 0) break;

                var decoded = decoder.GetChars(bytes, 0, read, chars, 0);
                if (decoded > 0) onChunk(new string(chars, 0, decoded));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The pipe closed underneath us — the Exited handler does the cleanup.
        }
    }

    private void ProcessOutput(string chunk)
    {
        foreach (var line in LineBuffer.Append(chunk)) ParseLine(line);
    }

    protected virtual void HandleStandardError(string chunk)
    {
        if (!string.IsNullOrWhiteSpace(chunk)) RaiseError(chunk);
    }

    private void HandleProcessExited()
    {
        _process = null;

        // Anything left without a trailing newline is still a valid final event.
        var remainder = LineBuffer.Flush();
        if (!string.IsNullOrWhiteSpace(remainder)) ParseLine(remainder);

        OnProcessTerminated();
    }

    // MARK: - Event raising

    protected void RaiseText(string text) => TextReceived?.Invoke(text);

    protected void RaiseError(string text, bool recordInHistory = false)
    {
        if (recordInHistory) History.Add(new AgentMessage(MessageRole.Error, text));
        ErrorReceived?.Invoke(text);
    }

    protected void RaiseToolUse(string toolName, JsonObject? input)
    {
        ToolUsed?.Invoke(toolName, input.ToDictionary());
    }

    protected void RaiseToolResult(string summary, bool isError) => ToolResultReceived?.Invoke(summary, isError);

    protected void RaiseSessionReady() => SessionReady?.Invoke();

    protected void RaiseTurnComplete() => TurnCompleted?.Invoke();

    protected void RaiseProcessExit() => ProcessExited?.Invoke();

    /// <summary>Marks the turn finished exactly once, however the provider signalled it.</summary>
    protected void CompleteTurnIfBusy()
    {
        if (!IsBusy) return;
        IsBusy = false;
        RaiseTurnComplete();
    }
}
