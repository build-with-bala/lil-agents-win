using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LilAgents.Agents;

namespace LilAgents.UI;

/// <summary>
/// The chat popover: title bar with provider switcher, refresh and copy, over a
/// <see cref="TerminalControl"/>.
/// </summary>
public sealed class PopoverWindow : OverlayWindow
{
    public const double PopoverWidth = 420;
    public const double PopoverHeight = 310;
    private const double TitleBarHeight = 28;

    private readonly PopoverTheme _theme;
    private readonly TextBlock _titleText;

    public TerminalControl Terminal { get; }

    public event Action<AgentProviderKind>? ProviderSelected;
    public event Action? RefreshRequested;
    public event Action? CopyRequested;

    /// <summary>The popover is the one overlay that must take focus — it has a text field.</summary>
    protected override bool CanActivate => true;

    public PopoverWindow(PopoverTheme theme, AgentProviderKind provider)
    {
        _theme = theme;
        Width = PopoverWidth;
        Height = PopoverHeight;
        ShowActivated = true;

        Terminal = new TerminalControl(theme, provider);

        _titleText = new TextBlock
        {
            Text = theme.TitleString(provider),
            FontFamily = theme.TitleFontFamily,
            FontSize = theme.TitleFontSize,
            FontWeight = theme.TitleFontWeight,
            Foreground = theme.Brush(theme.TitleText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var providerButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    _titleText,
                    new TextBlock
                    {
                        Text = " ⌄",
                        FontSize = theme.TitleFontSize,
                        Foreground = theme.Brush(theme.TitleText),
                        Opacity = 0.75,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            },
            Style = FlatButtonStyle(theme),
            Padding = new Thickness(12, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Switch provider"
        };
        providerButton.Click += (_, _) => ShowProviderMenu(providerButton);

        var refreshButton = IconButton("⟳", "New session", theme);
        refreshButton.Click += (_, _) => RefreshRequested?.Invoke();

        var copyButton = IconButton("⧉", "Copy last response", theme);
        copyButton.Click += (_, _) => CopyRequested?.Invoke();

        var titleBar = new Grid
        {
            Height = TitleBarHeight,
            Background = theme.Brush(theme.TitleBarBackground),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        Grid.SetColumn(providerButton, 0);
        Grid.SetColumn(refreshButton, 1);
        Grid.SetColumn(copyButton, 2);
        titleBar.Children.Add(providerButton);
        titleBar.Children.Add(refreshButton);
        titleBar.Children.Add(copyButton);

        var separator = new Border
        {
            Height = 1,
            Background = theme.Brush(theme.SeparatorColor)
        };

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            }
        };
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(separator, 1);
        Grid.SetRow(Terminal, 2);
        layout.Children.Add(titleBar);
        layout.Children.Add(separator);
        layout.Children.Add(Terminal);

        Content = new Border
        {
            Background = theme.Brush(theme.PopoverBackground),
            BorderBrush = theme.Brush(theme.PopoverBorder),
            BorderThickness = new Thickness(theme.PopoverBorderWidth),
            CornerRadius = new CornerRadius(theme.PopoverCornerRadius),
            // Clip the title bar's square corners to the shell's rounded ones.
            ClipToBounds = true,
            Child = layout,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 2,
                Opacity = 0.3,
                Color = Colors.Black
            }
        };
    }

    public void SetTitle(AgentProviderKind provider) => _titleText.Text = _theme.TitleString(provider);

    private void ShowProviderMenu(UIElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

        foreach (var provider in AgentProvider.All)
        {
            var item = new MenuItem
            {
                Header = provider.DisplayName(),
                IsCheckable = true,
                IsChecked = _titleText.Text == _theme.TitleString(provider),
                IsEnabled = provider.IsAvailable()
            };
            var captured = provider;
            item.Click += (_, _) => ProviderSelected?.Invoke(captured);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private static Button IconButton(string glyph, string tooltip, PopoverTheme theme)
    {
        return new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontSize = 13,
                Foreground = theme.Brush(theme.TitleText),
                Opacity = 0.75
            },
            Style = FlatButtonStyle(theme),
            Width = 26,
            ToolTip = tooltip
        };
    }

    /// <summary>
    /// Strips the default WPF chrome. Without this every title-bar control renders as a
    /// grey Aero-era push button, which fits none of the four themes.
    /// </summary>
    private static Style FlatButtonStyle(PopoverTheme theme)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(CursorProperty, System.Windows.Input.Cursors.Hand));

        var template = new ControlTemplate(typeof(Button));
        var presenterFactory = new FrameworkElementFactory(typeof(Border));
        presenterFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        presenterFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        presenterFactory.AppendChild(content);

        template.VisualTree = presenterFactory;
        style.Setters.Add(new Setter(Control.TemplateProperty, template));

        return style;
    }
}
