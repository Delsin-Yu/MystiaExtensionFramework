using System.Diagnostics.CodeAnalysis;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mystia.Assets;
using Mystia.Imgui;
using MirrorColor = Mystia.Numerics.Color;
using MirrorRect = Mystia.Numerics.Rect;
using MirrorVector2 = Mystia.Numerics.Vector2;
using UnityEngine;

namespace Mystia.Modding.Bridge;

// The engine side of Mystia.Assets.IAssetFactory: everything a mod's own resource code used to do to build
// an engine object out of the data it carries. The handles a mod holds are declared in Mystia.Assets and carry
// no engine reference at all; this file is the only place that builds one, which is why it lives under Game/
// with the rest of the interop bound code.
//
// The shapes are the ones the game itself uses for its own art: point filtered and clamped textures hidden
// from the scene teardown (so nothing the game does to a loaded scene releases a texture a mod still draws
// with), sprites cut out of a texture rectangle, and audio clips built from interleaved float samples.
internal sealed class UnityAssetFactory : IAssetFactory
{
    internal static readonly UnityAssetFactory Shared = new();

    // A decoded picture is still a picture when a chunk's CRC does not match: the decoder reports it instead of
    // refusing the file, and the report goes to the bridge trace, where a mod that shipped the file can see it.
    static UnityAssetFactory() => PngImage.Warning = GameBridgeHook.Trace;

    // A pixel buffer keeps four floats per pixel, four times what the RGBA32 texture it stands for costs, so
    // the ceiling is on the buffer rather than on the texture. 2048×2048 is far past what the game's own
    // pixel art needs and keeps a mod's typo from asking for hundreds of megabytes.
    private const int MaximumPixels = 2048 * 2048;

