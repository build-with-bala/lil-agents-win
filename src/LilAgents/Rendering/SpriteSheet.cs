using System.Windows.Media.Imaging;
using LilAgents.Platform;

namespace LilAgents.Rendering;

/// <summary>
/// The walk animation, as a sequence of alpha PNG frames.
///
/// macOS plays a transparent HEVC .mov through AVPlayerLayer. Windows has no equivalent:
/// Media Foundation will not decode HEVC's alpha auxiliary layer, and WPF's MediaElement
/// has no alpha path at all. So the videos were transcoded to per-frame PNGs.
///
/// Frames are held as compressed bytes and decoded on demand rather than all at once.
/// Fully decoding 241 frames at 225x400 BGRA would cost ~87 MB per character — unacceptable
/// for something that idles on a taskbar. Decoding one frame costs well under a millisecond,
/// and at 24 fps that is a rounding error of CPU. A small cache absorbs the redraws that
/// happen between frame changes.
/// </summary>
public sealed class SpriteSheet
{
    /// <summary>Frames per second the source videos were authored at.</summary>
    public const double FrameRate = 24.0;

    private readonly byte[]?[] _encodedFrames;
    private readonly Dictionary<int, BitmapSource> _decoded = new();
    private readonly LinkedList<int> _recency = new();
    private readonly object _gate = new();

    /// <summary>
    /// Enough to cover the frames a single rendered moment can touch, without letting the
    /// cache creep toward the full-decode cost this class exists to avoid.
    /// </summary>
    private const int CacheCapacity = 24;

    public int FrameCount => _encodedFrames.Length;

    public double Duration => FrameCount / FrameRate;

    /// <summary>Native pixel size of a frame, discovered from the first decode.</summary>
    public double PixelWidth { get; private set; } = 225;

    public double PixelHeight { get; private set; } = 400;

    public double AspectRatio => PixelHeight <= 0 ? 1 : PixelWidth / PixelHeight;

    private SpriteSheet(byte[]?[] frames)
    {
        _encodedFrames = frames;
    }

    public static SpriteSheet Load(string character)
    {
        var names = EmbeddedAssets.SpriteFrameNames(character);
        var frames = new byte[]?[names.Count];
        for (var i = 0; i < names.Count; i++) frames[i] = EmbeddedAssets.Read(names[i]);
        return new SpriteSheet(frames);
    }

    /// <summary>Frame index for a point in the loop, clamped to the available frames.</summary>
    public int FrameIndexAt(double seconds)
    {
        if (FrameCount == 0) return 0;
        var index = (int)Math.Floor(seconds * FrameRate);
        if (index < 0) index = 0;
        if (index >= FrameCount) index = FrameCount - 1;
        return index;
    }

    public BitmapSource? GetFrame(int index)
    {
        if (FrameCount == 0) return null;
        index = Math.Clamp(index, 0, FrameCount - 1);

        lock (_gate)
        {
            if (_decoded.TryGetValue(index, out var cached))
            {
                Touch(index);
                return cached;
            }
        }

        var bytes = _encodedFrames[index];
        if (bytes is null) return null;

        BitmapSource decoded;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            // OnLoad detaches from the stream so it can be disposed immediately.
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bitmap.EndInit();
            bitmap.Freeze();
            decoded = bitmap;
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException or IOException)
        {
            return null;
        }

        PixelWidth = decoded.PixelWidth;
        PixelHeight = decoded.PixelHeight;

        lock (_gate)
        {
            _decoded[index] = decoded;
            Touch(index);
            EvictIfNeeded();
        }

        return decoded;
    }

    /// <summary>
    /// Reads the alpha byte at a point in frame-local pixel coordinates.
    ///
    /// This replaces the macOS build's screen-capture hit test. AVPlayerLayer composites on
    /// the GPU, so upstream cannot read its own pixels and resorts to CGWindowListCreateImage
    /// on a 1x1 rect — a screen recording call, with the permission prompt that implies.
    /// With sprites the pixels are simply in hand.
    /// </summary>
    public byte AlphaAt(int frameIndex, int x, int y)
    {
        var frame = GetFrame(frameIndex);
        if (frame is null) return 0;
        if (x < 0 || y < 0 || x >= frame.PixelWidth || y >= frame.PixelHeight) return 0;

        try
        {
            var converted = frame.Format == System.Windows.Media.PixelFormats.Bgra32
                ? frame
                : new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);

            var pixel = new byte[4];
            converted.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
            return pixel[3];
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                                              or NotSupportedException)
        {
            return 0;
        }
    }

    private void Touch(int index)
    {
        _recency.Remove(index);
        _recency.AddFirst(index);
    }

    private void EvictIfNeeded()
    {
        while (_recency.Count > CacheCapacity)
        {
            var oldest = _recency.Last;
            if (oldest is null) break;
            _recency.RemoveLast();
            _decoded.Remove(oldest.Value);
        }
    }
}
