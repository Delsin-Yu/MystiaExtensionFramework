using Mystia.Assets;
using Mystia.Numerics;

namespace Mystia.Scenes;

/// <summary>
/// The presentation members of a scene session: the camera, the effect and audio layers, the floating text a
/// scene shows, and the world positions the running scene owns.
///
/// They act on the scene that is running right now, so they are only valid inside the <c>Setup</c>,
/// <c>Update</c> and <c>Shutdown</c> of the scene loop they were handed to; a scene change invalidates them
/// and every call made outside that window throws. A global loop never sees this interface.
/// <para>
/// No member here names an engine type: a mod passes framework handles and mirrored value types, and the
/// bridge does the engine work. An asset is named by the key it was filed under — an <c>IAssetLocator</c> key
/// for audio, a key <see cref="TryRegisterPrefab"/> filed for an effect — so the shared service never has to
/// know which mod is calling it.
/// </para>
/// </summary>
public interface IPresentationServices
{
    /// <summary>Shakes the camera for the given duration, with the given strength and frequency.</summary>
    void ShakeCamera(float duration, float strength, float frequency) => throw new NotSupportedException();

    /// <summary>
    /// Plays the effect template filed under <paramref name="assetPath"/> at a world position, and returns a
    /// handle for stopping it early.
    /// </summary>
    /// <param name="assetPath">The key the effect template was filed under.</param>
    /// <param name="position">The world position the effect is instantiated at.</param>
    /// <returns>
    /// A handle for the playing effect, or null when <paramref name="assetPath"/> was empty. A key nothing was
    /// filed under is reported and yields an inert handle whose <see cref="IVfxHandle.Stop"/> does nothing, so
    /// a well formed call always hands the caller a handle it may stop.
    /// </returns>
    IVfxHandle? PlayVfx(string assetPath, Vector3 position) => throw new NotSupportedException();

    /// <summary>
    /// Plays the template filed under <paramref name="assetPath"/> as a full screen overlay: every sprite
    /// renderer the template carries becomes one full screen layer of an overlay canvas, which is faded in on
    /// play and faded out by <see cref="IVfxHandle.Stop"/> before it is destroyed.
    /// </summary>
    /// <param name="assetPath">The key the overlay template was filed under.</param>
    /// <returns>A handle for the overlay, or null when <paramref name="assetPath"/> was empty.</returns>
    IVfxHandle? PlayScreenOverlay(string assetPath) => throw new NotSupportedException();

    /// <summary>Plays the audio clip filed under <paramref name="assetPath"/>, once.</summary>
    /// <param name="assetPath">The key the clip was filed under; an empty key or a miss is reported.</param>
    void PlayAudio(string assetPath) => throw new NotSupportedException();

    /// <summary>
    /// Files a game object a mod is working with as an effect template, under the key <see cref="PlayVfx"/> and
    /// <see cref="PlayScreenOverlay"/> resolve. The framework clones the object — the mod keeps its own
    /// instance — hides the clone and keeps it alive for the process.
    /// </summary>
    /// <param name="key">The key the template is resolved by; must not be empty.</param>
    /// <param name="source">The game object (or component) to clone; anything else is refused.</param>
    /// <returns>True when the template was filed.</returns>
    bool TryRegisterPrefab(string key, object source) => false;

    /// <summary>
    /// Wraps a host object a label hangs on — a component, a game object or a transform — into the opaque
    /// handle <see cref="SpawnLabel"/> and <see cref="AttachLabel"/> take. Main thread only.
    /// </summary>
    /// <param name="host">The object whose transform the label follows.</param>
    /// <returns>The handle, or null when <paramref name="host"/> is not an engine object.</returns>
    TransformHandle? Bind(object host) => null;

    /// <summary>
    /// Hangs a temporary floating line on <paramref name="host"/>: the text appears, stays for
    /// <paramref name="lifetimeSeconds"/> and then fades out and is destroyed. This is the one shot path.
    /// </summary>
    /// <param name="host">A handle <see cref="Bind"/> produced.</param>
    /// <param name="text">The line to show; empty text is refused.</param>
    /// <param name="style">The offset, colour and font size of the line.</param>
    /// <param name="lifetimeSeconds">How long the line stays before it fades; zero fades it at once.</param>
    /// <returns>The label, or null when the input was refused.</returns>
    IFloatingLabel? SpawnLabel(TransformHandle host, string text, FloatingLabelStyle style, float lifetimeSeconds = 5f) => null;

    /// <summary>
    /// Hangs a nameplate on <paramref name="host"/>: it stays until it is stopped or its host is destroyed.
    /// This is the long lived path, the one a player's own label uses.
    /// </summary>
    /// <param name="host">A handle <see cref="Bind"/> produced.</param>
    /// <param name="text">The name to show; empty text is refused.</param>
    /// <param name="style">The offset, colour and font size of the label.</param>
    /// <returns>The label, or null when the input was refused.</returns>
    IFloatingLabel? AttachLabel(TransformHandle host, string text, FloatingLabelStyle style) => null;

    /// <summary>World position of the player character.</summary>
    Vector3 PlayerPosition => throw new NotSupportedException();

    /// <summary>World position of the table of a desk.</summary>
    Vector3 TablePosition(int deskCode) => throw new NotSupportedException();
}

/// <summary>
/// A piece of text hung on a host object in the world: a temporary floating line
/// (<see cref="IPresentationServices.SpawnLabel"/>) or a nameplate that lives as long as its host
/// (<see cref="IPresentationServices.AttachLabel"/>). A mod holds the handle and never the engine object.
/// </summary>
public interface IFloatingLabel : IDisposable
{
    /// <summary>Rewrites the text. False when the label already ended or the text was empty.</summary>
    bool SetText(string text);

    /// <summary>Shows or hides the label without ending it.</summary>
    void SetVisible(bool visible);

    /// <summary>Ends the label now. Stopping twice is harmless.</summary>
    void Stop();
}

/// <summary>
/// How a floating label is drawn: where it sits relative to its host, its colour and its font size.
/// The default is a white 5 point line two world units above the host, which is what a floating text wants.
/// </summary>
/// <param name="Offset">The label's local position relative to the host.</param>
/// <param name="Color">The text colour, alpha included.</param>
/// <param name="FontSize">The font size; must be positive.</param>
public readonly record struct FloatingLabelStyle(Vector3 Offset, Color Color, float FontSize)
{
    /// <summary>The style a floating line uses when the caller has no opinion.</summary>
    public static FloatingLabelStyle Default => new(new Vector3(0f, 2f, 0f), Color.White, 5f);
}