    public bool TryCreateTexture(ReadOnlySpan<byte> encodedImage, [NotNullWhen(true)] out TextureHandle? texture)
    {
        texture = null;
        if (encodedImage.IsEmpty)
            return false;

        // The engine's own decoder is not part of the interop set this framework is built against, so the
        // decode is the framework's own and the engine only receives the finished pixels.
        if (!PngImage.TryDecode(encodedImage, out var image))
            return false;

        Texture2D? created = null;
        try
        {
            created = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false);
            var pixels = new Color32[image.Width * image.Height];
            // The engine stores rows bottom up; the decoder reports them top down.
            for (var y = 0; y < image.Height; y++)
            {
                var source = y * image.Width * 4;
                var target = (image.Height - 1 - y) * image.Width;
                for (var x = 0; x < image.Width; x++)
                {
                    var at = source + x * 4;
                    pixels[target + x] = new Color32(image.Pixels[at], image.Pixels[at + 1], image.Pixels[at + 2], image.Pixels[at + 3]);
                }
            }

            created.SetPixels32(pixels);
            created.Apply(false, false);
            Prepare(created, "MystiaTexture");
            texture = new UnityTextureHandle(created);
            return true;
        }
        catch (Exception error)
        {
            GameBridgeHook.Trace($"AssetFactory: a {image.Width}×{image.Height} texture could not be built: {error.GetBaseException().Message}");
            if (created is not null)
                UnityEngine.Object.DestroyImmediate(created);
            return false;
        }
    }

    public bool TryCreateSprite(
        TextureHandle texture,
        MirrorRect rect,
        MirrorVector2 pivot,
        float pixelsPerUnit,
        [NotNullWhen(true)] out SpriteHandle? sprite)
    {
        sprite = null;
        if (texture is null || !Geometry(rect, pivot, pixelsPerUnit))
            return false;

        var source = ImguiMirror.Resolve(texture) as Texture2D;
        if (source is null)
            return false;

        // A rectangle the texture does not cover is a mod's own arithmetic mistake; the engine would either
        // throw or hand back a sprite of nothing but transparent pixels.
        if (rect.X < 0f || rect.Y < 0f || rect.XMax > source.width || rect.YMax > source.height)
            return false;

        var created = Sprite.Create(
            source,
            new Rect(rect.X, rect.Y, rect.Width, rect.Height),
            new Vector2(pivot.X, pivot.Y),
            pixelsPerUnit);
        created.name = source.name;
        created.hideFlags |= HideFlags.HideAndDontSave;
        sprite = new UnitySpriteHandle(created);
        return true;
    }

    public bool TryGetTextureSize(TextureHandle texture, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (Image(texture) is not { } image)
            return false;

        width = image.width;
        height = image.height;
        return true;
    }

    public bool TryReadPixels(TextureHandle texture, [NotNullWhen(true)] out PixelBuffer? pixels)
    {
        pixels = null;
        if (Image(texture) is not { } image)
            return false;
        // A read back is a full copy of the texture in a buffer, so it is bounded the same way one a mod asks
        // for is: a mod's typo must not ask for hundreds of megabytes.
        if ((long)image.width * image.height > MaximumPixels)
            return false;

        try
        {
            // The engine hands its pixels back rows bottom up, which is the layout the buffer uses, so the
            // values go in exactly as they came out; the buffer uploads back into the same texture.
            var read = image.GetPixels();
            var seed = new MirrorColor[read.Length];
            for (var index = 0; index < read.Length; index++)
                seed[index] = new MirrorColor(read[index].r, read[index].g, read[index].b, read[index].a);

            var buffer = new PixelBuffer(image.width, image.height, texture, new PixelSink(image).Upload);
            buffer.Load(seed);
            pixels = buffer;
            return true;
        }
        catch (Exception error)
        {
            // The engine refuses to read a texture whose pixels it cannot reach (one the game unpacked from a
            // bundled asset, for instance), which is a mod's input rather than a framework failure.
            GameBridgeHook.Trace($"AssetFactory: the pixels of a {image.name} texture could not be read back: {error.GetBaseException().Message}");
            return false;
        }
    }

    public bool TryCreateCharacterSpriteSet(
        CharacterSpriteSetKind kind,
        CharacterSpriteSetFrames frames,
        CharacterSpriteSetStyle style,
        [NotNullWhen(true)] out CharacterSpriteSetHandle? set) =>
        CharacterSprites.TryCreate(kind, frames, style, out set);

    public bool TryCopyCharacterSpriteSet(
        object set,
        CharacterSpriteSetStyle style,
        [NotNullWhen(true)] out CharacterSpriteSetHandle? copy) =>
        CharacterSprites.TryCopy(set, style, out copy);

    /// <summary>
    /// Wraps a sprite the game holds. The object travels as <see cref="object"/> because the surface names no
    /// engine type, so this is where "is what the game handed the mod actually a sprite" is answered.
    /// </summary>
    public bool TryWrapSprite(object sprite, [NotNullWhen(true)] out SpriteHandle? handle)
    {
        handle = null;
        if (sprite is not Sprite engine || engine is null)
            return false;

        handle = new UnitySpriteHandle(engine);
        return true;
    }

    public bool TryUnwrapCharacterSpriteSet(
        object set,
        out CharacterSpriteSetFrames frames,
        out CharacterSpriteSetStyle style) =>
        CharacterSprites.TryUnwrap(set, out frames, out style);

    public bool TryCreateAudioClip(
        string name,
        ReadOnlySpan<float> interleavedSamples,
        int channels,
        int sampleRate,
        [NotNullWhen(true)] out AudioClipHandle? clip)
    {
        clip = null;

        // The game reports a clip by its name, so a nameless clip would be unusable even though the engine
        // would accept it.
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (channels is < 1 or > 8)
            return false;
        if (sampleRate <= 0)
            return false;
        if (interleavedSamples.IsEmpty || interleavedSamples.Length % channels != 0)
            return false;

        foreach (var sample in interleavedSamples)
        {
            if (!float.IsFinite(sample))
                return false;
        }

        try
        {
            var frames = interleavedSamples.Length / channels;
            var created = AudioClip.Create(name, frames, channels, sampleRate, false);
            created.SetData(new Il2CppStructArray<float>(interleavedSamples.ToArray()), 0);
            created.hideFlags |= HideFlags.HideAndDontSave;
            clip = new UnityAudioClipHandle(created);
            return true;
        }
        catch (Exception error)
        {
            GameBridgeHook.Trace($"AssetFactory: the audio clip '{name}' could not be built: {error.GetBaseException().Message}");
            return false;
        }
    }

    public bool TryCreatePixelTexture(int width, int height, [NotNullWhen(true)] out PixelBuffer? pixels)
    {
        pixels = null;
        if (width <= 0 || height <= 0)
            return false;
        if ((long)width * height > MaximumPixels)
            return false;

        Texture2D? image = null;
        try
        {
            image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Prepare(image, "MystiaPixels");
            var buffer = new PixelBuffer(width, height, new UnityTextureHandle(image), new PixelSink(image).Upload);
            // A fresh texture holds undefined bytes; the buffer starts transparent, so the two agree from the
            // first call on and a buffer that is never applied is transparent rather than noise.
            buffer.Apply();
            pixels = buffer;
            return true;
        }
        catch (Exception error)
        {
            GameBridgeHook.Trace($"AssetFactory: a {width}×{height} pixel texture could not be built: {error.GetBaseException().Message}");
            if (image is not null)
                UnityEngine.Object.DestroyImmediate(image);
            return false;
        }
    }

    public bool TryCreateSolidTexture(MirrorColor color, [NotNullWhen(true)] out TextureHandle? texture)
    {
        texture = null;
        if (!float.IsFinite(color.R) || !float.IsFinite(color.G) || !float.IsFinite(color.B) || !float.IsFinite(color.A))
            return false;

        // The engine's own white texture is exactly a single pixel of opaque white, so a panel background
        // costs nothing to build.
        if (color is { R: 1f, G: 1f, B: 1f, A: 1f })
        {
            texture = TextureHandle.White;
            return true;
        }

        var image = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        Prepare(image, "MystiaSolid");
        image.SetPixel(0, 0, new Color(color.R, color.G, color.B, color.A));
        image.Apply(false, false);
        texture = new UnityTextureHandle(image);
        return true;
    }

    private static bool Geometry(MirrorRect rect, MirrorVector2 pivot, float pixelsPerUnit)
    {
        if (!float.IsFinite(rect.X) || !float.IsFinite(rect.Y) || !float.IsFinite(rect.Width) || !float.IsFinite(rect.Height))
            return false;
        if (rect.Width <= 0f || rect.Height <= 0f)
            return false;
        if (!float.IsFinite(pivot.X) || !float.IsFinite(pivot.Y) || pivot.X is < 0f or > 1f || pivot.Y is < 0f or > 1f)
            return false;
        return float.IsFinite(pixelsPerUnit) && pixelsPerUnit > 0f;
    }

    // The engine texture behind a handle, or null for anything else: a handle the framework did not build (a
    // mod cannot build one itself, but another component of the framework could) is a miss, not a throw, so
    // every entry that takes a texture answers false rather than throwing at it.
    private static Texture2D? Image(TextureHandle? texture) => texture switch
    {
        UnityTextureHandle mirror => mirror.Texture as Texture2D,
        _ when ReferenceEquals(texture, TextureHandle.White) => Texture2D.whiteTexture,
        _ => null,
    };

    // What every texture a mod builds looks like: the game draws its own art point filtered, and a texture
    // hidden from the scene teardown survives a scene change a mod's handle outlives.
    private static void Prepare(Texture2D texture, string name)
    {
        texture.name = name;
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.hideFlags |= HideFlags.HideAndDontSave;
    }
}

