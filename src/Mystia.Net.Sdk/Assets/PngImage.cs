using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text;

namespace Mystia.Assets;

// The encoded image half of the texture path, the counterpart of WavAudio. It is engine free arithmetic — a
// PNG is a handful of chunks around one zlib stream of filtered scanlines — so it lives in the SDK where it
// can be exercised without a texture, and the bridge keeps the upload.
//
// The engine's own decoder (UnityEngine.ImageConversion) is not part of the interop set this framework is
// built against, which is why the framework carries this one, and why its counterpart PngWriter sits next to
// it.
/// <summary>
/// The pixels of a PNG file, decoded without the engine: four bytes per pixel, red first, one row after
/// another from the top left corner. The engine stores its rows bottom up, so the bridge flips them while
/// uploading.
/// <para>
/// What the decoder reads: grey, truecolour, indexed and grey or truecolour with an alpha channel; 1, 2, 4, 8
/// or 16 bits per sample, a sixteen bit sample yielding its high byte; a palette; the palette alpha table, the
/// single transparent grey level and the single transparent colour a <c>tRNS</c> chunk adds; and an Adam7
/// interlaced picture, whose seven passes are stitched back into one image. A sample below eight bits is
/// stretched over the whole range, so a four bit white is a white pixel rather than a dim one.
/// </para>
/// <para>
/// What it refuses, reporting <see langword="false"/> rather than half a picture: a colour type or a bit depth
/// that does not exist or that the colour type cannot be written with, an indexed image without a palette, a
/// <c>tRNS</c> chunk on a colour type that already carries its own alpha, a picture larger than 4096×4096 or
/// with a dimension that is not positive, a file that ends before its <c>IEND</c>, a header that is not the
/// first chunk or that appears twice, a palette that comes after the image data, an index the palette cannot
/// name, and a chunk that claims more bytes than the file holds.
/// </para>
/// <para>
/// A chunk whose CRC does not match is reported through <see cref="Warning"/> and read anyway: Unity's own
/// decoder does not check them, so refusing the file would drop art the game itself shows.
/// </para>
/// </summary>
internal sealed class PngImage
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // A decoded image costs four bytes per pixel here and four more in the texture it is uploaded into, so the
    // ceiling is on the image rather than on one dimension. 4096×4096 is past anything the game ships.
    private const int MaximumDimension = 4096;
    private const long MaximumPixels = 4096 * 4096;

    // The seven Adam7 passes, four values each: the column and the row a pass starts at, and the steps it takes
    // from there. A pass with no pixel in it is simply a pass of width zero.
    private static ReadOnlySpan<byte> Adam7 =>
    [
        0, 0, 8, 8,
        4, 0, 8, 8,
        0, 4, 4, 8,
        2, 0, 4, 4,
        0, 2, 2, 4,
        1, 0, 2, 2,
        0, 1, 1, 2,
    ];

    private PngImage(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>
    /// Where a problem the decode recovered from is reported — a chunk CRC that does not match its own chunk,
    /// which is the one thing this decoder reports and then reads through. The bridge points this at its trace,
    /// so a mod sees it in the log; a decode never fails over what it reports here, and with nothing listening
    /// the report is dropped.
    /// </summary>
    internal static Action<string>? Warning { get; set; }

    /// <summary>The width in pixels.</summary>
    internal int Width { get; }

    /// <summary>The height in pixels.</summary>
    internal int Height { get; }

    /// <summary>
    /// Four bytes per pixel, red first, one row after another from the top left corner — the order a PNG file
    /// itself uses. The engine stores its rows bottom up, so the bridge flips them while uploading.
    /// </summary>
    internal byte[] Pixels { get; }

    /// <summary>
    /// Decodes a PNG file, reporting <see langword="false"/> for anything it does not read rather than half a
    /// picture. What it reads and what it refuses is on the type; a chunk CRC that does not match is reported
    /// through <see cref="Warning"/> and does not refuse the file.
    /// </summary>
    /// <param name="png">The bytes of the file.</param>
    /// <param name="image">The decoded image, or null when the file was refused.</param>
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
        var interlaced = false;
        var hasHeader = false;
        var hasPalette = false;
        var hasTransparency = false;
        var hasData = false;
        var hasEnd = false;
        byte[]? palette = null;
        byte[]? transparency = null;
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

            Verify(png, offset, kind, length);

            if (kind.SequenceEqual("IHDR"u8))
            {
                // The header is the first chunk of a file, there is exactly one of them, and it is 13 bytes.
                if (hasHeader || offset != 8 || length < 13)
                    return false;
                var header = png.Slice(start, 13);
                width = BinaryPrimitives.ReadInt32BigEndian(header);
                height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(4));
                depth = header[8];
                color = header[9];
                // The compression method and the filter method are the only two values the spec defines, and
                // the interlace method is either none or Adam7.
                if (header[10] != 0 || header[11] != 0 || header[12] > 1)
                    return false;
                interlaced = header[12] == 1;
                hasHeader = true;
            }
            else if (kind.SequenceEqual("PLTE"u8))
            {
                // One palette, of whole RGB triples, and all of it before the image data.
                if (!hasHeader || hasData || hasPalette || length % 3 != 0 || length is 0 or > 768)
                    return false;
                palette = png.Slice(start, length).ToArray();
                hasPalette = true;
            }
            else if (kind.SequenceEqual("tRNS"u8))
            {
                if (!hasHeader || hasTransparency ||
                    !TryReadTransparency(png.Slice(start, length), color, out transparency))
                {
                    return false;
                }

                hasTransparency = true;
            }
            else if (kind.SequenceEqual("IDAT"u8))
            {
                if (!hasHeader)
                    return false;
                hasData = true;
                data.Write(png.Slice(start, length));
            }
            else if (kind.SequenceEqual("IEND"u8))
            {
                hasEnd = true;
                break;
            }

            offset = start + length + 4;
        }

        if (!hasHeader || !hasEnd)
            return false;
        if (width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension)
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
        if (channels == 0 || !DepthAllowed(color, depth))
            return false;
        if (indexed && palette is null)
            return false;
        if (data.Length == 0)
            return false;

        // Every scanline is one filter byte and the pixels themselves; below eight bits per pixel several
        // pixels share one byte, which is also the unit the filters work on.
        var samples = new Samples(color, depth, channels, palette, transparency);
        var bits = channels * depth;
        var unit = Math.Max(1, bits / 8);

        // An interlaced picture is written as seven sparser passes, one after another; a picture that is not
        // interlaced is written as one pass over all of it either way.
        var passes = interlaced ? 7 : 1;
        long expected = 0;
        for (var pass = 0; pass < passes; pass++)
        {
            var (xOffset, yOffset, xStep, yStep) = Layout(pass, interlaced);
            var (passWidth, passHeight) = Extent(width, height, xOffset, yOffset, xStep, yStep);
            // A pass with no pixel in it is not written at all, so it holds nothing to inflate either.
            if (passWidth == 0 || passHeight == 0)
                continue;
            expected += ((long)((passWidth * bits + 7) / 8) + 1) * passHeight;
        }

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
        // The widest row any pass has is the first pass's, so one buffer carries the row a pass reconstructs
        // its pixels against.
        var previous = new byte[(width * bits + 7) / 8];
        var cursor = 0;
        for (var pass = 0; pass < passes; pass++)
        {
            var (xOffset, yOffset, xStep, yStep) = Layout(pass, interlaced);
            var (passWidth, passHeight) = Extent(width, height, xOffset, yOffset, xStep, yStep);
            if (passWidth == 0 || passHeight == 0)
                continue;

            var stride = (passWidth * bits + 7) / 8;
            previous.AsSpan(0, stride).Clear();
            for (var y = 0; y < passHeight; y++)
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

                // A pass lays its pixels onto the image at its own offsets and steps, so a pixel's place in the
                // picture is not its place in the row.
                var line = yOffset + y * yStep;
                for (var x = 0; x < passWidth; x++)
                {
                    var at = (line * width + xOffset + x * xStep) * 4;
                    if (!samples.Place(pixels, at, row, x))
                        return false;
                }
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

    // A chunk's CRC covers its type and its payload together. A mismatch is reported and the chunk is read
    // anyway: the file is what it is, and Unity's own decoder would not even have looked.
    private static void Verify(ReadOnlySpan<byte> png, int offset, ReadOnlySpan<byte> kind, int length)
    {
        var stored = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset + 8 + length, 4));
        var computed = PngCrc32.Compute(png.Slice(offset + 4, 4 + length));
        if (stored == computed || Warning is not { } warn)
            return;

        warn($"PngImage: the {Encoding.ASCII.GetString(kind)} chunk at offset {offset} does not carry its own CRC" +
             $" ({stored:X8}, computed {computed:X8}); it is read anyway.");
    }

    // The transparency chunk: the alpha of every palette entry, or the one colour that is fully transparent.
    // The spec forbids it on the two colour types that already carry an alpha channel, which is why those two
    // are refused rather than ignored, and it is the two or six bytes one colour needs otherwise.
    private static bool TryReadTransparency(
        ReadOnlySpan<byte> chunk,
        int color,
        [NotNullWhen(true)] out byte[]? transparency)
    {
        transparency = null;
        switch (color)
        {
            // One grey level, in the sample range the depth defines.
            case 0 when chunk.Length == 2:
            // One red, green and blue triple.
            case 2 when chunk.Length == 6:
            // One alpha per palette entry; an entry the table stops short of stays opaque.
            case 3 when chunk.Length is > 0 and <= 768:
                transparency = chunk.ToArray();
                return true;
            default:
                return false;
        }
    }

    // A bit depth is readable when the colour type defines it: grey may be written at every depth, a colour
    // channel is eight or sixteen bits, and a palette index is at most eight, one byte.
    private static bool DepthAllowed(int color, int depth) => color switch
    {
        0 => depth is 1 or 2 or 4 or 8 or 16,
        2 => depth is 8 or 16,
        3 => depth is 1 or 2 or 4 or 8,
        4 or 6 => depth is 8 or 16,
        _ => false,
    };

    // Where a pass starts in the picture and how far apart its samples are: the whole picture for an image that
    // is not interlaced, and one of the seven Adam7 passes otherwise.
    private static (int X, int Y, int XStep, int YStep) Layout(int pass, bool interlaced)
    {
        if (!interlaced)
            return (0, 0, 1, 1);
        var at = pass * 4;
        return (Adam7[at], Adam7[at + 1], Adam7[at + 2], Adam7[at + 3]);
    }

    // How many pixels a pass holds: every step of it that lands inside the picture.
    private static (int Width, int Height) Extent(int width, int height, int xOffset, int yOffset, int xStep, int yStep)
    {
        var columns = width > xOffset ? (width - xOffset + xStep - 1) / xStep : 0;
        var rows = height > yOffset ? (height - yOffset + yStep - 1) / yStep : 0;
        return (columns, rows);
    }

    // The samples of one image and the tables a tRNS chunk adds, as the pixel loop needs them: one value
    // instead of six arguments, and the reading of a sample kept next to what a sample means.
    private readonly struct Samples
    {
        private readonly int _color;
        private readonly int _depth;
        private readonly int _channels;
        private readonly byte[]? _palette;
        private readonly byte[]? _transparency;

        internal Samples(int color, int depth, int channels, byte[]? palette, byte[]? transparency)
        {
            _color = color;
            _depth = depth;
            _channels = channels;
            _palette = palette;
            _transparency = transparency;
        }

        // Writes one pixel of a pass into the image; false when the pixel is an index the palette cannot name.
        internal bool Place(Span<byte> pixels, int at, ReadOnlySpan<byte> row, int x)
        {
            switch (_color)
            {
                case 0:
                {
                    var sample = Read(row, x, 0);
                    var level = Widen(sample);
                    pixels[at] = level;
                    pixels[at + 1] = level;
                    pixels[at + 2] = level;
                    pixels[at + 3] = _transparency is not null && sample == Grey() ? (byte)0 : (byte)255;
                    return true;
                }

                case 2:
                {
                    var red = Read(row, x, 0);
                    var green = Read(row, x, 1);
                    var blue = Read(row, x, 2);
                    pixels[at] = Widen(red);
                    pixels[at + 1] = Widen(green);
                    pixels[at + 2] = Widen(blue);
                    pixels[at + 3] = _transparency is not null
                                    && red == Coloured(0)
                                    && green == Coloured(1)
                                    && blue == Coloured(2)
                        ? (byte)0
                        : (byte)255;
                    return true;
                }

                case 3:
                {
                    var index = Read(row, x, 0);
                    if (index * 3 + 2 >= _palette!.Length)
                        return false;
                    pixels[at] = _palette[index * 3];
                    pixels[at + 1] = _palette[index * 3 + 1];
                    pixels[at + 2] = _palette[index * 3 + 2];
                    pixels[at + 3] = index < (_transparency?.Length ?? 0) ? _transparency![index] : (byte)255;
                    return true;
                }

                case 4:
                {
                    var level = Widen(Read(row, x, 0));
                    pixels[at] = level;
                    pixels[at + 1] = level;
                    pixels[at + 2] = level;
                    pixels[at + 3] = Widen(Read(row, x, 1));
                    return true;
                }

                default:
                {
                    pixels[at] = Widen(Read(row, x, 0));
                    pixels[at + 1] = Widen(Read(row, x, 1));
                    pixels[at + 2] = Widen(Read(row, x, 2));
                    pixels[at + 3] = Widen(Read(row, x, 3));
                    return true;
                }
            }
        }

        // One channel of one pixel, in the file's own sample range (0..2^depth-1).
        private int Read(ReadOnlySpan<byte> row, int x, int channel)
        {
            if (_depth == 16)
                return (row[(x * _channels + channel) * 2] << 8) | row[(x * _channels + channel) * 2 + 1];
            if (_depth == 8)
                return row[x * _channels + channel];

            // Below eight bits there is one channel per pixel and several pixels share a byte, the first pixel
            // in the high bits.
            var bit = x * _depth;
            return (row[bit >> 3] >> (8 - _depth - (bit & 7))) & ((1 << _depth) - 1);
        }

        // A sample widened to the eight bit channel the texture holds: sixteen bit samples keep their high byte,
        // and a one, two or four bit sample is stretched over the whole range, so a one bit white stays white.
        private byte Widen(int sample) => _depth switch
        {
            16 => (byte)(sample >> 8),
            8 => (byte)sample,
            _ => (byte)(sample * 255 / ((1 << _depth) - 1)),
        };

        // The grey level the transparency chunk names, as the two byte sample the file stores.
        private int Grey() => (_transparency![0] << 8) | _transparency[1];

        // One channel of the colour the transparency chunk names, each of them a two byte value.
        private int Coloured(int channel) =>
            (_transparency![channel * 2] << 8) | _transparency[channel * 2 + 1];
    }
}
