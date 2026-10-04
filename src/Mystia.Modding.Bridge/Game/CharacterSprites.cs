using Common.CharacterUtility;
using GameData.Core.Collections;
using GameData.Core.Collections.CharacterUtility;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mystia.Assets;
using UnityEngine;

namespace Mystia.Modding.Bridge;

// The engine side of the character pixel art a mod builds: the frames arrive as handles the factory cut, and
// the game's own ScriptableObject (CharacterSpriteSetCompact / CharacterSpriteSetFull) is what a character
// actually wears. Everything the mod did not state about the set is taken from the game's own fallback pixel
// set, so a mod that brought art and nothing else cannot get a movement flag wrong.
//
// What the set carries comes from the game's own animator (Assets/Scripts/Common/Character/CharacterAnimator.cs):
// UpdateVisual asks for the body frame `orientation * 3 + step` and the eyes frame `FaceMatrix[orientation,
// face] - (step == 1 ? 0 : 1)`, and Initialize reads the movement flags off the set. See the SDK file
// Assets/CharacterSpriteSet.cs for the layout a mod hands over.

/// <summary>The set a mod built, around the game's own sprite set object.</summary>
internal sealed class UnityCharacterSpriteSetHandle : CharacterSpriteSetHandle
{
    internal UnityCharacterSpriteSetHandle(CharacterSpriteSetCompact set) => Set = set;

    internal CharacterSpriteSetCompact Set { get; }
}

/// <summary>The character a mod wrapped, around the game's own character unit.</summary>
internal sealed class UnityCharacterHandle : CharacterHandle
{
    internal UnityCharacterHandle(CharacterControllerUnit unit) => Unit = unit;

    internal CharacterControllerUnit Unit { get; }
}

internal static class CharacterSprites
{
    // The body grid and the eye grid the game's animator indexes into: CharacterAnimator.UpdateVisual asks the
    // body layer for `orientation * 3 + step` (4 orientations × 3 steps) and the eyes layer for a face of
    // `DataBaseCharacter.FaceMatrix`, whose six eye directions run 1, 3, 13, 15 / 5, 7, 17, 19 / 9, 11, 21, 23 —
    // four frames each, 24 in all.
    private const int BodyFramesPerDirection = 3;
    private const int BodyFrames = 12;
    private const int EyeFramesPerDirection = 4;
    private const int EyeFrames = 24;

    // What the game's own pixel art is called when the framework builds one out of a mod's frames. A sprite set
    // is not resolved by name anywhere, so this is only what a log or a debugger shows.
    private const string Name = "MystiaCharacterSpriteSet";

    internal static bool TryCreate(
        CharacterSpriteSetKind kind,
        CharacterSpriteSetFrames frames,
        CharacterSpriteSetStyle style,
        out CharacterSpriteSetHandle? set)
    {
        set = null;

        // Everything a mod is refused for is decided here, before the engine or the game's own data is touched:
        // a set the animator could index out of is worse than no set at all, and a refusal that never reached
        // the engine is a refusal a mod can act on.
        if (kind is not (CharacterSpriteSetKind.Compact or CharacterSpriteSetKind.Full))
            return false;
        if (!Layer(frames.Main, BodyFramesPerDirection, BodyFrames))
            return false;
        if (!Layer(frames.Eyes, EyeFramesPerDirection, EyeFrames))
            return false;
        if (kind is CharacterSpriteSetKind.Full)
        {
            if (!Layer(frames.Hair, BodyFramesPerDirection, BodyFrames))
                return false;
            if (!Layer(frames.Back, BodyFramesPerDirection, BodyFrames))
                return false;
        }
        else if (!frames.Hair.IsEmpty || !frames.Back.IsEmpty)
        {
            // The compact animator draws the body, the eyes and the trims only: hair or back frames would be
            // carried by the set and never drawn, which is a mod's mistake worth reporting rather than hiding.
            return false;
        }

        if (!Finite(style))
            return false;

        var body = Sprites(frames.Main);
        var eyes = Sprites(frames.Eyes);
        var hair = Sprites(frames.Hair);
        var back = Sprites(frames.Back);
        if (body is null || eyes is null || hair is null || back is null)
            return false;

        // The game draws the layers on top of one another inside one frame, so a set whose frames disagree on
        // their size would draw a character of mismatched parts.
        var size = Size(body);
        if (!SameSize(size, eyes) || !SameSize(size, hair) || !SameSize(size, back))
            return false;

        try
        {
            set = kind is CharacterSpriteSetKind.Full
                ? BuildFull(body, eyes, hair, back, style)
                : BuildCompact(body, eyes, style);
            return true;
        }
        catch (Exception error)
        {
            GameBridgeHook.Trace($"CharacterSprites: the pixel sprite set could not be built: {error.GetBaseException().Message}");
            set = null;
            return false;
        }
    }

