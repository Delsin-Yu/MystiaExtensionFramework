using System.Diagnostics.CodeAnalysis;

using Common;
using Common.CharacterUtility;
using GameData.Core.Collections.CharacterUtility;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mystia.Assets;
using Mystia.Scenes;
using UnityEngine;
using UnityEngine.Tilemaps;

using NumericsVector2 = Mystia.Numerics.Vector2;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The character half of the always available capabilities (see <see cref="ICharacterServices"/>): the game's
/// own animated walk, plus the creation and removal of a character the way the game's story does it.
///
/// None of this needs the scene loop's service window: the game keeps <see cref="SceneDirector"/> for the whole
/// process, so a console command, a message handler and a scene loop all call the same entries. What <em>is</em>
/// read from the running scene — the day scene's active map for the ground height — is read through
/// <see cref="SceneLoopHost.Active"/>, so a call in a scene that has no such map is refused rather than aimed at
/// a stale one.
/// </summary>
internal sealed class CharacterServices : ICharacterServices
{
    internal static readonly CharacterServices Shared = new();

    /// <summary>Paths already reported, so a per frame call cannot flood the host log.</summary>
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    public bool WalkCharacter(string character, NumericsVector2 position, float speedMultiplier = 1f)
    {
        if (string.IsNullOrEmpty(character))
        {
            Report("WalkCharacter", "the character label was empty");
            return false;
        }

        if (!float.IsFinite(speedMultiplier) || speedMultiplier <= 0f)
        {
            Report("WalkCharacter", $"the speed multiplier {speedMultiplier} is not a positive, finite number");
            return false;
        }

        var director = SceneDirector.instance;
        if (director is null || !director.characterCollection.ContainsKey(character))
        {
            Report("WalkCharacter", $"the running scene files no character as '{character}'");
            return false;
        }

        // The game's own mover walks a list of waypoints and calls back when it arrives. The framework moves a
        // character to one position, which is the single waypoint form of the same call, and has no report of
        // its own to make when the walk ends.
        var wayPoints = new Il2CppStructArray<Vector2>(1);
        wayPoints[0] = new Vector2(position.X, position.Y);
        director.MoveCharacter(character, wayPoints, speedMultiplier, new Action(static () => { }));
        return true;
    }

    public CharacterHandle? CreateCharacter(CharacterCreateSpec spec)
    {
        if (string.IsNullOrEmpty(spec.Label))
        {
            Report("CreateCharacter", "the label was empty");
            return null;
        }

        if (!float.IsFinite(spec.MoveSpeedMultiplier))
        {
            Report("CreateCharacter", $"the move speed multiplier of '{spec.Label}' is not finite");
            return null;
        }

        if (!TryResolveSkin(spec.Skin, out var skin))
        {
            Report("CreateCharacter", $"the skin of '{spec.Label}' is neither a pixel set of the game's nor one this framework built");
            return null;
        }

        var director = SceneDirector.instance;
        if (director is null)
        {
            Report("CreateCharacter", "there is no scene director to create a character in");
            return null;
        }

        // The label is the game's own key: its own collection, its own lookups and its own scene teardown all
        // name a character by it, so a second character under one label is refused here rather than silently
        // replacing what the game already knows about.
        if (director.characterCollection.ContainsKey(spec.Label))
        {
            Report("CreateCharacter", $"a character is already filed as '{spec.Label}'");
            return null;
        }

        CharacterControllerUnit? character = null;
        try
        {
            // The game's own story spawn (SceneDirector.SpawnCharacter): a clone of the character template,
            // initialized with the pixel art and the speed, named after the label and filed in the collection.
            character = UnityEngine.Object.Instantiate(DataBaseCharacter.CharacterBase, director.transform)
                .GetComponent<CharacterControllerUnit>();
            character.name = spec.Label;
            character.Initialize(skin, spec.MoveSpeedMultiplier, spec.HasCollider);
            character.transform.position = new Vector3(spec.Position.X, spec.Position.Y, 0f);
            director.characterCollection.Add(spec.Label, character);
            character.gameObject.GetComponent<CharacterControllerInputGeneratorComponent>()?.OnTimelinePositionUpdated();
            return new UnityCharacterHandle(character);
        }
        catch (Exception error)
        {
            GameBridgeHook.Trace(
                $"CreateCharacter: '{spec.Label}' could not be created: {error.GetBaseException().Message}");
            if (character is not null)
                UnityEngine.Object.Destroy(character.gameObject);
            return null;
        }
    }