// A sprite the factory cut out of a texture: the engine object stays here and the mod only ever passes the
// handle back, either to the locator that files it or to the factory that cuts the next one.
internal sealed class UnitySpriteHandle : SpriteHandle
{
    internal UnitySpriteHandle(Sprite sprite) => Sprite = sprite;

    internal Sprite Sprite { get; }
}

internal sealed class UnityAudioClipHandle : AudioClipHandle
{
    internal UnityAudioClipHandle(AudioClip clip) => Clip = clip;

    internal AudioClip Clip { get; }
}

// Uploads a pixel buffer into the texture it stands for. The scratch array is reused between applies: an
// apply happens on the main thread and the engine copies the values out of the array during the call, so one
// array serves every upload a mod makes.
internal sealed class PixelSink
{
    private readonly Texture2D _texture;
    private Color[] _scratch = [];

    internal PixelSink(Texture2D texture) => _texture = texture;

    internal void Upload(ReadOnlySpan<MirrorColor> pixels)
    {
        if (_scratch.Length != pixels.Length)
            _scratch = new Color[pixels.Length];
        for (var index = 0; index < pixels.Length; index++)
        {
            var pixel = pixels[index];
            _scratch[index] = new Color(pixel.R, pixel.G, pixel.B, pixel.A);
        }

        _texture.SetPixels(_scratch);
        _texture.Apply(false, false);
    }
}