    /// <summary>
    /// The frames and the style of a set the game holds, as a mod would hand them to <see cref="TryCreate"/>:
    /// the game's own fallback art of either kind, or one the game loaded for a skin. False when the object is
    /// not one of the game's sets, or when its frames are not the whole grid the animator indexes - the frames
    /// travel as handles and are validated the same way a set a mod builds is.
    /// <para>
    /// What it does not carry is the set's own trims: those are part of what a rebuilt set takes from the game's
    /// fallback pixel art, exactly as a set a mod builds does, so a caller that reads a skin apart gets that
    /// skin's frames and flags and the game's own trimming.
    /// </para>
    /// </summary>
    internal static bool TryUnwrap(
        object set,
        out CharacterSpriteSetFrames frames,
        out CharacterSpriteSetStyle style)
    {
        frames = default;
        style = default;
        if (set is not CharacterSpriteSetCompact art || art is null)
            return false;

        var main = Wrapped(art.MainSprite);
        var eyes = Wrapped(art.EyeSprite);
        var hair = Wrapped(art.HairSprite);
        var back = Wrapped(art.BackSprite);
        if (main is null || eyes is null || hair is null || back is null)
            return false;

        frames = new CharacterSpriteSetFrames(main.Value, eyes.Value, hair.Value, back.Value);
        style = new CharacterSpriteSetStyle
        {
            DoNotUseEyeSprite = art.DoNotUseEyeSprite,
            HasPrebakedShadow = art.HasPrebakedShadow,
            AnimationSpeedMultiplier = art.AnimationSpeedMultiplier,
            ExtraYOffset = art.ExtraYOffset,
            MoveSpeedMultiplier = art.MoveSpeedMultiplier,
            IsHina = art.IsHina,
            RotatePerTime = art.RotatePerTime,
            DoNotHaveStepVFX = art.DoNotHaveStepVFX,
        };
        return true;
    }

    /// <summary>
    /// A layer of the game's own frames as handles, or null when the layer is missing or carries a hole: a
    /// layer the animator would index into nothing is a set no rebuild should be built from.
    /// </summary>
    private static ReadOnlyMemory<SpriteHandle>? Wrapped(Sprite[]? frames)
    {
        if (frames is null || frames.Length == 0)
            return null;

        var wrapped = new SpriteHandle[frames.Length];
        for (var index = 0; index < frames.Length; index++)
        {
            if (frames[index] is not { } frame)
                return null;
            wrapped[index] = new UnitySpriteHandle(frame);
        }

        return wrapped;
    }

    /// <summary>
    /// The character a mod wrapped, or null when the object carries none. The unit itself, its game object or
    /// any component on it are all the same character to a caller.
    /// </summary>
    internal static CharacterHandle? Bind(object character) => character switch
    {
        CharacterControllerUnit unit => new UnityCharacterHandle(unit),
        GameObject gameObject => gameObject.GetComponent<CharacterControllerUnit>() is { } onObject
            ? new UnityCharacterHandle(onObject)
            : null,
        Component component => component.GetComponent<CharacterControllerUnit>() is { } onComponent
            ? new UnityCharacterHandle(onComponent)
            : null,
        _ => null,
    };

    /// <summary>
    /// Puts a set on a character through the game's own <c>UpdateCharacterSprite</c>.
    /// </summary>
    internal static bool Apply(CharacterHandle character, CharacterSpriteSetHandle spriteSet, bool restart)
    {
        if (character is not UnityCharacterHandle host || host.Unit is null)
            return false;
        if (spriteSet is not UnityCharacterSpriteSetHandle built || built.Set is null)
            return false;

        var unit = host.Unit;
        if (restart)
        {
            // The game keeps the set a character already wears (TryUpdateCurrent answers "nothing changed" for
            // the same instance) and its Initialize starts another spin routine for a spinning set without
            // stopping the one it started before, so a set that stopped spinning would keep spinning at twice
            // the speed. Dropping the current visual and stopping the animator's routines is what makes a second
            // call mean "wear this now".
            unit.m_CurrentVisual = null;
            unit.animator?.StopAllCoroutines();
        }

        unit.UpdateCharacterSprite(built.Set);
        return true;
    }

    // ── the game's own objects ──

    private static CharacterSpriteSetHandle BuildCompact(Sprite[] body, Sprite[] eyes, CharacterSpriteSetStyle style)
    {
        var template = DataBaseCharacter.FallbackCompactPixel;
        var set = ScriptableObject.CreateInstance<CharacterSpriteSetCompact>();
        set.Initialize(
            Refs(body),
            style.DoNotUseEyeSprite ?? template?.DoNotUseEyeSprite ?? false,
            Refs(eyes),
            style.HasPrebakedShadow ?? template?.HasPrebakedShadow ?? false,
            style.AnimationSpeedMultiplier ?? template?.AnimationSpeedMultiplier ?? 1f,
            style.ExtraYOffset ?? template?.ExtraYOffset ?? 0f,
            style.IsHina ?? template?.IsHina ?? false,
            style.RotatePerTime ?? template?.RotatePerTime ?? 0.15f,
            style.DoNotHaveStepVFX ?? template?.DoNotHaveStepVFX ?? false,
            style.MoveSpeedMultiplier ?? template?.MoveSpeedMultiplier ?? 1f,
            template?.RemovableTrims ?? new Il2CppReferenceArray<CharacterSpriteSetCompact.RemovableTrimProperty>(0),
            template?.TrimSpritesDisplayFront ?? new Il2CppReferenceArray<Sprite>(0),
            template?.TrimSpritesDisplayBack ?? new Il2CppReferenceArray<Sprite>(0),
            template?.TrimFrontSpriteFrameSpeed ?? 0f,
            template?.TrimBackSpriteFrameSpeed ?? 0f);
        set.name = Name;
        set.hideFlags = HideFlags.HideAndDontSave;
        return new UnityCharacterSpriteSetHandle(set);
    }

