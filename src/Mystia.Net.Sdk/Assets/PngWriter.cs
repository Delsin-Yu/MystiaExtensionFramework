using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace Mystia.Assets;

// The write half of the image path, kept next to the decoder it is exercised against: the pixels a mod built
// or read back out of a texture, written out as the one format the game's own art ships in. Like the decoder
// it is engine free — a signature, a header, one deflated stream of scanlines and the end marker — which is
// why it lives in the SDK rather than in a mod, where the same encoder used to be copied by hand.
/// <summary>
/// Writes RGBA pixels out as a PNG file, the counterpart of <see cref="PngImage"/>: eight bits per channel,
/// truecolour with an alpha channel, no interlacing, and every scanline written with the <c>None</c> filter.
/// <para>
/// The result is a compliant file — the signature, an <c>IHDR</c>, one <c>IDAT</c> carrying a zlib stream and
/// an <c>IEND</c>, every chunk ending with the CRC of its own type and payload — so the framework's own
/// decoder reads it back, and so does anything else that reads a PNG.
/// </para>
/// <para>
/// Rows are the PNG's own: the first four bytes are the top left pixel, which is the order
/// <see cref="PngImage.Pixels"/> comes back in. The engine holds its rows bottom up, so a mod that reads a
/// texture back turns the rows over before handing them here. A picture is at most 8192 pixels a side and
/// 8192×8192 pixels in all, far past anything the game's own art or a sprite sheet is.
/// </para>
/// </summary>
public static class PngWriter
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // Eight thousand pixels a side, sixty seven million in all: the ceiling is on the file rather than on one
    // dimension, and it is there to keep a typo out of the gigabytes. The scanlines of a picture this size are
    // a quarter of a gigabyte, which is as much as writing one file should cost.
    private const int MaximumDimension = 8192;
    private const long MaximumPixels = 8192L * 8192;

    /// <summary>
    /// Encodes RGBA pixels as a PNG file.
    /// </summary>
    /// <param name="rgba">
    /// The pixels, four bytes each, red first, one row after another from the top left corner: exactly
    /// width × height × 4 bytes.
    /// </param>
    /// <param name="width">The width in pixels; must be positive and at most 8192.</param>
    /// <param name="height">The height in pixels; must be positive and at most 8192.</param>
    /// <param name="png">The bytes of the file, or null when the size or the pixels were refused.</param>
    /// <returns>
    /// False when a dimension is not positive or is past the ceiling, when the picture is past 8192×8192
    /// pixels, or when the pixels are not exactly four bytes per pixel — bad input is reported, never thrown.
    /// </returns>
    public static bool TryEncode(
        ReadOnlySpan<byte> rgba,
        int width,
        int height,
        [NotNullWhen(true)] out byte[]? png)
    {
        png = null;
        if (width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension)
            return false;
        if (width * (long)height > MaximumPixels)
            return false;
        if (rgba.Length != width * (long)height * 4)
            return false;

        // One filter byte per scanline, and the unfiltered pixel bytes after it.
        var stride = 1 + width * 4;
        var scanlines = new byte[height * stride];
        for (var y = 0; y < height; y++)
        {
            var at = y * stride;
            scanlines[at] = 0;
            rgba.Slice(y * width * 4, width * 4).CopyTo(scanlines.AsSpan(at + 1));
        }

        using var file = new MemoryStream();
        file.Write(Signature);
        Chunk(file, "IHDR"u8, Header(width, height));
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(scanlines);
            Chunk(file, "IDAT"u8, compressed.ToArray());
        }

        Chunk(file, "IEND"u8, []);
        png = file.ToArray();
        return true;
    }

    // Thirteen bytes: the size, then the format — eight bits per channel, truecolour with alpha, deflated, the
    // five filters available, and no interlacing, which are the three zeroes at the end.
    private static byte[] Header(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;
        return header;
    }

    // A chunk is its payload's length, its type, the payload itself, and the CRC of the type and the payload
    // read as one run of bytes.
    private static void Chunk(Stream file, ReadOnlySpan<byte> kind, ReadOnlySpan<byte> payload)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, payload.Length);
        file.Write(number);
        file.Write(kind);
        file.Write(payload);
        BinaryPrimitives.WriteUInt32BigEndian(number, PngCrc32.Compute(kind, payload));
        file.Write(number);
    }
}
