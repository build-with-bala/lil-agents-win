using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LilAgents.UI;

/// <summary>
/// The little speech bubble above a character: thinking phrases while a turn runs,
/// then a completion phrase when it lands.
/// </summary>
public sealed class BubbleWindow : OverlayWindow
{
    private readonly Border _shell;
    private readonly TextBlock _label;
    private PopoverTheme _theme;
    private bool _animating;

    public const double BubbleHeight = 26;

    public BubbleWindow(PopoverTheme theme)
    {
        _theme = theme;

        _label = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            FontFamily = theme.FontFamily,
            FontSize = theme.BubbleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = theme.Brush(theme.BubbleText)
        };

        _shell = new Border
        {
            Background = theme.Brush(theme.BubbleBackground),
            BorderBrush = theme.Brush(theme.BubbleBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(theme.BubbleCornerRadius),
            Child = _label,
            // A soft drop shadow so the bubble reads against a busy wallpaper.
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 1,
                Opacity = 0.25,
                Color = Colors.Black
            }
        };

        Content = _shell;
        Height = BubbleHeight;
        Width = 80;
    }

    public void ApplyTheme(PopoverTheme theme)
    {
        _theme = theme;
        _label.FontFamily = theme.FontFamily;
        _label.FontSize = theme.BubbleFontSize;
        _shell.Background = theme.Brush(theme.BubbleBackground);
        _shell.CornerRadius = new CornerRadius(theme.BubbleCornerRadius);
    }

    /// <summary>Sets the text and returns the width the bubble wants, in DIPs.</summary>
    public double SetText(string text, bool isCompletion)
    {
        _label.Text = text;
        _label.Foreground = _theme.Brush(isCompletion ? _theme.BubbleCompletionText : _theme.BubbleText);
        _shell.BorderBrush = _theme.Brush(isCompletion ? _theme.BubbleCompletionBorder : _theme.BubbleBorder);

        _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(Math.Ceiling(_label.DesiredSize.Width) + 32, 48);
    }

    /// <summary>Cross-fades to a new phrase, mirroring the macOS 0.2s out / 0.25s in.</summary>
    public void AnimateTo(string text, bool isCompletion, Action<double> applyWidth)
    {
        if (_animating)
        {
            applyWidth(SetText(text, isCompletion));
            return;
        }

        _animating = true;

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromSeconds(0.2));
        fadeOut.Completed += (_, _) =>
        {
            applyWidth(SetText(text, isCompletion));
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(0.25));
            fadeIn.Completed += (_, _) => _animating = false;
            _label.BeginAnimation(OpacityProperty, fadeIn);
        };

        _label.BeginAnimation(OpacityProperty, fadeOut);
    }
}
