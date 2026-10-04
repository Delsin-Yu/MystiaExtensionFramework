using Mystia.Assets;
using Mystia.Numerics;

namespace Mystia.Scenes;

/// <summary>
/// The game's own character operations, as actions a mod initiates.
/// <para>
/// A character is named the way the game itself names one: by a label — the key the running scene's character
/// collection files it under, which is also the id of a tracked day scene character and the name a timeline
/// track looks a character up by — and, for one a mod created, by the <see cref="CharacterHandle"/> it was
/// handed. No member here names the game's own character type, so a mod walks, creates and removes characters
/// without reaching into the game's objects to do it.
/// </para>
/// <para>
/// These are not bound to the scene loop window a scene services object lives in: the game keeps its scene
/// director for the whole process, so a mod reaches it from a global loop too — which is where a console command
/// lives, and where a mod's network state arrives. Everything is main thread only, because every member ends up
/// in the engine. A member that cannot do what it was asked answers false instead of throwing, so a caller keeps
/// control of what happens next.
/// </para>
/// <para>
/// What is <em>not</em> here is the day scene's own placement move (<c>RunTimeDayScene.MoveCharacter</c>): it is
/// a public entry that takes the position by value, so a mod writes it itself — <c>Move(character, map, new(x,
/// y), rotation, out _)</c> — without naming a Unity type, which is the whole of what a mod needs from it.
/// </para>
/// </summary>
public interface ICharacterServices
{
    /// <summary>
    /// Walks a character of the running scene to a position, animating the walk at
    /// <paramref name="speedMultiplier"/> times the character's own speed — what the game's own story does when
    /// it walks a character to its next mark. The call returns as soon as the walk starts; the character keeps
    /// walking while the scene runs.
    /// </summary>
    /// <param name="character">The label the character is filed under in the running scene.</param>
    /// <param name="position">The world position to walk to.</param>
    /// <param name="speedMultiplier">The walk speed as a multiple of the character's own; must be positive.</param>
    /// <returns>
    /// False when <paramref name="character"/> is empty, the speed multiplier is not finite and positive, or the
    /// running scene files no character under that label.
    /// </returns>
    bool WalkCharacter(string character, Vector2 position, float speedMultiplier = 1f);

    /// <summary>
    /// Creates a character the way the game's own story does: a clone of the game's character template,
    /// initialized with the spec's skin and move speed, named after the spec's label and registered in the
    /// running scene's own character collection under it, so the game's own lookups — a camera follow, a
    /// timeline track, the scene's own teardown — find it like any other character of that scene.
    /// <para>
    /// The character is created without the game's own input processor for ground height, exactly like a story
    /// character: call <see cref="SetCharacterHeightBlending"/> when it should walk the map's slopes. What it
    /// wears is what the spec's skin is; another set goes on later with
    /// <c>IPresentationServices.ApplyCharacterSprite</c>.
    /// </para>
    /// </summary>
    /// <param name="spec">What the character is created with.</param>
    /// <returns>
    /// The character, or null when the spec was refused (an empty label, a speed that is not finite, a skin that
    /// is not a pixel set) or when a character is already filed under the spec's label. The label is the game's
    /// own key, so a mod that wants to re-create a character destroys the old one first.
    /// </returns>
    CharacterHandle? CreateCharacter(CharacterCreateSpec spec);

    /// <summary>
    /// Takes the character out of the running scene's collection and destroys it, the counterpart of
    /// <see cref="CreateCharacter"/>. A character the game itself destroys when the scene tears down is not a
    /// caller's to destroy: that teardown covers it, and this answers false once it is gone.
    /// </summary>
    /// <param name="character">A handle <see cref="CreateCharacter"/> answered with.</param>
    /// <returns>True when the character was still the scene's and is gone.</returns>
    bool DestroyCharacter(CharacterHandle character);

    /// <summary>
    /// Gives the character the ground height blending of the running scene's own map — the one the game's own
    /// player character walks on — so a character a mod created follows the map's slopes and steps exactly as
    /// the player does. Call it again after the scene changed its map.
    /// </summary>
    /// <param name="character">A handle <see cref="CreateCharacter"/> answered with.</param>
    /// <returns>
    /// False when the character is not one of the running scene's, or when the running scene has no height map
    /// to take the ground height from (it is neither the day nor the work scene).
    /// </returns>
    bool SetCharacterHeightBlending(CharacterHandle character);
}

/// <summary>
/// What a character is created with (see <see cref="ICharacterServices.CreateCharacter"/>): the arguments the
/// game's own story spawns a character with, in the framework's own value types.
/// </summary>
/// <param name="Label">
/// The name the character is created with and filed under. It must not be empty: the scene's collection, the
/// game's own teardown and every lookup that names the character use it.
/// </param>
/// <param name="Skin">
/// The pixel art the character starts in: a set the game holds — one of its own skins, or its own fallback art —
/// or a <see cref="CharacterSpriteSetHandle"/> the asset factory built. Anything else is refused, and so is
/// null: a character without a set has no animator.
/// </param>
/// <param name="MoveSpeedMultiplier">The move speed as a multiple of the game's own base speed; must be finite.</param>
/// <param name="Position">
/// The world position the character is placed at, the origin by default — which is where the game's own spawn
/// leaves a character that its caller places afterwards.
/// </param>
/// <param name="HasCollider">
/// Whether the character carries the game's own collider. The game's story characters, and every character a mod
/// drives itself, pass false: without a collider the character neither blocks nor is blocked, and the collision
/// events the game would report for it never fire.
/// </param>
public readonly record struct CharacterCreateSpec(
    string Label,
    object? Skin,
    float MoveSpeedMultiplier,
    Vector2 Position = default,
    bool HasCollider = false);
