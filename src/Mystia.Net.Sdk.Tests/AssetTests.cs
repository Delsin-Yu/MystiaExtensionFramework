using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using Mystia.Assets;
using Mystia.Imgui;
using Mystia.Modding.Bridge;
using Mystia.Numerics;
using Mystia.Scenes;
using Xunit;

namespace Mystia.Tests;

// The asset surface is where a mod stops building engine objects itself: it hands over data, the framework
// hands back handles and files them into the asset pipeline. These tests pin everything the framework decides
// on its own and everything that can be checked without a running engine: the shape of the contract (no engine
// type is ever named), what the factory refuses, how a pixel buffer behaves, and how a PNG or a WAV is read.
// Building the texture, the sprite or the clip out of that data is the engine's part of the job.
public sealed class AssetTests
{
    private static readonly string[] BannedPrefixes = ["UnityEngine", "Il2Cpp", "Il2CppInterop"];

    /// <summary>
    /// The whole point of the facade: a mod may only name framework types, so no member of the factory or the
    /// locator may name anything from the engine or the interop, value types included.
    /// </summary>
    [Theory]
    [InlineData(typeof(IAssetFactory))]
    [InlineData(typeof(IAssetLocator))]
    public void Asset_members_never_name_the_engine(Type contract)
    {
        var framework = contract.Assembly;

        foreach (var member in contract.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            foreach (var type in TypesNamedBy(member))
            {
                var name = type.Namespace ?? string.Empty;

                Assert.DoesNotContain(BannedPrefixes, prefix => name.StartsWith(prefix, StringComparison.Ordinal));
                Assert.True(
                    type.IsGenericParameter
                    || type.Assembly == framework
                    || name.StartsWith("System", StringComparison.Ordinal),
                    $"{contract.Name}.{member.Name} names {type.FullName}, which does not come from the framework.");
            }
        }
    }

    /// <summary>
    /// The handles a mod holds are framework types too, so nothing a mod passes between the factory, the
    /// locator and the game can be an engine object it built itself.
    /// </summary>
    [Fact]
    public void Asset_types_are_framework_types()
    {
        foreach (var type in new[]
                 {
                     typeof(IAssetFactory),
                     typeof(IAssetLocator),
                     typeof(SpriteHandle),
                     typeof(AudioClipHandle),
                     typeof(PixelBuffer),
                     typeof(AssetReference),
                     typeof(WavAudio),
                 })
        {
            Assert.Equal("Mystia", type.Namespace?.Split('.')[0]);
            Assert.Equal("Mystia.Net.Sdk", type.Assembly.GetName().Name);
        }

        // The texture handle is the one the drawer already hands out, so a texture a mod built once is both
        // drawn and cut into sprites.
        Assert.Equal(
            typeof(TextureHandle),
            typeof(IAssetFactory).GetMethod(nameof(IAssetFactory.TryCreateSolidTexture))!.GetParameters()[1].ParameterType.GetElementType());
    }

    /// <summary>
    /// The capability split: a mod reaches the asset surface from anywhere, so both members hang off the
    /// always available services rather than off a scene.
    /// </summary>
    [Fact]
    public void Common_services_carry_the_asset_surface()
    {
        Assert.Equal(typeof(IAssetFactory), typeof(ICommonServices).GetProperty(nameof(ICommonServices.Assets))!.PropertyType);
        Assert.Equal(typeof(IAssetLocator), typeof(ICommonServices).GetProperty(nameof(ICommonServices.Locator))!.PropertyType);
    }

