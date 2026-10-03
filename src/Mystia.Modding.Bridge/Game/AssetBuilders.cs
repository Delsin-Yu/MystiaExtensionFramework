using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Common.DialogUtility;
using Common.UI;
using GameData.Core.Collections;
using GameData.Core.Collections.DaySceneUtility;
using GameData.CoreLanguage;
using GameData.CoreLanguage.Collections;
using GameData.Profile;
using GameData.Profile.SchedulerNodeCollection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mystia.Assets;
using Mystia.Data;
using UnityEngine;
using UnityEngine.AddressableAssets;

// The engine's own reference type shares its name with the framework's (a key and the address it is filed
// under): in this file a reference a mod hands in is the framework's, and the engine's own is only ever the
// thing a slot is filled with.
using AssetReference = Mystia.Assets.AssetReference;

namespace Mystia.Modding.Bridge;

// The engine side of Mystia.Assets.IGameDataBuilder: everything a built dialog package or scheduler node has to
// name a Unity or a game type for - the ScriptableObject itself, the DialogMeta lines and their DialogAction
// entries, the AssetReference each asset slot carries, the MissionNode / EventNode fields, the game's own
// tables the finished objects are published into. This is the work a mod used to do in its own dialog and node
// mappers (MetaMystia's DialogRegistry and Mappers); nothing module specific is left in it, so the builders are
// the same for every mod and the mod only describes the object.
//
// What crosses the boundary is values only: a SpriteHandle or an AudioClipHandle is resolved to the engine
// object the factory built it into and filed under a key of its own, an AssetReference is an address that is
// already filed, and the line texts are handed to the same replacement pass the data face's dialogs use.
//
// The record level mapping (a reward, a finish condition, a trigger, a day, an event and how a node's event
// names its dialog package) is the one the data face already uses: GameRecords.Reward / FinishCondition /
// Trigger / Event are shared, so a node built here and a node injected as MissionNodeData mean the same thing.

/// <summary>
/// The builder reached through <c>Mystia.Assets.GameDataBuilders</c>. It decides everything that does not need
/// the engine (see <c>GameDataValidation</c>) and keeps what it built, so a mod may ask whether a package was
/// built without holding the handle.
/// </summary>
internal static class AssetBuilderHost
{
    /// <summary>
    /// Hands the SDK builder the engine side while the bridge assembly loads, which is before the host calls any
    /// mod and before the first seam runs. The SDK resolves it per call, so the order between this and the
    /// framework's other module initializers does not matter.
    /// </summary>
    [ModuleInitializer]
    internal static void Hook() => GameDataBuilders.Install(UnityGameDataAssembler.Shared);
}

/// <summary>
/// The line texts a built dialog package shows. The game reads every line's text out of the package's own text
/// asset keyed by the line number, and a package built here has no text asset of its own; the framework's
/// <c>DialogScripts</c> fills the line numbers it remembers into the callback the game hands the replacement
/// dictionary, and this fills what that pass cannot: a branch's option texts live under negative ids (the
/// package's own text asset carries the line numbers, not the options), which is why the options are remembered
/// apart from the lines.
/// </summary>
internal static class AssetBuilderTexts
{
    private static readonly Dictionary<string, Dictionary<int, string>> Extras = new(StringComparer.Ordinal);

    /// <summary>Remembers the texts a built package shows for ids its line numbers do not cover.</summary>
    internal static void Remember(string name, Dictionary<int, string> texts) => Extras[name] = texts;

    /// <summary>
    /// Wraps the game's replacement callback so a built package's own texts are written into it. Wrapping what
    /// is already there keeps the ordering with the framework's own pass out of the question: whichever ran
    /// first, both are applied to the dictionary the game then reads.
    /// </summary>
    internal static void Fill(
        DialogPackage? package,
        ref Il2CppSystem.Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>> callback)
    {
        if (package is null || string.IsNullOrEmpty(package.name))
            return;
        if (!Extras.TryGetValue(package.name, out var texts) || texts.Count == 0)
            return;

        var prior = callback;
        var captured = texts;
        callback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>>>(
            new Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>>(map =>
            {
                prior?.Invoke(map);
                foreach (var (id, text) in captured)
                    map[id] = text;
            }))!;
    }

