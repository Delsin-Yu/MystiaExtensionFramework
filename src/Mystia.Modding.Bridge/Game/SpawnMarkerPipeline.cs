using HarmonyLib;
using UnityEngine;

using DayScene;
using DayScene.Input;
using DayScene.Interactables;
using GameData.Core.Collections.DaySceneUtility;
using GameData.Core.Collections.DaySceneUtility.Collections;
using GameData.RunTime.DaySceneUtility.Collection;

using Mystia.Data;

namespace Mystia.Modding.Bridge;

/// <summary>
/// Spawn marker injection.
/// <para>
/// A day scene map builds its marker table from the engine objects under <c>spawnMarkerField</c>
/// (<c>DaySceneMap.GenerateSpawnMarkerData</c>), so an injected point does not exist until an object of
/// that name is under that field. Two injection paths name their points differently, and the difference
/// decides whether the day scene can find them:
/// a map point is named "map label + point name" (<see cref="GameRecords.Marker"/>, the same name the
/// database pass writes into the map's label set), while a special guest's point carries the character
/// label verbatim - the day scene locates a tracked NPC by <c>npc.key</c>, which is exactly that label.
/// </para>
/// </summary>
internal static class SpawnMarkerPipeline
{
    private readonly record struct InjectedPoint(string MapLabel, string MarkerName, float X, float Y, CharacterRotationKind Rotation);

    private static readonly List<InjectedPoint> Points = [];

    // Labels of injected characters whose point is named after the character itself.
    private static readonly HashSet<string> Characters = new(StringComparer.Ordinal);

    /// <summary>
    /// Takes the day map and special guest data of the database pass. That data only exists while the
    /// tables are being collected (see <c>DatabaseInject.Collect</c>), so the points are remembered here.
    /// </summary>
    internal static void Remember(IReadOnlyList<DayMapData> maps, IReadOnlyList<SpecialGuestData> guests)
    {
        Points.Clear();
        Characters.Clear();
        foreach (var map in maps)
        {
            if (string.IsNullOrEmpty(map.Label))
                continue;
            foreach (var point in map.SpawnMarkers ?? [])
                Points.Add(new InjectedPoint(map.Label, GameRecords.Marker(map.Label, point), point.X, point.Y, point.Rotation));
        }

        foreach (var guest in guests)
        {
            var point = guest.SpawnMarker;
            if (point is null || string.IsNullOrEmpty(guest.Label))
                continue;
            // The point of a special guest keeps the character label verbatim: the day scene asks for the
            // marker by <c>npc.key</c>, which is this same label. The "map label + point name" join of the
            // map points would produce a name the day scene never asks for.
            Characters.Add(guest.Label);
            var marker = point.Value;
            Points.Add(new InjectedPoint(marker.MapLabel ?? "", guest.Label, marker.X, marker.Y, marker.Rotation));
        }
    }

    /// <summary>
    /// Fills in the points the map itself could not build. Each one becomes a <c>SpawnMarker</c> engine
    /// object under the map's <c>spawnMarkerField</c>, is added to the table the map just generated (the
    /// same dictionary instance the map keeps as <c>AllSpawnMarkers</c>), and is registered in
    /// <c>DataBaseDay.allSpawnMarkerLabels</c> for that map - the day scene resolves which map a marker
    /// belongs to through that label set alone.
    /// </summary>
    internal static void AddMissing(DaySceneMap map, Il2CppSystem.Collections.Generic.Dictionary<string, SpawnMarker> markers)
    {
        if (map is null || markers is null)
            return;
        if (map.spawnMarkerField is null)
        {
            GameBridgeHook.Trace($"SpawnMarkerPipeline: map {map.mapLabel} has no spawn marker field, injected points skipped");
            return;
        }

        foreach (var point in Points)
        {
            if (!string.Equals(point.MapLabel, map.mapLabel, StringComparison.Ordinal)
                || string.IsNullOrEmpty(point.MarkerName)
                || markers.ContainsKey(point.MarkerName))
                continue;

            var host = new GameObject(point.MarkerName);
            host.transform.SetParent(map.spawnMarkerField, false);
            host.transform.position = new Vector3(point.X, point.Y, 0f);
            var marker = host.AddComponent<SpawnMarker>();
            marker.spawnMarkerName = point.MarkerName;
            marker.targetRotation = (DayScenePlayerInputGenerator.CharacterRotation)(int)point.Rotation;
            markers.Add(point.MarkerName, marker);
            Labels(map.mapLabel)?.Add(point.MarkerName);
        }
    }

