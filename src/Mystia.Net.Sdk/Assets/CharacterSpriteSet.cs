namespace Mystia.Assets;

// A character's pixel art is the game's own unit of character visuals: a frame array per layer plus the
// movement flags its animator reads off the set, packed into a ScriptableObject (CharacterSpriteSetCompact, or
// CharacterSpriteSetFull for the layered one). A mod that carries such art used to build that ScriptableObject
// itself — CreateInstance, a 15 or 17 argument Initialize, hideFlags — which is the one thing no mod can do
// without the engine. Here it hands over sprites it cut with IAssetFactory.TryCreateSprite and mirrored values,
// and gets back an opaque handle; the engine object stays behind the bridge.

/// <summary>Which of the game's two character pixel sprite sets a mod is building.</summary>
public enum CharacterSpriteSetKind
{
    /// <summary>
    /// The set an ordinary walking character wears: a main layer and an eyes layer. It is the game's own
    /// <c>CharacterSpriteSetCompact</c>, drawn by the compact animator.
    /// </summary>
    Compact,

    /// <summary>
    /// The layered set the player character and the special guests wear: a main, eyes, hair and back layer. It
    /// is the game's own <c>CharacterSpriteSetFull</c>, which switches its character to the layered animator.
    /// </summary>
    Full,
}

/// <summary>
/// The frame sprites of one character pixel sprite set, grouped the way the game's own animator indexes them.
/// <para>
/// The animator draws a layer from one index per frame: a body index <c>orientation * 3 + step</c> over four
/// orientations and three steps, and an eyes index <c>FaceMatrix[orientation, face] - (step == 1 ? 0 : 1)</c>,
/// where the game's <c>FaceMatrix</c> holds the six eye directions 1, 3, 13, 15 / 5, 7, 17, 19 / 9, 11, 21, 23
/// and no eyes at all for the back view. A layer is therefore a flat array of whole directions, laid out
/// direction by direction — 3 frames per direction for the body, hair and back layers and 4 for the eyes — and
/// it has to carry every direction the animator can ask for.
/// </para>
/// <para>
/// Every frame has to be a sprite this framework built, all of one size: the game draws the layers on top of
/// one another inside a single frame. The frames a set is built from are the same sprites a mod filed with
/// <see cref="IAssetLocator"/>, so one cut sprite serves both the pipeline and the character.
/// </para>
/// </summary>
/// <param name="Main">
/// The body layer: at least 12 frames (4 directions × 3 steps), in whole directions of 3.
/// </param>
/// <param name="Eyes">
/// The eyes layer: at least 24 frames (the 6 eye directions × 4 frames), in whole directions of 4.
/// </param>
/// <param name="Hair">
/// The hair layer of a <see cref="CharacterSpriteSetKind.Full"/> set: at least 12 frames, in whole directions of
/// 3, exactly like <paramref name="Main"/>. A compact set has no hair layer and must carry none.
/// </param>
/// <param name="Back">
/// The back layer of a <see cref="CharacterSpriteSetKind.Full"/> set: at least 12 frames, in whole directions
/// of 3, exactly like <paramref name="Main"/>. A compact set has no back layer and must carry none.
/// </param>
public readonly record struct CharacterSpriteSetFrames(
    ReadOnlyMemory<SpriteHandle> Main,
    ReadOnlyMemory<SpriteHandle> Eyes,
    ReadOnlyMemory<SpriteHandle> Hair = default,
    ReadOnlyMemory<SpriteHandle> Back = default);

/// <summary>
/// How a character pixel sprite set behaves, which is the half of the game's asset that is not its frames.
/// Every member is optional, and a member left unset keeps the value the game's own fallback pixel set carries,
/// so a mod that only has art hands over <see cref="CharacterSpriteSetFrames"/> and nothing else — the defaults
/// it would otherwise have to copy out of the game's data by hand. A member that is set changes how the
/// character moves, not what it looks like.
/// <para>
/// A value that is not finite is refused, because the animator would carry it into the character's transform.
/// </para>
/// </summary>
public readonly record struct CharacterSpriteSetStyle
{
    /// <summary>
    /// The set has no eyes of its own, so the game keeps the eyes layer out of the parts that would otherwise
    /// draw one (a spell drawing a stun face reads exactly this).
    /// </summary>
    public bool? DoNotUseEyeSprite { get; init; }

    /// <summary>
    /// The shadow is already painted into the frames, so the game hides the shadow layer it would otherwise
    /// animate under the character.
    /// </summary>
    public bool? HasPrebakedShadow { get; init; }

    /// <summary>Multiplies the animator's own speed: how fast the walk cycle plays.</summary>
    public float? AnimationSpeedMultiplier { get; init; }

    /// <summary>
    /// Lifts the character by this many world units, for art that does not sit on the bottom edge of its tile.
    /// </summary>
    public float? ExtraYOffset { get; init; }

    /// <summary>Multiplies how fast the character walks while it wears the set.</summary>
    public float? MoveSpeedMultiplier { get; init; }

    /// <summary>
    /// The character spins on the spot instead of turning: the game's animator cycles the four orientations
    /// itself, once every <see cref="RotatePerTime"/> seconds, and ignores every orientation it is handed.
    /// </summary>
    public bool? IsHina { get; init; }

    /// <summary>The seconds between two steps of that spin.</summary>
    public float? RotatePerTime { get; init; }

    /// <summary>The set has no footstep effect, so the game skips the step sound and the dust it draws.</summary>
    public bool? DoNotHaveStepVFX { get; init; }

    /// <summary>No opinion about any of them: the game's own fallback pixel set supplies every value.</summary>
    public static CharacterSpriteSetStyle Default => default;
}

/// <summary>
/// A character pixel sprite set the framework built (<c>IAssetFactory.TryCreateCharacterSpriteSet</c>). A mod
/// holds it, puts it on a character with <c>IPresentationServices.ApplyCharacterSprite</c> and cannot build one
/// itself; the game's own sprite set object never crosses the boundary.
/// <para>
/// A set is immutable once it is built, so a mod that changes how a character looks (and not only what art it
/// wears) builds a second set — a spin turned on for one skin is the same frames with
/// <see cref="CharacterSpriteSetStyle.IsHina"/> set.
/// </para>
/// </summary>
public abstract class CharacterSpriteSetHandle
{
    internal CharacterSpriteSetHandle()
    {
    }
}
