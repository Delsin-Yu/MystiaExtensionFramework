using System.Reflection;
using Common.DialogUtility;
using GameData.Core.Collections;
using GameData.Core.Collections.DaySceneUtility;
using GameData.CoreLanguage.Collections;
using GameData.Profile;
using GameData.Profile.SchedulerNodeCollection;
using HarmonyLib;
using Mystia.Assets;
using Mystia.Data;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The check of everything the game data builders name on the game's side.
/// <para>
/// The engine side is compiled against the pinned interop, so a member that moved under another name is a
/// compile error rather than a silent mistake; what the compiler cannot check is a value, and the builders
/// carry two kinds: a description's speaker kind, side and action kind are the framework's own enums (the data
/// face's <c>SpeakerKind</c>, <c>DialogSide</c> and <c>DialogActionKind</c>) stored by value into the game's
/// own enums, and a node's event type is the framework's own number. Both have to line up name by name or a
/// built line is said by the wrong speaker and a built event plays the wrong thing.
/// </para>
/// <para>
/// Nothing here touches the IL2CPP runtime - it is reflection over the generated assembly's metadata - so the
/// check runs with the assembly and before the game is touched, which is what makes the builders' engine side
/// verifiable without a running engine.
/// </para>
/// </summary>
internal static class AssetBuilderTargets
{
    /// <summary>
    /// Checks every enum value the builders store and every member the engine side names. Throws
    /// <see cref="InvalidOperationException"/> naming the first thing this build does not have.
    /// </summary>
    internal static void Verify()
    {
        Kind<SpeakerKind>("the speaker kind", typeof(SpeakerIdentity.Identity));
        Kind<DialogSide>("the speaker side", typeof(Position));
        Kind<DialogActionKind>("the line action", typeof(ActionType));
        Event();

        Member("the dialog table", typeof(DataBaseDay), "allDialogPackages");
        Member("a package's lines", typeof(DialogPackage), "dialogMeta");
        Member("a line's id", typeof(DialogMeta), "dialogId");
        Member("a line's speaker", typeof(DialogMeta), "speakerIdentity");
        Member("a line's side", typeof(DialogMeta), "speakerPosition");
        Member("a line's actions", typeof(DialogMeta), "dialogAction");
        Member("a line's foreground flag", typeof(DialogMeta), "isSpeakInForeground");
        Member("a line's dimming flag", typeof(DialogMeta), "isDark");
        Member("a line's name substitution flag", typeof(DialogMeta), "useNameInText");
        Member("a line's override flag", typeof(DialogMeta), "useOverrideSprite");
        Member("a line's override portrayal", typeof(DialogMeta), "m_OverrideSpriteAsset");
        Member("an action's type", typeof(DialogAction), "actionType");
        Member("an action's set flag", typeof(DialogAction), "shouldSet");
        Member("an action's clean sides", typeof(DialogAction), "foregroundCleaning");
        Member("an action's image", typeof(DialogAction), "m_SpriteAsset");
        Member("an action's material", typeof(DialogAction), "m_MaterialAsset");
        Member("an action's sound", typeof(DialogAction), "m_AudioAsset");
        Member("an action's music package", typeof(DialogAction), "m_BgmPackageAsset");
        Member("a branch's selections", typeof(DialogAction), "selections");
        Member("a branch's jumps", typeof(DialogAction), "jumps");
        Member("a branch's prices", typeof(DialogAction), "prices");
        Member("a jump or exit code", typeof(DialogAction), "index");
        Member("the scheduler node table", typeof(DataBaseScheduler), "allNodes");
        Member("the mission language table", typeof(DataBaseLanguage), "Missions");
        Member("a node's label", typeof(SchedulerNode), "label");
        Member("a node's debug label", typeof(SchedulerNode), "debugLabel");
        Member("a node's type", typeof(SchedulerNode), "missionType");
        Member("a node's rewards", typeof(SchedulerNode), "rewards");
        Member("a node's post rewards", typeof(SchedulerNode), "postRewards");
        Member("a node's pre nodes", typeof(SchedulerNode), "preNodes");
        Member("a node's post missions", typeof(SchedulerNode), "postMissions");
        Member("a node's post missions after the performance", typeof(SchedulerNode), "postMissionsAfterPerformance");
        Member("a node's post events", typeof(SchedulerNode), "postEvents");
        Member("an event's type", typeof(SchedulerNode.Event), "eventType");
        Member("an event's dialog package", typeof(SchedulerNode.Event), "runtimeDialogPackage");
        Member("a scheduled event's trigger", typeof(SchedulerNode.ScheduledEvent), "trigger");
        Member("a scheduled event's event", typeof(SchedulerNode.ScheduledEvent), "eventData");
        Member("a mission's time limit", typeof(MissionNode), "missionTimeLimit");
        Member("a mission's timed flag", typeof(MissionNode), "isTimedMission");
        Member("a mission's looped flag", typeof(MissionNode), "loopedMission");
        Member("a mission's failed action", typeof(MissionNode), "missionFailedAction");
        Member("a mission's finish event", typeof(MissionNode), "missionFinishEvent");
        Member("a mission's failed event", typeof(MissionNode), "missionFailedEvent");
        Member("a mission's sender", typeof(MissionNode), "sender");
        Member("a mission's sender flag", typeof(MissionNode), "hasSender");
        Member("a mission's receiver", typeof(MissionNode), "reciever");
        Member("a mission's receiver flag", typeof(MissionNode), "hasReciever");
        Member("a mission's hidden receiver flag", typeof(MissionNode), "hideReciever");
        Member("a mission's finish conditions", typeof(MissionNode), "finishCondition");
        Member("an event node's scheduled event", typeof(EventNode), "scheduledEvent");
        Member("an event node's once flag", typeof(EventNode), "scheduleOnce");
        Member("an event node's archive flag", typeof(EventNode), "saveToArchiveOnce");
        Member("an event node's day end flag", typeof(EventNode), "autoCompleteAtDayEnd");
        Member("an event node's lock mode", typeof(EventNode), "eventLockMode");
    }

