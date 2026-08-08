using System.Windows;
using System.Windows.Interop;

namespace LilAgents.Platform;

/// <summary>
/// Converts between WPF device-independent units and the physical pixels every Win32
/// geometry call speaks in. The app positions windows with SetWindowPos rather than
/// Window.Left/Top so that a walker crossing from a 100% to a 150% display lands in
/// the right place instead of jumping.
/// </summary>
public static class DpiHelper
{
    public const double DefaultDpi = 96.0;

    public static uint GetDpiForMonitor(IntPtr monitor)
    {
        try
        {
            if (Native.GetDpiForMonitor(monitor, Native.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0)
            {
                return dpiX == 0 ? (uint)DefaultDpi : dpiX;
            }
        }
        catch (DllNotFoundException)
        {
            // shcore.dll is absent before Windows 8.1.
        }
        catch (EntryPointNotFoundException)
        {
        }
        return (uint)DefaultDpi;
    }

    public static uint GetDpiForWindowSafe(IntPtr hwnd)
    {
        try
        {
            var dpi = Native.GetDpiForWindow(hwnd);
            if (dpi > 0) return dpi;
        }
        catch (EntryPointNotFoundException)
        {
        }

        var monitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        return GetDpiForMonitor(monitor);
    }

    public static double PixelsToDips(double pixels, uint dpi) => pixels * DefaultDpi / dpi;

    public static double DipsToPixels(double dips, uint dpi) => dips * dpi / DefaultDpi;

    public static IntPtr HandleOf(Window window) => new WindowInteropHelper(window).Handle;

    public static IntPtr EnsureHandleOf(Window window) => new WindowInteropHelper(window).EnsureHandle();
}