    public bool DestroyCharacter(CharacterHandle character)
    {
        if (character is not UnityCharacterHandle host || host.Unit is null)
            return false;

        var director = SceneDirector.instance;
        if (director is null)
            return false;

        // Only the character this scene files under the label is the caller's to destroy: a scene teardown
        // destroyed the game's own characters already, and a label that moved on belongs to somebody else now.
        var label = host.Unit.name;
        if (!director.characterCollection.TryGetValue(label, out var registered) || registered != host.Unit)
            return false;

        director.characterCollection.Remove(label);
        UnityEngine.Object.Destroy(host.Unit.gameObject);
        return true;
    }

    public bool SetCharacterHeightBlending(CharacterHandle character)
    {
        if (character is not UnityCharacterHandle host || host.Unit is null)
            return false;

        if (HeightMap() is not { } heightMap)
            return false;

        var unit = host.Unit;
        // The one processor the game's own player characters get: the character is lifted and lowered by the
        // ground it walks on. The game adds it once and re-points it whenever the map changes.
        var processor = unit.GetInputProcessor<HeightBlendedInputProcessorComponent>()
            ?? unit.AddInputProcessor<HeightBlendedInputProcessorComponent>();
        processor.Initialize(heightMap);
        return true;
    }

    // ── moving and showing a character a mod drives itself ────────────────────────────────────────────────
    //
    // Every member below works on the game's own character unit through the two components the game puts on it:
    // the body the game moves a character with (CharacterControllerUnit.rb2d, a Rigidbody2D the unit requires)
    // and the collider it decides against for a character it is told not to collide with (cl2d). The semantics
    // are the game's own, read out of Common/Character/CharacterControllerUnit.cs:
    //
    //  · the game places a character by writing its transform and moves a walking one with
    //    Rigidbody2D.MovePosition along its input direction; it never drives one by velocity, and it zeroes the
    //    body's velocity as soon as one stands still (CharacterControllerUnit.FixedUpdate and its IsMoving
    //    setter, which writes Vector2.zero),
    //  · the game's own base character prefab ships a kinematic body with no gravity and a frozen rotation, and
    //    Initialize makes a character dynamic only when it is created with a collider; a character created
    //    without one has its collider destroyed,
    //  · the game keeps every character at z 0 and never writes z itself; what orders characters among each other
    //    is the sorting group and the animator, not z.

    public bool TryBindCharacter(string character, out CharacterHandle? handle)
    {
        handle = null;
        if (string.IsNullOrEmpty(character))
        {
            Report("TryBindCharacter", "the character label was empty");
            return false;
        }

        // The game's own collection, the one its lookups, its teardown and its day scene table use: the label is
        // the key a character is filed under for the whole process, which is what WalkCharacter resolves too.
        var director = SceneDirector.instance;
        if (director is null || !director.characterCollection.TryGetValue(character, out var unit) || unit is null || unit == null)
        {
            Report("TryBindCharacter", $"the running scene files no character as '{character}'");
            return false;
        }

        handle = new UnityCharacterHandle(unit);
        return true;
    }

    public bool TryGetCharacterPosition(CharacterHandle character, out NumericsVector2 position)
    {
        position = default;
        if (Unit(character) is not { } unit || unit.rb2d == null)
        {
            Report("TryGetCharacterPosition", "the handle names no character that is still there");
            return false;
        }

        // The body's own position, which is what the game's own mover writes and what a character is drawn from:
        // interpolation is off on the game's character prefab, so the body and the transform never disagree.
        var world = unit.rb2d.position;
        position = new NumericsVector2(world.x, world.y);
        return true;
    }

    public bool SetCharacterPosition(CharacterHandle character, NumericsVector2 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            Report("SetCharacterPosition", $"({position.X}, {position.Y}) is not a finite position");
            return false;
        }

        if (Unit(character) is not { } unit || unit.rb2d == null)
        {
            Report("SetCharacterPosition", "the handle names no character that is still there");
            return false;
        }