    internal static void ResetForTests() => Extras.Clear();
}

/// <summary>
/// The seams the built objects need: the game rebuilds its tables when its databases initialize, so what was
/// built is written into them again after each rebuild, and the text pass above is wrapped into the dialog
/// panel's opening. Every class here is found by the framework's own patch installer, which walks the types of
/// this assembly.
/// </summary>
internal static class AssetBuilderSeams
{
    [HarmonyPatch(typeof(UniversalGameManager), nameof(UniversalGameManager.OpenDialogMenu))]
    private static class DialogTexts
    {
        private static void Postfix(
            DialogPackage dialogPackage,
            ref Il2CppSystem.Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>> overrideReplaceTextCallback) =>
            AssetBuilderTexts.Fill(dialogPackage, ref overrideReplaceTextCallback);
    }

    [HarmonyPatch(typeof(UniversalGameManager), nameof(UniversalGameManager.OpenDialogMenuWithExitCode))]
    private static class DialogExitTexts
    {
        private static void Postfix(
            DialogPackage dialogPackage,
            ref Il2CppSystem.Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>> overrideReplaceTextCallback) =>
            AssetBuilderTexts.Fill(dialogPackage, ref overrideReplaceTextCallback);
    }

    [HarmonyPatch(typeof(DataBaseDay), nameof(DataBaseDay.Initialize))]
    private static class DayTable
    {
        private static void Postfix() => GameDataBuilders.RepublishAll();
    }

    [HarmonyPatch(typeof(DataBaseScheduler), nameof(DataBaseScheduler.Initialize))]
    private static class SchedulerTable
    {
        private static void Postfix() => GameDataBuilders.RepublishAll();
    }

    [HarmonyPatch(typeof(DataBaseLanguage), nameof(DataBaseLanguage.Initialize))]
    private static class LanguageTable
    {
        private static void Postfix() => GameDataBuilders.RepublishAll();
    }
}

/// <summary>
/// The engine implementation of the game data assembler. Everything it does reaches the engine, so it is main
/// thread only, and every failure - a handle the framework did not build, a table that is not there yet, the
/// engine refusing a call - is reported as a reason rather than thrown.
/// </summary>
internal sealed class UnityGameDataAssembler : IGameDataAssembler
{
    internal static readonly UnityGameDataAssembler Shared = new();

    // What one published node is written from: the engine object, the description it was built from (the dialog
    // package its events play is resolved when the node is published, because the package may be built later or
    // injected by another pass) and whether it carries a language entry.
    private sealed record NodeEntry(SchedulerNode Node, MissionNodeSpec? Mission, EventNodeSpec? Event);

    // The addresses one line's assets were resolved to, built before any engine object exists: an empty string
    // is the empty reference a slot the game may read has to carry.
    private sealed record LineAssets(string OverrideSprite, ActionAssets[] Actions);

    private readonly record struct ActionAssets(string Image, string Sound, string Bgm);

    private readonly Dictionary<string, DialogPackage> _dialogs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NodeEntry> _nodes = new(StringComparer.Ordinal);

    public void Trace(string message) => GameBridgeHook.Trace(message);