    /// <summary>
    /// Bad input is reported, never thrown: a mod that feeds a malformed file into the factory stays in
    /// control of what happens next. Nothing here reaches the engine — every one of these refusals is decided
    /// before the first engine call, which is what lets the test run outside the game process.
    /// </summary>
    [Fact]
    public void Factory_refuses_bad_input()
    {
        IAssetFactory factory = UnityAssetFactory.Shared;

        Assert.False(factory.TryCreateTexture(ReadOnlySpan<byte>.Empty, out var texture));
        Assert.Null(texture);

        // Bytes that are not an image at all, and bytes too short to carry a header.
        Assert.False(factory.TryCreateTexture([1, 2, 3], out texture));
        Assert.Null(texture);
        Assert.False(factory.TryCreateTexture(new byte[64], out texture));

        Assert.False(factory.TryCreatePixelTexture(0, 16, out var pixels));
        Assert.Null(pixels);
        Assert.False(factory.TryCreatePixelTexture(16, 0, out pixels));
        Assert.False(factory.TryCreatePixelTexture(-1, -1, out pixels));
        Assert.False(factory.TryCreatePixelTexture(4096, 4096, out pixels));

        // A clip the game cannot report, samples that are not whole frames, a channel count or a rate the
        // engine cannot build, and a value that is not a sample at all.
        Assert.False(factory.TryCreateAudioClip(string.Empty, [0f], 1, 44100, out var clip));
        Assert.Null(clip);
        Assert.False(factory.TryCreateAudioClip("   ", [0f], 1, 44100, out clip));
        Assert.False(factory.TryCreateAudioClip("clip", ReadOnlySpan<float>.Empty, 1, 44100, out clip));
        Assert.False(factory.TryCreateAudioClip("clip", [0f, 0f], 0, 44100, out clip));
        Assert.False(factory.TryCreateAudioClip("clip", [0f, 0f], 9, 44100, out clip));
        Assert.False(factory.TryCreateAudioClip("clip", [0f, 0f], 2, 0, out clip));
        Assert.False(factory.TryCreateAudioClip("clip", [0f], 2, 44100, out clip));
        Assert.False(factory.TryCreateAudioClip("clip", [0f, float.NaN], 2, 44100, out clip));

        // No texture, a rectangle of nothing, a pivot outside the rectangle and a scale of zero.
        Assert.False(factory.TryCreateSprite(null!, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 48f, out var sprite));
        Assert.Null(sprite);
        Assert.False(factory.TryCreateSprite(TextureHandle.White, new Rect(0f, 0f, 0f, 0f), new Vector2(0.5f, 0.5f), 48f, out sprite));
        Assert.False(factory.TryCreateSprite(TextureHandle.White, new Rect(0f, 0f, 1f, 1f), new Vector2(1.5f, 0.5f), 48f, out sprite));
        Assert.False(factory.TryCreateSprite(TextureHandle.White, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 0f, out sprite));

        Assert.False(factory.TryCreateSolidTexture(new Color(float.NaN, 0f, 0f), out texture));
        Assert.False(factory.TryCreateSolidTexture(new Color(0f, float.PositiveInfinity, 0f), out texture));
        Assert.Null(texture);
    }

    /// <summary>
    /// A one pixel texture of opaque white is the engine's own white texture: the flat colour a panel is
    /// filled with costs nothing to build, and a mod gets the same handle the drawer hands out as
    /// <see cref="IIMGUIDrawer.WhiteTexture"/>.
    /// </summary>
    [Fact]
    public void Solid_white_resolves_to_the_built_in_white_texture()
    {
        IAssetFactory factory = UnityAssetFactory.Shared;

        Assert.True(factory.TryCreateSolidTexture(Color.White, out var texture));
        Assert.NotNull(texture);
        Assert.Same(TextureHandle.White, texture);
    }

    /// <summary>
    /// A pixel buffer is the CPU side of a texture: it holds its own copy, nothing reaches the texture until
    /// it is applied, and the rows run bottom up exactly like the engine's own.
    /// </summary>
    [Fact]
    public void Pixel_buffer_paints_and_uploads()
    {
        Color[]? written = null;
        var buffer = new PixelBuffer(2, 2, TextureHandle.White, pixels => written = pixels.ToArray());

        Assert.Equal(2, buffer.Width);
        Assert.Equal(2, buffer.Height);
        Assert.Same(TextureHandle.White, buffer.Texture);

        // A buffer starts as transparent black, and building it uploaded nothing.
        Assert.Equal(new Color(0f, 0f, 0f, 0f), buffer.Get(0, 0));
        Assert.Null(written);

        buffer.Set(1, 1, Color.White);
        Assert.Equal(Color.White, buffer.Get(1, 1));
        Assert.Equal(new Color(0f, 0f, 0f, 0f), buffer.Get(0, 0));

        buffer.Fill(new Color(0.25f, 0.5f, 0.75f, 1f));
        Assert.Equal(new Color(0.25f, 0.5f, 0.75f, 1f), buffer.Get(0, 0));
        Assert.Equal(new Color(0.25f, 0.5f, 0.75f, 1f), buffer.Get(1, 1));

        // A block write is row major from its own bottom left corner, like the engine's SetPixels.
        buffer.Set(0, 0, 2, 1, [Color.Black, new Color(1f, 0f, 0f, 1f)]);
        Assert.Equal(Color.Black, buffer.Get(0, 0));
        Assert.Equal(new Color(1f, 0f, 0f, 1f), buffer.Get(1, 0));
        Assert.Equal(new Color(0.25f, 0.5f, 0.75f, 1f), buffer.Get(0, 1));

        var block = new Color[2];
        buffer.Get(0, 0, 2, 1, block);
        Assert.Equal(Color.Black, block[0]);
        Assert.Equal(new Color(1f, 0f, 0f, 1f), block[1]);

        buffer.Apply();
        Assert.NotNull(written);
        Assert.Equal(4, written!.Length);
        Assert.Equal(Color.Black, written[0]);
        Assert.Equal(new Color(1f, 0f, 0f, 1f), written[1]);
        Assert.Equal(new Color(0.25f, 0.5f, 0.75f, 1f), written[2]);
        Assert.Equal(new Color(0.25f, 0.5f, 0.75f, 1f), written[3]);
    }