        // The body's position, which is the teleport the engine understands for a body of either kind: a
        // transform write is what the game itself uses to place a character that is not being moved yet, and it
        // is the body that has to be told when one is. The depth is left where it is (see SetCharacterZ).
        unit.rb2d.position = new Vector2(position.X, position.Y);
        return true;
    }

    public bool SetCharacterVelocity(CharacterHandle character, NumericsVector2 velocity)
    {
        if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y))
        {
            Report("SetCharacterVelocity", $"({velocity.X}, {velocity.Y}) is not a finite velocity");
            return false;
        }

        if (Unit(character) is not { } unit || unit.rb2d == null)
        {
            Report("SetCharacterVelocity", "the handle names no character that is still there");
            return false;
        }

        unit.rb2d.velocity = new Vector2(velocity.X, velocity.Y);
        return true;
    }

    public bool SetCharacterKinematic(CharacterHandle character, bool kinematic)
    {
        if (Unit(character) is not { } unit || unit.rb2d == null)
        {
            Report("SetCharacterKinematic", "the handle names no character that is still there");
            return false;
        }

        unit.rb2d.isKinematic = kinematic;
        return true;
    }

    public bool SetCharacterColliderEnabled(CharacterHandle character, bool enabled)
    {
        if (Unit(character) is not { } unit)
        {
            Report("SetCharacterColliderEnabled", "the handle names no character that is still there");
            return false;
        }

        // A character the game created without a collider has none at all: Initialize destroys it (that is the
        // state the game's own story characters run in), and the game's own UpdateColliderStatus refuses the
        // call in the same situation. Nothing is added here that the game decided against.
        if (unit.cl2d == null)
        {
            Report("SetCharacterColliderEnabled", "the character was created without a collider, so there is none to switch");
            return false;
        }

        unit.cl2d.enabled = enabled;
        return true;
    }

    public bool SetCharacterZ(CharacterHandle character, float z)
    {
        if (!float.IsFinite(z))
        {
            Report("SetCharacterZ", $"the depth {z} is not a finite number");
            return false;
        }

        if (Unit(character) is not { } unit || unit.rb2d == null)
        {
            Report("SetCharacterZ", "the handle names no character that is still there");
            return false;
        }

        // x and y are kept, exactly like the mod's own SetZ did: a position write of the body would reset them,
        // and the depth is what a character is taken out of the camera's picture with (a large negative z) or
        // brought back with (0, the depth the game's own characters sit at).
        var target = unit.rb2d.transform;
        var world = target.position;
        target.position = new Vector3(world.x, world.y, z);
        return true;
    }

    /// <summary>
    /// The game's character behind a handle, or null when the handle names no character that is still there. Two
    /// ways a handle stops naming one: the reference a caller built it around is missing (a null handle), and the
    /// game's own object is gone (a scene teardown destroys its characters). The second is why the comparison
    /// goes through the engine's own operator — a plain null test only sees the first (a destroyed Unity object
    /// is not a null reference).
    /// </summary>
    private static CharacterControllerUnit? Unit(CharacterHandle character)
    {
        if (character is not UnityCharacterHandle host)
            return null;

        var unit = host.Unit;
        return unit is null || unit == null ? null : unit;
    }

    /// <summary>
    /// The ground height map of the running scene, i.e. the one the game's own player character samples: the day
    /// scene's active map, or the izakaya map of the night being worked. Null when neither is running.
    /// </summary>
    private static Tilemap? HeightMap() => SceneLoopHost.Active switch
    {
        SceneId.Day => DayScene.SceneManager.Instance?.CurrentActiveMap?.height,
        SceneId.Night => NightScene.MapManager.Instance?.height,
        _ => null,
    };

    /// <summary>
    /// The game's own pixel set behind the skin a spec carries: a set the game holds (either the compact or the
    /// layered one, which is what its own skin data and its fallback art are) or one this framework built, which
    /// the factory hands out wrapped.
    /// </summary>
    private static bool TryResolveSkin(object? skin, [NotNullWhen(true)] out CharacterSpriteSetCompact? set)
    {
        switch (skin)
        {
            case CharacterSpriteSetCompact gameSet:
                set = gameSet;
                return true;
            case UnityCharacterSpriteSetHandle built when built.Set is { } builtSet:
                set = builtSet;
                return true;
            default:
                set = null;
                return false;
        }
    }

    /// <summary>
    /// A refused call is reported once per kind, so a mod that asks for a character from an update loop must not
    /// flood the host log while a real refusal is still visible in it.
    /// </summary>
    private static void Report(string member, string reason)
    {
        if (!Reported.Add(member + ": " + reason))
            return;
        GameBridgeHook.Trace($"{member}: the call was refused ({reason}).");
    }
}
