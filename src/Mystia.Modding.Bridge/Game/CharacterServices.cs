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
