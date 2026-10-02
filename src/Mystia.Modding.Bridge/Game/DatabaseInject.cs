using System.Reflection;
using GameData.Core.Collections;
using GameData.Core.Collections.CharacterUtility;
using GameData.Core.Collections.DaySceneUtility;
using GameData.Core.Collections.DaySceneUtility.Collections;
using GameData.CoreLanguage.Collections;
using GameData.Profile;
using HarmonyLib;
using Mystia.Data;

namespace Mystia.Modding.Bridge;

internal static class DatabaseInject
{
    private static bool _collected;
    private static List<IngredientData> _ingredients = [];
    private static List<FoodData> _foods = [];
    private static List<BeverageData> _beverages = [];
    private static List<RecipeData> _recipes = [];
    private static List<CookerData> _cookers = [];
    private static List<IzakayaData> _izakayas = [];
    private static List<ItemData> _items = [];
    private static List<BadgeData> _badges = [];
    private static List<NormalGuestData> _normalGuests = [];
    private static List<SpecialGuestData> _specialGuests = [];
    private static List<NpcData> _npcs = [];
    private static List<DialogData> _dialogs = [];
    private static List<MerchantData> _merchants = [];
    private static readonly Dictionary<int, string> IngredientRoots = new();
    private static readonly Dictionary<int, string> FoodRoots = new();
    private static readonly Dictionary<int, string> BeverageRoots = new();
    private static readonly Dictionary<int, string> CookerRoots = new();
    private static readonly Dictionary<int, string> ItemRoots = new();
    private static readonly Dictionary<int, string> BadgeRoots = new();
    private static readonly Dictionary<int, string> GuestRoots = new();

    internal static IReadOnlyList<IngredientData> Ingredients => _ingredients;

    internal static void ResetForTests()
    {
        _collected = false;
        DialogScripts.Clear();
        IngredientRoots.Clear();
        FoodRoots.Clear();
        BeverageRoots.Clear();
        CookerRoots.Clear();
        ItemRoots.Clear();
        BadgeRoots.Clear();
        GuestRoots.Clear();
    }

    internal static void Collect()
    {
        if (_collected)
            return;
        _collected = true;
        _ingredients = [];
        _foods = [];
        _beverages = [];
        _recipes = [];
        _cookers = [];
        _izakayas = [];
        _items = [];
        _badges = [];
        _normalGuests = [];
        _specialGuests = [];
        _npcs = [];
        _dialogs = [];
        _merchants = [];
        foreach (var contributor in Dispatch.Instances<IDatabaseExtension>())
        {
            Contribute(contributor, _ingredients, contributor.OnInjectIngredients, IngredientRoots, static item => item.Id, static item => item.Picture);
            Contribute(contributor, _foods, contributor.OnInjectFoods, FoodRoots, static item => item.Id, static item => item.Picture);
            Contribute(contributor, _beverages, contributor.OnInjectBeverages, BeverageRoots, static item => item.Id, static item => item.Picture);
            contributor.OnInjectRecipes(_recipes);
            Contribute(contributor, _cookers, contributor.OnInjectCookers, CookerRoots, static item => item.Id, static item => item.Picture);
            contributor.OnInjectIzakayas(_izakayas);
            Contribute(contributor, _items, contributor.OnInjectItems, ItemRoots, static item => item.Id, static item => item.Picture);
            Contribute(contributor, _badges, contributor.OnInjectBadges, BadgeRoots, static item => item.Id, static item => item.Picture);
            contributor.OnInjectNormalGuests(_normalGuests);
            Contribute(contributor, _specialGuests, contributor.OnInjectSpecialGuests, GuestRoots, static item => item.Id, static item => Pictures(item));
            contributor.OnInjectNpcs(_npcs);
            contributor.OnInjectDialogs(_dialogs);
            contributor.OnInjectMerchants(_merchants);
        }
    }

    internal static void ApplyCore()
    {
        Collect();
        foreach (var item in _ingredients)
            Put(typeof(DataBaseCore), "Ingredients", item.Id, GameRecords.Ingredient(item));
        foreach (var item in _foods)
            Put(typeof(DataBaseCore), "Foods", item.Id, GameRecords.Food(item));
        foreach (var item in _beverages)
            Put(typeof(DataBaseCore), "Beverages", item.Id, GameRecords.Beverage(item));
        foreach (var item in _recipes)
            Put(typeof(DataBaseCore), "Recipes", item.Id, GameRecords.Recipe(item));
        foreach (var item in _cookers)
            Put(typeof(DataBaseCore), "Cookers", item.Id, GameRecords.Cooker(item));
        foreach (var item in _izakayas)
            Put(typeof(DataBaseCore), "Izakayas", item.Id, GameRecords.Izakaya(item));
        foreach (var item in _items)
            Put(typeof(DataBaseCore), "Items", item.Id, GameRecords.Item(item));
        foreach (var item in _badges)
            Put(typeof(DataBaseCore), "Badges", item.Id, GameRecords.Badge(item));
        GameRecords.PlaceSpawns(_specialGuests);
    }

