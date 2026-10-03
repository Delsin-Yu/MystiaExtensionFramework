using GameData.Core.Collections.DaySceneUtility;
using GameData.Core.Collections.DaySceneUtility.Collections;
using GameData.RunTime.Common;
using GameData.RunTime.DaySceneUtility;
using GameData.RunTime.DaySceneUtility.Collection;
using HarmonyLib;
using Mystia.Data;

namespace Mystia.Modding.Bridge;

/// <summary>
/// Injection chain for day scene merchants.
/// <para>
/// A stock merchant is two things: the definition in <c>DataBaseDay.allMerchants</c> and a runtime
/// record in <c>RunTimeDayScene.trackedMerchants</c> that carries the products generated for the day.
/// The stock game creates the runtime record from the merchant's own interaction script
/// (<c>MultipleInteractionsBehaviourComponent</c>); an injected merchant has no such script, so the
/// bridge creates both sides itself and applies the same owned-recipe filter that the script applies
/// before it opens the shop.
/// </para>
/// </summary>
internal static class MerchantPipeline
{
    private static readonly Dictionary<string, Merchant> Injected = new(StringComparer.Ordinal);

    /// <summary>Keys injected by the database pass; kept so the daily cleanup can tell them apart.</summary>
    internal static IReadOnlyCollection<string> Keys => Injected.Keys;

    internal static void ResetForTests() => Injected.Clear();

    /// <summary>Writes the merchant definition, its mapping entry and its runtime record with products.</summary>
    internal static void Register(MerchantData data, string origin)
    {
        if (string.IsNullOrEmpty(data.Key))
            return;
        var merchant = GameRecords.Merchant(data, GameRecords.FindDialog);
        Injected[data.Key] = merchant;

        var merchants = DataBaseDay.allMerchants;
        if (merchants is null)
        {
            GameBridgeHook.Trace("MerchantPipeline: the merchant table is not initialized yet");
            return;
        }

        // Merchant is a native structure: the generated indexer would copy the object header into the
        // value slot (see InteropTables).
        InteropTables.SetBoxedValue(merchants, data.Key, merchant);
        DataBaseDay.AllMerchantsMapping[data.Key] = origin;
        Track(data.Key);
    }

    /// <summary>Builds the runtime record for an injected merchant and generates today's products.</summary>
    internal static void Track(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;
        var tracked = new TrackedMerchant(key);
        try
        {
            tracked.GenerateProduct();
        }
        catch (Exception error)
        {
            GameBridgeHook.Trace($"MerchantPipeline: cannot generate products for {key}: {error.GetBaseException().Message}");
        }

        FilterOwnedRecipes(tracked);
        RunTimeDayScene.trackedMerchants[key] = tracked;
    }

    /// <summary>
    /// Missing key safe lookup. <c>DataBaseDay.RefMerchant</c> throws for unknown keys, and a save file
    /// written before a resource pack was removed still references those keys.
    /// </summary>
    internal static bool TryGetMerchant(string? key, out Merchant merchant)
    {
        merchant = null!;
        if (string.IsNullOrEmpty(key))
            return false;
        var merchants = DataBaseDay.allMerchants;
        if (merchants is null || !merchants.ContainsKey(key))
            return false;

        // Read through the indexer: the out parameter of TryGetValue would be filled with raw
        // structure data over a managed reference slot.
        merchant = merchants[key];
        return merchant is not null;
    }

    internal static bool IsMerchant(string? key) =>
        !string.IsNullOrEmpty(key) && DataBaseDay.allMerchants is { } merchants && merchants.ContainsKey(key);

    /// <summary>
    /// Drops the recipe products the player already owns, which is what the stock merchant script does
    /// right before it opens the shop panel (<c>MultipleInteractionsBehaviourComponent.OpenShop</c>).
    /// The stock loop removes entries by index while walking the list, so it can skip an entry; the
    /// rule itself is "a recipe the player owns is not sold".
    /// </summary>
    internal static void FilterOwnedRecipes(TrackedMerchant? merchant)
    {
        if (merchant?.products is null || merchant.products.Length == 0)
            return;
        var owned = RunTimeStorage.GetAllRecipes();
        if (owned is null || owned.Length == 0)
            return;

        var kept = new List<Product>(merchant.products.Length);
        foreach (var product in merchant.products)
        {
            if (product is null)
                continue;
            var skip = false;
            if (product.productType == Product.ProductType.Recipe)
                foreach (var recipe in owned)
                    if (recipe is not null && recipe.Id == product.productId)
                    {
                        skip = true;
                        break;
                    }

            if (!skip)
                kept.Add(product);
        }

        if (kept.Count != merchant.products.Length)
            merchant.products = kept.ToArray();
    }

    /// <summary>
    /// Daily cleanup entry: removes runtime records that no longer have a merchant definition. A
    /// resource pack that was removed after a save was written leaves such orphans behind, and
    /// <c>DataBaseDay.RefMerchant</c> would throw for them. Mods that own the removed pack are expected
    /// to call this from their day scene <c>Setup</c>.
    /// </summary>
    internal static void CleanOrphanedMerchants()
    {
        var merchants = DataBaseDay.allMerchants;
        var tracked = RunTimeDayScene.trackedMerchants;
        if (merchants is null || tracked is null)
            return;

        List<string>? orphans = null;
        foreach (var key in tracked.Keys)
        {
            if (key is null || merchants.ContainsKey(key) || Injected.ContainsKey(key))
                continue;

            // The stock merchant records (Rinnosuke and friends) live in allMerchants, so only entries
            // missing from both tables are orphans.
            (orphans ??= []).Add(key);
        }

        if (orphans is null)
            return;
        foreach (var key in orphans)
        {
            tracked.Remove(key);
            GameBridgeHook.Trace($"MerchantPipeline: removed orphaned merchant record {key}");
        }

        RunTimeDayScene.OnRequireCurrentMapRefreshCallback?.Invoke();
    }
}

internal static class MerchantSeams
{
    // The mod that previously owned this data patched the same method with the same intent, and the
    // hand-off assigns the missing key safety to the bridge (audit facts 2.1, row RefMerchant).
    [HarmonyPatch(typeof(DataBaseDay), nameof(DataBaseDay.RefMerchant))]
    private static class RefMerchant
    {
        private static bool Prefix(string key, ref Merchant __result)
        {
            if (MerchantPipeline.TryGetMerchant(key, out _))
                return true;

            // Returning a keyed empty merchant (never null) keeps the value slot valid for the caller
            // plus the IsMerchant/DoSell checks that guard every stock merchant path.
            var empty = new Merchant();
            empty.key = key ?? "";
            empty.welcomeDialogPackage = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameData.Profile.DialogPackage>(0);
            empty.nullDialogPackage = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameData.Profile.DialogPackage>(0);
            empty.merchandiseCollection = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Merchant.Merchandise>(0);
            __result = empty;
            GameBridgeHook.Trace($"MerchantPipeline: merchant {key} is unknown, returning an empty merchant");
            return false;
        }
    }

    // The stock script filters owned recipes when the shop opens; the bridge reads the same record, so
    // the filter runs whenever the game asks for it.
    [HarmonyPatch(typeof(RunTimeDayScene), nameof(RunTimeDayScene.GetMerchantData))]
    private static class GetMerchantData
    {
        private static void Postfix(ref TrackedMerchant __result) => MerchantPipeline.FilterOwnedRecipes(__result);
    }
}
