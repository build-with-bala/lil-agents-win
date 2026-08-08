using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LilAgents.Platform;
using LilAgents.Rendering;

namespace LilAgents.UI;

/// <summary>
/// The character itself: a transparent, click-through-where-transparent window holding
/// one sprite frame at a time.
/// </summary>
public sealed class WalkerWindow : OverlayWindow
{
    private readonly Image _image;
    private readonly ScaleTransform _flip = new(1, 1);
    private readonly SpriteSheet _sprites;
    private int _currentFrame = -1;

    /// <summary>
    /// Minimum alpha for a pixel to count as "the character". Matches the macOS build's
    /// threshold of 30, which lets clicks fall through the soft antialiased outline.
    /// </summary>
    private const byte HitTestAlphaThreshold = 30;

    public event Action? Clicked;

    public WalkerWindow(SpriteSheet sprites)
    {
        _sprites = sprites;

        _image = new Image
        {
            Stretch = Stretch.Uniform,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _flip,
            IsHitTestVisible = false
        };

        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Content = _image;
    }

    public void SetFrame(int frameIndex)
    {
        if (frameIndex == _currentFrame) return;
        var frame = _sprites.GetFrame(frameIndex);
        if (frame is null) return;
        _currentFrame = frameIndex;
        _image.Source = frame;
    }

    /// <summary>Mirrors the sprite when the character turns around.</summary>
    public void SetFacingRight(bool facingRight)
    {
        var target = facingRight ? 1.0 : -1.0;
        if (Math.Abs(_flip.ScaleX - target) > double.Epsilon) _flip.ScaleX = target;
    }

    // MARK: - Hit testing

    protected override IntPtr HandleMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_NCHITTEST) return IntPtr.Zero;

        // lParam packs screen coordinates as two signed 16-bit values; casting through
        // short is required or a negative X on a left-hand monitor reads as ~65000.
        var screenX = (short)(lParam.ToInt32() & 0xFFFF);
        var screenY = (short)((lParam.ToInt32() >> 16) & 0xFFFF);

        handled = true;
        return IsOverCharacter(screenX, screenY)
            ? new IntPtr(Native.HTCLIENT)
            : new IntPtr(Native.HTTRANSPARENT);
    }

    /// <summary>
    /// True when the sprite pixel under the cursor is opaque enough to click.
    ///
    /// Returning HTTRANSPARENT for everything else is what makes the window genuinely
    /// click-through: the desktop icon or browser tab behind the character's empty
    /// bounding box receives the click, exactly as it does on macOS when hitTest returns nil.
    /// </summary>
    private bool IsOverCharacter(int screenX, int screenY)
    {
        if (_currentFrame < 0) return false;
        if (!Native.GetWindowRect(Handle, out var rect)) return false;
        if (rect.Width <= 0 || rect.Height <= 0) return false;

        var localX = screenX - rect.Left;
        var localY = screenY - rect.Top;
        if (localX < 0 || localY < 0 || localX >= rect.Width || localY >= rect.Height) return false;

        // The sprite is drawn Stretch.Uniform into the window, so window-local coordinates
        // map to sprite pixels by a single uniform scale.
        var spriteX = (int)(localX * _sprites.PixelWidth / rect.Width);
        var spriteY = (int)(localY * _sprites.PixelHeight / rect.Height);

        // When the character faces left the image is mirrored, so mirror the lookup too.
        if (_flip.ScaleX < 0) spriteX = (int)_sprites.PixelWidth - 1 - spriteX;

        return _sprites.AlphaAt(_currentFrame, spriteX, spriteY) >= HitTestAlphaThreshold;
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        e.Handled = true;
        Clicked?.Invoke();
    }
}