    public bool TryAssembleDialog(DialogSpec spec, out string? reason)
    {
        reason = null;
        var name = spec.Name!;
        var lines = spec.Lines!;
        var template = StockDialog();
        if (template is null)
        {
            reason = "the day scene's dialog table holds no package to copy the engine side of a package from (build a dialog after the day database initialized once)";
            return false;
        }

        if (!Assets(name, lines, out var assets, out reason))
            return false;

        DialogPackage? package = null;
        try
        {
            package = UnityEngine.Object.Instantiate(template);
            package.name = name;
            package.hideFlags = HideFlags.HideAndDontSave;
            var metas = new Il2CppReferenceArray<DialogMeta>(lines.Count);
            var texts = new DialogLine[lines.Count];
            var options = new Dictionary<int, string>();
            var virtualId = -1;
            for (var index = 0; index < lines.Count; index++)
            {
                metas[index] = Meta(lines[index], index, assets[index], options, ref virtualId);
                // The text pass is keyed by the line number, which is the meta's dialog id.
                texts[index] = new DialogLine { Text = lines[index].Text };
            }

            package.dialogMeta = metas;
            DialogScripts.Remember(name, texts);
            AssetBuilderTexts.Remember(name, options);
            _dialogs[name] = package;
            return true;
        }
        catch (Exception error)
        {
            // A package that died halfway is destroyed whole: it is hidden from the scene teardown, so nothing
            // else would ever release it.
            if (package is not null)
                UnityEngine.Object.DestroyImmediate(package);
            reason = $"the engine refused the package: {error.GetBaseException().Message}";
            return false;
        }
    }

    public bool TryPublishDialog(string name, out string? reason)
    {
        reason = null;
        if (!_dialogs.TryGetValue(name, out var package))
        {
            reason = "the package was not built here";
            return false;
        }

        var table = DataBaseDay.allDialogPackages;
        if (table is null)
        {
            reason = "the day scene's dialog table is not initialized";
            return false;
        }

        table[name] = package;
        return true;
    }

    public bool TryAssembleMissionNode(MissionNodeSpec spec, out string? reason)
    {
        reason = null;
        var label = spec.Label!;
        try
        {
            var node = ScriptableObject.CreateInstance<MissionNode>();
            node.name = label;
            node.hideFlags = HideFlags.HideAndDontSave;
            node.label = label;
            node.debugLabel = spec.DebugLabel ?? label;
            node.missionType = (SchedulerNode.SchedulerType)spec.MissionType;
            node.isTimedMission = spec.IsTimedMission;
            node.loopedMission = spec.Looped;
            node.missionFailedAction = (MissionNode.MissionFailedAction)spec.MissionFailedAction;
            node.hasSender = !string.IsNullOrEmpty(spec.Sender);
            node.sender = spec.Sender ?? "";
            node.hasReciever = !string.IsNullOrEmpty(spec.Receiver);
            node.reciever = spec.Receiver ?? "";
            node.hideReciever = spec.HideReceiver;
            node.missionTimeLimit = GameRecords.Trigger(spec.MissionTimeLimit);
            node.missionFinishEvent = GameRecords.Event(spec.MissionFinishEvent, GameRecords.FindDialog);
            node.missionFailedEvent = GameRecords.Event(spec.MissionFailedEvent, GameRecords.FindDialog);
            node.rewards = Rewards(spec.Rewards);
            node.postRewards = Rewards(spec.PostRewards);
            node.finishCondition = Conditions(spec.FinishConditions);
            node.preNodes = Strings(spec.PreNodes);
            node.postMissions = Strings(spec.PostMissions);
            node.postMissionsAfterPerformance = Strings(spec.PostMissionsAfterPerformance);
            node.postEvents = Strings(spec.PostEvents);
            _nodes[label] = new NodeEntry(node, spec, null);
            return true;
        }
        catch (Exception error)
        {
            reason = $"the engine refused the node: {error.GetBaseException().Message}";
            return false;
        }
    }

