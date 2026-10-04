using System.Diagnostics.CodeAnalysis;
using Mystia.Imgui;
using Mystia.Numerics;

namespace Mystia.Assets;

// The entry point for building engine assets out of data a mod carries. Nothing on this surface names an
// engine type: a mod hands over bytes, samples and colours, and gets back the framework's opaque handles,
// which it can only ever hand back (to the locator, or to the IMGUI drawer when it draws the texture).
//
// Every member builds an engine object, so it belongs to the main thread. The services object a mod reaches
// this through is valid from any thread, but the object it produces is not, so a background thread hops
// first with MainThread.
/// <summary>
/// Builds the assets a mod draws, plays, wears or files into the game's own asset pipeline. A mod hands over
/// data it carries — encoded image bytes, decoded audio samples, colours, sprites it already cut — and receives
/// an opaque handle; no engine object is ever named here, which is why a mod cannot build one behind the
/// framework's back.
/// <para>
/// Every member creates an engine object, so every member is main thread only: from a background thread hop
/// with <c>ICommonServices.MainThread</c> first. Nothing here throws over data a mod got wrong — bad input is
/// reported as <see langword="false"/> with a null handle, so a mod that feeds a malformed file into the
/// factory stays in control of what happens next.
/// </para>
/// </summary>
public interface IAssetFactory
{
    /// <summary>
    /// Decodes an encoded image into a texture, built ready for pixel art: point filtering, clamped wrapping
    /// and hidden from the scene teardown, so nothing the game does to a loaded scene releases a texture a
    /// mod still draws with.
    /// <para>
    /// The decode is the framework's own — the engine's image module is not part of the interop set this
    /// framework is built against — and it reads the PNGs art is written as: grey, truecolour, indexed and
    /// grey or truecolour with an alpha channel, at 1, 2, 4, 8 or 16 bits per sample, with the palette and
    /// with the transparency a <c>tRNS</c> chunk adds, interlaced (Adam7) included. No other encoding: a JPEG
    /// is refused, and no picture past 4096×4096 is read. What it cannot read comes back as false, and a chunk
    /// whose CRC does not match is only reported.
    /// </para>
    /// </summary>
    /// <param name="encodedImage">The bytes of the file. Empty bytes are refused.</param>
    /// <param name="texture">The decoded texture, or null when the bytes are not an image this build reads.</param>
    bool TryCreateTexture(ReadOnlySpan<byte> encodedImage, [NotNullWhen(true)] out TextureHandle? texture);

    /// <summary>
    /// Builds a sprite out of one rectangle of a texture, which is how a mod cuts a sprite sheet into the
    /// sprites the game expects.
    /// </summary>
    /// <param name="texture">A texture this factory produced.</param>
    /// <param name="rect">The part of the texture the sprite spans, in pixels from the bottom left corner.</param>
    /// <param name="pivot">The pivot inside that rectangle, normalized: (0.5, 0) is the bottom middle.</param>
    /// <param name="pixelsPerUnit">The pixels per world unit; must be positive.</param>
    /// <param name="sprite">The sprite, or null when the texture or the geometry was refused.</param>
    bool TryCreateSprite(
        TextureHandle texture,
        Rect rect,
        Vector2 pivot,
        float pixelsPerUnit,
        [NotNullWhen(true)] out SpriteHandle? sprite);

    /// <summary>
    /// Reports the pixel size of a texture, which is what a mod needs when the layout of a sprite sheet decides
    /// what the sheet is: a mod that cuts a skin into frames reads the size before it can cut anything.
    /// <para>
    /// The engine is asked, never a header the framework could guess at: what a mod hands in may be a texture
    /// the factory decoded, the engine's own white texture, or a texture the game handed it.
    /// </para>
    /// </summary>
    /// <param name="texture">A texture this factory produced, or the engine's own white texture.</param>
    /// <param name="width">The width in pixels; zero when the texture was refused.</param>
    /// <param name="height">The height in pixels; zero when the texture was refused.</param>
    /// <returns>False when the handle is not a texture this framework built.</returns>
    bool TryGetTextureSize(TextureHandle texture, out int width, out int height);

    /// <summary>
    /// Reads a texture's pixels back into a buffer a mod can inspect, the counterpart of
    /// <see cref="TryCreatePixelTexture"/>: cutting one texture into another means reading the source first.
    /// <para>
    /// The buffer holds copies on the CPU, laid out the way the engine lays a texture out — the origin is the
    /// bottom left corner and y grows upwards — and it uploads back into the same texture through
    /// <see cref="PixelBuffer.Apply"/>, so a mod may read a texture, paint it and write it back.
    /// </para>
    /// </summary>
    /// <param name="texture">A texture this factory produced; the engine reads back only a texture it can reach
    /// its pixels of, which a texture the game unpacked from a bundled asset is not.</param>
    /// <param name="pixels">The buffer, or null when the texture was refused.</param>
    bool TryReadPixels(TextureHandle texture, [NotNullWhen(true)] out PixelBuffer? pixels);

