using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LilAgents.Agents;

namespace LilAgents.UI;

/// <summary>
/// The themed chat surface inside the popover: a read-only transcript plus an input line.
///
/// Built in code rather than XAML because every visual property is theme-driven and gets
/// rebuilt when the user switches style — a static XAML tree would just be a place to
/// hang bindings that all point back here anyway.
/// </summary>
public sealed class TerminalControl : UserControl
{
    private readonly RichTextBox _transcript;
    private readonly TextBox _input;
    private readonly TextBlock _placeholder;
    private readonly Paragraph _paragraph;
    private readonly MarkdownRenderer _markdown;

    private string _currentAssistantText = "";
    private string _lastAssistantText = "";
    private bool _isStreaming;
    private bool _showingSessionMessage;

    public event Action<string>? MessageSubmitted;
    public event Action? ClearRequested;

    public PopoverTheme Theme { get; }

    private AgentProviderKind _provider;

    public AgentProviderKind Provider
    {
        get => _provider;
        set
        {
            _provider = value;
            _placeholder.Text = value.InputPlaceholder();
        }
    }

    public TerminalControl(PopoverTheme theme, AgentProviderKind provider)
    {
        Theme = theme;
        _markdown = new MarkdownRenderer(theme);

        _paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = theme.FontSize * 1.45,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        };

