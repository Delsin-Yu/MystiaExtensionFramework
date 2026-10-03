using System.Reflection;
using DEYU.AssetHandleUtility;
using GameData.Core.Collections;
using GameData.Core.Collections.CharacterUtility;
using GameData.Core.Collections.DaySceneUtility;
using GameData.Core.Collections.DaySceneUtility.Collections;
using GameData.Core.Collections.NightSceneUtility;
using GameData.CoreLanguage;
using GameData.CoreLanguage.Collections;
using GameData.Profile;
using GameData.Profile.SchedulerNodeCollection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mystia.Data;
using NightScene.EventUtility;
using UnityEngine;
using UnityEngine.AddressableAssets;

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
    private static List<ClothesData> _clothes = [];
    private static List<SpellData> _spells = [];
    private static List<BuffData> _buffs = [];
    private static List<MissionNodeData> _missionNodes = [];
    private static List<EventNodeData> _eventNodes = [];
    private static List<DayMapData> _dayMaps = [];
    private static readonly Dictionary<int, string> IngredientRoots = new();
    private static readonly Dictionary<int, string> FoodRoots = new();
    private static readonly Dictionary<int, string> BeverageRoots = new();
    private static readonly Dictionary<int, string> RecipeRoots = new();
    private static readonly Dictionary<int, string> ClothRoots = new();
    private static readonly Dictionary<int, string> SpellRoots = new();
    private static readonly Dictionary<int, string> BuffRoots = new();
    private static readonly Dictionary<string, string> NodeOrigins = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> MapOrigins = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> MerchantOrigins = new(StringComparer.Ordinal);
    private static readonly List<(ScriptableObject Node, SchedulerEventData? Finish, SchedulerEventData? Failed, SchedulerEventData? Scheduled)> PendingDialogs = [];
    private static readonly Dictionary<int, string> CookerRoots = new();
    private static readonly Dictionary<int, string> ItemRoots = new();
    private static readonly Dictionary<int, string> BadgeRoots = new();
    private static readonly Dictionary<int, string> GuestRoots = new();

    internal static IReadOnlyList<IngredientData> Ingredients => _ingredients;

    internal static void ResetForTests()
    {
        _collected = false;
        DialogScripts.Clear();
        MerchantPipeline.ResetForTests();
        IngredientRoots.Clear();
        FoodRoots.Clear();
        BeverageRoots.Clear();
        RecipeRoots.Clear();
        ClothRoots.Clear();
        SpellRoots.Clear();
        BuffRoots.Clear();
        NodeOrigins.Clear();
        MapOrigins.Clear();
        MerchantOrigins.Clear();
        PendingDialogs.Clear();
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
        _clothes = [];
        _spells = [];
        _buffs = [];
        _missionNodes = [];
        _eventNodes = [];
        _dayMaps = [];
        foreach (var contributor in Dispatch.Instances<IDatabaseExtension>())
        {
            Contribute(contributor, _ingredients, contributor.OnInjectIngredients, IngredientRoots, static item => item.Id, static item => item.Picture);
            Contribute(contributor, _foods, contributor.OnInjectFoods, FoodRoots, static item => item.Id, static item => item.Picture);
            Contribute(contributor, _beverages, contributor.OnInjectBeverages, BeverageRoots, static item => item.Id, static item => item.Picture);
            CollectOrigin(contributor, _recipes, contributor.OnInjectRecipes, RecipeRoots, static item => item.Id);
            Contribute(contributor, _cookers, contributor.OnInjectCookers, CookerRoots, static item => item.Id, static item => item.Picture);
            contributor.OnInjectIzakayas(_izakayas);
            Contribute(contributor, _items, contributor.OnInjectItems, ItemRoots, static item => item.Id, static item => item.Picture);
            Contribute(contributor, _badges, contributor.OnInjectBadges, BadgeRoots, static item => item.Id, static item => item.Picture);
            contributor.OnInjectNormalGuests(_normalGuests);
            Contribute(contributor, _specialGuests, contributor.OnInjectSpecialGuests, GuestRoots, static item => item.Id, static item => Pictures(item));
            contributor.OnInjectNpcs(_npcs);
            contributor.OnInjectDialogs(_dialogs);
            CollectOrigin(contributor, _merchants, contributor.OnInjectMerchants, MerchantOrigins, static item => item.Key ?? "");
            CollectOrigin(contributor, _clothes, contributor.OnInjectClothes, ClothRoots, static item => item.Id);
            CollectOrigin(contributor, _spells, contributor.OnInjectSpells, SpellRoots, static item => item.Id);
            CollectOrigin(contributor, _buffs, contributor.OnInjectBuffs, BuffRoots, static item => item.Id);
            CollectOrigin(contributor, _missionNodes, contributor.OnInjectMissionNodes, NodeOrigins, static item => item.Label ?? "");
            CollectOrigin(contributor, _eventNodes, contributor.OnInjectEventNodes, NodeOrigins, static item => item.Label ?? "");
            CollectOrigin(contributor, _dayMaps, contributor.OnInjectDayMaps, MapOrigins, static item => item.Label ?? "");
        }

        // The spawn point injection reads the day map and special guest data, which only exists here.
        SpawnMarkerPipeline.Remember(_dayMaps, _specialGuests);
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
        ApplyClothes();
        ApplyModMappings();
    }

    /// <summary>
    /// Clothes are an item plus a profile that selects a player pixel set; both tables are rebuilt by
    /// <c>DataBaseCore.Initialize</c>, so they are written here. The language and pixel halves follow in
    /// <see cref="ApplyClothesLanguage"/> and <see cref="ApplyClothesPixels"/>.
    /// </summary>
    internal static void ApplyClothes()
    {
        Collect();
        if (_clothes.Count == 0)
            return;

        var template = ClothesTemplate();
        var items = DataBaseCore.Items;
        var profiles = DataBaseCore.Clothes;
        if (items is null || profiles is null)
            return;
        foreach (var item in _clothes)
        {
            items[item.Id] = GameRecords.ClothItem(item);
            if (template is null)
            {
                GameBridgeHook.Trace($"DatabaseInject: no stock clothes profile to copy visuals from, clothes {item.Id} skipped");
                continue;
            }

            // The skin index is assigned in ApplyClothesPixels, where the player pixel table exists.
            profiles[item.Id] = GameRecords.Cloth(item, 0, template);
        }
    }

    /// <summary>
    /// The three mapping tables record which entry came from which mod. The value is the injecting mod's
    /// own identity (see <see cref="OriginOf"/>), never a hard coded mod name.
    /// </summary>
    internal static void ApplyModMappings()
    {
        Collect();
        var foods = DataBaseCore.FoodsMapping;
        var beverages = DataBaseCore.BeveragesMapping;
        var recipes = DataBaseCore.RecipesMapping;
        if (foods is null || beverages is null || recipes is null)
            return;
        foreach (var item in _foods)
            WriteOrigin(foods, item.Id, FoodRoots);
        foreach (var item in _beverages)
            WriteOrigin(beverages, item.Id, BeverageRoots);
        foreach (var item in _recipes)
            WriteOrigin(recipes, item.Id, RecipeRoots);
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
        ApplyClothesLanguage();
        ApplyBuffs();
        ApplyMissionLanguage();
    }

    /// <summary>Clothes are items in the language table as well.</summary>
    internal static void ApplyClothesLanguage()
    {
        Collect();
        var items = DataBaseLanguage.Items;
        if (items is null)
            return;
        foreach (var item in _clothes)
            items[item.Id] = GameRecords.ClothText(item, Root(ClothRoots, item.Id));
    }

    /// <summary>
    /// Buff titles and descriptions, keyed by <c>EventManager.BuffType</c>. The table is rebuilt by
    /// <c>DataBaseLanguage.Initialize</c>, so the write belongs to the language pass.
    /// </summary>
    internal static void ApplyBuffs()
    {
        Collect();
        var descriptions = DataBaseLanguage.BuffDescription;
        if (descriptions is null)
            return;
        foreach (var item in _buffs)
            descriptions[(EventManager.BuffType)item.Id] = GameRecords.BuffText(item, Root(BuffRoots, item.Id));
    }

    /// <summary>
    /// Mission and event names. <c>DataBaseLanguage.Missions</c> is rebuilt by
    /// <c>DataBaseLanguage.Initialize</c>, which runs before the scheduler pass, so the node table
    /// (<see cref="ApplyScheduler"/>) and this language pass are two halves of the same batch.
    /// </summary>
    internal static void ApplyMissionLanguage()
    {
        Collect();
        var missions = DataBaseLanguage.Missions;
        if (missions is null)
            return;
        foreach (var node in _missionNodes)
        {
            if (string.IsNullOrEmpty(node.Label))
                continue;
            missions[node.Label] = new LanguageBase(node.Name ?? node.Label, node.Description ?? "");
        }
    }

    /// <summary>
    /// Spell cards. <c>DataBaseNight.Initialize</c> rebuilds the spell handle table and the declaration
    /// portrait table, and it runs after <c>DataBaseLanguage.Initialize</c>, so all four writes land here:
    /// the two language entries, the "has a spell" flag and the instance plus declaration portraits.
    /// </summary>
    internal static void ApplySpells()
    {
        Collect();
        if (_spells.Count == 0)
            return;
        var language = DataBaseLanguage.SpellLang;
        var hasSpell = DataBaseCharacter.CharacterHasSpell;
        var spells = DataBaseNight.SpecialGuestSpell;
        var portrayals = DataBaseNight.SpecialGuestSpellPortrayal;
        if (language is null || hasSpell is null || spells is null || portrayals is null)
            return;
        foreach (var spell in _spells)
        {
            var root = Root(SpellRoots, spell.Id);
            var positive = spell.PositivePortrait ?? spell.Portrait;
            var negative = spell.NegativePortrait ?? spell.Portrait;
            if (string.IsNullOrWhiteSpace(positive) || string.IsNullOrWhiteSpace(negative))
            {
                // Without a declaration portrait the game's card declaration has nothing to draw, so the
                // card is left out entirely instead of half registering it.
                GameBridgeHook.Trace($"DatabaseInject: spell {spell.Id} has no declaration portrait, skipped");
                continue;
            }

            language[spell.Id] = GameRecords.SpellLanguage(spell);
            // The game asks with the guest id. A mod that only fills the spell id (the shape where the
            // spell id doubles as the owner id) is covered by the fallback.
            hasSpell[spell.CharacterId != 0 ? spell.CharacterId : spell.Id] = true;
            var instance = BridgeSpell.Create(spell.Id, spell.Label ?? "");
            spells[spell.Id] = new SpellAssetHandle(instance);
            // SpriteFiles caches one sprite per file, so the declaration shares the sprite of the dialogue
            // portrait (pivot 0.5/0.5). The mod that previously owned this data recreated the sprite with
            // the declaration pivot (0.497/0.644); a faithful port needs a second sprite per file.
            IAssetHandle<Sprite> first = new SpriteAssetHandle(SpriteFiles.Load(root, positive));
            IAssetHandle<Sprite> second = new SpriteAssetHandle(SpriteFiles.Load(root, negative));
            // The value is a native tuple: the generated indexer would copy the object header into it.
            InteropTables.SetBoxedValue(
                portrayals,
                spell.Id,
                new Il2CppSystem.ValueTuple<IAssetHandle<Sprite>, IAssetHandle<Sprite>>(first, second));
        }
    }

    /// <summary>
    /// Scheduler nodes. <c>DataBaseScheduler.Initialize</c> rebuilds the node table and its mapping, so
    /// both are written here; the node languages are written in <see cref="ApplyMissionLanguage"/> and the
    /// dialog packages the nodes point at are resolved later, in <see cref="ApplySchedulerDialogs"/>.
    /// </summary>
    internal static void ApplyScheduler()
    {
        Collect();
        PendingDialogs.Clear();
        if (_missionNodes.Count == 0 && _eventNodes.Count == 0)
            return;
        var nodes = DataBaseScheduler.allNodes;
        var mappings = DataBaseScheduler.AllNodesMapping;
        if (nodes is null || mappings is null)
            return;
        foreach (var data in _missionNodes)
        {
            if (string.IsNullOrEmpty(data.Label))
                continue;
            var node = GameRecords.MissionNodeRecord(data, Absent);
            nodes[data.Label] = node;
            mappings[data.Label] = OriginOf(NodeOrigins, data.Label);
            PendingDialogs.Add((node, data.MissionFinishEvent, data.MissionFailedEvent, null));
        }

        foreach (var data in _eventNodes)
        {
            if (string.IsNullOrEmpty(data.Label))
                continue;
            var node = GameRecords.EventNodeRecord(data, Absent);
            nodes[data.Label] = node;
            mappings[data.Label] = OriginOf(NodeOrigins, data.Label);
            PendingDialogs.Add((node, null, null, data.ScheduledEvent));
        }
    }

    /// <summary>
    /// Second half of the scheduler pass. <c>DataBaseScheduler.Initialize</c> runs before
    /// <c>DataBaseDay.Initialize</c>, so at that point the dialog packages do not exist yet: the nodes are
    /// created without them and their dialog carrying fields are filled in here, after
    /// <see cref="ApplyDay"/> has written the packages.
    /// </summary>
    internal static void ApplySchedulerDialogs()
    {
        Collect();
        if (PendingDialogs.Count == 0)
            return;
        foreach (var (node, finish, failed, scheduled) in PendingDialogs)
        {
            switch (node)
            {
                case MissionNode mission:
                    mission.missionFinishEvent = GameRecords.Event(finish, GameRecords.FindDialog);
                    mission.missionFailedEvent = GameRecords.Event(failed, GameRecords.FindDialog);
                    break;
                case EventNode entity:
                    var pending = entity.scheduledEvent;
                    pending.eventData = GameRecords.Event(scheduled, GameRecords.FindDialog);
                    entity.scheduledEvent = pending;
                    break;
            }
        }

        PendingDialogs.Clear();
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

        ApplyClothesPixels();
    }

    /// <summary>
    /// Player pixel sets for injected clothes. The stock table is extended, never replaced: existing
    /// entries keep their index, and the injected entries are appended after them, which is what the
    /// skin index assigned in <see cref="ApplyClothes"/> points at.
    /// </summary>
    internal static void ApplyClothesPixels()
    {
        Collect();
        if (_clothes.Count == 0)
            return;
        var self = DataBaseCharacter.SelfSpriteSet;
        if (self is null)
            return;
        var existing = self.dlcs;
        var start = existing?.Length ?? 0;
        var combined = new Il2CppReferenceArray<CharacterSpriteSetCompact>(start + _clothes.Count);
        for (var i = 0; i < start; i++)
            combined[i] = existing![i];
        var profiles = DataBaseCore.Clothes;
        for (var i = 0; i < _clothes.Count; i++)
        {
            var item = _clothes[i];
            combined[start + i] = GameRecords.CompactPixel(item.Body, item.Eyes, Root(ClothRoots, item.Id));
            if (profiles is not null && profiles.ContainsKey(item.Id))
                profiles[item.Id].skinIndex = GameRecords.SkinSelection(start + i);
        }

        self.dlcs = combined;
    }

    internal static void ApplyDay()
    {
        Collect();
        var npcs = new List<NPC>(_npcs.Count);
        foreach (var item in _npcs)
            npcs.Add(GameRecords.Npc(item));
        var table = DataBaseDay.allNPCs;
        foreach (var npc in npcs)
        {
            if (string.IsNullOrEmpty(npc.key))
                continue;
            // NPC is a native structure: the generated indexer would copy the object header into it.
            InteropTables.SetBoxedValue(table, npc.key, npc);
        }

        MapNpcLabels(npcs);
        ApplyNpcNames();
        foreach (var item in _dialogs)
        {
            if (string.IsNullOrEmpty(item.Name))
                continue;
            Put(typeof(DataBaseDay), "allDialogPackages", item.Name, GameRecords.Dialog(item));
        }

        ApplySchedulerDialogs();
        foreach (var item in _merchants)
            MerchantPipeline.Register(item, OriginOf(MerchantOrigins, item.Key ?? ""));
        ApplyDayMaps();
    }

    /// <summary>
    /// Day scene display names. <c>DaySceneLanguage.Initialize</c> rebuilds the table, so the same write
    /// happens again in <see cref="ApplyDayLanguage"/>; writing it here as well keeps the name present if
    /// the language pass is not reached (for example when only the day database reloads).
    /// </summary>
    internal static void ApplyNpcNames()
    {
        Collect();
        var names = DaySceneLanguage.DaySceneNPCLanguage;
        if (names is null)
            return;
        foreach (var item in _npcs)
        {
            if (string.IsNullOrEmpty(item.Key) || string.IsNullOrEmpty(item.Name))
                continue;
            names[item.Key] = item.Name;
        }
    }

    /// <summary>
    /// Day maps: the node in <c>DataBaseDay.mapData</c>, the addressable reference, the spawn marker and
    /// collectable label sets, and the mod mapping. The map language is written by
    /// <see cref="ApplyDayLanguage"/>. The engine side (tilemaps, camera, in memory GameObjects) is not
    /// part of the data face and stays with the mod, so a map without an addressable reference is
    /// registered as data only.
    /// </summary>
    internal static void ApplyDayMaps()
    {
        Collect();
        if (_dayMaps.Count == 0)
            return;
        var maps = DataBaseDay.mapData;
        var references = DataBaseDay.mapReference;
        var spawnMarkers = DataBaseDay.allSpawnMarkerLabels;
        var collectables = DataBaseDay.allCollectablesLabels;
        var mappings = DataBaseDay.MapDataMapping;
        if (maps is null || spawnMarkers is null || collectables is null || mappings is null)
            return;
        foreach (var map in _dayMaps)
        {
            if (string.IsNullOrEmpty(map.Label))
                continue;
            var reference = AddressableReference(map.MapAsset);
            if (reference is null)
                GameBridgeHook.Trace($"DatabaseInject: day map {map.Label} has no addressable reference, registered as data only");
            else
                references[map.Label] = reference;
            // MapNode is a native structure: the generated indexer would copy the object header into it.
            InteropTables.SetBoxedValue(maps, map.Label, GameRecords.MapNode(map, reference));
            spawnMarkers[map.Label] = new Il2CppSystem.Collections.Generic.HashSet<string>();
            collectables[map.Label] = new Il2CppSystem.Collections.Generic.HashSet<string>();
            foreach (var marker in GameRecords.SpawnMarkerLabels(map))
                spawnMarkers[map.Label].Add(marker);
            foreach (var collectable in map.Collectables ?? [])
                collectables[map.Label].Add(collectable);
            mappings[map.Label] = OriginOf(MapOrigins, map.Label);
        }
    }

    /// <summary>
    /// Day scene language: the map names and the NPC display names. Both tables are rebuilt by
    /// <c>DaySceneLanguage.Initialize</c>, which is the reason for this late pass.
    /// </summary>
    internal static void ApplyDayLanguage()
    {
        Collect();
        var maps = DaySceneLanguage.MapLanguageData;
        if (maps is not null)
            foreach (var map in _dayMaps)
            {
                if (string.IsNullOrEmpty(map.Label))
                    continue;
                maps[map.Label] = new LanguageBase(map.Name ?? map.Label, map.Description ?? "");
            }

        ApplyNpcNames();
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
        var table = DataBaseLanguage.SpecialGuest;
        foreach (var guest in _specialGuests)
        {
            // The value is a native tuple: the generated indexer would copy the object header into it.
            InteropTables.SetBoxedValue(
                table,
                guest.Id,
                new Il2CppSystem.ValueTuple<string, string, string, string>(
                    guest.Name ?? "",
                    guest.Description1 ?? "",
                    guest.Description2 ?? "",
                    guest.Description3 ?? ""));
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

    /// <summary>
    /// Fills the label mapping both ways: <c>StringMappingData</c> (label to identity) and
    /// <c>InvStringMappingData</c> (identity to labels). Both are dictionaries of a C# structure, which
    /// the interop marshals correctly, so the generated indexers are used directly.
    /// </summary>
    private static void MapNpcLabels(IReadOnlyList<NPC> npcs)
    {
        var forward = DataBaseCharacter.StringMappingData;
        var inverse = DataBaseCharacter.InvStringMappingData;
        if (forward is null || inverse is null)
            return;
        foreach (var npc in npcs)
        {
            if (string.IsNullOrEmpty(npc.key))
                continue;
            var identity = npc.identity;
            forward[npc.key] = identity;
            var existing = inverse.ContainsKey(identity) ? inverse[identity] : null;
            var count = existing?.Length ?? 0;
            var known = false;
            for (var i = 0; i < count; i++)
            {
                if (existing![i] != npc.key)
                    continue;
                known = true;
                break;
            }

            if (known)
                continue;
            var updated = new Il2CppStringArray(count + 1);
            for (var i = 0; i < count; i++)
                updated[i] = existing![i];
            updated[count] = npc.key;
            inverse[identity] = updated;
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
        var root = OriginOf(contributor);
        foreach (var item in list)
        {
            var id = key(item);
            var picture = token(item);
            if (!before.TryGetValue(id, out var previous) || !string.Equals(previous, picture, StringComparison.Ordinal))
                roots[id] = root;
        }
    }

    /// <summary>
    /// Collects one hook and remembers which mod contributed each entry. A key that was already present
    /// before the caller ran keeps its original attribution, so redefining a stock entry does not hand
    /// the stock id to a mod.
    /// </summary>
    private static void CollectOrigin<T, TKey>(
        IDatabaseExtension contributor,
        List<T> list,
        Action<List<T>> inject,
        Dictionary<TKey, string> origins,
        Func<T, TKey> key)
        where TKey : notnull
    {
        var known = new HashSet<TKey>();
        foreach (var item in list)
            known.Add(key(item));
        inject(list);
        var origin = OriginOf(contributor);
        foreach (var item in list)
        {
            var id = key(item);
            if (known.Add(id))
                origins[id] = origin;
        }
    }

    /// <summary>
    /// The injecting mod's own identity, as declared by its mod.json. The host binds every contributor to
    /// that id; the assembly name is only a fallback when the host could not bind one. Nothing here is
    /// ever a hard coded mod name.
    /// </summary>
    private static string OriginOf(IDatabaseExtension contributor)
    {
        var root = ContentOrigin.Of(contributor);
        if (!string.IsNullOrWhiteSpace(root))
            return root;
        var assembly = contributor.GetType().Assembly.GetName().Name;
        return string.IsNullOrWhiteSpace(assembly) ? "unknown" : assembly!;
    }

    private static void WriteOrigin(Il2CppSystem.Collections.Generic.Dictionary<int, string> table, int id, Dictionary<int, string> origins)
    {
        if (table is null || !origins.TryGetValue(id, out var origin) || string.IsNullOrWhiteSpace(origin))
            return;
        table[id] = origin;
    }

    private static string OriginOf(Dictionary<string, string> origins, string key) =>
        origins.TryGetValue(key, out var origin) ? origin : "";

    private static ClothesProfile.Clothes? ClothesTemplate()
    {
        var profiles = DataBaseCore.Clothes;
        return profiles is null || !profiles.ContainsKey(-1) ? null : profiles[-1];
    }

    /// <summary>
    /// Turns the map asset field into an addressable reference. The data face carries a path or a GUID;
    /// only a GUID can be resolved by Addressables (a mod that ships its own catalog), anything else means
    /// the map stays data only and the mod builds the engine side itself.
    /// </summary>
    private static AssetReference? AddressableReference(string? mapAsset)
    {
        if (string.IsNullOrWhiteSpace(mapAsset))
            return null;
        var value = mapAsset.Trim();
        if (value.Length != 32 || !value.All(character => Uri.IsHexDigit(character)))
        {
            GameBridgeHook.Trace($"DatabaseInject: map asset {value} is not an addressable GUID");
            return null;
        }

        return new AssetReference(value);
    }

    private static string Pictures(SpecialGuestData guest) =>
        Join(guest.Portraits) + "#" + Join(guest.Body) + "#" + Join(guest.Eyes);

    // Used as the dialog resolver while the dialog packages do not exist yet (the scheduler pass).
    private static DialogPackage? Absent(string name) => null;

    private static string Join(string[]? paths) => paths is null ? "" : string.Join("|", paths);

    private static string Root(Dictionary<int, string> roots, int id) =>
        roots.TryGetValue(id, out var root) ? root : "";

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
