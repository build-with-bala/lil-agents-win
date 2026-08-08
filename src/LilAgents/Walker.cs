using System.Windows;
using System.Windows.Media;
using LilAgents.Agents;
using LilAgents.Platform;
using LilAgents.Rendering;
using LilAgents.UI;

namespace LilAgents;

public enum CharacterSize { Large, Medium, Small }

public static class CharacterSizeExtensions
{
    /// <summary>Character height in device-independent units.</summary>
    public static double Height(this CharacterSize size) => size switch
    {
        CharacterSize.Large => 200,
        CharacterSize.Medium => 150,
        CharacterSize.Small => 100,
        _ => 200
    };

    public static string DisplayName(this CharacterSize size) => size.ToString();

    public static CharacterSize Parse(string? raw) => raw?.ToLowerInvariant() switch
    {
        "medium" => CharacterSize.Medium,
        "small" => CharacterSize.Small,
        _ => CharacterSize.Large
    };
}

/// <summary>
/// One character: its walk state machine, window, bubble, popover and agent session.
/// Port of the macOS WalkerCharacter.
/// </summary>
public sealed class Walker
{
    // MARK: - Identity and tuning

    public string Name { get; }
    private readonly SpriteSheet _sprites;

    public double AccelStart { get; init; } = 3.0;
    public double FullSpeedStart { get; init; } = 3.75;
    public double DecelStart { get; init; } = 7.5;
    public double WalkStop { get; init; } = 8.25;
    public (double Min, double Max) WalkAmountRange { get; init; } = (0.25, 0.5);
    public double YOffset { get; init; }
    public double FlipXOffset { get; init; }
    public Color CharacterColor { get; init; } = Colors.Gray;

    /// <summary>The walk videos are authored as a ten-second loop.</summary>
    private const double VideoDuration = 10.0;

    /// <summary>Walk distances were tuned against a 500px reference strip.</summary>
    private const double ReferenceWidth = 500.0;

    private const double MinimumSeparation = 0.12;

    // MARK: - Walk state

    public double PositionProgress { get; set; }
    public bool IsWalking { get; private set; }
    public bool IsPaused { get; private set; } = true;
    public double PauseEndTime { get; set; }
    public bool GoingRight { get; private set; } = true;

    private double _walkStartTime;
    private double _walkStartPixel;
    private double _walkEndPixel;
    private double _walkEndProgress;
    private double _currentTravelDistance = 500;

    // MARK: - Windows and session

    private readonly WalkerWindow _window;
    private BubbleWindow? _bubble;
    private PopoverWindow? _popover;
    private IAgentSession? _session;

    public AgentsController? Controller { get; set; }
    public bool IsIdleForPopover { get; private set; }
    public bool IsOnboarding { get; set; }
    public bool IsManuallyVisible { get; private set; } = true;

    private bool _hiddenForEnvironment;
    private double? _environmentHiddenAt;
    private bool _popoverVisibleBeforeHide;
    private bool _bubbleVisibleBeforeHide;

    private double _displayHeightDips;
    private uint _dpi = 96;

    // MARK: - Bubble state

    private static readonly string[] ThinkingPhrases =
    [
        "hmm...", "thinking...", "one sec...", "ok hold on",
        "let me check", "working on it", "almost...", "bear with me",
        "on it!", "gimme a sec", "brb", "processing...",
        "hang tight", "just a moment", "figuring it out",
        "crunching...", "reading...", "looking...",
        "cooking...", "vibing...", "digging in",
        "connecting dots", "give me a sec",
        "don't rush me", "calculating...", "assembling…"
    ];

    private static readonly string[] CompletionPhrases =
    [
        "done!", "all set!", "ready!", "here you go", "got it!",
        "finished!", "ta-da!", "voila!",
        "boom!", "there ya go!", "check it out!"
    ];

    private static readonly string[] CompletionSounds =
    [
        "ping-aa.mp3", "ping-bb.mp3", "ping-cc.mp3", "ping-dd.mp3", "ping-ee.mp3",
        "ping-ff.mp3", "ping-gg.mp3", "ping-hh.mp3", "ping-jj.m4a"
    ];

