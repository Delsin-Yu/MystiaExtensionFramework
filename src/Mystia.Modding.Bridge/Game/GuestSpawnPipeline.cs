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
    internal static void ApplyNormal(ref List<NormalGuest> guests)
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
            var guests = new List<NormalGuest>();
            if (__result is not null)
            {
                foreach (var item in (System.Collections.IEnumerable)__result)
                {
                    if (item is NormalGuest guest)
                        guests.Add(guest);
                }
            }

            GuestSpawnPipeline.ApplyNormal(ref guests);
            var native = new Il2CppSystem.Collections.Generic.List<NormalGuest>(guests.Count);
            foreach (var guest in guests)
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
