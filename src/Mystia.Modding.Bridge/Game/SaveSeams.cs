using System.Text.Json;
using System.Text.Json.Nodes;
using GameData.RunTime.Common;
using GameData.Utils;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mystia;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The record list a mod's own save data rides in. It is a list of strings, each one the JSON envelope
/// <c>{"module":"...","data":{...}}</c> of exactly one mod.
/// <para>
/// This half is pure managed: it never touches a game type, so the rules that matter - replace only this
/// mod's record, copy every other record through byte for byte, keep a record nobody can read - are
/// testable with fake records.
/// </para>
/// </summary>
internal sealed class ModSaveRecords
{
    private readonly List<string> _records;

    internal ModSaveRecords(IEnumerable<string>? records) => _records = records is null ? [] : [.. records];

    internal string[] ToArray() => [.. _records];

    /// <summary>
    /// The raw JSON text stored for one mod, or null when the save holds no record for it. Text that is not
    /// the envelope, or an envelope without a module name, belongs to nobody and reads as null here; it is
    /// still copied through verbatim when the list is written back.
    /// </summary>
    internal string? Data(string moduleId)
    {
        if (string.IsNullOrEmpty(moduleId))
            return null;
        foreach (var record in _records)
        {
            if (!TryModuleOf(record, out var module) || !string.Equals(module, moduleId, StringComparison.Ordinal))
                continue;
            // A record whose "data" is missing or JSON null means the same thing to the mod: no data.
            return Envelope(record)?["data"]?.ToJsonString();
        }

        return null;
    }

    /// <summary>
    /// Replaces the mod's own record with a fresh envelope. Every other record is left untouched, including
    /// a record for a mod that is not installed right now, and a mod that has no record yet gets one
    /// appended, so the order of the records already in the save never changes.
    /// </summary>
    internal void Replace(string moduleId, JsonObject data)
    {
        if (string.IsNullOrEmpty(moduleId))
            return;

        var record = new JsonObject { ["module"] = moduleId, ["data"] = data }.ToJsonString();
        for (var index = 0; index < _records.Count; index++)
        {
            if (!TryModuleOf(_records[index], out var module) || !string.Equals(module, moduleId, StringComparison.Ordinal))
                continue;
            _records[index] = record;
            return;
        }

        _records.Add(record);
    }

