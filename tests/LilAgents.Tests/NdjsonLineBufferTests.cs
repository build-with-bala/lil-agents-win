using LilAgents.Agents;
using Xunit;

namespace LilAgents.Tests;

/// <summary>
/// Pipe reads split wherever they like. These cover the boundaries that actually corrupt
/// agent output when handled naively.
/// </summary>
public class NdjsonLineBufferTests
{
    [Fact]
    public void ReturnsNothingUntilALineCompletes()
    {
        var buffer = new NdjsonLineBuffer();
        Assert.Empty(buffer.Append("{\"type\":\"as"));
        Assert.True(buffer.HasPartialLine);
    }

    [Fact]
    public void ReassemblesALineSplitAcrossThreeReads()
    {
        var buffer = new NdjsonLineBuffer();
        buffer.Append("{\"type\":");
        buffer.Append("\"result\"");
        var lines = buffer.Append("}\n");

        Assert.Equal(["{\"type\":\"result\"}"], lines);
        Assert.False(buffer.HasPartialLine);
    }

    [Fact]
    public void SplitsMultipleLinesArrivingInOneRead()
    {
        var buffer = new NdjsonLineBuffer();
        var lines = buffer.Append("{\"a\":1}\n{\"b\":2}\n{\"c\":3}\n");
        Assert.Equal(["{\"a\":1}", "{\"b\":2}", "{\"c\":3}"], lines);
    }

    [Fact]
    public void KeepsTrailingPartialLineAfterCompleteOnes()
    {
        var buffer = new NdjsonLineBuffer();
        var lines = buffer.Append("{\"a\":1}\n{\"b\":");
        Assert.Equal(["{\"a\":1}"], lines);
        Assert.True(buffer.HasPartialLine);

        var rest = buffer.Append("2}\n");
        Assert.Equal(["{\"b\":2}"], rest);
    }

    [Fact]
    public void StripsCarriageReturnsFromCrlfOutput()
    {
        // A CLI writing in text mode on Windows emits \r\n; the \r must not reach the parser.
        var buffer = new NdjsonLineBuffer();
        var lines = buffer.Append("{\"a\":1}\r\n{\"b\":2}\r\n");
        Assert.Equal(["{\"a\":1}", "{\"b\":2}"], lines);
    }

    [Fact]
    public void SkipsEmptyLines()
    {
        var buffer = new NdjsonLineBuffer();
        var lines = buffer.Append("{\"a\":1}\n\n\n{\"b\":2}\n");
        Assert.Equal(["{\"a\":1}", "{\"b\":2}"], lines);
    }

    [Fact]
    public void FlushReturnsUnterminatedRemainder()
    {
        var buffer = new NdjsonLineBuffer();
        buffer.Append("{\"final\":true}");
        Assert.Equal("{\"final\":true}", buffer.Flush());
        Assert.False(buffer.HasPartialLine);
    }

    [Fact]
    public void PreservesMultiByteCharactersAcrossChunks()
    {
        // The decoder upstream of this class handles byte-level splits; here we only need
        // the assembled text to survive intact.
        var buffer = new NdjsonLineBuffer();
        buffer.Append("{\"text\":\"café ");
        var lines = buffer.Append("→ 日本語\"}\n");
        Assert.Equal(["{\"text\":\"café → 日本語\"}"], lines);
    }
}
