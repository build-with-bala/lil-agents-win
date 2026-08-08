using System.Windows;
using System.Windows.Media;
using LilAgents.Agents;

namespace LilAgents.UI;

/// <summary>
/// The four visual styles, ported colour-for-colour from the macOS build.
///
/// Font substitutions are the only intentional divergence: SF Mono, Chicago and Geneva
/// do not exist on Windows, so each falls back to the nearest shipped equivalent
/// (Cascadia Mono for the mono faces, Segoe UI for the system faces).
/// </summary>
public sealed record PopoverTheme
{
    public required string Name { get; init; }

    // Popover shell
    public required Color PopoverBackground { get; init; }
    public required Color PopoverBorder { get; init; }
    public required double PopoverBorderWidth { get; init; }
    public required double PopoverCornerRadius { get; init; }
    public required Color TitleBarBackground { get; init; }
    public required Color TitleText { get; init; }
    public required FontFamily TitleFontFamily { get; init; }
    public required double TitleFontSize { get; init; }
    public required FontWeight TitleFontWeight { get; init; }
    public required TitleFormat TitleFormat { get; init; }
    public required Color SeparatorColor { get; init; }

    // Terminal
    public required FontFamily FontFamily { get; init; }
    public required double FontSize { get; init; }
    public required FontWeight FontWeight { get; init; }
    public required FontWeight BoldFontWeight { get; init; }
    public required Color TextPrimary { get; init; }
    public required Color TextDim { get; init; }
    public required Color AccentColor { get; init; }
    public required Color ErrorColor { get; init; }
    public required Color SuccessColor { get; init; }
    public required Color InputBackground { get; init; }
    public required double InputCornerRadius { get; init; }

    // Thinking bubble
    public required Color BubbleBackground { get; init; }
    public required Color BubbleBorder { get; init; }
    public required Color BubbleText { get; init; }
    public required Color BubbleCompletionBorder { get; init; }
    public required Color BubbleCompletionText { get; init; }
    public required double BubbleFontSize { get; init; }
    public required double BubbleCornerRadius { get; init; }

    public string TitleString(AgentProviderKind provider) => provider.TitleString(TitleFormat);

    // MARK: - Font families

    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");
    private static readonly FontFamily SystemFont = new("Segoe UI");
    private static readonly FontFamily RoundedFont = new("Segoe UI Variable Display, Segoe UI");
    private static readonly FontFamily RetroFont = new("Tahoma, Segoe UI");

    private static Color Rgb(double r, double g, double b, double a = 1.0) =>
        Color.FromArgb(
            (byte)Math.Round(a * 255),
            (byte)Math.Round(r * 255),
            (byte)Math.Round(g * 255),
            (byte)Math.Round(b * 255));

    private static Color Gray(double white, double a = 1.0) => Rgb(white, white, white, a);

    // MARK: - Presets

    public static readonly PopoverTheme Peach = new()
    {
        Name = "Peach",
        PopoverBackground = Rgb(1.0, 0.97, 0.92, 0.97),
        PopoverBorder = Rgb(0.95, 0.55, 0.65, 0.8),
        PopoverBorderWidth = 2.5,
        PopoverCornerRadius = 24,
        TitleBarBackground = Rgb(0.98, 0.93, 0.88),
        TitleText = Rgb(0.85, 0.35, 0.45),
        TitleFontFamily = RoundedFont,
        TitleFontSize = 13,
        TitleFontWeight = FontWeights.Heavy,
        TitleFormat = TitleFormat.LowercaseTilde,
        SeparatorColor = Rgb(0.95, 0.55, 0.65, 0.25),
        FontFamily = RoundedFont,
        FontSize = 13,
        FontWeight = FontWeights.Normal,
        BoldFontWeight = FontWeights.SemiBold,
        TextPrimary = Rgb(0.2, 0.18, 0.22),
        TextDim = Rgb(0.5, 0.47, 0.52),
        AccentColor = Rgb(0.85, 0.35, 0.45),
        ErrorColor = Rgb(0.9, 0.3, 0.2),
        SuccessColor = Rgb(0.3, 0.72, 0.5),
        InputBackground = Rgb(1.0, 0.98, 0.95),
        InputCornerRadius = 14,
        BubbleBackground = Rgb(1.0, 0.95, 0.90, 0.95),
        BubbleBorder = Rgb(0.95, 0.55, 0.65, 0.6),
        BubbleText = Rgb(0.55, 0.5, 0.52),
        BubbleCompletionBorder = Rgb(0.3, 0.75, 0.5, 0.7),
        BubbleCompletionText = Rgb(0.2, 0.6, 0.4),
        BubbleFontSize = 11.5,
        BubbleCornerRadius = 14
    };