    public bool TryAssembleEventNode(EventNodeSpec spec, out string? reason)
    {
        reason = null;
        var label = spec.Label!;
        try
        {
            var node = ScriptableObject.CreateInstance<EventNode>();
            node.name = label;
            node.hideFlags = HideFlags.HideAndDontSave;
            node.label = label;
            node.debugLabel = spec.DebugLabel ?? label;
            node.scheduleOnce = spec.ScheduleOnce;
            node.saveToArchiveOnce = spec.SaveToArchiveOnce;
            node.autoCompleteAtDayEnd = spec.AutoCompleteAtDayEnd;
            node.eventLockMode = (EventNode.EventLockMode)spec.EventLockMode;
            node.scheduledEvent = new SchedulerNode.ScheduledEvent
            {
                trigger = GameRecords.Trigger(spec.Trigger),
                eventData = GameRecords.Event(spec.ScheduledEvent, GameRecords.FindDialog),
            };
            node.rewards = Rewards(spec.Rewards);
            node.postRewards = Rewards(spec.PostRewards);
            node.preNodes = Strings(spec.PreNodes);
            node.postMissions = Strings(spec.PostMissions);
            node.postMissionsAfterPerformance = Strings(spec.PostMissionsAfterPerformance);
            node.postEvents = Strings(spec.PostEvents);
            _nodes[label] = new NodeEntry(node, null, spec);
            return true;
        }
        catch (Exception error)
        {
            reason = $"the engine refused the node: {error.GetBaseException().Message}";
            return false;
        }
    }

    public bool TryPublishNode(string label, out string? reason)
    {
        reason = null;
        if (!_nodes.TryGetValue(label, out var entry))
        {
            reason = "the node was not built here";
            return false;
        }

        var nodes = DataBaseScheduler.allNodes;
        if (nodes is null)
        {
            reason = "the scheduler's node table is not initialized";
            return false;
        }

        // The dialog a node's event plays is resolved against the day scene's table now, not when the node was
        // built: a package built here, or injected through the data face, is found whatever the order was.
        Resolve(entry);
        nodes[label] = entry.Node;
        WriteLanguage(label, entry);
        return true;
    }

    // ── the record level ──

    /// <summary>
    /// Points a node's events at the dialog packages they name. A node is published after the day scene's table
    /// exists, so a package under the same name is what the node carries; a name the table does not hold leaves
    /// the event without a package, which is what the data face's own pass does with a missing name.
    /// </summary>
    private static void Resolve(NodeEntry entry)
    {
        switch (entry.Node)
        {
            case MissionNode mission when entry.Mission is { } spec:
                mission.missionFinishEvent = GameRecords.Event(spec.MissionFinishEvent, GameRecords.FindDialog);
                mission.missionFailedEvent = GameRecords.Event(spec.MissionFailedEvent, GameRecords.FindDialog);
                break;
            case EventNode node when entry.Event is { } spec:
                var scheduled = node.scheduledEvent;
                scheduled.eventData = GameRecords.Event(spec.ScheduledEvent, GameRecords.FindDialog);
                node.scheduledEvent = scheduled;
                break;
        }
    }

    /// <summary>
    /// The mission language entry of a built node: the mission panel reads a node's name out of
    /// <c>DataBaseLanguage.Missions</c>. The stock event nodes have no entry of their own, so an event is only
    /// written when the description named it; the table is rebuilt with the language database, which is why the
    /// write is repeated with the node.
    /// </summary>
    private static void WriteLanguage(string label, NodeEntry entry)
    {
        var missions = DataBaseLanguage.Missions;
        if (missions is null)
            return;

        string? name;
        string? description;
        if (entry.Mission is { } mission)
        {
            // A mission is named even when the description does not name it: the panel reads the entry by the
            // node's label, which is what the data face's own pass falls back to as well.
            name = mission.Name ?? label;
            description = mission.Description ?? "";
        }
        else if (entry.Event is { } @event)
        {
            // The stock event nodes have no language entry of their own, so an event writes one only when the
            // description asked for it.
            if (string.IsNullOrWhiteSpace(@event.Name) && string.IsNullOrWhiteSpace(@event.Description))
                return;
            name = @event.Name;
            description = @event.Description ?? "";
        }
        else
        {
            return;
        }

        missions[label] = new LanguageBase(name, description);
    }