    /// <summary>Whether the label is an injected character that owns a point, named with that same label.</summary>
    internal static bool OwnsMarker(string? label) => !string.IsNullOrEmpty(label) && Characters.Contains(label);

    /// <summary>
    /// Rebuilds the points of the current map. A map instance keeps the marker table it generated the first
    /// time, so re-entering a map does not rebuild it; this is the entry point of
    /// <c>IDaySceneMapServices.RefreshSpawnMarkers</c> as well as of the refresh the bridge runs after a map swap.
    /// </summary>
    internal static void RefreshCurrent()
    {
        var map = DayScene.SceneManager.instance?.CurrentActiveMap;
        if (map is not null)
            Refresh(map);
    }

    // GenerateSpawnMarkerData re-scans the field's children, so the rebuild keeps the points injected by the
    // postfix (they are children of that field) and adds whatever is still missing.
    private static void Refresh(DaySceneMap map) => map.AllSpawnMarkers = map.GenerateSpawnMarkerData();

    // The label set of a map is created by the database pass for injected maps; a map that only the mod
    // builds has no key yet, and an unregistered marker name would be attributed to no map at all.
    private static Il2CppSystem.Collections.Generic.HashSet<string>? Labels(string mapLabel)
    {
        var table = DataBaseDay.allSpawnMarkerLabels;
        if (table is null)
        {
            GameBridgeHook.Trace($"SpawnMarkerPipeline: the spawn marker label table is not initialized, points of map {mapLabel} stay unregistered");
            return null;
        }

        if (!table.ContainsKey(mapLabel))
            table[mapLabel] = new Il2CppSystem.Collections.Generic.HashSet<string>();
        return table[mapLabel];
    }
}

internal static class SpawnMarkerSeams
{
    [HarmonyPatch(typeof(DaySceneMap), nameof(DaySceneMap.GenerateSpawnMarkerData))]
    private static class Generate
    {
        private static void Postfix(DaySceneMap __instance, Il2CppSystem.Collections.Generic.Dictionary<string, SpawnMarker> __result) =>
            SpawnMarkerPipeline.AddMissing(__instance, __result);
    }
}

internal static class InjectedCharacterPositionSeams
{
    /// <summary>
    /// Locates an injected character on its own point.
    /// <para>
    /// A tracked NPC is positioned through the destination stored in it, and for an injected special guest
    /// that destination is the template's (the database pass copies the stock guest record), so the point
    /// would be looked up under a name the guest does not own. The point of an injected character is its
    /// label, which is also <c>npc.key</c>. The destination change only reaches the copy used by this call:
    /// the tracked NPC itself keeps its destination and dialog pool.
    /// </para>
    /// </summary>
    [HarmonyPatch(typeof(DaySceneMap), nameof(DaySceneMap.SolveAndUpdateCharacterPositionInternal))]
    private static class Solve
    {
        private static bool Prefix(
            DaySceneMap __instance,
            ref Il2CppSystem.Collections.Generic.Dictionary<string, TrackedNPC> npcs,
            ref TrackedNPC npc,
            ref bool isNPCOnMap)
        {
            if (npc is null || !SpawnMarkerPipeline.OwnsMarker(npc.key))
                return true;

            var markerName = npc.key;
            if (!__instance.AllSpawnMarkers.ContainsKey(markerName))
            {
                // The character has no point on this map: hide it instead of letting the original read a
                // missing marker out of AllSpawnMarkers (which logs and throws).
                isNPCOnMap = false;
                return false;
            }

            npc = npc.Clone();
            npc.currentDestination = new NPC.Destination { spawnMarker = markerName };
            npc.overridePosition = null;
            if (!npcs.ContainsKey(markerName))
            {
                // The map's runtime NPC table does not hold the character yet (the mod that owns it fills
                // that table after the map load): position it for this call through a table of its own.
                npcs = new Il2CppSystem.Collections.Generic.Dictionary<string, TrackedNPC>();
                npcs.Add(markerName, npc);
            }

            return true;
        }
    }
}
