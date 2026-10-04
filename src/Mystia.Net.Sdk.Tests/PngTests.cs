using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Mystia.Assets;
using Xunit;

namespace Mystia.Tests;

// The PNG pair the texture path is built on: the decoder that turns the files the game's own art ships in into
// the pixels a texture is uploaded with, and the writer next to it that turns pixels back into such a file.
// These tests pin what the decoder reads and what it refuses, the parts of the format that decide whether a
// picture comes out right (the transparency a tRNS chunk adds, the interlaced layout, the wider sample
// depths), and that what the writer produces is a file anything that reads a PNG reads back unchanged.
public sealed class PngTests
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// A palette can name the alpha of every entry it has. Before this the whole table was dropped and the
    /// pixels came back opaque, which is the one decoding mistake nobody notices as a mistake.
    /// </summary>
    [Fact]
    public void A_palette_alpha_table_becomes_the_alpha_of_the_pixels()
    {
        // Three pixels of one byte each, and a table that stops after the second entry: the pixel beyond it
        // keeps the opaque default an entry without an alpha has.
        Assert.True(PngImage.TryDecode(
            Png(3, 1, 3, Rows([0x00, 0x01, 0x02]), palette: [255, 0, 0, 0, 255, 0, 0, 0, 255], transparency: [0, 128]),
            out var image));

        Assert.Equal(new byte[] { 255, 0, 0, 0, 0, 255, 0, 128, 0, 0, 255, 255 }, image.Pixels);
    }

    /// <summary>The other half of a tRNS chunk: one colour of the picture is the transparent one.</summary>
    [Fact]
    public void A_single_transparent_colour_becomes_alpha_zero()
    {
        // Grey: the level 40 is the transparent one, and its neighbours are not.
        Assert.True(PngImage.TryDecode(Png(3, 1, 0, Rows([7, 40, 200]), transparency: [0, 40]), out var image));
        Assert.Equal(new byte[] { 7, 7, 7, 255, 40, 40, 40, 0, 200, 200, 200, 255 }, image.Pixels);

        // Truecolour: the triple (10, 20, 30) is transparent, and one channel off it is not.
        Assert.True(PngImage.TryDecode(
            Png(2, 1, 2, Rows([10, 20, 30, 10, 20, 31]), transparency: [0, 10, 0, 20, 0, 30]),
            out image));
        Assert.Equal(new byte[] { 10, 20, 30, 0, 10, 20, 31, 255 }, image.Pixels);

        // At sixteen bits the whole sample is what is compared, not the high byte alone.
        Assert.True(PngImage.TryDecode(
            Png(2, 1, 0, Rows([0x12, 0x34, 0x12, 0x35]), depth: 16, transparency: [0x12, 0x34]),
            out image));
        Assert.Equal(new byte[] { 0x12, 0x12, 0x12, 0, 0x12, 0x12, 0x12, 255 }, image.Pixels);

        // A one bit picture names the sample 1, not the byte it shares with its neighbour.
        Assert.True(PngImage.TryDecode(Png(2, 1, 0, Rows([0b1000_0000]), depth: 1, transparency: [0, 1]), out image));
        Assert.Equal(new byte[] { 255, 255, 255, 0, 0, 0, 0, 255 }, image.Pixels);
    }

    /// <summary>A transparency chunk where the spec does not allow one is refused rather than half applied.</summary>
    [Fact]
    public void Transparency_is_refused_where_the_spec_forbids_it()
    {
        // The two colour types that carry their own alpha channel cannot also carry a transparent colour.
        Assert.False(PngImage.TryDecode(Png(1, 1, 6, Rows([1, 2, 3, 4]), transparency: [0, 0, 0, 0, 0, 0]), out _));
        Assert.False(PngImage.TryDecode(Png(1, 1, 4, Rows([1, 4]), transparency: [0, 0, 0, 0]), out _));

        // A grey level is two bytes and a colour is six; anything else names nothing at all.
        Assert.False(PngImage.TryDecode(Png(1, 1, 0, Rows([1]), transparency: [0, 1, 0]), out _));
        Assert.False(PngImage.TryDecode(Png(1, 1, 2, Rows([1, 2, 3]), transparency: [0, 1, 0, 2]), out _));

        // An alpha table that holds nothing, and a second table next to the first.
        Assert.False(PngImage.TryDecode(Png(1, 1, 3, Rows([0]), palette: [1, 2, 3], transparency: []), out _));
        Assert.False(PngImage.TryDecode(
            Build(
                ("IHDR", Header(1, 1, 3, 8, false)),
                ("PLTE", [1, 2, 3]),
                ("tRNS", [0]),
                ("tRNS", [128]),
                ("IDAT", Deflate(Rows([0]))),
                ("IEND", [])),
            out _));

        // A colour type that does not exist carries no transparency either.
        Assert.False(PngImage.TryDecode(Png(1, 1, 5, Rows([1]), transparency: [0, 1]), out _));
    }

    /// <summary>
    /// A chunk CRC covers its type and its payload, and a file whose CRCs do not match is still a picture:
    /// the mismatch is reported and the file is read, because Unity's own decoder never looks at a CRC and
    /// refusing the file would drop art the game itself shows.
    /// </summary>
    [Fact]
    public void A_chunk_whose_crc_does_not_match_is_reported_and_read_anyway()
    {
        var file = Png(2, 1, 6, Rows([1, 2, 3, 4, 5, 6, 7, 8]));
        foreach (var offset in Chunks(file))
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(offset));
            file.AsSpan(offset + 8 + length, 4).Clear();
        }

        var reports = new List<string>();
        var previous = PngImage.Warning;
        PngImage.Warning = reports.Add;
        try
        {
            Assert.True(PngImage.TryDecode(file, out var image));
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, image.Pixels);
        }
        finally
        {
            PngImage.Warning = previous;
        }

        // Every chunk of the file is named, and the header, the data and the end marker are among them.
        foreach (var kind in new[] { "IHDR", "IDAT", "IEND" })
            Assert.Contains(reports, report => report.Contains(kind, StringComparison.Ordinal) && report.Contains("CRC", StringComparison.Ordinal));
    }

    /// <summary>
    /// Adam7 splits a picture into seven sparser passes, and a decoder that assumes a row is a row puts every
    /// pixel of an interlaced file in the wrong place. The same picture written both ways has to come out the
    /// same.
    /// </summary>
    [Fact]
    public void An_interlaced_picture_decodes_to_the_same_pixels_as_a_plain_one()
    {
        // Sizes that put all seven passes to work, single pixel passes included.
        foreach (var (width, height) in new[] { (1, 1), (2, 3), (5, 5), (8, 8), (9, 4) })
        {
            var expected = Pixels(width, height);

            Assert.True(PngWriter.TryEncode(expected, width, height, out var plain));
            Assert.True(PngImage.TryDecode(plain, out var image));

            Assert.True(PngImage.TryDecode(
                Png(width, height, 6, Interlaced(width, height, expected), interlaced: true),
                out var woven), $"{width}×{height}");
            Assert.Equal(width, woven.Width);
            Assert.Equal(height, woven.Height);
            Assert.Equal(image.Pixels, woven.Pixels);
            Assert.Equal(expected, woven.Pixels);
        }
    }

    /// <summary>
    /// Interlacing and a sub byte depth together: each pass packs its own pixels into its own bytes, so the
    /// stride a pass has to be read with is that pass's and not the picture's.
    /// </summary>
    [Fact]
    public void An_interlaced_one_bit_picture_reads_every_pass_at_its_own_stride()
    {
        // A 2×2 one bit grey picture — white, black on the first row and black, white on the second — written
        // as Adam7 writes it: the top left pixel is a pass of its own, the right column one pixel per pass, and
        // the two pixels of the second row share the byte of the last pass.
        Assert.True(PngImage.TryDecode(
            Png(2, 2, 0, Rows([0b1000_0000], [0x00], [0b0100_0000]), depth: 1, interlaced: true),
            out var image));

        Assert.Equal(
            new byte[] { 255, 255, 255, 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255, 255 },
            image.Pixels);
    }

    /// <summary>Sixteen bit samples are read as their high byte, which is the scale a byte channel wants.</summary>
    [Fact]
    public void A_sixteen_bit_sample_yields_its_high_byte()
    {
        // Every sample is eight bits of value and eight bits the decoder drops.
        Assert.True(PngImage.TryDecode(Png(2, 1, 0, Rows([0x12, 0x34, 0xAB, 0xCD]), depth: 16), out var image));
        Assert.Equal(new byte[] { 0x12, 0x12, 0x12, 255, 0xAB, 0xAB, 0xAB, 255 }, image.Pixels);

        Assert.True(PngImage.TryDecode(
            Png(1, 1, 2, Rows([0x11, 0xFF, 0x22, 0xFF, 0x33, 0xFF]), depth: 16),
            out image));
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 255 }, image.Pixels);

        Assert.True(PngImage.TryDecode(Png(1, 1, 4, Rows([0x44, 0x00, 0x55, 0xFF]), depth: 16), out image));
        Assert.Equal(new byte[] { 0x44, 0x44, 0x44, 0x55 }, image.Pixels);

        Assert.True(PngImage.TryDecode(
            Png(1, 1, 6, Rows([0x66, 0x00, 0x77, 0x00, 0x88, 0x00, 0x99, 0x00]), depth: 16),
            out image));
        Assert.Equal(new byte[] { 0x66, 0x77, 0x88, 0x99 }, image.Pixels);
    }

    /// <summary>
    /// Below eight bits several pixels share a byte, and a sample is stretched over the whole range: a one bit
    /// image is black and white, not black and a nearly invisible grey.
    /// </summary>
    [Fact]
    public void A_low_bit_depth_is_stretched_over_the_whole_range()
    {
        // Two pixels to the byte, the first of them in the high bit.
        Assert.True(PngImage.TryDecode(Png(2, 1, 0, Rows([0b1000_0000]), depth: 1), out var image));
        Assert.Equal(new byte[] { 255, 255, 255, 255, 0, 0, 0, 255 }, image.Pixels);

        // Two bits: black, a third, two thirds, white.
        Assert.True(PngImage.TryDecode(Png(4, 1, 0, Rows([0b1101_1000]), depth: 2), out image));
        Assert.Equal(
            new byte[] { 255, 255, 255, 255, 85, 85, 85, 255, 170, 170, 170, 255, 0, 0, 0, 255 },
            image.Pixels);

        // Four bits: two pixels to the byte as well, and the values in between.
        Assert.True(PngImage.TryDecode(Png(2, 1, 0, Rows([0xF8]), depth: 4), out image));
        Assert.Equal(new byte[] { 255, 255, 255, 255, 136, 136, 136, 255 }, image.Pixels);

        // Grey with alpha is written at four bits by no writer, and neither is a palette index at sixteen.
        Assert.False(PngImage.TryDecode(Png(1, 1, 4, Rows([0x00]), depth: 4), out _));
        Assert.False(PngImage.TryDecode(Png(1, 1, 3, Rows([0x00]), depth: 16, palette: [1, 2, 3]), out _));
    }

    /// <summary>A bit depth no colour type defines is refused, and so is a depth one of them cannot be written at.</summary>
    [Fact]
    public void A_bit_depth_its_colour_type_cannot_carry_is_refused()
    {
        Assert.False(PngImage.TryDecode(Png(1, 1, 2, Rows([0]), depth: 4), out _));
        Assert.False(PngImage.TryDecode(Png(1, 1, 2, Rows([0]), depth: 2), out _));
        Assert.False(PngImage.TryDecode(Png(1, 1, 6, Rows([0]), depth: 4), out _));
        Assert.False(PngImage.TryDecode(Png(1, 1, 0, Rows([0]), depth: 3), out _));

        // The depths the spec does define for those colour types are read.
        Assert.True(PngImage.TryDecode(Png(2, 1, 0, Rows([0x08]), depth: 4), out _));
        Assert.True(PngImage.TryDecode(Png(2, 1, 3, Rows([0x21]), depth: 4, palette: [1, 2, 3, 4, 5, 6, 7, 8, 9]), out _));
    }

    /// <summary>
    /// A file that is not whole is refused rather than half read: the header comes first and only once, the
    /// palette comes before the pixels that index it, and the end marker is there.
    /// </summary>
    [Fact]
    public void A_file_that_is_not_whole_is_refused()
    {
        var rows = Deflate(Rows([1, 2, 3, 4]));
        var header = Header(1, 1, 6, 8, false);

        // A file that stops after its data chunk.
        Assert.False(PngImage.TryDecode(Build(("IHDR", header), ("IDAT", rows)), out _));

        // A header that is not the first chunk, and a second header next to the first.
        Assert.False(PngImage.TryDecode(Build(("IDAT", rows), ("IHDR", header), ("IEND", [])), out _));
        Assert.False(PngImage.TryDecode(
            Build(("IHDR", header), ("IDAT", rows), ("IHDR", header), ("IEND", [])),
            out _));

        // A palette that comes after the image data.
        Assert.False(PngImage.TryDecode(
            Build(("IHDR", Header(1, 1, 3, 8, false)), ("IDAT", rows), ("PLTE", [1, 2, 3]), ("IEND", [])),
            out _));

        // An interlace method that does not exist.
        var interlace = Header(1, 1, 6, 8, false);
        interlace[12] = 2;
        Assert.False(PngImage.TryDecode(Build(("IHDR", interlace), ("IDAT", rows), ("IEND", [])), out _));
    }

    /// <summary>
    /// The other half of reading a container strictly: a chunk the decoder does not know is skipped, not
    /// refused, so a file written by a tool that adds its own chunks is still read.
    /// </summary>
    [Fact]
    public void An_unknown_chunk_between_the_data_chunks_is_still_read_through()
    {
        // The two halves of one deflate stream, with a chunk nobody knows between them.
        var compressed = Deflate(Rows([1, 2, 3, 4], [5, 6, 7, 8]));
        var cut = compressed.Length / 2;

        Assert.True(PngImage.TryDecode(
            Build(
                ("IHDR", Header(1, 2, 6, 8, false)),
                ("IDAT", compressed[..cut]),
                ("gbRA", [9, 9, 9]),
                ("IDAT", compressed[cut..]),
                ("IEND", [])),
            out var image));

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, image.Pixels);
    }

    /// <summary>The CRC every reader of a PNG computes, pinned against the value the spec names.</summary>
    [Fact]
    public void The_crc_is_the_one_every_reader_computes()
    {
        Assert.Equal(0xCBF43926u, PngCrc32.Compute("123456789"u8));

        // The tests build their files with their own table, so that one is pinned here too.
        Assert.Equal(0xCBF43926u, Crc32("123456789"u8.ToArray()));
    }

    /// <summary>
    /// What the writer produces is a file the decoder reads back pixel for pixel, whatever the size.
    /// </summary>
    [Fact]
    public void Encoding_writes_a_file_the_decoder_reads_back()
    {
        foreach (var (width, height) in new[] { (1, 1), (2, 3), (16, 16), (37, 5) })
        {
            var pixels = Pixels(width, height);
            Assert.True(PngWriter.TryEncode(pixels, width, height, out var png));

            Compliant(png, width, height);

            // The data chunk carries one unfiltered scanline after another, the top row first.
            var stride = 1 + width * 4;
            var expected = new byte[height * stride];
            for (var y = 0; y < height; y++)
                pixels.AsSpan(y * width * 4, width * 4).CopyTo(expected.AsSpan(y * stride + 1));
            Assert.Equal(expected, Inflate(Payload(png, "IDAT")));

            Assert.True(PngImage.TryDecode(png, out var image));
            Assert.Equal(width, image.Width);
            Assert.Equal(height, image.Height);
            Assert.Equal(pixels, image.Pixels);
        }
    }

    /// <summary>Encoding refuses what it cannot write, and reports it rather than throwing.</summary>
    [Fact]
    public void The_writer_refuses_what_it_cannot_encode()
    {
        Assert.False(PngWriter.TryEncode(ReadOnlySpan<byte>.Empty, 0, 1, out var png));
        Assert.Null(png);
        Assert.False(PngWriter.TryEncode([], 1, 0, out png));
        Assert.False(PngWriter.TryEncode([], -1, 1, out png));
        Assert.False(PngWriter.TryEncode([], 1, -1, out png));

        // Four bytes per pixel, no more and no less.
        Assert.False(PngWriter.TryEncode(new byte[3], 1, 1, out png));
        Assert.False(PngWriter.TryEncode(new byte[5], 1, 1, out png));
        Assert.False(PngWriter.TryEncode(new byte[4], 1, 2, out png));

        // Past the ceiling of one file, per side.
        Assert.False(PngWriter.TryEncode(new byte[4], 8193, 1, out png));
        Assert.False(PngWriter.TryEncode(new byte[4], 1, 8193, out png));

        // One opaque white pixel, which is the smallest picture there is.
        Assert.True(PngWriter.TryEncode([255, 255, 255, 255], 1, 1, out png));
        Assert.NotNull(png);
    }

    // Everything a reader of a PNG demands: the signature, a chunk layout that adds up, the CRC of every chunk,
    // a header that says eight bit truecolour with alpha and no interlacing, the chunks in the order a simple
    // writer writes them, and the end marker last.
    private static void Compliant(byte[] png, int width, int height)
    {
        Assert.Equal(Signature, png.AsSpan(0, 8).ToArray());

        var kinds = new List<string>();
        foreach (var offset in Chunks(png))
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            var start = offset + 8;
            var kind = Encoding.ASCII.GetString(png, offset + 4, 4);
            Assert.True(start + length + 4 <= png.Length);
            Assert.Equal(
                Crc32(png.AsSpan(offset + 4, 4 + length).ToArray()),
                BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(start + length)));

            if (kind == "IHDR")
            {
                Assert.Equal(width, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(start)));
                Assert.Equal(height, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(start + 4)));
                Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, png.AsSpan(start + 8, 5).ToArray());
            }

            kinds.Add(kind);
        }

        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, kinds);
    }

    // A PNG assembled the way a writer assembles one: the signature, the chunks in the order they are given,
    // and a real CRC at the end of each of them.
    private static byte[] Build(params (string Kind, byte[] Payload)[] chunks)
    {
        using var file = new MemoryStream();
        file.Write(Signature);
        foreach (var (kind, payload) in chunks)
            Chunk(file, kind, payload);
        return file.ToArray();
    }

    private static void Chunk(Stream file, string kind, byte[] payload)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, payload.Length);
        file.Write(number);

        var type = Encoding.ASCII.GetBytes(kind);
        file.Write(type);
        file.Write(payload);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(type, payload));
        file.Write(number);
    }

    // A whole picture: the header, the palette and the transparency when there is one, one deflated data chunk
    // and the end marker.
    private static byte[] Png(
        int width,
        int height,
        byte color,
        byte[] scanlines,
        byte depth = 8,
        byte[]? palette = null,
        byte[]? transparency = null,
        bool interlaced = false)
    {
        var chunks = new List<(string Kind, byte[] Payload)>
        {
            ("IHDR", Header(width, height, color, depth, interlaced)),
        };
        if (palette is not null)
            chunks.Add(("PLTE", palette));
        if (transparency is not null)
            chunks.Add(("tRNS", transparency));
        chunks.Add(("IDAT", Deflate(scanlines)));
        chunks.Add(("IEND", []));
        return Build(chunks.ToArray());
    }

    private static byte[] Header(int width, int height, byte color, byte depth, bool interlaced)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = depth;
        header[9] = color;
        header[12] = interlaced ? (byte)1 : (byte)0;
        return header;
    }

    // The rows of a picture, each one behind the filter byte that says its bytes are unfiltered.
    private static byte[] Rows(params byte[][] rows)
    {
        var raw = new byte[rows.Sum(row => row.Length + 1)];
        var at = 0;
        foreach (var row in rows)
        {
            row.CopyTo(raw, at + 1);
            at += row.Length + 1;
        }

        return raw;
    }

    // The same picture written as the seven Adam7 passes an interlaced file carries: each pass its own sparse
    // rectangle of the image, its rows one after another.
    private static byte[] Interlaced(int width, int height, byte[] rgba)
    {
        var raw = new List<byte>();
        for (var pass = 0; pass < 7; pass++)
        {
            var columns = width > StartX[pass] ? (width - StartX[pass] + StepX[pass] - 1) / StepX[pass] : 0;
            var rows = height > StartY[pass] ? (height - StartY[pass] + StepY[pass] - 1) / StepY[pass] : 0;
            // A pass with no pixel in it is not written at all, not written as empty rows.
            if (columns == 0 || rows == 0)
                continue;

            for (var row = 0; row < rows; row++)
            {
                raw.Add(0);
                for (var column = 0; column < columns; column++)
                {
                    var at = ((StartY[pass] + row * StepY[pass]) * width + StartX[pass] + column * StepX[pass]) * 4;
                    for (var channel = 0; channel < 4; channel++)
                        raw.Add(rgba[at + channel]);
                }
            }
        }

        return raw.ToArray();
    }

    // A picture whose every pixel carries its own value, so that a misplaced pass shows up as a wrong pixel
    // rather than as a colour that happens to match its neighbour.
    private static byte[] Pixels(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = (y * width + x) * 4;
                rgba[at] = (byte)(x * 7 + 1);
                rgba[at + 1] = (byte)(y * 11 + 3);
                rgba[at + 2] = (byte)(x + y * 5 + 17);
                rgba[at + 3] = (byte)(x * 3 + y * 2 + 9);
            }
        }

        return rgba;
    }

    // Where every chunk of a file starts, the signature skipped.
    private static IEnumerable<int> Chunks(byte[] png)
    {
        var offset = 8;
        while (offset + 12 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            var kind = Encoding.ASCII.GetString(png, offset + 4, 4);
            yield return offset;
            offset += 8 + length + 4;
            if (kind == "IEND")
                yield break;
        }
    }

    // The payload of the first chunk of that type.
    private static byte[] Payload(byte[] png, string kind)
    {
        foreach (var offset in Chunks(png))
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            if (Encoding.ASCII.GetString(png, offset + 4, 4) == kind)
                return png.AsSpan(offset + 8, length).ToArray();
        }

        return [];
    }

    private static byte[] Deflate(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw);
        return output.ToArray();
    }

    private static byte[] Inflate(byte[] compressed)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress))
            zlib.CopyTo(output);
        return output.ToArray();
    }

    // The CRC the tests build their files with, computed here rather than borrowed from the code under test so
    // that the two are checked against something that does not share their table. The reference vector test
    // pins it.
    private static uint Crc32(params byte[][] runs)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var run in runs)
        {
            foreach (var value in run)
                crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var value = n;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            table[n] = value;
        }

        return table;
    }

    // The Adam7 passes, as the spec writes them: where a pass starts and how far apart its samples are.
    private static readonly byte[] StartX = [0, 4, 0, 2, 0, 1, 0];

    private static readonly byte[] StartY = [0, 0, 4, 0, 2, 0, 1];

    private static readonly byte[] StepX = [8, 8, 4, 4, 2, 2, 1];

    private static readonly byte[] StepY = [8, 8, 8, 4, 4, 2, 2];
}
