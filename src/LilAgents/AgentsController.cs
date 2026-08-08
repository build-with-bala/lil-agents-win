using System.Diagnostics;
using System.Windows.Media;
using LilAgents.Agents;
using LilAgents.Platform;
using LilAgents.UI;

namespace LilAgents;

/// <summary>
/// Owns the characters and drives the frame loop.
///
/// The macOS build ticks from a CVDisplayLink; the Windows equivalent is
/// CompositionTarget.Rendering, which fires once per composition pass on the UI thread —
/// no marshalling needed, and it stops firing when the desktop is not composing.
/// </summary>
public sealed class AgentsController : IDisposable
{
    private readonly List<Walker> _walkers = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly MouseHook _mouseHook = new();

    private IReadOnlyList<MonitorGeometry> _monitors = [];
    private TaskbarGeometry.TaskbarInfo _taskbar;
    private double _lastGeometryRefresh = -1;
    private double _lastTopmostReassert;
    private bool _hiddenForEnvironment;
    private bool _disposed;

    /// <summary>Taskbar and monitor layout are re-read on this cadence, not every frame.</summary>
    private const double GeometryRefreshInterval = 0.5;

    /// <summary>How often topmost placement is re-asserted against the taskbar.</summary>
    private const double TopmostReassertInterval = 2.0;

    public IReadOnlyList<Walker> Walkers => _walkers;

    public double Now => _clock.Elapsed.TotalSeconds;

    public MonitorGeometry? ActiveMonitor { get; private set; }

    public int PinnedMonitorIndex
    {
        get => Settings.Current.PinnedMonitorIndex;
        set
        {
            Settings.Current.PinnedMonitorIndex = value;
            Settings.Current.Save();
        }
    }

    // MARK: - Startup

    public void Start()
    {
        var bruce = new Walker("Bruce", "bruce")
        {
            AccelStart = 3.0,
            FullSpeedStart = 3.75,
            DecelStart = 8.0,
            WalkStop = 8.5,
            WalkAmountRange = (0.4, 0.65),
            YOffset = -3,
            FlipXOffset = 0,
            CharacterColor = Color.FromRgb(102, 184, 140)
        };

        var jazz = new Walker("Jazz", "jazz")
        {
            AccelStart = 3.9,
            FullSpeedStart = 4.5,
            DecelStart = 8.0,
            WalkStop = 8.75,
            WalkAmountRange = (0.35, 0.6),
            YOffset = -7,
            FlipXOffset = -9,
            CharacterColor = Color.FromRgb(255, 102, 0)
        };

        bruce.PositionProgress = 0.3;
        jazz.PositionProgress = 0.7;

        var now = Now;
        bruce.PauseEndTime = now + Random.Shared.NextDouble() * 1.5 + 0.5;
        jazz.PauseEndTime = now + Random.Shared.NextDouble() * 6.0 + 8.0;

        _walkers.Add(bruce);
        _walkers.Add(jazz);

        foreach (var walker in _walkers)
        {
            walker.Controller = this;
            walker.Setup();
        }

        RefreshGeometry(force: true);

        // Provider detection touches the registry and disk, so it runs off-thread and
        // only then picks a first-run default.
        _ = AgentProvider.DetectAvailableProvidersAsync().ContinueWith(_ =>
        {
            UiDispatcher.Invoke(() =>
            {
                if (Settings.Current.HasCompletedOnboarding) return;
                var first = AgentProvider.FirstAvailable();
                foreach (var walker in _walkers) walker.Provider = first;
            });
        }, TaskScheduler.Default);

        _mouseHook.MouseDown += OnGlobalMouseDown;
        _mouseHook.Start();

        CompositionTarget.Rendering += OnRendering;

        if (!Settings.Current.HasCompletedOnboarding) TriggerOnboarding();
    }