    // A framework enum stored by value into a game enum: every member the framework names has to be the game's
    // own value, or a description says one thing and the game does another.
    private static void Kind<TKind>(string what, Type native)
        where TKind : struct, Enum
    {
        foreach (var name in Enum.GetNames<TKind>())
        {
            var frameworkValue = Convert.ToInt32(Enum.Parse<TKind>(name));
            var value = Native(native, name) ?? throw new InvalidOperationException(
                $"The game data builders name {what} '{name}', which this build's {native.Name} does not hold: the " +
                "interop was generated for another build.");

            if (value != frameworkValue)
            {
                throw new InvalidOperationException(
                    $"The game data builders store {what} '{name}' as {frameworkValue}, and this build's " +
                    $"{native.Name} is {value}: a built description would be read as something else.");
            }
        }
    }

    // A node's event type is carried as the framework's own number, so the three values the builder decides on
    // are checked against the game's enum by name.
    private static void Event()
    {
        Number("nothing", NodeEventTypes.None, "Null");
        Number("a timeline", NodeEventTypes.Timeline, "Timeline");
        Number("a dialog package", NodeEventTypes.Dialog, "Dialog");
    }

    private static void Number(string what, int value, string name)
    {
        var native = typeof(SchedulerNode.Event.EventType);
        var actual = Native(native, name) ?? throw new InvalidOperationException(
            $"The game data builders name the event '{name}' ({what}), which this build's {native.Name} does not " +
            "hold: the interop was generated for another build.");

        if (actual != value)
        {
            throw new InvalidOperationException(
                $"The game data builders store the event '{name}' ({what}) as {value}, and this build's " +
                $"{native.Name} is {actual}: a built node would fire the wrong thing.");
        }
    }

    // The generator emits a game enum as a C# enum; a build that emitted its members as constants instead is
    // read through the field, because that is what the name then is.
    private static int? Native(Type native, string name)
    {
        if (native.IsEnum)
            return Enum.GetNames(native).Contains(name) ? Convert.ToInt32(Enum.Parse(native, name)) : null;

        var field = native.GetField(name, BindingFlags.Public | BindingFlags.Static);
        return field is null ? null : Convert.ToInt32(field.GetRawConstantValue() ?? field.GetValue(null));
    }

    private static void Member(string what, Type owner, string name)
    {
        if (AccessTools.Field(owner, name) is null && AccessTools.Property(owner, name) is null
            && AccessTools.Method(owner, name) is null)
        {
            throw new InvalidOperationException(
                $"The game data builders' {what} ({owner.FullName}.{name}) is not a member of this build: the " +
                "interop was generated for another build.");
        }
    }
}