    internal static void ApplyLanguage()
    {
        Collect();
        foreach (var item in _ingredients)
            Put(typeof(DataBaseLanguage), "Ingredients", item.Id, GameRecords.IngredientText(item, Root(IngredientRoots, item.Id)));
        foreach (var item in _foods)
            Put(typeof(DataBaseLanguage), "Foods", item.Id, GameRecords.FoodText(item, Root(FoodRoots, item.Id)));
        foreach (var item in _beverages)
            Put(typeof(DataBaseLanguage), "Beverages", item.Id, GameRecords.BeverageText(item, Root(BeverageRoots, item.Id)));
        foreach (var item in _cookers)
            Put(typeof(DataBaseLanguage), "Cookers", item.Id, GameRecords.CookerText(item, Root(CookerRoots, item.Id)));
        foreach (var item in _izakayas)
            Put(typeof(DataBaseLanguage), "Izakayas", item.Id, GameRecords.IzakayaText(item));
        foreach (var item in _items)
            Put(typeof(DataBaseLanguage), "Items", item.Id, GameRecords.ItemText(item, Root(ItemRoots, item.Id)));
        foreach (var item in _badges)
            Put(typeof(DataBaseLanguage), "Badges", item.Id, GameRecords.BadgeText(item, Root(BadgeRoots, item.Id)));
        foreach (var item in _normalGuests)
            Put(typeof(DataBaseLanguage), "NormalGuest", item.Id, GameRecords.NormalGuestText(item));
        PutSpecialGuestText();
        PutRequests("SpecialGuestFoodRequest", static guest => guest.FoodRequests);
        PutRequests("SpecialGuestBevRequest", static guest => guest.BeverageRequests);
    }

    internal static void ApplyCharacters()
    {
        Collect();
        foreach (var item in _normalGuests)
            Put(typeof(DataBaseCharacter), "NormalGuest", item.Id, GameRecords.NormalGuest(item));
        foreach (var item in _specialGuests)
        {
            Put(typeof(DataBaseCharacter), "SpecialGuest", item.Id, GameRecords.SpecialGuest(item));
            Put(typeof(DataBaseCharacter), "SpecialGuestVisual", item.Id, GameRecords.Visual(item, Root(GuestRoots, item.Id)));
        }
    }

    internal static void ApplyDay()
    {
        Collect();
        var npcs = new List<NPC>(_npcs.Count);
        foreach (var item in _npcs)
            npcs.Add(GameRecords.Npc(item));
        PutKeyed(typeof(DataBaseDay), "allNPCs", npcs, static npc => npc.key, static npc => npc);
        MapNpcLabels(npcs);
        foreach (var item in _dialogs)
        {
            if (string.IsNullOrEmpty(item.Name))
                continue;
            Put(typeof(DataBaseDay), "allDialogPackages", item.Name, GameRecords.Dialog(item));
        }

        foreach (var item in _merchants)
        {
            if (string.IsNullOrEmpty(item.Key))
                continue;
            Put(typeof(DataBaseDay), "allMerchants", item.Key, GameRecords.Merchant(item, GameRecords.FindDialog));
        }
    }

    internal static void ApplyNightLanguage()
    {
        Collect();
        foreach (var item in _specialGuests)
        {
            if (item.Evaluations is not null)
                Put(typeof(NightSceneLanguage), "SpecialEvaluation", item.Id, item.Evaluations);
            if (item.Conversations is null)
                continue;
            Put(typeof(NightSceneLanguage), "SpecialConversation", item.Id, Lines(typeof(NightSceneLanguage), "SpecialConversation", item.Conversations));
        }
    }