    private void TriggerOnboarding()
    {
        var bruce = _walkers.FirstOrDefault();
        if (bruce is null) return;

        bruce.IsOnboarding = true;

        // Let the character be on screen for a beat before it says hello.
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!bruce.IsOnboarding) return;
            bruce.ShowBubble("hi!", isCompletion: true);
            bruce.PlayCompletionSound();
        };
        timer.Start();
    }

    public void CompleteOnboarding()
    {
        Settings.Current.HasCompletedOnboarding = true;
        Settings.Current.Save();
        foreach (var walker in _walkers) walker.IsOnboarding = false;
    }

    // MARK: - Frame loop

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = Now;

        if (now - _lastGeometryRefresh >= GeometryRefreshInterval) RefreshGeometry(force: false);

        if (ActiveMonitor is not { } monitor) return;

        if (!UpdateEnvironmentVisibility(now)) return;

        if (now - _lastTopmostReassert >= TopmostReassertInterval)
        {
            _lastTopmostReassert = now;
            foreach (var walker in _walkers)
            {
                if (walker.IsManuallyVisible) walker.ReassertTopmost();
            }
        }

        var active = _walkers.Where(w => w.IsManuallyVisible).ToList();
        if (active.Count == 0) return;

        // Nudge a paused character's timer forward while a sibling is mid-walk, so the two
        // do not end up permanently in lockstep.
        var anyWalking = active.Any(w => w.IsWalking);
        foreach (var walker in active)
        {
            if (walker.IsIdleForPopover) continue;
            if (walker.IsPaused && now >= walker.PauseEndTime && anyWalking)
            {
                walker.PauseEndTime = now + 5.0 + Random.Shared.NextDouble() * 5.0;
            }
        }

        var band = TaskbarGeometry.ComputeBand(monitor, _taskbar, active.Max(w => w.DisplayWidthPixels));

        foreach (var walker in active)
        {
            walker.UpdateDpi(monitor.Dpi);
            walker.Update(now, band);
        }
    }

    private void RefreshGeometry(bool force)
    {
        _lastGeometryRefresh = Now;

        var monitors = TaskbarGeometry.EnumerateMonitors();
        if (monitors.Count == 0 && !force) return;

        _monitors = monitors;
        _taskbar = TaskbarGeometry.Query();
        ActiveMonitor = TaskbarGeometry.ResolveActiveMonitor(_monitors, _taskbar, PinnedMonitorIndex);
    }

    /// <summary>
    /// Hides everything while the taskbar is retracted.
    ///
    /// The macOS analogue keys off the Dock's reserved area disappearing. On Windows an
    /// auto-hidden taskbar leaves the characters standing on nothing over a full-screen
    /// app, so they retreat until it comes back.
    /// </summary>
    private bool UpdateEnvironmentVisibility(double now)
    {
        var shouldShow = !_taskbar.IsAutoHide || _taskbar.Rect.Height > 0;

        if (shouldShow == !_hiddenForEnvironment) return shouldShow;

        _hiddenForEnvironment = !shouldShow;

        foreach (var walker in _walkers)
        {
            if (shouldShow) walker.ShowForEnvironmentIfNeeded(now);
            else walker.HideForEnvironment(now);
        }

        return shouldShow;
    }

    // MARK: - Global input

    private void OnGlobalMouseDown(int x, int y)
    {
        UiDispatcher.Invoke(() =>
        {
            foreach (var walker in _walkers)
            {
                if (!walker.IsIdleForPopover) continue;
                if (walker.IsPointInsideWindows(x, y)) continue;

                if (walker.IsOnboarding) walker.CloseOnboarding();
                else walker.ClosePopover();
            }
        });
    }

    // MARK: - Menu actions

    public IReadOnlyList<MonitorGeometry> Monitors => _monitors;

    public void ApplyThemeChange()
    {
        foreach (var walker in _walkers) walker.RebuildPopover();
    }

    public void SetProviderForAll(AgentProviderKind provider)
    {
        foreach (var walker in _walkers) walker.SwitchProvider(provider);
    }

    public void SetSizeForAll(CharacterSize size)
    {
        foreach (var walker in _walkers) walker.Size = size;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CompositionTarget.Rendering -= OnRendering;
        _mouseHook.MouseDown -= OnGlobalMouseDown;
        _mouseHook.Dispose();

        foreach (var walker in _walkers)
        {
            walker.TerminateSession();
            walker.CloseAllWindows();
        }
    }
}