    public static readonly PopoverTheme Midnight = new()
    {
        Name = "Midnight",
        PopoverBackground = Rgb(0.07, 0.07, 0.07, 0.96),
        PopoverBorder = Rgb(1.0, 0.4, 0.0, 0.7),
        PopoverBorderWidth = 1.5,
        PopoverCornerRadius = 12,
        TitleBarBackground = Rgb(0.1, 0.1, 0.1),
        TitleText = Rgb(1.0, 0.4, 0.0),
        TitleFontFamily = MonoFont,
        TitleFontSize = 11,
        TitleFontWeight = FontWeights.Bold,
        TitleFormat = TitleFormat.Uppercase,
        SeparatorColor = Rgb(1.0, 0.4, 0.0, 0.3),
        FontFamily = MonoFont,
        FontSize = 12.5,
        FontWeight = FontWeights.Normal,
        BoldFontWeight = FontWeights.Medium,
        TextPrimary = Colors.White,
        TextDim = Gray(0.6),
        AccentColor = Rgb(1.0, 0.4, 0.0),
        ErrorColor = Rgb(1.0, 0.3, 0.2),
        SuccessColor = Rgb(0.4, 0.65, 0.4),
        InputBackground = Rgb(0.12, 0.12, 0.12),
        InputCornerRadius = 4,
        BubbleBackground = Rgb(0.1, 0.1, 0.1, 0.92),
        BubbleBorder = Rgb(1.0, 0.4, 0.0, 0.6),
        BubbleText = Gray(0.7),
        BubbleCompletionBorder = Rgb(0.3, 0.8, 0.3, 0.7),
        BubbleCompletionText = Rgb(0.3, 0.85, 0.3),
        BubbleFontSize = 10.5,
        BubbleCornerRadius = 12
    };

    public static readonly PopoverTheme Cloud = new()
    {
        Name = "Cloud",
        PopoverBackground = Rgb(0.94, 0.95, 0.96, 0.98),
        PopoverBorder = Rgb(0.78, 0.80, 0.84, 0.6),
        PopoverBorderWidth = 1,
        PopoverCornerRadius = 16,
        TitleBarBackground = Rgb(0.88, 0.90, 0.93),
        TitleText = Rgb(0.3, 0.3, 0.35),
        TitleFontFamily = SystemFont,
        TitleFontSize = 13,
        TitleFontWeight = FontWeights.SemiBold,
        TitleFormat = TitleFormat.LowercaseTilde,
        SeparatorColor = Rgb(0.8, 0.82, 0.85, 0.4),
        FontFamily = SystemFont,
        FontSize = 13,
        FontWeight = FontWeights.Normal,
        BoldFontWeight = FontWeights.SemiBold,
        TextPrimary = Rgb(0.15, 0.15, 0.2),
        TextDim = Rgb(0.5, 0.5, 0.55),
        AccentColor = Rgb(0.0, 0.47, 0.84),
        ErrorColor = Rgb(0.85, 0.2, 0.15),
        SuccessColor = Rgb(0.2, 0.65, 0.3),
        InputBackground = Colors.White,
        InputCornerRadius = 8,
        BubbleBackground = Rgb(0.94, 0.95, 0.97, 0.95),
        BubbleBorder = Rgb(0.0, 0.47, 0.84, 0.4),
        BubbleText = Rgb(0.45, 0.47, 0.52),
        BubbleCompletionBorder = Rgb(0.2, 0.7, 0.3, 0.6),
        BubbleCompletionText = Rgb(0.15, 0.55, 0.2),
        BubbleFontSize = 10.5,
        BubbleCornerRadius = 12
    };