    private static CharacterSpriteSetHandle BuildFull(Sprite[] body, Sprite[] eyes, Sprite[] hair, Sprite[] back, CharacterSpriteSetStyle style)
    {
        var template = DataBaseCharacter.FallbackFullPixel;
        var set = ScriptableObject.CreateInstance<CharacterSpriteSetFull>();
        set.Initialize(
            Refs(body),
            style.DoNotUseEyeSprite ?? template?.DoNotUseEyeSprite ?? false,
            Refs(eyes),
            Refs(hair),
            Refs(back),
            style.HasPrebakedShadow ?? template?.HasPrebakedShadow ?? false,
            style.AnimationSpeedMultiplier ?? template?.AnimationSpeedMultiplier ?? 1f,
            style.ExtraYOffset ?? template?.ExtraYOffset ?? 0f,
            style.IsHina ?? template?.IsHina ?? false,
            style.RotatePerTime ?? template?.RotatePerTime ?? 0.15f,
            // CharacterSpriteSetFull.Initialize names this parameter `shouldHaveStepVFX` and then hands it
            // straight to the compact Initialize's `doNotHaveStepVFX`, so what travels here is the "no step
            // effect" flag, exactly as in the compact call above (CharacterSpriteSetFull.cs:48).
            style.DoNotHaveStepVFX ?? template?.DoNotHaveStepVFX ?? false,
            style.MoveSpeedMultiplier ?? template?.MoveSpeedMultiplier ?? 1f,
            template?.RemovableTrims ?? new Il2CppReferenceArray<CharacterSpriteSetCompact.RemovableTrimProperty>(0),
            template?.TrimSpritesDisplayFront ?? new Il2CppReferenceArray<Sprite>(0),
            template?.TrimSpritesDisplayBack ?? new Il2CppReferenceArray<Sprite>(0),
            template?.TrimFrontSpriteFrameSpeed ?? 0f,
            template?.TrimBackSpriteFrameSpeed ?? 0f);
        set.name = Name;
        set.hideFlags = HideFlags.HideAndDontSave;
        return new UnityCharacterSpriteSetHandle(set);
    }

    // ── the frames a mod hands over ──

    /// <summary>
    /// Whether a layer carries the whole grid the animator can index: whole directions of
    /// <paramref name="perDirection"/> frames each, never fewer than <paramref name="minimum"/>, and no hole in
    /// it — a null frame would leave a character invisible for one step of its walk.
    /// </summary>
    private static bool Layer(ReadOnlyMemory<SpriteHandle> frames, int perDirection, int minimum)
    {
        if (frames.Length < minimum || frames.Length % perDirection != 0)
            return false;
        foreach (var frame in frames.Span)
        {
            if (frame is null)
                return false;
        }

        return true;
    }

    // The engine sprites behind a layer of handles, or null when a handle is not a sprite this framework built:
    // a mod cannot build a SpriteHandle itself, so an unknown one is either null or a handle of another kind.
    private static Sprite[]? Sprites(ReadOnlyMemory<SpriteHandle> frames)
    {
        var sprites = new Sprite[frames.Length];
        for (var index = 0; index < frames.Length; index++)
        {
            if (frames.Span[index] is not UnitySpriteHandle mirror || mirror.Sprite is null)
                return null;
            sprites[index] = mirror.Sprite;
        }

        return sprites;
    }

    private static (float Width, float Height) Size(Sprite[] sprites) =>
        sprites.Length == 0 ? (0f, 0f) : (sprites[0].rect.width, sprites[0].rect.height);

    private static bool SameSize((float Width, float Height) size, Sprite[] sprites)
    {
        foreach (var sprite in sprites)
        {
            var rect = sprite.rect;
            if (rect.width != size.Width || rect.height != size.Height)
                return false;
        }

        return true;
    }

    private static bool Finite(CharacterSpriteSetStyle style) =>
        Finite(style.AnimationSpeedMultiplier)
        && Finite(style.ExtraYOffset)
        && Finite(style.MoveSpeedMultiplier)
        && Finite(style.RotatePerTime);

    private static bool Finite(float? value) => value is null || float.IsFinite(value.Value);

    private static Il2CppReferenceArray<Sprite> Refs(Sprite[] sprites)
    {
        var array = new Il2CppReferenceArray<Sprite>(sprites.Length);
        for (var index = 0; index < sprites.Length; index++)
            array[index] = sprites[index];
        return array;
    }
}