    private static void PutSpecialGuestText()
    {
        foreach (var guest in _specialGuests)
        {
            var dictionary = Member(typeof(DataBaseLanguage), "SpecialGuest");
            if (dictionary is null)
                continue;
            var valueType = Indexer(dictionary).PropertyType;
            var text = Activator.CreateInstance(valueType, guest.Name ?? "", guest.Description1 ?? "", guest.Description2 ?? "", guest.Description3 ?? "");
            Indexer(dictionary).SetValue(dictionary, text, [guest.Id]);
        }
    }

    private static void PutRequests(string property, Func<SpecialGuestData, GuestRequestLine[]?> lines)
    {
        foreach (var guest in _specialGuests)
        {
            var requests = lines(guest);
            if (requests is null || requests.Length == 0)
                continue;
            var dictionary = Member(typeof(DataBaseLanguage), property);
            if (dictionary is null)
                continue;
            var valueType = Indexer(dictionary).PropertyType;
            var inner = Activator.CreateInstance(valueType)!;
            var innerIndexer = Indexer(inner);
            foreach (var request in requests)
                innerIndexer.SetValue(inner, request.Line, [request.TagId]);
            Indexer(dictionary).SetValue(dictionary, inner, [guest.Id]);
        }
    }

    private static object Lines(Type owner, string property, string[] lines)
    {
        var dictionary = Member(owner, property);
        var element = Indexer(dictionary!).PropertyType.GetElementType()!;
        var array = Array.CreateInstance(element, lines.Length);
        for (var index = 0; index < lines.Length; index++)
            array.SetValue(Activator.CreateInstance(element, lines[index]), index);
        return array;
    }

    private static void MapNpcLabels(IReadOnlyList<NPC> npcs)
    {
        var forward = Member(typeof(DataBaseCharacter), "StringMappingData");
        var inverse = Member(typeof(DataBaseCharacter), "InvStringMappingData");
        if (forward is null || inverse is null)
            return;
        var forwardIndex = Indexer(forward);
        var inverseIndex = Indexer(inverse);
        foreach (var npc in npcs)
        {
            if (string.IsNullOrEmpty(npc.key))
                continue;
            forwardIndex.SetValue(forward, npc.identity, [npc.key]);
            string[] names;
            try
            {
                names = inverseIndex.GetValue(inverse, [npc.identity]) as string[] ?? [];
            }
            catch (TargetInvocationException)
            {
                names = [];
            }

            if (Array.IndexOf(names, npc.key) >= 0)
                continue;
            var updated = new string[names.Length + 1];
            Array.Copy(names, updated, names.Length);
            updated[^1] = npc.key;
            inverseIndex.SetValue(inverse, updated, [npc.identity]);
        }
    }

    private static void Contribute<T>(
        IDatabaseExtension contributor,
        List<T> list,
        Action<List<T>> inject,
        Dictionary<int, string> roots,
        Func<T, int> key,
        Func<T, string?> token)
    {
        var before = new Dictionary<int, string?>();
        foreach (var item in list)
            before[key(item)] = token(item);
        inject(list);
        var root = ContentOrigin.Of(contributor);
        foreach (var item in list)
        {
            var id = key(item);
            var picture = token(item);
            if (!before.TryGetValue(id, out var previous) || !string.Equals(previous, picture, StringComparison.Ordinal))
                roots[id] = root;
        }
    }

    private static string Pictures(SpecialGuestData guest) =>
        Join(guest.Portraits) + "#" + Join(guest.Body) + "#" + Join(guest.Eyes);

    private static string Join(string[]? paths) => paths is null ? "" : string.Join("|", paths);

    private static string Root(Dictionary<int, string> roots, int id) =>
        roots.TryGetValue(id, out var root) ? root : "";

    private static void PutKeyed<T>(Type owner, string member, IReadOnlyList<T> records, Func<T, string?> key, Func<T, object?> value)
    {
        foreach (var record in records)
        {
            var id = key(record);
            var stored = value(record);
            if (string.IsNullOrEmpty(id) || stored is null)
                continue;
            Put(owner, member, id, stored);
        }
    }

    private static void Put(Type owner, string member, object key, object value)
    {
        var dictionary = Member(owner, member);
        if (dictionary is null)
            return;
        Indexer(dictionary).SetValue(dictionary, value, [key]);
    }

    internal static object? Member(Type owner, string name)
    {
        var property = AccessTools.Property(owner, name);
        if (property is not null)
            return property.GetValue(null);
        var field = AccessTools.Field(owner, name);
        return field?.GetValue(null);
    }

    internal static PropertyInfo Indexer(object dictionary)
    {
        return dictionary.GetType().GetProperties().First(property => property.GetIndexParameters().Length == 1);
    }
}
