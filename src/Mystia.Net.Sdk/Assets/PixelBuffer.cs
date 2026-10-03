using Mystia.Imgui;
using Mystia.Numerics;

namespace Mystia.Assets;

// The engine side of a pixel buffer. The SDK keeps the pixels and the arithmetic; the bridge hands in the
// texture the buffer stands for and the one call that uploads them, so a buffer can be filled and read back
// without the engine being there at all.
/// <summary>Uploads a buffer's whole content into the texture it stands for; the bridge supplies this.</summary>
internal delegate void PixelUpload(ReadOnlySpan<Color> pixels);

// The CPU side counterpart of the engine's own SetPixels/GetPixels/Apply: a mod paints a texture pixel by
// pixel or in blocks and uploads the result in one call. The layout is the engine's: the origin is the
// bottom left corner and y grows upwards, so a colour painted at (0, 0) is the bottom left pixel.
/// <summary>
/// The pixels of a texture the factory built, painted on the CPU. Rows are addressed the way the engine
/// addresses them — the origin is the bottom left corner, y grows upwards — so a buffer and a texture with
/// the same content look the same.
/// <para>
/// The buffer holds its own copy: nothing reaches the texture until <see cref="Apply"/> is called, and a
/// buffer that was never applied leaves the texture it stands for fully transparent. Coordinates outside the
/// buffer and block writes that do not cover a whole block are programming mistakes and throw; a mod that
/// paints from its own data checks its own bounds.
/// </para>
/// </summary>
public sealed class PixelBuffer
{
    private readonly Color[] _pixels;
    private readonly PixelUpload _upload;

    internal PixelBuffer(int width, int height, TextureHandle texture, PixelUpload upload)
    {
        Width = width;
        Height = height;
        Texture = texture;
        _upload = upload;
        _pixels = new Color[width * height];
    }

    /// <summary>The width in pixels.</summary>
    public int Width { get; }

    /// <summary>The height in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// The texture this buffer belongs to, for the members that take one — a sprite cut out of the buffer's
    /// texture is built from this handle.
    /// </summary>
    public TextureHandle Texture { get; }

    /// <summary>Writes one pixel.</summary>
    public void Set(int x, int y, Color color)
    {
        Bounds(x, y);
        _pixels[y * Width + x] = color;
    }

    /// <summary>
    /// Writes a block of pixels, which is how a mod moves a rectangle of one texture into another. The
    /// values are row major from the bottom left corner of the block, exactly like the engine's own
    /// <c>SetPixels</c>, and there must be one value per pixel of the block: width × height of them.
    /// </summary>
    public void Set(int x, int y, int width, int height, ReadOnlySpan<Color> colors)
    {
        Block(x, y, width, height, colors.Length, "colors");
        for (var row = 0; row < height; row++)
            colors.Slice(row * width, width).CopyTo(_pixels.AsSpan((y + row) * Width + x, width));
    }

    /// <summary>Reads one pixel.</summary>
    public Color Get(int x, int y)
    {
        Bounds(x, y);
        return _pixels[y * Width + x];
    }

    /// <summary>
    /// Reads a block of pixels into <paramref name="destination"/>, row major from the bottom left corner of
    /// the block, the counterpart of <see cref="Set(int, int, int, int, ReadOnlySpan{Color})"/>.
    /// </summary>
    public void Get(int x, int y, int width, int height, Span<Color> destination)
    {
        Block(x, y, width, height, destination.Length, "destination");
        for (var row = 0; row < height; row++)
            _pixels.AsSpan((y + row) * Width + x, width).CopyTo(destination.Slice(row * width, width));
    }

    /// <summary>Fills the whole buffer with one colour; a buffer is transparent black before the first fill.</summary>
    public void Fill(Color color) => _pixels.AsSpan().Fill(color);

    /// <summary>
    /// Uploads the buffer into its texture. Until this is called the texture keeps whatever it had, so a mod
    /// that writes in several passes applies once at the end.
    /// </summary>
    public void Apply() => _upload(_pixels);

    private void Bounds(int x, int y)
    {
        if ((uint)x >= (uint)Width)
            throw new ArgumentOutOfRangeException(nameof(x), x, $"The buffer is {Width} pixels wide.");
        if ((uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(y), y, $"The buffer is {Height} pixels tall.");
    }

    private void Block(int x, int y, int width, int height, int count, string name)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "A block must be at least one pixel wide.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "A block must be at least one pixel tall.");
        if ((uint)x > (uint)(Width - width) || (uint)y > (uint)(Height - height))
            throw new ArgumentOutOfRangeException(
                nameof(x),
                x,
                $"A {width}×{height} block at ({x}, {y}) does not fit in a {Width}×{Height} buffer.");
        if (count != width * height)
            throw new ArgumentException($"A {width}×{height} block needs {width * height} values, not {count}.", name);
    }
}