    private static int _lastSoundIndex = -1;
    private static readonly MediaPlayer SoundPlayer = new();

    private readonly Random _random = new();
    private double _lastPhraseUpdate;
    private string _currentPhrase = "";
    private double _completionBubbleExpiry;
    private bool _showingCompletion;
    private double _bubbleWidth = 80;

    // MARK: - Construction

    public Walker(string name, string spriteFolder)
    {
        Name = name;
        _sprites = SpriteSheet.Load(spriteFolder);
        _displayHeightDips = Size.Height();

        _window = new WalkerWindow(_sprites);
        _window.Clicked += HandleClick;
    }

    public AgentProviderKind Provider
    {
        get => AgentProvider.Parse(Settings.Current.GetProvider(Name));
        set => Settings.Current.SetProvider(Name, value.Serialize());
    }

    public CharacterSize Size
    {
        get => CharacterSizeExtensions.Parse(Settings.Current.GetSize(Name));
        set
        {
            Settings.Current.SetSize(Name, value.ToString().ToLowerInvariant());
            _displayHeightDips = value.Height();
        }
    }

    public double DisplayHeightPixels => DpiHelper.DipsToPixels(_displayHeightDips, _dpi);

    public double DisplayWidthPixels => DisplayHeightPixels * _sprites.AspectRatio;

    public bool IsAgentBusy => _session?.IsBusy ?? false;

    public PopoverTheme ResolvedTheme => PopoverTheme.Current.WithCharacterColor(CharacterColor);

    public void Setup()
    {
        _window.ShowNoActivate();
        _window.SetFrame(0);
        IsManuallyVisible = Settings.Current.GetVisible(Name);
        if (!IsManuallyVisible) _window.Hide();
    }

    // MARK: - Visibility

    public void SetManuallyVisible(bool visible)
    {
        IsManuallyVisible = visible;
        Settings.Current.SetVisible(Name, visible);

        if (visible)
        {
            if (!_hiddenForEnvironment) _window.ShowNoActivate();
        }
        else
        {
            _window.Hide();
            _popover?.Hide();
            _bubble?.Hide();
        }
    }

    public void HideForEnvironment(double now)
    {
        if (_hiddenForEnvironment) return;

        _hiddenForEnvironment = true;
        _environmentHiddenAt = now;
        _popoverVisibleBeforeHide = _popover?.IsVisible ?? false;
        _bubbleVisibleBeforeHide = _bubble?.IsVisible ?? false;

        _window.Hide();
        _popover?.Hide();
        _bubble?.Hide();
    }

    public void ShowForEnvironmentIfNeeded(double now)
    {
        if (!_hiddenForEnvironment) return;

        // Roll every deadline forward by the hidden interval so a character does not
        // resume mid-walk having "missed" several seconds of animation.
        var hiddenDuration = now - (_environmentHiddenAt ?? now);
        _walkStartTime += hiddenDuration;
        PauseEndTime += hiddenDuration;
        _completionBubbleExpiry += hiddenDuration;
        _lastPhraseUpdate += hiddenDuration;

        _hiddenForEnvironment = false;
        _environmentHiddenAt = null;

        if (!IsManuallyVisible) return;

        _window.ShowNoActivate();

        if (IsIdleForPopover && _popoverVisibleBeforeHide && _popover is not null)
        {
            _popover.ShowNoActivate();
            _popover.Activate();
            _popover.Terminal.FocusInput();
        }

        if (_bubbleVisibleBeforeHide) UpdateThinkingBubble(now);
    }

    // MARK: - Click and popover

    private void HandleClick()
    {
        if (IsOnboarding)
        {
            OpenOnboardingPopover();
            return;
        }

        if (IsIdleForPopover) ClosePopover();
        else OpenPopover();
    }

