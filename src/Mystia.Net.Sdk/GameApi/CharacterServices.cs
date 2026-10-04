using Mystia.Assets;
using Mystia.Numerics;

namespace Mystia.Scenes;

/// <summary>
/// The game's own character operations, as actions a mod initiates.
/// <para>
/// A character is named the way the game itself names one: by a label — the key the running scene's character
/// collection files it under, which is also the id of a tracked day scene character and the name a timeline
/// track looks a character up by (a label yields the handle for that character as well,
/// <see cref="TryBindCharacter"/>) — and, for one a mod created, by the <see cref="CharacterHandle"/> it was
/// handed. No member here names the game's own character type, so a mod walks, creates, moves and removes
/// characters without reaching into the game's objects to do it.
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

    /// <summary>
    /// Wraps the character the running scene files under a label into the handle every other member here takes,
    /// which is how a mod reaches a character it did not create itself: the game's own player character (filed
    /// as <c>Self</c>) and the story characters of the running scene. The label names a character for the whole
    /// process — the collection belongs to the game's scene director, not to a scene loop's window — so this is
    /// callable from a global loop and from the callers of one.
    /// <para>
    /// The handle wraps the character the label is filed under right now: after a scene teardown the game's
    /// character is gone and the members that take the handle answer false, so a mod that keeps one across a
    /// scene change asks again instead of trusting it. For a character of its own, a mod uses the handle
    /// <see cref="CreateCharacter"/> answered with and never needs this.
    /// </para>
    /// </summary>
    /// <param name="character">The label the character is filed under in the running scene.</param>
    /// <param name="handle">The handle, when the call answers true.</param>
    /// <returns>False when <paramref name="character"/> is empty, when there is no scene director to ask, or
    /// when the running scene files no character under that label.</returns>
    bool TryBindCharacter(string character, out CharacterHandle? handle);

    /// <summary>
    /// Reads where a character is, in the world.
    /// <para>
    /// The position is the character's own: the game places its characters by writing their transform and moves
    /// a walking one through the body behind it, and both are the same point, so a mod reads where the character
    /// is drawn and driven from one place.
    /// </para>
    /// </summary>
    /// <param name="character">A handle <see cref="CreateCharacter"/> answered with or <see cref="TryBindCharacter"/> produced.</param>
    /// <param name="position">The character's world position (x and y), when the call answers true.</param>
    /// <returns>
    /// False when the handle is not one this framework produced — which is the answer for a null handle and for
    /// one whose character is gone (the game destroys a scene's characters when it tears the scene down).
    /// </returns>
    bool TryGetCharacterPosition(CharacterHandle character, out Vector2 position);

    /// <summary>
    /// Puts a character at a world position, at once, wherever it currently is: the game's own way of placing a
    /// character (what it does when it spawns one at a mark, or teleports one to the next map), so nothing that
    /// is running catches up over a few frames.
    /// <para>
    /// This is a placement, not a walk: <see cref="WalkCharacter"/> is the animated way to a position and this is
    /// the instantaneous one. A mod that drives a character itself — a character whose position comes off the
    /// network, say — places it here whenever a new position arrives, and the game's own height blending and
    /// sorting follow the character as they do for any other.
    /// </para>
    /// </summary>
    /// <param name="character">A handle <see cref="CreateCharacter"/> answered with or <see cref="TryBindCharacter"/> produced.</param>
    /// <param name="position">The world position to place the character at; both components must be finite.</param>
    /// <returns>
    /// False when the handle is not one this framework produced, when its character is gone, or when a component
    /// of the position is not a finite number.
    /// </returns>
    bool SetCharacterPosition(CharacterHandle character, Vector2 position);

    /// <summary>
    /// Writes the velocity of the body behind a character — the physics velocity a dynamic body carries, which
    /// the engine integrates between steps.
    /// <para>
    /// What the game itself does with that velocity is worth knowing before reaching for this: the game moves a
    /// character through <c>MovePosition</c> along its input direction and never drives one by velocity, and it
    /// zeroes the body's velocity as soon as one is not moving (<c>CharacterControllerUnit.FixedUpdate</c> and
    /// its <c>IsMoving</c> setter). A velocity written here is therefore the physics step's own input to a body
    /// the game is not currently moving, and it is dropped the moment the game decides the character stands
    /// still. A mod that wants a character to keep moving asks the game to move it — the game's own input
    /// velocity and moving flag are public on its character unit, which a mod names without naming a Unity type.
    /// </para>
    /// <para>
    /// On a kinematic body — which is what a character created without a collider has, and what
    /// <see cref="SetCharacterKinematic"/> makes of any body — this writes the value and the engine ignores it:
    /// such a body only moves where the game's own mover puts it.
    /// </para>
    /// </summary>
    /// <param name="character">A handle <see cref="CreateCharacter"/> answered with or <see cref="TryBindCharacter"/> produced.</param>
    /// <param name="velocity">The velocity to write; both components must be finite.</param>
    /// <returns>False when the handle names no character of this framework's, or when a component of the
    /// velocity is not a finite number.</returns>
    bool SetCharacterVelocity(CharacterHandle character, Vector2 velocity);

    /// <summary>
    /// Sets whether the body behind a character is kinematic, i.e. whether the engine moves it.
    /// <para>
    /// A kinematic body obeys nothing but the game's own mover: it does not fall, is not pushed and is not
    /// stopped by what it overlaps, and it moves where it is placed. A dynamic one is integrated by the engine
    /// as well. The game's own prefab is kinematic and the game makes a character dynamic when it initializes it
    /// with a collider (<c>CharacterControllerUnit.Initialize</c>), so this is how a mod takes the engine's
    /// gravity and collision response out of a character it places itself.
    /// </para>
    /// </summary>
    /// <param name="character">A handle <see cref="CreateCharacter"/> answered with or <see cref="TryBindCharacter"/> produced.</param>
    /// <param name="kinematic">True to keep the engine from moving the character, false to let it.</param>
    /// <returns>False when the handle names no character of this framework's.</returns>
    bool SetCharacterKinematic(CharacterHandle character, bool kinematic);

    /// <summary>
    /// Turns the character's own collider on or off, i.e. whether a character takes part in physics at all:
    /// what it blocks, what blocks it and what it overlaps.
    /// <para>
    /// A character carries a collider only if it was created with one
    /// (<see cref="CharacterCreateSpec.HasCollider"/>): a character created without one has had its collider
    /// destroyed by the game, which is the state the game's own story characters run in. This member answers
    /// false for such a character — the framework does not add a collider the game decided against, and it does
    /// not report a toggle that did nothing.
    /// </para>
    /// </summary>
    /// <param name="character">A handle <see cref="CreateCharacter"/> answered with or <see cref="TryBindCharacter"/> produced.</param>
    /// <param name="enabled">True to let the character take part in physics, false to take it out.</param>
    /// <returns>False when the handle names no character of this framework's, or when its character carries no
    /// collider to switch.</returns>
    bool SetCharacterColliderEnabled(CharacterHandle character, bool enabled);

    /// <summary>
    /// Writes the z of a character's world position, leaving x and y where they are. The game keeps its own
    /// characters at z 0 and never writes z itself, so this is the mod's own lever on depth: the engine draws
    /// what is in front of the camera, so a large negative z takes a character out of the picture without
    /// taking it out of the scene — its animating, its height blending and the labels hung on it all keep
    /// running — and 0 puts it back among the characters of the scene. The order characters are drawn in among
    /// themselves comes from the sorting group and the animator, not from this.
    /// </summary>
    /// <param name="character">A handle <see cref="CreateCharacter"/> answered with or <see cref="TryBindCharacter"/> produced.</param>
    /// <param name="z">The depth to write; must be finite.</param>
    /// <returns>False when the handle names no character of this framework's, or when the value is not a finite
    /// number.</returns>
    bool SetCharacterZ(CharacterHandle character, float z);
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
