namespace LilAgents.Agents;

/// <summary>
/// Accumulates decoded stdout chunks and yields complete lines.
///
/// The agent CLIs emit newline-delimited JSON, but a pipe read boundary lands wherever
/// it likes — a single event can arrive split across three reads, and two events can
/// arrive in one. Everything upstream of the JSON parser has to tolerate that.
/// </summary>
public sealed class NdjsonLineBuffer
{
    private readonly System.Text.StringBuilder _buffer = new();

    public bool HasPartialLine => _buffer.Length > 0;

    /// <summary>Adds a chunk and returns whatever complete lines it completed.</summary>
    public IReadOnlyList<string> Append(string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return [];

        _buffer.Append(chunk);

        var lines = new List<string>();
        var content = _buffer.ToString();
        var start = 0;

        while (true)
        {
            var newline = content.IndexOf('\n', start);
            if (newline < 0) break;

            var line = content[start..newline];
            // Tolerate CRLF: on Windows a CLI writing text-mode output produces \r\n.
            if (line.EndsWith('\r')) line = line[..^1];
            if (line.Length > 0) lines.Add(line);
            start = newline + 1;
        }

        _buffer.Clear();
        if (start < content.Length) _buffer.Append(content[start..]);

        return lines;
    }

    /// <summary>Returns and clears any trailing content that never got a newline.</summary>
    public string Flush()
    {
        var remainder = _buffer.ToString();
        _buffer.Clear();
        return remainder.TrimEnd('\r');
    }

    public void Clear() => _buffer.Clear();
}
