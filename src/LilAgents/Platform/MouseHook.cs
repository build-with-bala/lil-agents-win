namespace LilAgents.Platform;

/// <summary>
/// Global mouse-down watcher, replacing the macOS build's
/// <c>NSEvent.addGlobalMonitorForEvents</c>. Used to dismiss the popover when the user
/// clicks anywhere outside it.
///
/// WPF's Deactivated event is not enough on its own: the walker windows are
/// WS_EX_NOACTIVATE, so clicks elsewhere frequently never change activation.
/// </summary>
public sealed class MouseHook : IDisposable
{
    private readonly Native.LowLevelMouseProc _proc;
    private IntPtr _hook = IntPtr.Zero;
    private bool _disposed;

    /// <summary>Fires on any global left/right mouse-down, with screen coordinates in pixels.</summary>
    public event Action<int, int>? MouseDown;

    public MouseHook()
    {
        // Field-held so the delegate is not collected while Windows holds the pointer.
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        var module = Native.GetModuleHandle(null);
        _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, module, 0);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            if (message is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN)
            {
                var data = System.Runtime.InteropServices.Marshal
                    .PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                // Never block the click — just observe it, then let it through.
                MouseDown?.Invoke(data.pt.X, data.pt.Y);
            }
        }

        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