    public static readonly PopoverTheme Moss = new()
    {
        Name = "Moss",
        PopoverBackground = Rgb(0.82, 0.84, 0.78, 0.98),
        PopoverBorder = Rgb(0.55, 0.58, 0.50, 0.8),
        PopoverBorderWidth = 2,
        PopoverCornerRadius = 10,
        TitleBarBackground = Rgb(0.72, 0.75, 0.68),
        TitleText = Rgb(0.15, 0.17, 0.12),
        TitleFontFamily = RetroFont,
        TitleFontSize = 12,
        TitleFontWeight = FontWeights.Bold,
        TitleFormat = TitleFormat.Capitalized,
        SeparatorColor = Rgb(0.55, 0.58, 0.50, 0.5),
        FontFamily = RetroFont,
        FontSize = 12,
        FontWeight = FontWeights.Normal,
        BoldFontWeight = FontWeights.Bold,
        TextPrimary = Rgb(0.1, 0.12, 0.08),
        TextDim = Rgb(0.35, 0.38, 0.30),
        AccentColor = Rgb(0.2, 0.22, 0.15),
        ErrorColor = Rgb(0.6, 0.15, 0.1),
        SuccessColor = Rgb(0.15, 0.4, 0.15),
        InputBackground = Rgb(0.88, 0.90, 0.84),
        InputCornerRadius = 3,
        BubbleBackground = Rgb(0.82, 0.84, 0.78, 0.95),
        BubbleBorder = Rgb(0.55, 0.58, 0.50, 0.7),
        BubbleText = Rgb(0.4, 0.42, 0.38),
        BubbleCompletionBorder = Rgb(0.2, 0.5, 0.2, 0.7),
        BubbleCompletionText = Rgb(0.15, 0.4, 0.15),
        BubbleFontSize = 10.5,
        BubbleCornerRadius = 8
    };

    public static readonly IReadOnlyList<PopoverTheme> AllThemes = [Peach, Midnight, Cloud, Moss];

    public static PopoverTheme Current
    {
        get
        {
            var saved = Settings.Current.ThemeName;
            foreach (var theme in AllThemes)
            {
                if (theme.Name == saved) return theme;
            }
            return Peach;
        }
        set
        {
            Settings.Current.ThemeName = value.Name;
            Settings.Current.Save();
        }
    }

    // MARK: - Modifiers

    /// <summary>
    /// Tints the popover with the character's own colour. Only Peach opts in, matching
    /// the macOS build — the other three are deliberately monochrome.
    /// </summary>
    public PopoverTheme WithCharacterColor(Color color)
    {
        if (Name != "Peach") return this;

        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var light = Rgb(Math.Min(r + 0.4, 1), Math.Min(g + 0.4, 1), Math.Min(b + 0.4, 1), 0.25);
        var border = Color.FromArgb((byte)(0.6 * 255), color.R, color.G, color.B);

        return this with
        {
            PopoverBorder = border,
            TitleBarBackground = Rgb(Math.Min(r * 0.3 + 0.7, 1), Math.Min(g * 0.3 + 0.7, 1), Math.Min(b * 0.3 + 0.7, 1)),
            TitleText = color,
            SeparatorColor = light,
            AccentColor = color,
            BubbleBackground = Rgb(Math.Min(r * 0.15 + 0.85, 1), Math.Min(g * 0.15 + 0.85, 1),
                Math.Min(b * 0.15 + 0.85, 1), 0.95),
            BubbleBorder = border
        };
    }

    public SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
