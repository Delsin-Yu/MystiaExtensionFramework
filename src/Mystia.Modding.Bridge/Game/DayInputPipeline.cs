using Common.CharacterUtility;
using Mystia.Listeners;
using Mystia.Numerics;

namespace Mystia.Modding.Bridge;

/// <summary>
/// Translates the engine's character objects into the framework's <see cref="DayCharacter"/> identity and
/// hands the day input notifications out. Which character is the local player is decided here, against the
/// game's own collection, so no mod has to know how the game spells "Self".
/// </summary>
internal static class DayInputPipeline
{
    /// <summary>The framework identity of one engine character, as the day input seams report it.</summary>
    internal static DayCharacter Describe(CharacterControllerUnit unit) =>
        new(unit.name, IsLocalPlayer(unit.name));

    internal static void CharacterReady(DayCharacter character) =>
        Dispatch.Run<IDayInputListener>(listener => listener.OnCharacterReady(in character));

    internal static void Move(DayCharacter character, Vector2 direction) =>
        Dispatch.Run<IDayInputListener>(listener => listener.OnMoveInput(in character, direction));

    // The game keeps the characters it drives in its scene director's collection under their own names; the
    // local player is the one filed as "Self", which is what the game's own input generator drives.
    private static bool IsLocalPlayer(string name)
    {
        var collection = Common.SceneDirector.instance?.characterCollection;
        if (collection is null || !collection.ContainsKey("Self"))
            return false;

        var self = collection["Self"];
        return self is not null && self.name == name;
    }
}
