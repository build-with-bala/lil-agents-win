using System.Windows;
using System.Windows.Interop;
using LilAgents.Platform;

namespace LilAgents.UI;

/// <summary>
/// Base for every always-on-top, chrome-free window the app puts on screen.
///
/// Stands in for the macOS build's borderless NSWindow at <c>NSWindow.Level.statusBar</c>
/// with <c>collectionBehavior = [.moveToActiveSpace, .stationary]</c>. The Windows recipe
/// is different in kind: Topmost gets you into the right z-band, but WS_EX_TOOLWINDOW is
/// what keeps the window out of Alt-Tab, and WS_EX_NOACTIVATE is what stops a click on a
/// character from yanking focus away from whatever the user was typing in.
/// </summary>
public abstract class OverlayWindow : Window
{
    private bool _stylesApplied;

    protected OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = null;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    /// <summary>When false the window never takes focus (walkers and bubbles).</summary>
    protected virtual bool CanActivate => false;

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public uint CurrentDpi
    {
        get
        {
            var handle = Handle;
            return handle == IntPtr.Zero ? 96 : DpiHelper.GetDpiForWindowSafe(handle);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyExtendedStyles();

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProcHook);
        }
    }

    private void ApplyExtendedStyles()
    {
        if (_stylesApplied) return;
        var handle = Handle;
        if (handle == IntPtr.Zero) return;

        var style = Native.GetWindowLong(handle, Native.GWL_EXSTYLE);
        style |= Native.WS_EX_TOOLWINDOW;
        if (!CanActivate) style |= Native.WS_EX_NOACTIVATE;
        Native.SetWindowLong(handle, Native.GWL_EXSTYLE, style);

        _stylesApplied = true;
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        return HandleMessage(hwnd, msg, wParam, lParam, ref handled);
    }

    protected virtual IntPtr HandleMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        => IntPtr.Zero;

    // MARK: - Positioning

    /// <summary>
    /// Moves and resizes in physical pixels.
    ///
    /// Window.Left/Top are device-independent units resolved against whichever monitor
    /// WPF currently associates with the window, which makes a walker crossing a DPI
    /// boundary jump. All layout maths here is done in pixels and pushed through
    /// SetWindowPos so the geometry never round-trips through a stale scale factor.
    /// </summary>
    public void SetPixelBounds(double x, double y, double width, double height)
    {
        var handle = Handle;
        if (handle == IntPtr.Zero) return;

        Native.SetWindowPos(handle, IntPtr.Zero,
            (int)Math.Round(x), (int)Math.Round(y),
            (int)Math.Round(width), (int)Math.Round(height),
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    public void SetPixelPosition(double x, double y)
    {
        var handle = Handle;
        if (handle == IntPtr.Zero) return;

        Native.SetWindowPos(handle, IntPtr.Zero,
            (int)Math.Round(x), (int)Math.Round(y), 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Re-asserts topmost placement.
    ///
    /// The taskbar is itself a topmost window, and z-order within that band shifts as
    /// other topmost windows appear. Without periodic reassertion a walker eventually
    /// slides behind the taskbar it is supposed to be standing on.
    /// </summary>
    public void ReassertTopmost()
    {
        var handle = Handle;
        if (handle == IntPtr.Zero) return;

        Native.SetWindowPos(handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void ShowNoActivate()
    {
        if (!IsVisible) Show();
        ReassertTopmost();
    }

    /// <summary>
    /// Hit-tests a screen point against the window's real rect.
    ///
    /// Deliberately asks Win32 rather than reading Left/Top: those are WPF's idea of where
    /// the window is in DIPs, and this class moves windows with SetWindowPos, so the
    /// properties can lag a frame behind the truth.
    /// </summary>
    public bool ContainsScreenPoint(int x, int y)
    {
        var handle = Handle;
        if (handle == IntPtr.Zero || !IsVisible) return false;
        if (!Native.GetWindowRect(handle, out var rect)) return false;

        return x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;
    }

    public Native.RECT? PixelRect
    {
        get
        {
            var handle = Handle;
            if (handle == IntPtr.Zero) return null;
            return Native.GetWindowRect(handle, out var rect) ? rect : null;
        }
    }
}