    public void OpenPopover()
    {
        // Only one popover at a time, matching the macOS behaviour.
        if (Controller is not null)
        {
            foreach (var sibling in Controller.Walkers)
            {
                if (!ReferenceEquals(sibling, this) && sibling.IsIdleForPopover) sibling.ClosePopover();
            }
        }

        IsIdleForPopover = true;
        IsWalking = false;
        IsPaused = true;
        _window.SetFrame(0);

        _showingCompletion = false;
        HideBubble();

        if (_session is null)
        {
            var session = Provider.CreateSession();
            _session = session;
            WireSession(session);
            session.Start();
        }

        _popover ??= CreatePopover();

        if (_session is { History.Count: > 0 }) _popover.Terminal.ReplayHistory(_session.History);

        UpdatePopoverPosition();
        _popover.ShowNoActivate();
        _popover.Activate();
        _popover.Terminal.FocusInput();
    }

    public void ClosePopover()
    {
        if (!IsIdleForPopover) return;

        _popover?.Hide();
        IsIdleForPopover = false;

        var now = Controller?.Now ?? 0;

        if (_showingCompletion)
        {
            // Give the user the full three seconds from when they could actually see it.
            _completionBubbleExpiry = now + 3.0;
            ShowBubble(_currentPhrase, isCompletion: true);
        }
        else if (IsAgentBusy)
        {
            _currentPhrase = "";
            _lastPhraseUpdate = 0;
            UpdateThinkingPhrase(now);
            ShowBubble(_currentPhrase, isCompletion: false);
        }

        PauseEndTime = now + RandomBetween(2.0, 5.0);
    }

    private void OpenOnboardingPopover()
    {
        _showingCompletion = false;
        HideBubble();

        IsIdleForPopover = true;
        IsWalking = false;
        IsPaused = true;
        _window.SetFrame(0);

        _popover ??= CreatePopover();
        _popover.Terminal.SetInputEnabled(false);
        _popover.Terminal.ShowStaticMessage(
            """
            hey! we're bruce and jazz — your lil taskbar agents.

            click either of us to open an AI chat. we'll walk around while you work and let you know when your agent's thinking.

            check the tray icon (bottom right) for themes, sounds, and more options.

            click anywhere outside to dismiss, then click us again to start chatting.
            """);

        UpdatePopoverPosition();
        _popover.ShowNoActivate();
    }

    public void CloseOnboarding()
    {
        _popover?.Hide();
        _popover = null;
        IsIdleForPopover = false;
        IsOnboarding = false;
        IsPaused = true;
        PauseEndTime = (Controller?.Now ?? 0) + RandomBetween(1.0, 3.0);
        Controller?.CompleteOnboarding();
    }