    private static Il2CppReferenceArray<SchedulerNode.Reward> Rewards(IReadOnlyList<SchedulerRewardData>? rewards)
    {
        rewards ??= [];
        var array = new Il2CppReferenceArray<SchedulerNode.Reward>(rewards.Count);
        for (var index = 0; index < rewards.Count; index++)
            array[index] = GameRecords.Reward(rewards[index]);
        return array;
    }

    private static Il2CppReferenceArray<MissionNode.FinishCondition> Conditions(IReadOnlyList<MissionFinishConditionData>? conditions)
    {
        conditions ??= [];
        var array = new Il2CppReferenceArray<MissionNode.FinishCondition>(conditions.Count);
        for (var index = 0; index < conditions.Count; index++)
            array[index] = GameRecords.FinishCondition(conditions[index]);
        return array;
    }

    private static Il2CppStringArray Strings(IReadOnlyList<string>? values)
    {
        values ??= [];
        var array = new Il2CppStringArray(values.Count);
        for (var index = 0; index < values.Count; index++)
            array[index] = values[index];
        return array;
    }

    // ── the dialog package ──

    private static DialogMeta Meta(
        DialogLineSpec line,
        int index,
        LineAssets assets,
        Dictionary<int, string> options,
        ref int virtualId)
    {
        var speaker = line.Speaker!.Value;
        var meta = new DialogMeta
        {
            dialogId = index,
            speakerIdentity = new SpeakerIdentity(
                (SpeakerIdentity.Identity)(int)speaker.Kind,
                speaker.Id,
                speaker.Portrait),
            speakerPosition = (Position)(int)line.Side,
            isSpeakInForeground = line.IsSpeakInForeground,
            isDark = line.IsDark,
            useNameInText = line.UseNameInText,
            useOverrideSprite = assets.OverrideSprite.Length != 0,
            m_OverrideSpriteAsset = new AssetReferenceSprite(assets.OverrideSprite),
        };

        var actions = line.Actions ?? [];
        var built = new Il2CppReferenceArray<DialogAction>(actions.Count);
        for (var at = 0; at < actions.Count; at++)
            built[at] = Action(actions[at], assets.Actions[at], options, ref virtualId);
        meta.dialogAction = built;
        return meta;
    }

    // One inline action. Every asset slot the game may read is filled with a reference, empty where the action
    // does not carry that asset: the game asks a slot for its key without checking it for null (the mod that
    // previously owned this code carried the same comment), so a slot that is never filled is one the panel
    // crashes on.
    private static DialogAction Action(
        DialogActionSpec spec,
        ActionAssets assets,
        Dictionary<int, string> options,
        ref int virtualId)
    {
        var action = new DialogAction
        {
            actionType = (ActionType)(int)spec.Kind,
            shouldSet = spec.ShouldSet,
            m_SpriteAsset = new AssetReferenceSprite(assets.Image),
            m_SpriteENAsset = new AssetReferenceSprite(""),
            m_SpriteJPAsset = new AssetReferenceSprite(""),
            m_SpriteKOAsset = new AssetReferenceSprite(""),
            m_SpriteCNTAsset = new AssetReferenceSprite(""),
            m_MaterialAsset = new AssetReferenceT<Material>(""),
            m_AudioAsset = new AssetReferenceT<AudioClip>(assets.Sound),
            m_BgmPackageAsset = new AssetReferenceT<LoopedBGMPackage>(assets.Bgm),
        };

        switch (spec.Kind)
        {
            case DialogActionKind.ForegroundCleaning:
                var sides = spec.CleanSides ?? [];
                var cleaning = new Il2CppStructArray<Position>(sides.Count);
                for (var index = 0; index < sides.Count; index++)
                    cleaning[index] = (Position)(int)sides[index];
                action.foregroundCleaning = cleaning;
                break;

            case DialogActionKind.Branch:
                var branch = spec.Options!;
                var selections = new Il2CppStructArray<int>(branch.Count);
                var jumps = new Il2CppStructArray<int>(branch.Count);
                var prices = new Il2CppStructArray<int>(branch.Count);
                for (var index = 0; index < branch.Count; index++)
                {
                    var option = branch[index];
                    // An option's text is not in the package's text asset, so it is shown under an id of its
                    // own: the game asks the replacement dictionary for the id the selection carries, and the
                    // ids of a text asset are the line numbers, which is why these are negative.
                    selections[index] = virtualId--;
                    options[selections[index]] = option.Text ?? "";
                    jumps[index] = option.Jump - 1;
                    prices[index] = option.Price ?? 0;
                }

                action.selections = selections;
                action.jumps = jumps;
                action.prices = prices;
                break;

            case DialogActionKind.Goto:
            case DialogActionKind.End:
                action.index = spec.Index ?? 0;
                break;
        }

        return action;
    }