    /// <summary>
    /// Builds the game's own character pixel art — the set a character wears, which the game's animator draws
    /// its layers out of (<see cref="CharacterSpriteSetKind"/>). The frames are sprites the factory cut, and a
    /// set only carries what the mod states about it: <see cref="CharacterSpriteSetStyle"/> is where a mod says
    /// that its character spins or walks faster, and everything it leaves out keeps the value the game's own
    /// pixel art carries, which is what a mod that brought nothing but art wants.
    /// <para>
    /// The result is put on a character with <c>IPresentationServices.ApplyCharacterSprite</c>; the game's own
    /// sprite set object never leaves the bridge.
    /// </para>
    /// </summary>
    /// <param name="kind">The compact set (main and eyes) or the layered full set (main, eyes, hair and back).</param>
    /// <param name="frames">The frame sprites per layer, grouped as the game's animator indexes them.</param>
    /// <param name="style">What the set does beyond its frames; leave it at
    /// <see cref="CharacterSpriteSetStyle.Default"/> to keep the game's own values.</param>
    /// <param name="set">The set, or null when the frames, the kind or the style was refused.</param>
    bool TryCreateCharacterSpriteSet(
        CharacterSpriteSetKind kind,
        CharacterSpriteSetFrames frames,
        CharacterSpriteSetStyle style,
        [NotNullWhen(true)] out CharacterSpriteSetHandle? set);

    /// <summary>
    /// Copies a character pixel set the game holds — one of its own skins, or the fallback art — with the flags
    /// <paramref name="style"/> states replacing the ones the set carries. A copy keeps everything else the set
    /// has, so it is exact in a way a set built from frames is not: the frames, the trims and the values the
    /// game's own <c>Initialize</c> never takes (where a character sits on the notebook page, how the day scene
    /// highlights it) all come along. This is how a set the game owns gets a movement flag of a mod's own.
    /// <para>
    /// What <paramref name="style"/> leaves unset keeps the copied set's own value, which is where this differs
    /// from <see cref="TryCreateCharacterSpriteSet"/>: there, an unset member takes the game's fallback value.
    /// </para>
    /// </summary>
    /// <param name="set">The game's own set object, either kind.</param>
    /// <param name="style">The flags to replace, unset members leaving the copied set's own.</param>
    /// <param name="copy">The copy, or null when the object was not a set or a value was refused.</param>
    bool TryCopyCharacterSpriteSet(
        object set,
        CharacterSpriteSetStyle style,
        [NotNullWhen(true)] out CharacterSpriteSetHandle? copy);

    /// <summary>
    /// Builds an audio clip out of already decoded samples. The factory does not decode a container: a WAV
    /// file is decoded by <see cref="WavAudio.TryDecode"/> first, and the three values it reports are exactly
    /// the three this method takes.
    /// </summary>
    /// <param name="name">The clip's name. It must not be empty: the game reports and resolves clips by name.</param>
    /// <param name="interleavedSamples">
    /// Every channel's samples, one frame at a time (left, right, left, right, …), each in -1..1.
    /// </param>
    /// <param name="channels">The channels in one frame, 1 to 8.</param>
    /// <param name="sampleRate">The frames per second; must be positive.</param>
    /// <param name="clip">The clip, or null when the samples, the format or the name was refused.</param>
    bool TryCreateAudioClip(
        string name,
        ReadOnlySpan<float> interleavedSamples,
        int channels,
        int sampleRate,
        [NotNullWhen(true)] out AudioClipHandle? clip);

    /// <summary>
    /// Builds an empty, fully transparent texture a mod paints itself. Painting happens on the CPU and only
    /// reaches the engine when <see cref="PixelBuffer.Apply"/> is called, so a mod that computes a tile in
    /// several passes uploads it once.
    /// </summary>
    /// <param name="width">The width in pixels; must be positive.</param>
    /// <param name="height">The height in pixels; must be positive.</param>
    /// <param name="pixels">The buffer, or null when a dimension is not positive or the texture is too large.</param>
    bool TryCreatePixelTexture(int width, int height, [NotNullWhen(true)] out PixelBuffer? pixels);

    /// <summary>
    /// Builds a texture of a single pixel of one colour. An opaque white is the engine's own white texture —
    /// the texture a mod tints with <c>IIMGUIDrawer.Color</c> to fill a panel with a flat colour — so that
    /// request is served without building anything.
    /// </summary>
    /// <param name="color">The colour of the pixel; a component that is not finite is refused.</param>
    /// <param name="texture">The texture, or null when the colour was refused.</param>
    bool TryCreateSolidTexture(Color color, [NotNullWhen(true)] out TextureHandle? texture);

    /// <summary>
    /// Wraps a sprite the game itself holds — a frame of the game's own art, or one a mod reached through the
    /// game's own asset reference — so it can be handed to a surface that speaks in handles, the way a sprite
    /// this factory cut is. Nothing is built and nothing is copied: the handle answers for the object the game
    /// loaded, which is what makes a mod able to say "this one" about art it did not bring.
    /// </summary>
    /// <param name="sprite">The object the game holds. Anything that is not a sprite is refused.</param>
    /// <param name="handle">The handle, or null when the object was refused.</param>
    bool TryWrapSprite(object sprite, [NotNullWhen(true)] out SpriteHandle? handle);

    /// <summary>
    /// Reads a character pixel sprite set the game holds apart into the frames and the style a mod would hand
    /// to <see cref="TryCreateCharacterSpriteSet"/>: the set the game's own fallback art of either kind is
    /// (<c>DataBaseCharacter.FallbackCompactPixel</c> and its layered counterpart), or one a mod reached through
    /// the game's own skin data. That is how a set is rebuilt from the game's own art — the same frames with a
    /// movement flag the mod states itself, or with the frames it painted over replaced by its own.
    /// </summary>
    /// <param name="set">The game's own set object, either kind.</param>
    /// <param name="frames">The set's frames, wrapped as handles; empty when the set was refused.</param>
    /// <param name="style">The set's own movement flags, so a rebuild that states nothing keeps them.</param>
    bool TryUnwrapCharacterSpriteSet(
        object set,
        out CharacterSpriteSetFrames frames,
        out CharacterSpriteSetStyle style);
}
