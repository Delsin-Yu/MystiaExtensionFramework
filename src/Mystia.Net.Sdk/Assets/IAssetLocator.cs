using System.Diagnostics.CodeAnalysis;

namespace Mystia.Assets;

// Files assets into the game's own asset pipeline, so game code that resolves an address and a mod that
// registered one meet on the same key. The engine's own reference type stays behind the bridge; a mod holds
// an AssetReference, which is a key and the address that key is filed under.
//
// Registering reaches into the engine's provider registry, so it is main thread only. The lookups are plain
// dictionary reads over what was registered and never build the table themselves, so a mod may ask about a
// key at any time and from any thread; an empty table reports a miss.
/// <summary>
/// Publishes assets a mod built into the game's asset pipeline, under a key the mod chooses. Registering a
/// sprite or a clip makes it resolvable by that key — the game's own code, a mod, or an <c>AssetReference</c>
/// built from <see cref="AssetReference.Address"/> all reach the same object, because the framework files the
/// asset where the game's own loader looks.
/// <para>
/// Registering is main thread only. A key belongs to the process and not to one mod, so a key has to be
/// namespaced by the mod that owns it; registering a key twice replaces the asset that was filed under it and
/// reports it in the bridge trace.
/// </para>
/// </summary>
public interface IAssetLocator
{
    /// <summary>
    /// Files a sprite under <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The key the sprite is resolved by; must not be empty.</param>
    /// <param name="sprite">A sprite this framework built.</param>
    /// <param name="reference">The reference the asset is filed under, or null when the key or the sprite was refused.</param>
    bool TryRegisterSprite(string key, SpriteHandle sprite, [NotNullWhen(true)] out AssetReference? reference);

    /// <summary>
    /// Files an audio clip under <paramref name="key"/>. The clip keeps the name it was built with, because
    /// the game's own audio code looks clips up by name as well as by address.
    /// </summary>
    /// <param name="key">The key the clip is resolved by; must not be empty.</param>
    /// <param name="clip">A clip this framework built.</param>
    /// <param name="reference">The reference the asset is filed under, or null when the key or the clip was refused.</param>
    bool TryRegisterAudioClip(string key, AudioClipHandle clip, [NotNullWhen(true)] out AssetReference? reference);

    /// <summary>The reference filed under <paramref name="key"/>, or null when nothing was ever filed under it.</summary>
    bool TryGetReference(string key, [NotNullWhen(true)] out AssetReference? reference);

    /// <summary>The sprite filed under <paramref name="key"/>, or null when nothing (or something else) was.</summary>
    bool TryResolveSprite(string key, [NotNullWhen(true)] out SpriteHandle? sprite);

    /// <summary>The audio clip filed under <paramref name="key"/>, or null when nothing (or something else) was.</summary>
    bool TryResolveAudioClip(string key, [NotNullWhen(true)] out AudioClipHandle? clip);

    /// <summary>
    /// Whether <paramref name="key"/> was filed. False before anything was ever registered: asking about a
    /// key does not build the asset table.
    /// </summary>
    bool IsRegistered(string key);

    /// <summary>
    /// Removes <paramref name="key"/> from the asset table. The object itself stays alive — a mod that built
    /// it still holds it — so this releases nothing but the lookup. True when the key was filed.
    /// </summary>
    bool Unregister(string key);
}
