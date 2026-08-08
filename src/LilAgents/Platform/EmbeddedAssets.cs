using System.Reflection;

namespace LilAgents.Platform;

/// <summary>
/// Reads assets out of the assembly. Everything ships embedded so the published
/// single-file exe has no companion folder; sounds additionally get spilled to disk
/// on first use because MediaPlayer can only play from a URI, not a Stream.
/// </summary>
public static class EmbeddedAssets
{
    private static readonly Assembly Assembly = typeof(EmbeddedAssets).Assembly;
    private static readonly Lazy<string[]> Names = new(() => Assembly.GetManifestResourceNames());
    private static readonly Dictionary<string, string> ExtractedSounds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static string CacheDirectory
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "lil-agents", "cache");
        }
    }

    public static byte[]? Read(string resourceName)
    {
        using var stream = Assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static Stream? Open(string resourceName) => Assembly.GetManifestResourceStream(resourceName);

    /// <summary>
    /// Sprite frames for a character, ordered by frame index.
    /// Resource names look like "LilAgents.Assets.sprites.bruce.frame_000.png".
    /// </summary>
    public static IReadOnlyList<string> SpriteFrameNames(string character)
    {
        var prefix = $"LilAgents.Assets.sprites.{character}.frame_";
        return Names.Value
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> AllNames => Names.Value;

    /// <summary>
    /// Writes a sound to the cache directory (once) and returns its path.
    /// Returns null if the resource is missing or the disk write fails.
    /// </summary>
    public static string? ExtractSound(string fileName)
    {
        lock (Gate)
        {
            if (ExtractedSounds.TryGetValue(fileName, out var cached) && File.Exists(cached)) return cached;

            var resourceName = $"LilAgents.Assets.sounds.{fileName}";
            var bytes = Read(resourceName);
            if (bytes is null) return null;

            try
            {
                Directory.CreateDirectory(CacheDirectory);
                var destination = Path.Combine(CacheDirectory, fileName);
                if (!File.Exists(destination) || new FileInfo(destination).Length != bytes.Length)
                {
                    File.WriteAllBytes(destination, bytes);
                }
                ExtractedSounds[fileName] = destination;
                return destination;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public static System.Drawing.Icon? LoadIcon(string name)
    {
        using var stream = Open($"LilAgents.Assets.icons.{name}");
        return stream is null ? null : new System.Drawing.Icon(stream);
    }
}
