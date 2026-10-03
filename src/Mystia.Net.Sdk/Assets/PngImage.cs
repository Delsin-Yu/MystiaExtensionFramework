using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace Mystia.Assets;

// The encoded image half of the texture path, the counterpart of WavAudio. It is engine free arithmetic — a
// PNG is a handful of chunks around one zlib stream of filtered scanlines — so it lives in the SDK where it
// can be exercised without a texture, and the bridge keeps the upload.
//
// The engine's own decoder (UnityEngine.ImageConversion) is not part of the interop set this framework is
// built against, which is why the framework carries this one. It reads what the game's own art loader reads,
// one step wider: 8 bit grey, truecolour and either with alpha, plus the indexed image pixel art is usually
// written as, at 1, 2, 4 or 8 bits per pixel.
internal sealed class PngImage
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // A decoded image costs four bytes per pixel here and four more in the texture it is uploaded into, so the
    // ceiling is on the image rather than on one dimension. 4096×4096 is past anything the game ships.
    private const int MaximumDimension = 4096;
    private const long MaximumPixels = 4096 * 4096;

    private PngImage(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>The width in pixels.</summary>
    internal int Width { get; }

    /// <summary>The height in pixels.</summary>
    internal int Height { get; }

    /// <summary>
    /// Four bytes per pixel, red first, one row after another from the top left corner — the order a PNG file
    /// itself uses. The engine stores its rows bottom up, so the bridge flips them while uploading.
    /// </summary>
    internal byte[] Pixels { get; }

    internal static bool TryDecode(ReadOnlySpan<byte> png, [NotNullWhen(true)] out PngImage? image)
    {
        image = null;

        // The signature and the 25 bytes of the smallest possible header chunk.
        if (png.Length < 33 || !png.Slice(0, 8).SequenceEqual(Signature))
            return false;

        var width = 0;
        var height = 0;
        var depth = 0;
        var color = 0;
        var hasHeader = false;
        byte[]? palette = null;
        var data = new MemoryStream();

        var offset = 8;
        while (offset + 12 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.Slice(offset, 4));
            var kind = png.Slice(offset + 4, 4);
            var start = offset + 8;
            // A chunk carries its payload and a trailing CRC.
            if (length < 0 || (long)start + length + 4 > png.Length)
                return false;

            if (kind.SequenceEqual("IHDR"u8))
            {
                if (length < 13)
                    return false;
                var header = png.Slice(start, 13);
                width = BinaryPrimitives.ReadInt32BigEndian(header);
                height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(4));
                depth = header[8];
                color = header[9];
                // The compression method, the filter method and the interlace method are the only values this
                // decoder reads: 0, 0 and none.
                if (header[10] != 0 || header[11] != 0 || header[12] != 0)
                    return false;
                hasHeader = true;
            }
            else if (kind.SequenceEqual("PLTE"u8))
            {
                if (length % 3 != 0 || length is 0 or > 768)
                    return false;
                palette = png.Slice(start, length).ToArray();
            }
            else if (kind.SequenceEqual("IDAT"u8))
            {
                data.Write(png.Slice(start, length));
            }
            else if (kind.SequenceEqual("IEND"u8))
            {
                break;
            }

            offset = start + length + 4;
        }

        if (!hasHeader || width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension)
            return false;
        if (width * (long)height > MaximumPixels)
            return false;

        // A colour type the decoder does not read, an image that needs a palette it does not have, and a bit
        // depth that colour type cannot be written with.
        var indexed = color == 3;
        var channels = color switch
        {
            0 => 1, // grey
            2 => 3, // truecolour
            3 => 1, // a palette index
            4 => 2, // grey with alpha
            6 => 4, // truecolour with alpha
            _ => 0,
        };
        if (channels == 0 || (indexed && palette is null) || (indexed ? depth is not (1 or 2 or 4 or 8) : depth != 8))
            return false;
        if (data.Length == 0)
            return false;

        // Every scanline is one filter byte and the pixels themselves; a sub byte depth packs several pixels
        // into one byte, which is also the unit the filters work on.
        var stride = indexed ? ((width * depth) + 7) / 8 : width * channels;
        var unit = Math.Max(1, channels * depth / 8);
        var expected = (stride + 1) * (long)height;
        if (expected > int.MaxValue)
            return false;

        var raw = new byte[expected];
        try
        {
            data.Position = 0;
            using var inflate = new ZLibStream(data, CompressionMode.Decompress);
            var read = 0;
            while (read < raw.Length)
            {
                var count = inflate.Read(raw, read, raw.Length - read);
                if (count <= 0)
                    return false; // the image is cut short
                read += count;
            }
        }
        catch (InvalidDataException)
        {
            return false;
        }

        var pixels = new byte[width * height * 4];
        var previous = new byte[stride];
        var cursor = 0;
        for (var y = 0; y < height; y++)
        {
            var filter = raw[cursor++];
            if (filter > 4)
                return false;
            var row = raw.AsSpan(cursor, stride);
            cursor += stride;

            for (var x = 0; x < stride; x++)
            {
                var left = x >= unit ? row[x - unit] : (byte)0;
                var up = previous[x];
                var upLeft = x >= unit ? previous[x - unit] : (byte)0;
                row[x] = filter switch
                {
                    0 => row[x],
                    1 => (byte)(row[x] + left),
                    2 => (byte)(row[x] + up),
                    3 => (byte)(row[x] + ((left + up) >> 1)),
                    _ => (byte)(row[x] + Paeth(left, up, upLeft)),
                };
            }

            row.CopyTo(previous);
            var target = pixels.AsSpan(y * width * 4);
            for (var x = 0; x < width; x++)
            {
                var at = x * 4;
                if (indexed)
                {
                    var bit = x * depth;
                    var index = (row[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
                    if (index * 3 + 2 >= palette!.Length)
                        return false;
                    target[at] = palette[index * 3];
                    target[at + 1] = palette[index * 3 + 1];
                    target[at + 2] = palette[index * 3 + 2];
                    target[at + 3] = 255;
                    continue;
                }

                var source = x * channels;
                target[at] = row[source];
                target[at + 1] = channels >= 3 ? row[source + 1] : row[source];
                target[at + 2] = channels >= 3 ? row[source + 2] : row[source];
                target[at + 3] = channels switch
                {
                    4 => row[source + 3],
                    2 => row[source + 1],
                    _ => (byte)255,
                };
            }
        }

        image = new PngImage(width, height, pixels);
        return true;
    }

    private static byte Paeth(byte left, byte up, byte upLeft)
    {
        var estimate = left + up - upLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var diagonal = Math.Abs(estimate - upLeft);
        if (leftDistance <= upDistance && leftDistance <= diagonal)
            return left;
        return upDistance <= diagonal ? up : upLeft;
    }
}