    /// <summary>A write the buffer cannot hold is a mod's own arithmetic mistake and is reported as one.</summary>
    [Fact]
    public void Pixel_buffer_checks_its_bounds()
    {
        var buffer = new PixelBuffer(2, 2, TextureHandle.White, _ => { });

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Set(2, 0, Color.White));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Get(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Set(-1, 0, Color.White));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Set(0, 0, 0, 1, ReadOnlySpan<Color>.Empty));
        Assert.Throws<ArgumentException>(() => buffer.Set(0, 0, 2, 1, [Color.Black]));
        Assert.Throws<ArgumentException>(() => buffer.Get(0, 0, 1, 1, new Color[2]));
    }

    /// <summary>
    /// Asking the asset table about a key is a plain lookup: it never builds the table, so it answers — with a
    /// miss — long before anything was registered, and refuses what it cannot file.
    /// </summary>
    [Fact]
    public void Locator_answers_before_anything_was_filed()
    {
        IAssetLocator locator = AssetLocator.Shared;
        const string key = "mystia.tests/never-filed";

        Assert.False(locator.IsRegistered(key));
        Assert.False(locator.TryGetReference(key, out var reference));
        Assert.Null(reference);
        Assert.False(locator.TryResolveSprite(key, out var sprite));
        Assert.Null(sprite);
        Assert.False(locator.TryResolveAudioClip(key, out var clip));
        Assert.Null(clip);
        Assert.False(locator.Unregister(key));

        // An empty key, or a handle the framework did not build, is refused before the engine is reached.
        Assert.False(locator.TryRegisterSprite(string.Empty, null!, out reference));
        Assert.False(locator.TryRegisterAudioClip(string.Empty, null!, out _));
        Assert.False(locator.TryRegisterAudioClip(key, null!, out _));
        Assert.False(locator.TryGetReference(string.Empty, out reference));
        Assert.False(locator.IsRegistered(string.Empty));
    }

    /// <summary>
    /// The reference a registration reports is a key and the address that key is filed under. A mod may build
    /// the engine's own reference from the address, which is what makes the address part of the contract.
    /// </summary>
    [Fact]
    public void Reference_carries_its_key_and_address()
    {
        var reference = new AssetReference("mystia.tests/panel", "8f14e45fceea167a5a36dedd4bea2543");

        Assert.Equal("mystia.tests/panel", reference.Key);
        Assert.Equal("8f14e45fceea167a5a36dedd4bea2543", reference.Address);
        Assert.Equal(reference.Address, reference.ToString());
    }

    /// <summary>
    /// A PNG is read without the engine: every scanline is unfiltered, a colour type the decoder does not read
    /// is refused, and the pixels come back top down with four bytes each — which is what the bridge flips
    /// while uploading, because the engine stores its rows bottom up.
    /// </summary>
    [Fact]
    public void Png_decodes_what_the_game_loader_reads()
    {
        // Two pixels, truecolour with alpha, no filter.
        Assert.True(PngImage.TryDecode(Png(2, 1, 6, [0, 255, 0, 0, 255, 0, 0, 255, 255]), out var image));
        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 }, image.Pixels);

        // Truecolour without alpha, and grey without and with one.
        Assert.True(PngImage.TryDecode(Png(1, 1, 2, [0, 10, 20, 30]), out image));
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, image.Pixels);
        Assert.True(PngImage.TryDecode(Png(1, 1, 0, [0, 40]), out image));
        Assert.Equal(new byte[] { 40, 40, 40, 255 }, image.Pixels);
        Assert.True(PngImage.TryDecode(Png(1, 1, 4, [0, 40, 128]), out image));
        Assert.Equal(new byte[] { 40, 40, 40, 128 }, image.Pixels);

        // Two rows, the second one filtered with Sub: every byte is added to the pixel before it, and the sum
        // wraps inside a byte exactly like the engine's own unfiltering (255 + 255 is 254, not 510).
        Assert.True(PngImage.TryDecode(
            Png(2, 2, 6, [0, 10, 0, 0, 255, 20, 0, 0, 255, 1, 5, 0, 0, 255, 5, 0, 0, 255]),
            out image));
        Assert.Equal(2, image.Height);
        Assert.Equal(new byte[] { 10, 0, 0, 255, 20, 0, 0, 255 }, image.Pixels.AsSpan(0, 8).ToArray());
        Assert.Equal(new byte[] { 5, 0, 0, 255, 10, 0, 0, 254 }, image.Pixels.AsSpan(8, 8).ToArray());

        // An indexed image, which is what pixel art is usually written as: one byte per pixel at 8 bits, and
        // two pixels per byte at 4.
        Assert.True(PngImage.TryDecode(Png(2, 1, 3, [0, 1, 0], palette: [255, 0, 0, 0, 255, 0]), out image));
        Assert.Equal(new byte[] { 0, 255, 0, 255, 255, 0, 0, 255 }, image.Pixels);
        Assert.True(PngImage.TryDecode(Png(2, 1, 3, [0, 0x10], depth: 4, palette: [255, 0, 0, 0, 255, 0]), out image));
        Assert.Equal(new byte[] { 0, 255, 0, 255, 255, 0, 0, 255 }, image.Pixels);
    }

    /// <summary>An image this decoder cannot read is refused rather than half read.</summary>
    [Fact]
    public void Png_refuses_what_it_cannot_read()
    {
        Assert.False(PngImage.TryDecode(ReadOnlySpan<byte>.Empty, out _));
        Assert.False(PngImage.TryDecode(new byte[64], out _));
        Assert.False(PngImage.TryDecode(Png(1, 1, 5, [0, 1]), out _));                      // no such colour type
        Assert.False(PngImage.TryDecode(Png(1, 1, 3, [0, 1], palette: [1, 2, 3]), out _));  // an index outside the palette
        Assert.False(PngImage.TryDecode(Png(1, 1, 3, [0, 1]), out _));                      // indexed without a palette
        Assert.False(PngImage.TryDecode(Png(1, 1, 3, [0, 1], palette: [1, 2]), out _));     // a palette that is not triples
        Assert.False(PngImage.TryDecode(Png(1, 1, 3, [0, 1], depth: 16, palette: [1, 2, 3]), out _));
        Assert.False(PngImage.TryDecode(Png(1, 1, 0, [0, 1], depth: 3), out _));            // no such bit depth
        Assert.False(PngImage.TryDecode(Png(1, 1, 2, [0, 1], depth: 4), out _));            // truecolour at four bits
        Assert.False(PngImage.TryDecode(Png(0, 1, 6, [0]), out _));                         // no pixels
        Assert.False(PngImage.TryDecode(Png(1, 1, 6, [0, 1], filter: 9), out _));           // no such filter
        Assert.False(PngImage.TryDecode(Truncated(Png(1, 1, 6, [0, 1, 2, 3]), 40), out _)); // cut short
        Assert.False(PngImage.TryDecode(Truncated(Png(1, 1, 6, [0, 1, 2, 3]), 43), out _));
    }

    /// <summary>
    /// A WAV file becomes the interleaved float samples the factory takes, with the scaling the game's own
    /// audio expects, and a file the decoder cannot read is refused.
    /// </summary>
    [Fact]
    public void Wav_decodes_pcm_and_refuses_what_it_cannot_read()
    {
        // One frame of stereo, half scale each way.
        Assert.True(WavAudio.TryDecode(Wav([16384, -16384], 2, 44100), out var audio));
        Assert.Equal(2, audio.Channels);
        Assert.Equal(44100, audio.SampleRate);
        Assert.Equal(1, audio.Frames);
        Assert.Equal(0.5f, audio.Samples[0]);
        Assert.Equal(-0.5f, audio.Samples[1]);

        // A frame cut short at the end of the data chunk is dropped rather than played as half a frame.
        Assert.True(WavAudio.TryDecode(Wav([16384, -16384, 16384], 2, 44100), out audio));
        Assert.Equal(1, audio.Frames);
        Assert.Equal(2, audio.Samples.Length);

        Assert.True(WavAudio.TryDecode(Wav([0], 1, 8000), out audio));
        Assert.Equal(1, audio.Channels);
        Assert.Equal(8000, audio.SampleRate);
        Assert.Equal(0f, audio.Samples[0]);

        Assert.True(WavAudio.TryDecode(Wav([-32768], 1, 48000), out audio));
        Assert.Equal(-1f, audio.Samples[0]);

        Assert.False(WavAudio.TryDecode(ReadOnlySpan<byte>.Empty, out _));
        Assert.False(WavAudio.TryDecode(new byte[44], out _));

        var riff = Wav([1, 2], 1, 44100);
        riff[0] = (byte)'X';
        Assert.False(WavAudio.TryDecode(riff, out _));

        // ADPCM, and a file that claims floats but does not carry 32 bit ones.
        var adpcm = Wav([1, 2], 1, 44100);
        BinaryPrimitives.WriteInt16LittleEndian(adpcm.AsSpan(20), 2);
        Assert.False(WavAudio.TryDecode(adpcm, out _));

        var float16 = Wav([1, 2], 1, 44100);
        BinaryPrimitives.WriteInt16LittleEndian(float16.AsSpan(20), 3);
        Assert.False(WavAudio.TryDecode(float16, out _));

        // A chunk that claims more bytes than the file holds.
        var truncated = Wav([1, 2], 1, 44100);
        BinaryPrimitives.WriteInt32LittleEndian(truncated.AsSpan(40), 4096);
        Assert.False(WavAudio.TryDecode(truncated, out _));
    }

    // A PNG built the way a writer builds one: the signature, the header, the palette when there is one, one
    // deflated data chunk and the end marker, every chunk carrying the CRC of its own type and payload.
    private static byte[] Png(
        int width,
        int height,
        byte colorType,
        byte[] scanlines,
        byte depth = 8,
        byte? filter = null,
        byte[]? palette = null)
    {
        if (filter is { } forced && scanlines.Length > 0)
            scanlines[0] = forced;

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = depth;
        header[9] = colorType;

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", header);
        if (palette is not null)
            Chunk(png, "PLTE", palette);
        Chunk(png, "IDAT", Deflate(scanlines));
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string kind, byte[] payload)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, payload.Length);
        png.Write(number);

        var type = System.Text.Encoding.ASCII.GetBytes(kind);
        png.Write(type);
        png.Write(payload);
        BinaryPrimitives.WriteUInt32BigEndian(number, PngCrc32.Compute(type, payload));
        png.Write(number);
    }

    private static byte[] Deflate(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw);
        return output.ToArray();
    }

    private static byte[] Truncated(byte[] image, int length)
    {
        var cut = new byte[length];
        image.AsSpan(0, length).CopyTo(cut);
        return cut;
    }

    // A 16 bit PCM WAV: the header the decoder reads and the samples themselves.
    private static byte[] Wav(short[] samples, int channels, int sampleRate)
    {
        var data = new byte[samples.Length * 2];
        for (var index = 0; index < samples.Length; index++)
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(index * 2), samples[index]);

        var wav = new byte[44 + data.Length];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 36 + data.Length);
        "WAVE"u8.CopyTo(wav.AsSpan(8));
        "fmt "u8.CopyTo(wav.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), sampleRate * channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), data.Length);
        data.CopyTo(wav.AsSpan(44));
        return wav;
    }

    private static IEnumerable<Type> TypesNamedBy(MemberInfo member)
    {
        var named = member switch
        {
            PropertyInfo property => new[] { property.PropertyType }
                .Concat(property.GetIndexParameters().Select(parameter => parameter.ParameterType)),
            MethodInfo method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType),
            FieldInfo field => [field.FieldType],
            EventInfo @event when @event.EventHandlerType is { } handler => [handler],
            _ => [],
        };

        return named.SelectMany(Flatten);
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.IsArray && type.GetElementType() is { } element)
            yield return element;

        foreach (var argument in type.GetGenericArguments())
            yield return argument;
    }
}
