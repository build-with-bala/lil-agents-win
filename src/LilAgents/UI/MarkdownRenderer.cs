using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace LilAgents.UI;

/// <summary>
/// The small Markdown subset the terminal understands: fenced and inline code, three
/// heading levels, bullets, bold, links and bare URLs.
///
/// Stateful across calls, unlike the macOS original. Upstream re-enters its renderer per
/// streaming chunk with <c>inCodeBlock</c> reset each time, so a fence that straddles a
/// chunk boundary loses its formatting. Holding the flag on the instance fixes that, and
/// costs nothing — <see cref="Reset"/> clears it when a new turn starts.
/// </summary>
public sealed class MarkdownRenderer(PopoverTheme theme)
{
    private bool _inCodeBlock;

    public PopoverTheme Theme { get; set; } = theme;

    public void Reset() => _inCodeBlock = false;

    public IReadOnlyList<Inline> Render(string text)
    {
        var inlines = new List<Inline>();
        if (string.IsNullOrEmpty(text)) return inlines;

        var lines = text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var suffix = i < lines.Length - 1 ? "\n" : "";

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                _inCodeBlock = !_inCodeBlock;
                continue;
            }

            if (_inCodeBlock)
            {
                // Uses `suffix`, not a bare "\n": splitting "print(1)\n" yields a trailing
                // empty element that means "the text ended with a newline", not "a blank
                // line follows". Emitting \n for it would double every line break when the
                // block arrives across several streaming chunks.
                var code = line + suffix;
                if (code.Length > 0) inlines.Add(CodeRun(code));
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                inlines.Add(HeadingRun(line[4..] + suffix, Theme.FontSize));
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inlines.Add(HeadingRun(line[3..] + suffix, Theme.FontSize + 1));
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                inlines.Add(HeadingRun(line[2..] + suffix, Theme.FontSize + 2));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                inlines.Add(new Run("  • ")
                {
                    FontFamily = Theme.FontFamily,
                    FontSize = Theme.FontSize,
                    Foreground = Theme.Brush(Theme.AccentColor)
                });
                inlines.AddRange(RenderInline(line[2..] + suffix));
            }
            else
            {
                inlines.AddRange(RenderInline(line + suffix));
            }
        }

        return inlines;
    }

    // MARK: - Inline parsing

    internal IReadOnlyList<Inline> RenderInline(string text)
    {
        var inlines = new List<Inline>();
        var plain = new System.Text.StringBuilder();

        void FlushPlain()
        {
            if (plain.Length == 0) return;
            inlines.Add(new Run(plain.ToString())
            {
                FontFamily = Theme.FontFamily,
                FontSize = Theme.FontSize,
                FontWeight = Theme.FontWeight,
                Foreground = Theme.Brush(Theme.TextPrimary)
            });
            plain.Clear();
        }

        var i = 0;
        while (i < text.Length)
        {
            // `inline code`
            if (text[i] == '`')
            {
                var close = text.IndexOf('`', i + 1);
                if (close > i)
                {
                    FlushPlain();
                    inlines.Add(new Run(text[(i + 1)..close])
                    {
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        FontSize = Theme.FontSize - 0.5,
                        Foreground = Theme.Brush(Theme.AccentColor),
                        Background = Theme.Brush(Theme.InputBackground)
                    });
                    i = close + 1;
                    continue;
                }
            }

            // **bold**
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var close = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (close > i + 1)
                {
                    FlushPlain();
                    inlines.Add(new Run(text[(i + 2)..close])
                    {
                        FontFamily = Theme.FontFamily,
                        FontSize = Theme.FontSize,
                        FontWeight = Theme.BoldFontWeight,
                        Foreground = Theme.Brush(Theme.TextPrimary)
                    });
                    i = close + 2;
                    continue;
                }
            }

            // [label](url)
            if (text[i] == '[')
            {
                var closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                {
                    var closeParen = text.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket + 1)
                    {
                        FlushPlain();
                        var label = text[(i + 1)..closeBracket];
                        var url = text[(closeBracket + 2)..closeParen];
                        inlines.Add(BuildLink(label, url));
                        i = closeParen + 1;
                        continue;
                    }
                }
            }

            // Bare http(s) URL
            if (text[i] == 'h' &&
                (text.AsSpan(i).StartsWith("https://") || text.AsSpan(i).StartsWith("http://")))
            {
                var end = i;
                while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != ')' && text[end] != '>')
                {
                    end++;
                }

                FlushPlain();
                var url = text[i..end];
                inlines.Add(BuildLink(url, url));
                i = end;
                continue;
            }

            plain.Append(text[i]);
            i++;
        }

        FlushPlain();
        return inlines;
    }

    // MARK: - Run builders

    private Inline BuildLink(string label, string url)
    {
        var run = new Run(label)
        {
            FontFamily = Theme.FontFamily,
            FontSize = Theme.FontSize
        };

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            run.Foreground = Theme.Brush(Theme.AccentColor);
            run.TextDecorations = TextDecorations.Underline;
            return run;
        }

        var hyperlink = new Hyperlink(run)
        {
            NavigateUri = uri,
            Foreground = Theme.Brush(Theme.AccentColor),
            TextDecorations = TextDecorations.Underline,
            Cursor = System.Windows.Input.Cursors.Hand
        };

        hyperlink.RequestNavigate += (_, args) =>
        {
            try
            {
                // UseShellExecute hands the URL to the default browser rather than trying
                // to run it as a program.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = args.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                                  or InvalidOperationException)
            {
            }
            args.Handled = true;
        };

        return hyperlink;
    }

    private Run CodeRun(string text) => new(text)
    {
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        FontSize = Theme.FontSize - 1,
        Foreground = Theme.Brush(Theme.TextPrimary),
        Background = Theme.Brush(Theme.InputBackground)
    };

    private Run HeadingRun(string text, double size) => new(text)
    {
        FontFamily = Theme.FontFamily,
        FontSize = size,
        FontWeight = FontWeights.Bold,
        Foreground = Theme.Brush(Theme.AccentColor)
    };
}
