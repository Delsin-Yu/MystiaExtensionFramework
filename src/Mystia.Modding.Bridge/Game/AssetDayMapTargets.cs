using System.Reflection;
using HarmonyLib;

using DayScene;
using DayScene.Input;
using DayScene.Interactables;
using GameData.Core.Collections.DaySceneUtility;
using GameData.Profile;

using Mystia.Data;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The check of everything the day map path names on the game's side.
/// <para>
/// The builder is compiled against the pinned interop, so a member that moved under another name is a compile
/// error rather than a silent mistake; what the compiler cannot check is a value, and the map path carries one:
/// a spawn point's facing is the framework's own <c>CharacterRotationKind</c>, stored by value into the game's
/// own rotation enum, so the two enums have to line up name by name. Nothing here touches the IL2CPP runtime -
/// it is reflection over the generated assembly's metadata - so the check runs with the assembly and before the
/// game is touched, which is what makes the map path's engine side verifiable without a running engine.
/// </para>
/// </summary>
internal static class DayMapTargets
{
    /// <summary>
    /// Checks the facing mapping and every member the engine side of the map path names. Throws
    /// <see cref="InvalidOperationException"/> naming the first thing this build does not have.
    /// </summary>
    internal static void Verify()
    {
        Facing("Down", CharacterRotationKind.Down);
        Facing("Left", CharacterRotationKind.Left);
        Facing("Up", CharacterRotationKind.Up);
        Facing("Right", CharacterRotationKind.Right);
        Facing("Null", CharacterRotationKind.Null);

        Member("the map's spawn marker field", typeof(DaySceneMap), "spawnMarkerField");
        Member("the map's collectable field", typeof(DaySceneMap), "collectableField");
        Member("the map's camera follow flag", typeof(DaySceneMap), "shouldCameraFollow");
        Member("the map's camera default position", typeof(DaySceneMap), "cameraDefaultPosition");
        Member("the map's camera boundary", typeof(DaySceneMap), "boundingShape");
        Member("the map's music", typeof(DaySceneMap), "mapBGM");
        Member("the map's height map", typeof(DaySceneMap), "height");
        Member("a spawn marker's name", typeof(SpawnMarker), "spawnMarkerName");
        Member("a spawn marker's facing", typeof(SpawnMarker), "targetRotation");
        Member("a spawn marker's radius override", typeof(SpawnMarker), "overrideRadius");
        Member("a spawn marker's radius flag", typeof(SpawnMarker), "shouldOverrideRadius");
        Member("the music's intro clip", typeof(LoopedBGMPackage), "intro");
        Member("the music's loop clip", typeof(LoopedBGMPackage), "loop");
        Member("the day map reference table", typeof(DataBaseDay), "mapReference");
    }

    private static void Facing(string name, CharacterRotationKind kind)
    {
        var native = typeof(DayScenePlayerInputGenerator.CharacterRotation);
        var value = FacingValue(native, name) ?? throw new InvalidOperationException(
            $"The day map path names the facing '{name}', which this build's {native.Name} does not hold: the " +
            "interop was generated for another build.");

        if (value != (int)kind)
        {
            throw new InvalidOperationException(
                $"The day map path stores the facing '{name}' as {(int)kind}, and this build's {native.Name} is " +
                $"{value}: a spawn point would face the wrong way.");
        }
    }

    // The generator emits a game enum as a C# enum; a build that emitted its members as constants instead is
    // read through the field, because that is what the name then is.
    private static int? FacingValue(Type native, string name)
    {
        if (native.IsEnum)
            return Enum.GetNames(native).Contains(name) ? (int)Enum.Parse(native, name) : null;

        var field = native.GetField(name, BindingFlags.Public | BindingFlags.Static);
        return field is null ? null : Convert.ToInt32(field.GetRawConstantValue() ?? field.GetValue(null));
    }

    private static void Member(string what, Type owner, string name)
    {
        if (AccessTools.Field(owner, name) is null && AccessTools.Property(owner, name) is null
            && AccessTools.Method(owner, name) is null)
        {
            throw new InvalidOperationException(
                $"The day map path's {what} ({owner.FullName}.{name}) is not a member of this build: the interop " +
                "was generated for another build.");
        }
    }
}