    /// <summary>
    /// Resolves every asset a package names before an engine object exists: a handle the framework built is
    /// filed under a key of its own and read back as the address the engine's reference is built from, and a
    /// reference is used as it is. A handle this framework did not build cannot be filed, which is why the
    /// package is refused instead of carrying an address nothing resolves.
    /// </summary>
    private static bool Assets(
        string name,
        IReadOnlyList<DialogLineSpec> lines,
        [NotNullWhen(true)] out LineAssets[]? assets,
        out string? reason)
    {
        reason = null;
        assets = new LineAssets[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var number = index + 1;
            if (!Sprite($"{name}/override/{number}", line.OverrideSprite, line.OverrideSpriteReference, out var over, out reason))
            {
                reason = $"line {number} carries {reason}";
                return false;
            }

            var actions = line.Actions ?? [];
            var resolved = new ActionAssets[actions.Count];
            for (var at = 0; at < actions.Count; at++)
            {
                var action = actions[at];
                var where = $"line {number} action {at + 1}";
                if (!Sprite($"{name}/image/{number}/{at + 1}", action.Sprite, action.SpriteReference, out var image, out reason))
                {
                    reason = $"{where} carries {reason}";
                    return false;
                }

                if (!Clip($"{name}/sound/{number}/{at + 1}", action.Sound, action.SoundReference, out var sound, out reason))
                {
                    reason = $"{where} carries {reason}";
                    return false;
                }

                resolved[at] = new ActionAssets(image, sound, action.BgmPackage?.Address ?? "");
            }

            assets[index] = new LineAssets(over, resolved);
        }

        return true;
    }

    private static bool Sprite(string key, SpriteHandle? sprite, AssetReference? reference, out string address, out string? reason)
    {
        address = "";
        reason = null;
        if (reference is not null)
        {
            address = reference.Address;
            return true;
        }

        if (sprite is null)
            return true;
        if (!AssetLocator.Shared.TryRegisterSprite(key, sprite, out var filed))
        {
            reason = $"an image this framework did not build ({sprite.GetType().Name})";
            return false;
        }

        address = filed!.Address;
        return true;
    }

    private static bool Clip(string key, AudioClipHandle? clip, AssetReference? reference, out string address, out string? reason)
    {
        address = "";
        reason = null;
        if (reference is not null)
        {
            address = reference.Address;
            return true;
        }

        if (clip is null)
            return true;
        if (!AssetLocator.Shared.TryRegisterAudioClip(key, clip, out var filed))
        {
            reason = $"a sound this framework did not build ({clip.GetType().Name})";
            return false;
        }

        address = filed!.Address;
        return true;
    }

    /// <summary>
    /// A stock package the engine side of a built one is copied from: the day scene's own packages carry the
    /// text asset reference every dialog panel loads, the language independent sprite slots and whatever else
    /// the build does not fill in, so a package built from one is a package the panel can open. What the
    /// description describes (the lines, their speakers and their actions) replaces the template's own.
    /// </summary>
    private static DialogPackage? StockDialog()
    {
        var table = DataBaseDay.allDialogPackages;
        if (table is null)
            return null;
        foreach (var package in table.Values)
        {
            if (package is not null)
                return package;
        }

        return null;
    }
}
