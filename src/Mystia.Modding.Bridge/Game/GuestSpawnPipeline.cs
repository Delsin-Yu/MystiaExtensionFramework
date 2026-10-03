using System.Collections;
using GameData.Core.Collections.CharacterUtility;
using GameData.Core.Collections.NightSceneUtility;
using GameData.Profile;
using HarmonyLib;
using Mystia.Listeners;
using Mystia.Scenes;
using NightScene.CookingUtility;
using NightScene.GuestManagementUtility;
using UnityEngine;

namespace Mystia.Modding.Bridge;

internal static class GuestSpawnPipeline
{
    /// <summary>The rolled guests of one group, as the framework's own descriptions.</summary>
    internal static List<GuestDescription> Describe(IEnumerable? guests)
    {
        var descriptions = new List<GuestDescription>();
        if (guests is null)
            return descriptions;
        foreach (var item in guests)
        {
            if (item is NormalGuest guest)
                descriptions.Add(new GuestDescription(guest.id));
        }

        return descriptions;
    }

    /// <summary>The descriptions every modifier left behind, resolved back to the game's own guests.</summary>
    internal static List<NormalGuest> Resolve(IReadOnlyList<GuestDescription> guests)
    {
        var resolved = new List<NormalGuest>(guests.Count);
        foreach (var description in guests)
        {
            if (Resolve(description) is { } guest)
                resolved.Add(guest);
            else
                Report(description);
        }

        return resolved;
    }

    internal static void ApplyNormal(ref List<GuestDescription> guests)
    {
        foreach (var modifier in Dispatch.Instances<IGuestSpawnModifier>())
            modifier.OnNormalGuestsGenerating(ref guests);
    }

    internal static void ApplySpecial(ref int guestId)
    {
        foreach (var modifier in Dispatch.Instances<IGuestSpawnModifier>())
            modifier.OnSpecialGuestGenerating(ref guestId);
    }

    internal static bool HasVisualModifier => Dispatch.Instances<IGuestSpawnModifier>().Any();

    internal static int StockVisualIndex(int guestId)
    {
        var visuals = AccessTools.Property(typeof(DataBaseCharacter), "NormalGuestVisual").GetValue(null);
        if (visuals is null)
            return 0;
        var args = new object?[] { guestId, null };
        var found = visuals.GetType().GetMethod("TryGetValue")?.Invoke(visuals, args) is true;
        if (!found || args[1] is not System.Collections.IList list || list.Count == 0)
            return 0;
        return UnityEngine.Random.Range(0, list.Count);
    }

    internal static void ApplyVisual(int guestId, ref int visualIndex)
    {
        foreach (var modifier in Dispatch.Instances<IGuestSpawnModifier>())
            modifier.OnNormalGuestVisual(guestId, ref visualIndex);
    }

    /// <summary>
    /// The game's own guest behind one description, or null when the database does not carry its id: nothing
    /// can spawn a guest the game has no record of, so such a description is reported and dropped rather than
    /// handed to the spawn call.
    /// </summary>
    private static NormalGuest? Resolve(GuestDescription description)
    {
        if (description.IsEmpty)
            return null;
        var guests = DataBaseCharacter.NormalGuest;
        if (guests is null)
            return null;
        return guests.TryGetValue(description.Id, out var guest) ? guest : null;
    }

    private static void Report(GuestDescription description)
    {
        if (!_reported.Add(description.Id))
            return;
        GameBridgeHook.Trace($"GuestSpawnPipeline: no normal guest {description.Id} in the character database; the description was dropped.");
    }

    // One report per id, so a modifier that returns an unknown guest from its update path cannot flood the log.
    private static readonly HashSet<int> _reported = [];

    internal static GuestProfilePair BuildVisual(int id, int index)
    {
        var type = typeof(DataBaseCharacter);
        var bg = (Color)AccessTools.Property(type, "UnifiedNormalGuestBGColor").GetValue(null)!;
        var text = (Color)AccessTools.Property(type, "UnifiedNormalGuestTextColor").GetValue(null)!;
        var compact = Compact(id, index);
        var pair = new GuestProfilePair(id, bg, text, null, ScriptableObject.CreateInstance<CharacterSkinSets>());
        AccessTools.Method(pair.CharacterPixel.GetType(), "Initialize").Invoke(pair.CharacterPixel, [compact, null, null]);
        return pair;
    }

    private static object Compact(int id, int index)
    {
        var visuals = AccessTools.Property(typeof(DataBaseCharacter), "NormalGuestVisual").GetValue(null)
            ?? throw new InvalidOperationException("Normal guest visuals are not loaded.");
        var args = new object?[] { id, null };
        var found = visuals.GetType().GetMethod("TryGetValue")?.Invoke(visuals, args) is true;
        if (!found || args[1] is not IList list || index < 0 || index >= list.Count)
            return DataBaseCharacter.FallbackCompactPixel;
        return list[index] ?? DataBaseCharacter.FallbackCompactPixel;
    }
}

internal static class GuestSpawnSeams
{
    [HarmonyPatch(typeof(CookSystemManager), nameof(CookSystemManager.GetRandomNormalGuestGroups))]
    private static class NormalRoll
    {
        private static void Postfix(ref Il2CppSystem.Collections.Generic.IEnumerable<NormalGuest> __result)
        {
            var guests = GuestSpawnPipeline.Describe((System.Collections.IEnumerable?)__result);
            GuestSpawnPipeline.ApplyNormal(ref guests);
            var native = new Il2CppSystem.Collections.Generic.List<NormalGuest>(guests.Count);
            foreach (var guest in GuestSpawnPipeline.Resolve(guests))
                native.Add(guest);
            __result = (Il2CppSystem.Collections.Generic.IEnumerable<NormalGuest>)(object)native;
        }
    }

    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.SpawnSpecialGuestGroup))]
    private static class SpecialId
    {
        private static void Prefix(ref int id) => GuestSpawnPipeline.ApplySpecial(ref id);
    }

    [HarmonyPatch(typeof(DataBaseCharacter), nameof(DataBaseCharacter.RefNormalGuestVisual))]
    private static class Visual
    {
        private static bool Prefix(int id, ref GuestProfilePair __result)
        {
            if (!GuestSpawnPipeline.HasVisualModifier)
                return true;
            var index = GuestSpawnPipeline.StockVisualIndex(id);
            GuestSpawnPipeline.ApplyVisual(id, ref index);
            if (index < 0)
                return true;
            __result = GuestSpawnPipeline.BuildVisual(id, index);
            return false;
        }
    }
}
