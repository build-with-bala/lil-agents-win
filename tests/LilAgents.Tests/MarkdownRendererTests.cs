using System.Windows.Documents;
using LilAgents.UI;
using Xunit;

namespace LilAgents.Tests;

/// <summary>
/// Markdown rendering. WPF text objects want an STA apartment, and xunit runs tests in
/// MTA, so each case is executed on a dedicated STA thread rather than pulling in an
/// extra test-runner package for it.
/// </summary>
public class MarkdownRendererTests
{
    private static T OnStaThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) throw failure;
        return result;
    }

    private static string RenderToText(string markdown, Action<MarkdownRenderer>? configure = null)
    {
        return OnStaThread(() =>
        {
            var renderer = new MarkdownRenderer(PopoverTheme.Peach);
            configure?.Invoke(renderer);
            var inlines = renderer.Render(markdown);
            return string.Concat(inlines.Select(TextOf));
        });
    }

    private static string TextOf(Inline inline) => inline switch
    {
        Run run => run.Text,
        Hyperlink link => string.Concat(link.Inlines.Select(TextOf)),
        _ => ""
    };

    [Fact]
    public void PassesPlainTextThroughUnchanged()
    {
        Assert.Equal("hello world", RenderToText("hello world"));
    }

    [Fact]
    public void StripsBoldMarkersButKeepsTheText()
    {
        Assert.Equal("this is bold now", RenderToText("this is **bold** now"));
    }

    [Fact]
    public void StripsInlineCodeBackticks()
    {
        Assert.Equal("run npm install first", RenderToText("run `npm install` first"));
    }

    [Fact]
    public void RendersHeadingsWithoutTheirHashes()
    {
        Assert.Equal("Title\n", RenderToText("# Title\n"));
        Assert.Equal("Sub\n", RenderToText("## Sub\n"));
        Assert.Equal("Deep\n", RenderToText("### Deep\n"));
    }

    [Fact]
    public void ReplacesBulletMarkersWithADot()
    {
        Assert.Equal("  • first\n  • second\n", RenderToText("- first\n* second\n"));
    }

    [Fact]
    public void KeepsLinkLabelAndDropsTheUrl()
    {
        Assert.Equal("see the docs here", RenderToText("see [the docs](https://example.com) here"));
    }

    [Fact]
    public void KeepsBareUrlsIntact()
    {
        Assert.Equal("go to https://example.com now", RenderToText("go to https://example.com now"));
    }

    [Fact]
    public void StripsCodeFencesButKeepsTheirContents()
    {
        Assert.Equal("const x = 1;\n", RenderToText("```js\nconst x = 1;\n```"));
    }

    [Fact]
    public void CarriesCodeBlockStateAcrossStreamingChunks()
    {
        // The macOS original resets its fence flag per chunk, so a block split across two
        // stream reads loses its formatting. Holding the state on the instance fixes it.
        var text = OnStaThread(() =>
        {
            var renderer = new MarkdownRenderer(PopoverTheme.Peach);
            var first = renderer.Render("```python\nprint(1)\n");
            var second = renderer.Render("print(2)\n```\n");
            return string.Concat(first.Concat(second).Select(TextOf));
        });

        Assert.Equal("print(1)\nprint(2)\n", text);
    }

    [Fact]
    public void ResetClearsAnUnclosedCodeFence()
    {
        var text = OnStaThread(() =>
        {
            var renderer = new MarkdownRenderer(PopoverTheme.Peach);
            renderer.Render("```\nunterminated\n");
            renderer.Reset();
            return string.Concat(renderer.Render("**bold**").Select(TextOf));
        });

        Assert.Equal("bold", text);
    }

    [Fact]
    public void LeavesUnmatchedMarkersAsLiteralCharacters()
    {
        Assert.Equal("2 * 3 = 6", RenderToText("2 * 3 = 6"));
        Assert.Equal("a `dangling backtick", RenderToText("a `dangling backtick"));
    }

    [Fact]
    public void HandlesEmptyInput()
    {
        Assert.Equal("", RenderToText(""));
    }
}
