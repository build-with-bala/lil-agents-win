using LilAgents.Platform;
using Xunit;

namespace LilAgents.Tests;

/// <summary>
/// Walk-band placement across taskbar edges, alignments and monitor offsets.
/// This is the piece with no macOS counterpart, so it carries the most new logic.
/// </summary>
public class TaskbarGeometryTests
{
    private static Native.RECT Rect(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };

    private static MonitorGeometry Monitor(
        int left = 0, int top = 0, int right = 1920, int bottom = 1080, int workBottom = 1032) =>
        new(IntPtr.Zero,
            Rect(left, top, right, bottom),
            Rect(left, top, right, workBottom),
            true,
            96);

    [Fact]
    public void CentresTheBandOnAWindows11Taskbar()
    {
        var monitor = Monitor();
        var taskbar = new TaskbarGeometry.TaskbarInfo(
            Rect(0, 1032, 1920, 1080), TaskbarEdge.Bottom, IsAutoHide: false, IsCentered: true);

        var band = TaskbarGeometry.ComputeBand(monitor, taskbar, characterWidth: 113);

        Assert.Equal(1920 * TaskbarGeometry.BandWidthFraction, band.Width, 6);
        // Equal margins either side.
        Assert.Equal(1920 - band.Right, band.X, 6);
        Assert.Equal(1032, band.TopY);
    }

    [Fact]
    public void LeftAlignsTheBandWhenTaskbarButtonsAreLeftAligned()
    {
        var monitor = Monitor();
        var taskbar = new TaskbarGeometry.TaskbarInfo(
            Rect(0, 1032, 1920, 1080), TaskbarEdge.Bottom, IsAutoHide: false, IsCentered: false);

        var band = TaskbarGeometry.ComputeBand(monitor, taskbar, characterWidth: 113);

        Assert.Equal(1920 * TaskbarGeometry.LeftAlignedStartInsetFraction, band.X, 6);
    }

    [Fact]
    public void StandsOnTheUnderSideOfATopAnchoredTaskbar()
    {
        var monitor = Monitor(workBottom: 1080);
        var taskbar = new TaskbarGeometry.TaskbarInfo(
            Rect(0, 0, 1920, 48), TaskbarEdge.Top, IsAutoHide: false, IsCentered: true);

        var band = TaskbarGeometry.ComputeBand(monitor, taskbar, characterWidth: 113);

        Assert.Equal(48, band.TopY);
    }

    [Fact]
    public void FallsBackToTheWorkAreaForAVerticalTaskbar()
    {
        // Walking a vertical strip makes no sense, so the characters go to the bottom of
        // the usable desktop instead.
        var monitor = Monitor(right: 1920, bottom: 1080, workBottom: 1080);
        var taskbar = new TaskbarGeometry.TaskbarInfo(
            Rect(0, 0, 62, 1080), TaskbarEdge.Left, IsAutoHide: false, IsCentered: false);

        var band = TaskbarGeometry.ComputeBand(monitor, taskbar, characterWidth: 113);

        Assert.Equal(1080, band.TopY);
    }

    [Fact]
    public void FallsBackToTheWorkAreaWhenTheTaskbarAutoHides()
    {
        var monitor = Monitor(workBottom: 1078);
        var taskbar = new TaskbarGeometry.TaskbarInfo(
            Rect(0, 1078, 1920, 1080), TaskbarEdge.Bottom, IsAutoHide: true, IsCentered: true);

        var band = TaskbarGeometry.ComputeBand(monitor, taskbar, characterWidth: 113);

        Assert.Equal(1078, band.TopY);
    }

    [Fact]
    public void OffsetsTheBandOntoASecondaryMonitor()
    {
        // A monitor to the right of primary has a non-zero origin; the band must follow it
        // rather than being computed in primary-relative coordinates.
        var monitor = new MonitorGeometry(IntPtr.Zero,
            Rect(1920, 0, 4480, 1440), Rect(1920, 0, 4480, 1392), false, 144);
        var taskbar = new TaskbarGeometry.TaskbarInfo(
            Rect(1920, 1392, 4480, 1440), TaskbarEdge.Bottom, IsAutoHide: false, IsCentered: true);

        var band = TaskbarGeometry.ComputeBand(monitor, taskbar, characterWidth: 169);

        Assert.True(band.X >= 1920, $"band started at {band.X}, left of the monitor");
        Assert.True(band.Right <= 4480, $"band ended at {band.Right}, right of the monitor");
        Assert.Equal(2560 * TaskbarGeometry.BandWidthFraction, band.Width, 6);
    }

    [Fact]
    public void NeverReturnsABandNarrowerThanTheCharacter()
    {
        // A very small monitor would otherwise produce a band the character cannot fit in,
        // making the travel distance negative.
        var monitor = Monitor(right: 200, bottom: 200, workBottom: 180);
        var taskbar = new TaskbarGeometry.TaskbarInfo(
            Rect(0, 180, 200, 200), TaskbarEdge.Bottom, IsAutoHide: false, IsCentered: true);

        var band = TaskbarGeometry.ComputeBand(monitor, taskbar, characterWidth: 113);

        Assert.True(band.Width >= 113);
    }

    [Fact]
    public void KeepsTheBandInsideTheMonitorWhenLeftAlignedAndWide()
    {
        var monitor = Monitor(right: 200, bottom: 200, workBottom: 180);
        var taskbar = new TaskbarGeometry.TaskbarInfo(
            Rect(0, 180, 200, 200), TaskbarEdge.Bottom, IsAutoHide: false, IsCentered: false);

        var band = TaskbarGeometry.ComputeBand(monitor, taskbar, characterWidth: 180);

        Assert.True(band.X >= 0);
        Assert.True(band.Right <= 200);
    }

    [Fact]
    public void PinnedMonitorWinsOverTheTaskbarMonitor()
    {
        var monitors = new List<MonitorGeometry>
        {
            Monitor(),
            new(new IntPtr(2), Rect(1920, 0, 3840, 1080), Rect(1920, 0, 3840, 1080), false, 96)
        };
        var taskbar = new TaskbarGeometry.TaskbarInfo(
            Rect(0, 1032, 1920, 1080), TaskbarEdge.Bottom, IsAutoHide: false, IsCentered: true);

        var resolved = TaskbarGeometry.ResolveActiveMonitor(monitors, taskbar, pinnedIndex: 1);

        Assert.Equal(1920, resolved.Bounds.Left);
    }

    [Fact]
    public void FallsBackToPrimaryWhenThePinnedIndexIsStale()
    {
        // The user pinned display 3, then unplugged it.
        var monitors = new List<MonitorGeometry> { Monitor() };
        var taskbar = new TaskbarGeometry.TaskbarInfo(
            default, TaskbarEdge.Bottom, IsAutoHide: true, IsCentered: true);

        var resolved = TaskbarGeometry.ResolveActiveMonitor(monitors, taskbar, pinnedIndex: 2);

        Assert.True(resolved.IsPrimary);
    }
}
