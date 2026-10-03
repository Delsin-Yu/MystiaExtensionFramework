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
/// Builds the assets a mod draws, plays or files into the game's own asset pipeline. A mod hands over data it
/// carries — encoded image bytes, decoded audio samples, colours — and receives an opaque handle; no engine
/// object is ever named here, which is why a mod cannot build one behind the framework's back.
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
    /// framework is built against — and it reads the PNGs art is written as: 8 bit grey and truecolour, with
    /// or without an alpha channel, and indexed images at 1, 2, 4 or 8 bits per pixel. No interlaced image
    /// and no other encoding: a JPEG is refused.
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
}