    private static JsonObject? Envelope(string? record)
    {
        if (string.IsNullOrEmpty(record))
            return null;
        try
        {
            return JsonNode.Parse(record) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryModuleOf(string? record, out string module)
    {
        module = "";
        if (Envelope(record)?["module"] is not JsonValue value || !value.TryGetValue(out string? name) || string.IsNullOrEmpty(name))
            return false;
        module = name;
        return true;
    }
}

/// <summary>
/// The framework's own key inside the player's save.
/// <para>
/// The game keeps a non-active DLC entry of <c>schedulerPartialDLC</c> aside in
/// <c>RunTimePlayerData.NotLoadedDLCSchedulerSaveData</c> and copies it into the next save untouched, so
/// data written here survives a save without the mod, and comes back when the mod is installed again. The
/// key must never be registered as a real DLC and never be added to the active labels: it only works
/// because the game treats it as a DLC that is not loaded.
/// </para>
/// <para>
/// One mod's data is one string in the carrier's <c>finishedEvents</c> array, so the array is the only
/// field that is used and the remaining collections are written empty. A mod only ever sees its own JSON;
/// nothing of the game's save structure is passed on.
/// </para>
/// </summary>
internal static class ModSaveSeams
{
    /// <summary>The carrier key. It has to stay clear of CORE, the official DLC names and any recovery prefix.</summary>
    internal const string StorageKey = "MystiaExtensionFramework";

    /// <summary>Each mod's data as of its last load or save, keyed by mod id.</summary>
    private static readonly Dictionary<string, JsonObject> Current = new(StringComparer.Ordinal);

    [HarmonyPatch(typeof(SaveManagement), nameof(SaveManagement.LoadPlayerData))]
    private static class PlayerDataLoaded
    {
        // LoadPlayerData has built the running game state by now, which is where the carrier lives.
        private static void Postfix(bool __runOriginal)
        {
            if (!__runOriginal)
                return;

            Current.Clear();
            try
            {
                if (!TryOpen(out var records, out var error))
                {
                    GameBridgeHook.Trace($"Mod save data was not read: {error}");
                    return;
                }

                Dispatch.Run<IModSaveHandler>(handler => Load(handler, records));
            }
            catch (Exception failure)
            {
                // Loading the save must not fail because a mod had data the framework could not read.
                GameBridgeHook.Trace($"Mod save data was not read: {failure}");
            }
        }
    }

    [HarmonyPatch(typeof(SaveManagement), nameof(SaveManagement.WriteCurrentPlayerDataToSlotAsync))]
    [HarmonyPriority(Priority.Last)]
    private static class PlayerDataSaving
    {
        // The entry only starts its state machine here; generating the save data, serialising it and
        // writing the file all happen later on the thread pool. The mods are collected and the carrier is
        // swapped on the main thread first, so that background pass reads a snapshot nobody mutates.
        private static void Prefix(bool __runOriginal)
        {
            if (!__runOriginal)
                return;

            try
            {
                if (!TryOpen(out var records, out var error))
                {
                    GameBridgeHook.Trace($"Mod save data was left as it is: {error}");
                    return;
                }

                var captured = false;
                Dispatch.Run<IModSaveHandler>(handler => captured |= Capture(handler, records));
                if (captured)
                    Publish(records.ToArray());
            }
            catch (Exception failure)
            {
                // The save itself goes on: the carrier keeps whatever the game already had.
                GameBridgeHook.Trace($"Mod save data was left as it is: {failure}");
            }
        }
    }

    [HarmonyPatch(typeof(SaveManagement), nameof(SaveManagement.DisposeGameStatusAndBackToMainMenu))]
    private static class GameStatusDropped
    {
        // There is no game state behind the main menu, and the next game must not inherit the data of the
        // one that just ended.
        private static void Prefix() => Current.Clear();
    }

    private static void Load(IModSaveHandler handler, ModSaveRecords records)
    {
        var modId = ContentOrigin.Of(handler);
        if (string.IsNullOrEmpty(modId))
        {
            GameBridgeHook.Trace($"A {handler.GetType().FullName} save handler has no mod origin; it was skipped.");
            return;
        }

        var raw = records.Data(modId);
        using var document = raw is null ? null : JsonDocument.Parse(raw);
        try
        {
            handler.OnModLoad(document);
        }
        catch (Exception error)
        {
            GameBridgeHook.Trace($"Mod '{modId}' failed to read its save data: {error}");
        }

        var data = raw is null ? null : JsonNode.Parse(raw) as JsonObject;
        if (raw is not null && data is null)
            GameBridgeHook.Trace($"The save data of mod '{modId}' is not a JSON object; it is treated as empty.");
        Current[modId] = data ?? new JsonObject();
    }

    private static bool Capture(IModSaveHandler handler, ModSaveRecords records)
    {
        var modId = ContentOrigin.Of(handler);
        if (string.IsNullOrEmpty(modId))
        {
            GameBridgeHook.Trace($"A {handler.GetType().FullName} save handler has no mod origin; it was skipped.");
            return false;
        }

        var current = Current.TryGetValue(modId, out var state) ? state : new JsonObject();
        var target = new JsonObject();
        try
        {
            handler.OnModSave(current, target);
        }
        catch (Exception error)
        {
            // A broken callback must not erase what the player already has: the mod's record stays as it was.
            GameBridgeHook.Trace($"Mod '{modId}' failed to write its save data, its previous record is kept: {error}");
            return false;
        }

        records.Replace(modId, target);
        Current[modId] = (JsonObject)target.DeepClone();
        return true;
    }

    private static bool TryOpen(out ModSaveRecords records, out string error)
    {
        records = new ModSaveRecords(null);
        if (!TryReadCarrier(out var values, out error))
            return false;
        records = new ModSaveRecords(values);
        return true;
    }

    /// <summary>Reads the record list out of the carrier. False when the carrier cannot be trusted as ours.</summary>
    private static bool TryReadCarrier(out string[] values, out string error)
    {
        values = [];
        error = "";
        var table = RunTimePlayerData.NotLoadedDLCSchedulerSaveData;
        if (table is null)
        {
            error = "there is no running game state holding the carrier";
            return false;
        }

        if (!table.ContainsKey(StorageKey))
            return true;

        var carrier = table[StorageKey];
        if (carrier is not null && !IsCarrier(carrier))
        {
            error = $"the entry under '{StorageKey}' is not this framework's carrier, the original data was kept";
            return false;
        }

        if (carrier is null || carrier.finishedEvents is null)
        {
            error = $"the entry under '{StorageKey}' has no record array, the original data was kept";
            return false;
        }

        var records = new string[carrier.finishedEvents.Length];
        for (var index = 0; index < records.Length; index++)
            records[index] = carrier.finishedEvents[index];
        values = records;
        return true;
    }

    /// <summary>
    /// A carrier this framework wrote: only <c>finishedEvents</c> is used, so every other collection is
    /// empty. Anything else under this key was written by someone else and is left alone.
    /// </summary>
    private static bool IsCarrier(PlayerSaveFile.DLCSchedulerSaveData carrier) =>
        carrier.scheduledEvents is null || carrier.scheduledEvents.Count == 0
        && (carrier.scheduledNews is null || carrier.scheduledNews.Count == 0)
        && (carrier.scheduledNewsReplaceContents is null || carrier.scheduledNewsReplaceContents.Count == 0)
        && (carrier.allTrackingMissions is null || carrier.allTrackingMissions.Count == 0)
        && (carrier.finishedMissions is null || carrier.finishedMissions.Length == 0);

    /// <summary>
    /// Builds a new carrier and a new dictionary and swaps the dictionary in. The previous objects are left
    /// as they are: a save already in flight keeps reading its own snapshot, and only this framework's own
    /// entry changes while every other DLC entry is carried over untouched.
    /// </summary>
    private static void Publish(string[] records)
    {
        var carrier = new PlayerSaveFile.DLCSchedulerSaveData
        {
            // The game records the progress the entry was written at; it is never used as a version marker.
            dlcSaveDate = RunTimePlayerData.GetDay().CorrectedDay,
            scheduledEvents = new(),
            scheduledNews = new(),
            scheduledNewsReplaceContents = new(),
            allTrackingMissions = new(),
            finishedEvents = ToIl2CppStrings(records),
            finishedMissions = new Il2CppStringArray(0),
        };

        var current = RunTimePlayerData.NotLoadedDLCSchedulerSaveData;
        var replacement = new Il2CppSystem.Collections.Generic.Dictionary<string, PlayerSaveFile.DLCSchedulerSaveData>();
        if (current is not null)
        {
            foreach (var entry in current)
                replacement[entry.Key] = entry.Value;
        }

        replacement[StorageKey] = carrier;
        RunTimePlayerData.NotLoadedDLCSchedulerSaveData = replacement;
    }

    private static Il2CppStringArray ToIl2CppStrings(string[] records)
    {
        var array = new Il2CppStringArray(records.Length);
        for (var index = 0; index < records.Length; index++)
            array[index] = records[index];
        return array;
    }
}