    private PopoverWindow CreatePopover()
    {
        var theme = ResolvedTheme;
        var popover = new PopoverWindow(theme, Provider);

        popover.Terminal.MessageSubmitted += message => _session?.Send(message);
        popover.Terminal.ClearRequested += ResetSession;
        popover.RefreshRequested += () =>
        {
            if (!IsOnboarding) ResetSession();
        };
        popover.CopyRequested += () => popover.Terminal.RunSlashCommand("/copy");
        popover.ProviderSelected += SwitchProvider;

        // Escape dismisses, matching the macOS local key monitor on keyCode 53.
        popover.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Escape) return;
            args.Handled = true;
            if (IsOnboarding) CloseOnboarding();
            else ClosePopover();
        };

        return popover;
    }

    public void RebuildPopover()
    {
        var wasOpen = IsIdleForPopover;
        _popover?.Hide();
        _popover = null;
        _bubble?.Hide();
        _bubble = null;

        if (!wasOpen) return;

        _popover = CreatePopover();
        if (_session is { History.Count: > 0 }) _popover.Terminal.ReplayHistory(_session.History);
        UpdatePopoverPosition();
        _popover.ShowNoActivate();
        _popover.Activate();
        _popover.Terminal.FocusInput();
    }

    public void SwitchProvider(AgentProviderKind provider)
    {
        if (provider == Provider) return;

        Provider = provider;
        _session?.Terminate();
        _session = null;

        _popover?.Hide();
        _popover = null;
        _bubble?.Hide();
        _bubble = null;

        IsIdleForPopover = false;
        OpenPopover();
    }

    public void ResetSession()
    {
        _session?.Terminate();
        _session = null;
        _showingCompletion = false;
        _currentPhrase = "";
        _completionBubbleExpiry = 0;
        HideBubble();

        _popover?.Terminal.ResetState();
        _popover?.Terminal.ShowSessionMessage();

        var session = Provider.CreateSession();
        _session = session;
        WireSession(session);
        session.Start();
    }

    public void TerminateSession()
    {
        _session?.Terminate();
        _session = null;
    }

    private void WireSession(IAgentSession session)
    {
        session.TextReceived += text => _popover?.Terminal.AppendStreamingText(text);

        session.TurnCompleted += () =>
        {
            _popover?.Terminal.EndStreaming();
            PlayCompletionSound();
            ShowCompletionBubble();
        };

        session.ErrorReceived += text => _popover?.Terminal.AppendError(text);

        session.ToolUsed += (toolName, input) =>
            _popover?.Terminal.AppendToolUse(toolName, FormatToolInput(input));

        session.ToolResultReceived += (summary, isError) =>
            _popover?.Terminal.AppendToolResult(summary, isError);

        session.ProcessExited += () =>
        {
            _popover?.Terminal.EndStreaming();
            _popover?.Terminal.AppendError($"{Provider.DisplayName()} session ended.");
        };
    }

    private static string FormatToolInput(IReadOnlyDictionary<string, object?> input)
    {
        if (input.TryGetValue("command", out var command) && command is string commandText) return commandText;
        if (input.TryGetValue("file_path", out var path) && path is string pathText) return pathText;
        if (input.TryGetValue("pattern", out var pattern) && pattern is string patternText) return patternText;
        return string.Join(", ", input.Keys.Order().Take(3));
    }

    // MARK: - Geometry

    /// <summary>
    /// Places the character window for the current position.
    ///
    /// The macOS original works in a bottom-left origin space where the window's y is its
    /// bottom edge. Windows is top-left origin, so the same standing position becomes
    /// <c>bandTop - height * 0.85 - yOffset</c>: the character overlaps the taskbar by 15%
    /// of its height, and yOffset keeps its sign meaning "nudge downward".
    /// </summary>
    private void PositionWindow(WalkBand band)
    {
        var width = DisplayWidthPixels;
        var height = DisplayHeightPixels;
        var scale = _dpi / DpiHelper.DefaultDpi;

        var flipCompensation = GoingRight ? 0 : FlipXOffset * scale;
        var x = band.X + _currentTravelDistance * PositionProgress + flipCompensation;
        var y = band.TopY - height * 0.85 - YOffset * scale;

        _window.SetPixelBounds(x, y, width, height);
    }

    public void UpdateDpi(uint dpi) => _dpi = dpi == 0 ? 96 : dpi;

    private void UpdatePopoverPosition()
    {
        if (_popover is null || !IsIdleForPopover) return;
        if (_window.PixelRect is not { } charRect) return;

        var dpi = _dpi;
        var popoverWidth = DpiHelper.DipsToPixels(PopoverWindow.PopoverWidth, dpi);
        var popoverHeight = DpiHelper.DipsToPixels(PopoverWindow.PopoverHeight, dpi);
        var overlap = DpiHelper.DipsToPixels(15, dpi);

        var x = (charRect.Left + charRect.Right) / 2.0 - popoverWidth / 2.0;
        var y = charRect.Top + overlap - popoverHeight;

        if (Controller?.ActiveMonitor is { } monitor)
        {
            var margin = DpiHelper.DipsToPixels(4, dpi);
            x = Math.Clamp(x, monitor.Bounds.Left + margin, monitor.Bounds.Right - popoverWidth - margin);
            y = Math.Max(y, monitor.Bounds.Top + margin);
        }

        _popover.SetPixelPosition(x, y);
    }

    // MARK: - Bubble

    public void UpdateThinkingBubble(double now)
    {
        if (_showingCompletion)
        {
            if (now >= _completionBubbleExpiry)
            {
                _showingCompletion = false;
                HideBubble();
                return;
            }

            if (IsIdleForPopover)
            {
                // Pause the countdown while the popover covers the bubble.
                _completionBubbleExpiry += 1.0 / 60.0;
                HideBubble();
            }
            else
            {
                ShowBubble(_currentPhrase, isCompletion: true);
            }
            return;
        }

        if (IsAgentBusy && !IsIdleForPopover)
        {
            var previous = _currentPhrase;
            UpdateThinkingPhrase(now);

            if (_currentPhrase != previous && previous.Length > 0 && _bubble is { IsVisible: true })
            {
                _bubble.AnimateTo(_currentPhrase, false, width =>
                {
                    _bubbleWidth = width;
                    PositionBubble();
                });
            }
            else
            {
                ShowBubble(_currentPhrase, isCompletion: false);
            }
        }
        else
        {
            HideBubble();
        }
    }

    private void UpdateThinkingPhrase(double now)
    {
        if (_currentPhrase.Length != 0 && now - _lastPhraseUpdate <= RandomBetween(3.0, 5.0)) return;

        var next = ThinkingPhrases[_random.Next(ThinkingPhrases.Length)];
        while (next == _currentPhrase && ThinkingPhrases.Length > 1)
        {
            next = ThinkingPhrases[_random.Next(ThinkingPhrases.Length)];
        }

        _currentPhrase = next;
        _lastPhraseUpdate = now;
    }

    public void ShowCompletionBubble()
    {
        _currentPhrase = CompletionPhrases[_random.Next(CompletionPhrases.Length)];
        _showingCompletion = true;
        _completionBubbleExpiry = (Controller?.Now ?? 0) + 3.0;
        _lastPhraseUpdate = 0;

        if (!IsIdleForPopover) ShowBubble(_currentPhrase, isCompletion: true);
    }

    public void ShowBubble(string text, bool isCompletion)
    {
        if (string.IsNullOrEmpty(text)) return;

        _bubble ??= new BubbleWindow(ResolvedTheme);
        _bubbleWidth = _bubble.SetText(text, isCompletion);
        PositionBubble();

        if (!_bubble.IsVisible) _bubble.ShowNoActivate();
    }

    private void PositionBubble()
    {
        if (_bubble is null) return;
        if (_window.PixelRect is not { } charRect) return;

        var widthPixels = DpiHelper.DipsToPixels(_bubbleWidth, _dpi);
        var heightPixels = DpiHelper.DipsToPixels(BubbleWindow.BubbleHeight, _dpi);

        var x = (charRect.Left + charRect.Right) / 2.0 - widthPixels / 2.0;
        // macOS anchors the bubble's bottom 88% of the way up from the character's feet;
        // in top-down coordinates that is 12% down from its head.
        var y = charRect.Top + charRect.Height * 0.12 - heightPixels;

        _bubble.SetPixelBounds(x, y, widthPixels, heightPixels);
    }

    private void HideBubble()
    {
        if (_bubble is { IsVisible: true }) _bubble.Hide();
    }

    public void PlayCompletionSound()
    {
        if (!Settings.Current.SoundsEnabled) return;

        int index;
        do
        {
            index = _random.Next(CompletionSounds.Length);
        } while (index == _lastSoundIndex && CompletionSounds.Length > 1);

        _lastSoundIndex = index;

        var path = EmbeddedAssets.ExtractSound(CompletionSounds[index]);
        if (path is null) return;

        try
        {
            SoundPlayer.Open(new Uri(path));
            SoundPlayer.Play();
        }
        catch (Exception exception) when (exception is UriFormatException or InvalidOperationException)
        {
        }
    }

    // MARK: - Walking

    public void StartWalk(double now)
    {
        IsPaused = false;
        IsWalking = true;
        _walkStartTime = now;

        GoingRight = PositionProgress switch
        {
            > 0.85 => false,
            < 0.15 => true,
            _ => _random.Next(2) == 0
        };

        var walkStartProgress = PositionProgress;
        var walkPixels = RandomBetween(WalkAmountRange.Min, WalkAmountRange.Max) * ReferenceWidth;
        var walkAmount = _currentTravelDistance > 0 ? walkPixels / _currentTravelDistance : 0.3;

        _walkEndProgress = GoingRight
            ? Math.Min(walkStartProgress + walkAmount, 1.0)
            : Math.Max(walkStartProgress - walkAmount, 0.0);

        // Keep the characters from ending up stacked on top of each other.
        if (Controller is not null)
        {
            foreach (var sibling in Controller.Walkers)
            {
                if (ReferenceEquals(sibling, this)) continue;
                var siblingPosition = sibling.PositionProgress;
                if (Math.Abs(_walkEndProgress - siblingPosition) >= MinimumSeparation) continue;

                _walkEndProgress = GoingRight
                    ? Math.Max(walkStartProgress, siblingPosition - MinimumSeparation)
                    : Math.Min(walkStartProgress, siblingPosition + MinimumSeparation);
            }
        }

        // Endpoints are kept in pixels so the walk keeps a constant speed even if the
        // band is recomputed mid-stride (monitor change, taskbar resize).
        _walkStartPixel = walkStartProgress * _currentTravelDistance;
        _walkEndPixel = _walkEndProgress * _currentTravelDistance;

        _window.SetFacingRight(GoingRight);
    }

    public void EnterPause(double now)
    {
        IsWalking = false;
        IsPaused = true;
        _window.SetFrame(0);
        PauseEndTime = now + RandomBetween(5.0, 12.0);
    }

    public void Update(double now, WalkBand band)
    {
        _currentTravelDistance = Math.Max(band.Width - DisplayWidthPixels, 0);

        if (IsIdleForPopover)
        {
            PositionWindow(band);
            UpdatePopoverPosition();
            UpdateThinkingBubble(now);
            return;
        }

        if (IsPaused)
        {
            if (now >= PauseEndTime)
            {
                StartWalk(now);
            }
            else
            {
                PositionWindow(band);
                UpdateThinkingBubble(now);
                return;
            }
        }

        if (IsWalking)
        {
            var elapsed = now - _walkStartTime;
            var curve = new MovementCurve(AccelStart, FullSpeedStart, DecelStart, WalkStop);
            var normalised = elapsed >= VideoDuration ? 1.0 : curve.Evaluate(Math.Min(elapsed, VideoDuration));
            var currentPixel = _walkStartPixel + (_walkEndPixel - _walkStartPixel) * normalised;

            if (_currentTravelDistance > 0)
            {
                PositionProgress = Math.Clamp(currentPixel / _currentTravelDistance, 0, 1);
            }

            _window.SetFrame(_sprites.FrameIndexAt(elapsed));

            if (elapsed >= VideoDuration)
            {
                _walkEndProgress = PositionProgress;
                EnterPause(now);
                PositionWindow(band);
                UpdateThinkingBubble(now);
                return;
            }

            PositionWindow(band);
        }

        UpdateThinkingBubble(now);
    }

    public void ReassertTopmost()
    {
        _window.ReassertTopmost();
        if (_bubble is { IsVisible: true }) _bubble.ReassertTopmost();
        if (_popover is { IsVisible: true }) _popover.ReassertTopmost();
    }

    public bool IsPointInsideWindows(int x, int y) =>
        _window.ContainsScreenPoint(x, y) || (_popover?.ContainsScreenPoint(x, y) ?? false);

    public void CloseAllWindows()
    {
        _window.Close();
        _bubble?.Close();
        _popover?.Close();
    }

    private double RandomBetween(double min, double max) => min + _random.NextDouble() * (max - min);
}
