using Microsoft.Win32;

namespace LilAgents.Platform;

public enum TaskbarEdge { Left, Top, Right, Bottom }

/// <summary>
/// Where a walker is allowed to walk, in physical screen pixels.
/// </summary>
public readonly record struct WalkBand(double X, double Width, double TopY)
{
    public double Right => X + Width;
}

public readonly record struct MonitorGeometry(
    IntPtr Handle,
    Native.RECT Bounds,
    Native.RECT WorkArea,
    bool IsPrimary,
    uint Dpi);

/// <summary>
/// The Windows answer to the macOS build's Dock-geometry maths.
///
/// macOS derives a narrow strip from the Dock's icon count (tilesize x number of
/// persistent apps). Windows exposes no equivalent query — there is no API for
/// "how wide is the cluster of taskbar buttons" — so the band is derived from the
/// taskbar rect plus the Win11 alignment setting instead.
/// </summary>
public static class TaskbarGeometry
{
    /// <summary>Fraction of the monitor's width the characters roam across.</summary>
    public const double BandWidthFraction = 0.40;

    /// <summary>Left inset when the taskbar is left-aligned, to clear Start + search.</summary>
    public const double LeftAlignedStartInsetFraction = 0.02;

    public readonly record struct TaskbarInfo(
        Native.RECT Rect,
        TaskbarEdge Edge,
        bool IsAutoHide,
        bool IsCentered);

    // MARK: - Queries

    public static TaskbarInfo Query()
    {
        var data = new Native.APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.APPBARDATA>()
        };

        var edge = TaskbarEdge.Bottom;
        var rect = default(Native.RECT);

        if (Native.SHAppBarMessage(Native.ABM_GETTASKBARPOS, ref data) != IntPtr.Zero)
        {
            rect = data.rc;
            edge = data.uEdge switch
            {
                Native.ABE_LEFT => TaskbarEdge.Left,
                Native.ABE_TOP => TaskbarEdge.Top,
                Native.ABE_RIGHT => TaskbarEdge.Right,
                _ => TaskbarEdge.Bottom
            };
        }

        var state = new Native.APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.APPBARDATA>()
        };
        var stateFlags = (uint)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref state).ToInt64();
        var autoHide = (stateFlags & Native.ABS_AUTOHIDE) != 0;

        return new TaskbarInfo(rect, edge, autoHide, IsTaskbarCentered());
    }

    /// <summary>
    /// Windows 11 centres taskbar buttons by default (TaskbarAl=1); Windows 10 and
    /// opted-out Win11 users are left-aligned (TaskbarAl=0). The key is absent on
    /// Windows 10, where left alignment is the only option.
    /// </summary>
    public static bool IsTaskbarCentered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            if (key?.GetValue("TaskbarAl") is int value) return value == 1;
        }
        catch
        {
            // Registry unreadable (policy, sandbox) — fall through to the default.
        }
        return false;
    }

    public static IReadOnlyList<MonitorGeometry> EnumerateMonitors()
    {
        var results = new List<MonitorGeometry>();

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref Native.RECT rect, IntPtr data)
        {
            var info = new Native.MONITORINFO
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>()
            };
            if (Native.GetMonitorInfo(hMonitor, ref info))
            {
                results.Add(new MonitorGeometry(
                    hMonitor,
                    info.rcMonitor,
                    info.rcWork,
                    (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0,
                    DpiHelper.GetDpiForMonitor(hMonitor)));
            }
            return true;
        }

        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);

        if (results.Count == 0)
        {
            // EnumDisplayMonitors can come back empty on a locked session. Fall back to
            // the primary monitor so the app still has somewhere to put the characters.
            var pt = new Native.POINT { X = 0, Y = 0 };
            var handle = Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONEAREST);
            var info = new Native.MONITORINFO
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>()
            };
            if (Native.GetMonitorInfo(handle, ref info))
            {
                results.Add(new MonitorGeometry(handle, info.rcMonitor, info.rcWork, true,
                    DpiHelper.GetDpiForMonitor(handle)));
            }
        }

        return results;
    }

    /// <summary>
    /// The monitor the taskbar lives on — the Windows equivalent of the macOS build's
    /// "screen that currently shows the Dock". Falls back to primary when the taskbar
    /// rect is empty (auto-hidden and fully retracted).
    /// </summary>
    public static MonitorGeometry ResolveActiveMonitor(
        IReadOnlyList<MonitorGeometry> monitors, TaskbarInfo taskbar, int pinnedIndex)
    {
        if (pinnedIndex >= 0 && pinnedIndex < monitors.Count) return monitors[pinnedIndex];

        if (taskbar.Rect.Width > 0 && taskbar.Rect.Height > 0)
        {
            var rect = taskbar.Rect;
            var handle = Native.MonitorFromRect(ref rect, Native.MONITOR_DEFAULTTONEAREST);
            foreach (var monitor in monitors)
            {
                if (monitor.Handle == handle) return monitor;
            }
        }

        foreach (var monitor in monitors)
        {
            if (monitor.IsPrimary) return monitor;
        }

        return monitors.Count > 0 ? monitors[0] : default;
    }

    // MARK: - Band maths

    /// <summary>
    /// Computes the strip the characters walk along, in physical pixels.
    ///
    /// Bottom/top taskbar: a band across the taskbar itself, positioned to sit under
    /// the button cluster (centred on Win11, left-anchored otherwise).
    ///
    /// Left/right taskbar or auto-hidden: walking a vertical strip makes no sense and
    /// an auto-hidden bar has no stable rect, so the band falls back to the bottom of
    /// the monitor's work area.
    /// </summary>
    public static WalkBand ComputeBand(MonitorGeometry monitor, TaskbarInfo taskbar, double characterWidth)
    {
        var monitorLeft = (double)monitor.Bounds.Left;
        var monitorWidth = (double)monitor.Bounds.Width;

        var usableWidth = Math.Max(monitorWidth * BandWidthFraction, characterWidth);
        usableWidth = Math.Min(usableWidth, monitorWidth);

        var horizontalBar = taskbar.Edge is TaskbarEdge.Bottom or TaskbarEdge.Top;
        var barVisible = !taskbar.IsAutoHide && taskbar.Rect.Height > 0;

        double topY;
        if (horizontalBar && barVisible)
        {
            // Characters stand on the taskbar's inner edge: its top when the bar is at
            // the bottom of the screen, its bottom when the bar is along the top.
            topY = taskbar.Edge == TaskbarEdge.Bottom ? taskbar.Rect.Top : taskbar.Rect.Bottom;
        }
        else
        {
            topY = monitor.WorkArea.Bottom;
        }

        double x;
        if (taskbar.IsCentered && horizontalBar)
        {
            x = monitorLeft + (monitorWidth - usableWidth) / 2.0;
        }
        else
        {
            x = monitorLeft + monitorWidth * LeftAlignedStartInsetFraction;
            if (x + usableWidth > monitorLeft + monitorWidth)
            {
                x = monitorLeft + monitorWidth - usableWidth;
            }
        }

        return new WalkBand(x, usableWidth, topY);
    }
}