        _transcript = new RichTextBox
        {
            Document = new FlowDocument(_paragraph)
            {
                PagePadding = new Thickness(2, 4, 2, 4),
                Background = Brushes.Transparent
            },
            IsReadOnly = true,
            IsDocumentEnabled = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = theme.Brush(theme.TextPrimary),
            FontFamily = theme.FontFamily,
            FontSize = theme.FontSize,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0)
        };

        _input = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = theme.Brush(theme.TextPrimary),
            CaretBrush = theme.Brush(theme.TextPrimary),
            FontFamily = theme.FontFamily,
            FontSize = theme.FontSize,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 2, 8, 2),
            AcceptsReturn = false
        };
        _input.KeyDown += OnInputKeyDown;
        _input.TextChanged += (_, _) => UpdatePlaceholderVisibility();

        _placeholder = new TextBlock
        {
            Text = provider.InputPlaceholder(),
            Foreground = theme.Brush(theme.TextDim),
            FontFamily = theme.FontFamily,
            FontSize = theme.FontSize,
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var inputShell = new Border
        {
            Background = theme.Brush(theme.InputBackground),
            CornerRadius = new CornerRadius(theme.InputCornerRadius),
            Height = 30,
            Margin = new Thickness(10, 6, 10, 8),
            Child = new Grid { Children = { _placeholder, _input } }
        };

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        var transcriptHost = new Border
        {
            Margin = new Thickness(10, 8, 10, 0),
            Child = _transcript
        };
        Grid.SetRow(transcriptHost, 0);
        Grid.SetRow(inputShell, 1);
        layout.Children.Add(transcriptHost);
        layout.Children.Add(inputShell);

        Content = layout;
        _provider = provider;
    }

    public void FocusInput() => _input.Focus();

    public void SetInputEnabled(bool enabled)
    {
        _input.IsEnabled = enabled;
        _placeholder.Visibility = enabled && _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePlaceholderVisibility()
    {
        _placeholder.Visibility = _input.Text.Length == 0 && _input.IsEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // MARK: - Input

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        var text = _input.Text.Trim();
        if (text.Length == 0) return;
        _input.Clear();

        if (HandleSlashCommand(text)) return;

        if (_showingSessionMessage)
        {
            _paragraph.Inlines.Clear();
            _showingSessionMessage = false;
        }

        AppendUser(text);
        _isStreaming = true;
        _currentAssistantText = "";
        _markdown.Reset();
        MessageSubmitted?.Invoke(text);
    }

    public void RunSlashCommand(string command) => HandleSlashCommand(command);

    private bool HandleSlashCommand(string text)
    {
        if (!text.StartsWith('/')) return false;

        switch (text.Trim().ToLowerInvariant())
        {
            case "/clear":
                ResetState();
                ClearRequested?.Invoke();
                return true;

            case "/copy":
            {
                var toCopy = string.IsNullOrEmpty(_lastAssistantText) ? "nothing to copy yet" : _lastAssistantText;
                try
                {
                    Clipboard.SetText(toCopy);
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // Another process can hold the clipboard open; not worth surfacing.
                }
                AppendPlain("  ✓ copied to clipboard\n", Theme.SuccessColor);
                return true;
            }

            case "/help":
                AppendHelp();
                return true;

            default:
                AppendPlain($"  unknown command: {text} (try /help)\n", Theme.ErrorColor);
                return true;
        }
    }

    private void AppendHelp()
    {
        AddInline(new Run("  lil agents — slash commands\n")
        {
            FontWeight = Theme.BoldFontWeight,
            Foreground = Theme.Brush(Theme.AccentColor)
        });

        void Row(string command, string description)
        {
            AddInline(new Run($"  {command}  ")
            {
                FontWeight = Theme.BoldFontWeight,
                Foreground = Theme.Brush(Theme.TextPrimary)
            });
            AddInline(new Run(description + "\n") { Foreground = Theme.Brush(Theme.TextDim) });
        }

        Row("/clear", "clear chat history");
        Row("/copy ", "copy last response");
        Row("/help ", "show this message");
        ScrollToBottom();
    }

    // MARK: - Transcript

    public void ResetState()
    {
        _isStreaming = false;
        _currentAssistantText = "";
        _lastAssistantText = "";
        _showingSessionMessage = false;
        _markdown.Reset();
        _paragraph.Inlines.Clear();
    }

    public void ShowSessionMessage()
    {
        _paragraph.Inlines.Clear();
        AddInline(new Run("  ✦ new session\n") { Foreground = Theme.Brush(Theme.AccentColor) });
        _showingSessionMessage = true;
    }

    public void ShowStaticMessage(string message)
    {
        _paragraph.Inlines.Clear();
        AddInline(new Run(message) { Foreground = Theme.Brush(Theme.TextPrimary) });
        ScrollToBottom();
    }

    private void EnsureNewline()
    {
        if (_paragraph.Inlines.Count == 0) return;
        if (_paragraph.Inlines.LastInline is Run { Text.Length: > 0 } run && !run.Text.EndsWith('\n'))
        {
            AddInline(new Run("\n"));
        }
    }

    public void AppendUser(string text)
    {
        EnsureNewline();
        AddInline(new Run("\n> ")
        {
            FontWeight = Theme.BoldFontWeight,
            Foreground = Theme.Brush(Theme.AccentColor)
        });
        AddInline(new Run(text + "\n")
        {
            FontWeight = Theme.BoldFontWeight,
            Foreground = Theme.Brush(Theme.TextPrimary)
        });
        ScrollToBottom();
    }

    public void AppendStreamingText(string text)
    {
        var cleaned = text;
        // Providers like to open a turn with blank lines; drop them so the reply starts
        // flush against the prompt.
        if (_currentAssistantText.Length == 0) cleaned = cleaned.TrimStart('\n');

        _currentAssistantText += cleaned;
        if (cleaned.Length == 0) return;

        foreach (var inline in _markdown.Render(cleaned)) _paragraph.Inlines.Add(inline);
        ScrollToBottom();
    }

    public void EndStreaming()
    {
        if (!_isStreaming) return;
        _isStreaming = false;
        if (_currentAssistantText.Length > 0) _lastAssistantText = _currentAssistantText;
        _currentAssistantText = "";
    }

    public void AppendError(string text) => AppendPlain(text + "\n", Theme.ErrorColor);

    public void AppendToolUse(string toolName, string summary)
    {
        EndStreaming();
        AddInline(new Run($"  {toolName.ToUpperInvariant()} ")
        {
            FontWeight = Theme.BoldFontWeight,
            Foreground = Theme.Brush(Theme.AccentColor)
        });
        AddInline(new Run(summary + "\n") { Foreground = Theme.Brush(Theme.TextDim) });
        ScrollToBottom();
    }

    public void AppendToolResult(string summary, bool isError)
    {
        AddInline(new Run(isError ? "  FAIL " : "  DONE ")
        {
            FontWeight = Theme.BoldFontWeight,
            Foreground = Theme.Brush(isError ? Theme.ErrorColor : Theme.SuccessColor)
        });
        AddInline(new Run(summary + "\n") { Foreground = Theme.Brush(Theme.TextDim) });
        ScrollToBottom();
    }

    public void ReplayHistory(IReadOnlyList<AgentMessage> messages)
    {
        _paragraph.Inlines.Clear();
        _markdown.Reset();

        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case MessageRole.User:
                    AppendUser(message.Text);
                    break;

                case MessageRole.Assistant:
                    foreach (var inline in _markdown.Render(message.Text + "\n")) _paragraph.Inlines.Add(inline);
                    _lastAssistantText = message.Text;
                    break;

                case MessageRole.Error:
                    AppendError(message.Text);
                    break;

                case MessageRole.ToolUse:
                    AppendPlain($"  {message.Text}\n", Theme.AccentColor);
                    break;

                case MessageRole.ToolResult:
                    var isError = message.Text.StartsWith("ERROR:", StringComparison.Ordinal);
                    AppendPlain($"  {message.Text}\n", isError ? Theme.ErrorColor : Theme.SuccessColor);
                    break;
            }
        }

        ScrollToBottom();
    }

    private void AppendPlain(string text, Color color)
    {
        AddInline(new Run(text) { Foreground = Theme.Brush(color) });
        ScrollToBottom();
    }

    private void AddInline(Run run)
    {
        run.FontFamily ??= Theme.FontFamily;
        if (double.IsNaN(run.FontSize) || run.FontSize <= 0) run.FontSize = Theme.FontSize;
        _paragraph.Inlines.Add(run);
    }

    private void ScrollToBottom() => _transcript.ScrollToEnd();
}
