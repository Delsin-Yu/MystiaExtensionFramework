using System.Reflection;
using Common;
using Common.DialogUtility;
using GameData.Core.Collections;
using GameData.Core.Collections.CharacterUtility;
using GameData.Core.Collections.DaySceneUtility;
using GameData.Core.Collections.DaySceneUtility.Collections;
using GameData.Core.Collections.NightSceneUtility;
using GameData.CoreLanguage;
using GameData.Profile;
using GameData.Profile.SchedulerNodeCollection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mystia.Data;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Mystia.Modding.Bridge;

internal static class GameRecords
{
    internal static Ingredient Ingredient(IngredientData data) =>
        new(data.Id, data.BaseValue, data.Level, data.Prefix, Ints(data.Tags));

    internal static ObjectLanguageBase IngredientText(IngredientData data, string root) =>
        Text(data.Name, data.Description, data.Picture, root);

    internal static Sellable Food(FoodData data) =>
        MakeSellable(data.Id, data.BaseValue, data.Level, data.Tags, data.BannedTags, Sellable.SellableType.Food, data.Collab);

    internal static ObjectLanguageBase FoodText(FoodData data, string root) =>
        Text(data.Name, data.Description, data.Picture, root);

    internal static Sellable Beverage(BeverageData data) =>
        MakeSellable(data.Id, data.BaseValue, data.Level, data.Tags, data.BannedTags, Sellable.SellableType.Beverage, data.Collab);

    internal static ObjectLanguageBase BeverageText(BeverageData data, string root) =>
        Text(data.Name, data.Description, data.Picture, root);

    internal static Recipe Recipe(RecipeData data) =>
        new(data.Id, data.FoodId, (Cooker.CookerType)data.Cooker, data.Seconds, Ints(data.Ingredients));

    internal static Cooker Cooker(CookerData data)
    {
        var kind = (int)data.Kind;
        var template = Values<Cooker>(DatabaseInject.Member(typeof(DataBaseCore), "Cookers"))
                .FirstOrDefault(cooker => (int)cooker.Type == kind)
            ?? Values<Cooker>(DatabaseInject.Member(typeof(DataBaseCore), "Cookers")).FirstOrDefault()
            ?? throw new InvalidOperationException("A stock cooker is required before a new one can be generated.");
        return (Cooker)InvokeLongest(typeof(Cooker), parameter => parameter.Name switch
        {
            "id" => data.Id,
            "type" => Enum.ToObject(parameter.ParameterType, kind),
            "cookerSeries" => Enum.ToObject(parameter.ParameterType, data.Series),
            _ => Read(template, parameter.Name ?? ""),
        });
    }

    internal static ObjectLanguageBase CookerText(CookerData data, string root) =>
        Text(data.Name, data.Description, data.Picture, root);

    internal static Izakaya Izakaya(IzakayaData data)
    {
        var crowds = data.Crowds ?? [];
        var normal = new Il2CppReferenceArray<Izakaya.NormalGuestGroup>(crowds.Length);
        for (var i = 0; i < crowds.Length; i++)
            normal[i] = new Izakaya.NormalGuestGroup(Ints(crowds[i].GuestIds), crowds[i].Weight);
        var spawns = data.SpecialGuests ?? [];
        var special = new Il2CppReferenceArray<Izakaya.SpecialGuestGroup>(spawns.Length);
        for (var i = 0; i < spawns.Length; i++)
            special[i] = Spawn(spawns[i].GuestId, spawns[i]);
        var izakaya = new Izakaya(
            data.Id,
            new Vector2Int(data.FundMin, data.FundMax),
            new Vector2(data.GuestGapMin, data.GuestGapMax),
            normal,
            data.SpecialGuestGap,
            special,
            data.Music,
            data.Map ?? "");
        izakaya.guestTableCount = data.Tables;
        izakaya.cookTableCount = data.CookTables;
        return izakaya;
    }

    internal static LanguageBase IzakayaText(IzakayaData data) =>
        new(data.Name ?? "", data.Description ?? "");

    internal static Item Item(ItemData data) => new(data.Id);

    internal static ObjectLanguageBase ItemText(ItemData data, string root) =>
        Text(data.Name, data.Description, data.Picture, root);

    internal static Item ClothItem(ClothesData data) => new(data.Id);

    internal static ObjectLanguageBase ClothText(ClothesData data, string root) =>
        Text(data.Name, data.Description, data.Picture, root);

    // Clothes reuse the stock profile's visual asset reference: the runtime portrait comes from
    // IPortraitProvider, so the addressable override the stock entry points at is never loaded.
    internal static ClothesProfile.Clothes Cloth(ClothesData data, int skinIndex, ClothesProfile.Clothes template)
    {
        var profile = new ClothesProfile.Clothes();
        profile.index = data.Id;
        profile.frameTime = 0f;
        profile.izakayaSkinIndex = data.IzakayaSkinIndex;
        profile.izkayaHorizontalOffset = data.IzkayaHorizontalOffset;
        profile.m_OverrideVisualAsset = template.m_OverrideVisualAsset;
        profile.notebookHorizontalOffset = data.NotebookHorizontalOffset;
        profile.notebookVerticalOffset = data.NotebookVerticalOffset;
        profile.notebookUITitleOffset = new Vector2(data.NotebookTitleHorizontalOffset, data.NotebookTitleVerticalOffset);
        var selection = new CharacterSkinSets.SkinSelectionInfo();
        selection.selectedType = CharacterSkinSets.SelectedType.DLC;
        selection.index = skinIndex;
        profile.skinIndex = selection;
        return profile;
    }

    internal static CharacterSpriteSetCompact CompactPixel(string[]? body, string[]? eyes, string root)
    {
        var fallback = DataBaseCharacter.FallbackCompactPixel;
        if (body is not { Length: > 0 } && eyes is not { Length: > 0 })
            return fallback ?? EmptyPixel();

        var pixel = ScriptableObject.CreateInstance<CharacterSpriteSetCompact>();
        var main = body is { Length: > 0 } ? AsRefs(Load(body, root)) : fallback?.MainSprite ?? AsRefs([]);
        var eye = eyes is { Length: > 0 } ? AsRefs(Load(eyes, root)) : fallback?.EyeSprite ?? AsRefs([]);
        if (fallback is null)
            pixel.Initialize(main, true, eye, false, 1f, 0f, false, 0f, false, 1f, new Il2CppReferenceArray<CharacterSpriteSetCompact.RemovableTrimProperty>(0), AsRefs([]), AsRefs([]), 0f, 0f);
        else
            pixel.Initialize(main, fallback.DoNotUseEyeSprite, eye, fallback.HasPrebakedShadow, fallback.AnimationSpeedMultiplier, fallback.ExtraYOffset, fallback.IsHina, fallback.RotatePerTime, fallback.DoNotHaveStepVFX, fallback.MoveSpeedMultiplier, fallback.RemovableTrims, fallback.TrimSpritesDisplayFront, fallback.TrimSpritesDisplayBack, fallback.TrimFrontSpriteFrameSpeed, fallback.TrimBackSpriteFrameSpeed);
        pixel.hideFlags = HideFlags.HideAndDontSave;
        return pixel;
    }

    internal static Il2CppReferenceArray<LanguageBase> SpellLanguage(SpellData data)
    {
        var language = new Il2CppReferenceArray<LanguageBase>(2);
        language[0] = new LanguageBase(data.Name ?? "", data.Description ?? "");
        language[1] = new LanguageBase(data.NegativeName ?? "", data.NegativeDescription ?? "");
        return language;
    }

    internal static ObjectLanguageBase BuffText(BuffData data, string root) =>
        Text(data.Name, data.Description, data.Picture, root);

    internal static SchedulerNode.Reward Reward(SchedulerRewardData data)
    {
        var reward = new SchedulerNode.Reward();
        reward.rewardType = (SchedulerNode.Reward.RewardType)data.RewardType;
        reward.rewardId = data.RewardId ?? "";
        if (data.ObjectType.HasValue)
            reward.objectType = (SchedulerNode.Reward.ObjectType)data.ObjectType.Value;
        reward.rewardIntArray = Ints(data.RewardIntArray);
        return reward;
    }

    internal static MissionNode.FinishCondition FinishCondition(MissionFinishConditionData data)
    {
        var condition = new MissionNode.FinishCondition();
        condition.conditionType = (MissionNode.FinishCondition.ConditionType)data.ConditionType;
        condition.label = data.Label ?? "";
        condition.amount = data.Amount ?? 0;
        condition.tag = data.Tag ?? 0;
        condition.tags = Ints(data.Tags);
        if (data.SellableType.HasValue)
            condition.sellableType = (Sellable.SellableType)data.SellableType.Value;
        var product = new Product();
        product.productType = (Product.ProductType)(data.ProductType ?? 0);
        product.productId = data.ProductId ?? 0;
        product.productAmount = data.ProductAmount ?? 0;
        product.productLabel = "";
        condition.product = product;
        return condition;
    }

    internal static SchedulerNode.Trigger Trigger(SchedulerTriggerData? data)
    {
        var trigger = new SchedulerNode.Trigger();
        if (data is not { } source)
            return trigger;
        trigger.triggerType = (SchedulerNode.Trigger.TriggerType)source.TriggerType;
        trigger.triggerId = source.TriggerId ?? "";
        trigger.labels = Strings([]);
        if (source.Time is { } time)
            trigger.time = Day(time);
        return trigger;
    }

    internal static SchedulerNode.Day Day(SchedulerDayData data)
    {
        var day = new SchedulerNode.Day();
        day.dayType = (SchedulerNode.Day.DayType)data.DayType;
        day.dayCalcType = (SchedulerNode.Day.CalculateType)data.CalcType;
        day.day = data.Day;
        day.dayRange = new Vector2Int(data.DayRangeMin, data.DayRangeMax);
        return day;
    }

    internal static SchedulerNode.Event Event(SchedulerEventData? data, Func<string, DialogPackage?> find)
    {
        var scheduled = new SchedulerNode.Event();
        if (data is not { } source)
            return scheduled;
        scheduled.eventType = (SchedulerNode.Event.EventType)source.EventType;
        if (!string.IsNullOrEmpty(source.DialogPackage))
            scheduled.runtimeDialogPackage = find(source.DialogPackage);
        return scheduled;
    }

    internal static MissionNode MissionNodeRecord(MissionNodeData data, Func<string, DialogPackage?> find)
    {
        var node = ScriptableObject.CreateInstance<MissionNode>();
        node.name = data.Label ?? "";
        node.label = data.Label ?? "";
        node.debugLabel = data.DebugLabel ?? data.Label ?? "";
        node.missionType = (SchedulerNode.SchedulerType)data.MissionType;
        node.isTimedMission = data.IsTimedMission;
        node.missionFailedAction = (MissionNode.MissionFailedAction)data.MissionFailedAction;
        node.hasSender = !string.IsNullOrEmpty(data.Sender);
        node.sender = data.Sender ?? "";
        node.hasReciever = !string.IsNullOrEmpty(data.Receiver);
        node.reciever = data.Receiver ?? "";
        node.missionTimeLimit = Trigger(data.MissionTimeLimit);
        node.missionFinishEvent = Event(data.MissionFinishEvent, find);
        node.missionFailedEvent = Event(data.MissionFailedEvent, find);
        node.rewards = Rewards(data.Rewards);
        node.postRewards = Rewards(data.PostRewards);
        node.finishCondition = Conditions(data.FinishConditions);
        node.preNodes = Strings(data.PreNodes);
        node.postMissions = Strings(data.PostMissions);
        node.postMissionsAfterPerformance = Strings(data.PostMissionsAfterPerformance);
        node.postEvents = Strings(data.PostEvents);
        node.hideFlags = HideFlags.HideAndDontSave;
        return node;
    }

    internal static EventNode EventNodeRecord(EventNodeData data, Func<string, DialogPackage?> find)
    {
        var node = ScriptableObject.CreateInstance<EventNode>();
        node.name = data.Label ?? "";
        node.label = data.Label ?? "";
        node.debugLabel = data.DebugLabel ?? data.Label ?? "";
        var scheduled = new SchedulerNode.ScheduledEvent();
        scheduled.trigger = Trigger(data.Trigger);
        scheduled.eventData = Event(data.ScheduledEvent, find);
        node.scheduledEvent = scheduled;
        node.rewards = Rewards(data.Rewards);
        node.postRewards = Rewards(data.PostRewards);
        node.preNodes = Strings(data.PreNodes);
        node.postMissions = Strings(data.PostMissions);
        node.postMissionsAfterPerformance = Strings(data.PostMissionsAfterPerformance);
        node.postEvents = Strings(data.PostEvents);
        node.hideFlags = HideFlags.HideAndDontSave;
        return node;
    }

    // MapNode is a native structure, so its value is written through InteropTables by the caller.
    internal static DaySceneMapProfile.MapNode MapNode(DayMapData data, AssetReference? reference)
    {
        var node = new DaySceneMapProfile.MapNode();
        node.mapName = data.Label ?? "";
        node.parent = data.Parent ?? "";
        node.mapAssetReference = reference!;
        node.mapCollectableLabels = Strings(data.Collectables);
        node.mapSpawnMarkerLabels = Strings(SpawnMarkerLabels(data));
        node.level1IzakayaId = Ints(data.Level1IzakayaIds);
        node.level2IzakayaId = Ints(data.Level2IzakayaIds);
        node.level3IzakayaId = Ints(data.Level3IzakayaIds);
        return node;
    }

    internal static string[] SpawnMarkerLabels(DayMapData data)
    {
        var markers = data.SpawnMarkers ?? [];
        var labels = new string[markers.Length];
        for (var i = 0; i < markers.Length; i++)
            labels[i] = Marker(data.Label, markers[i]);
        return labels;
    }

    internal static string Marker(string? mapLabel, SpawnMarkerData marker) =>
        $"{mapLabel ?? ""}_{marker.Label ?? ""}";

    internal static CharacterSkinSets.SkinSelectionInfo SkinSelection(int index)
    {
        var selection = new CharacterSkinSets.SkinSelectionInfo();
        selection.selectedType = CharacterSkinSets.SelectedType.DLC;
        selection.index = index;
        return selection;
    }

    private static Il2CppReferenceArray<SchedulerNode.Reward> Rewards(SchedulerRewardData[]? rewards)
    {
        rewards ??= [];
        var array = new Il2CppReferenceArray<SchedulerNode.Reward>(rewards.Length);
        for (var i = 0; i < rewards.Length; i++)
            array[i] = Reward(rewards[i]);
        return array;
    }

    private static Il2CppReferenceArray<MissionNode.FinishCondition> Conditions(MissionFinishConditionData[]? conditions)
    {
        conditions ??= [];
        var array = new Il2CppReferenceArray<MissionNode.FinishCondition>(conditions.Length);
        for (var i = 0; i < conditions.Length; i++)
            array[i] = FinishCondition(conditions[i]);
        return array;
    }

    internal static Badge Badge(BadgeData data) => new(data.Id);

    internal static ObjectLanguageBase BadgeText(BadgeData data, string root) =>
        Text(data.Name, data.Description, data.Picture, root);

    internal static NormalGuest NormalGuest(NormalGuestData data) =>
        new(
            data.Id,
            data.FundMultiplier,
            data.Evaluation,
            Ints(data.FoodTags),
            Ints(data.BeverageTags),
            Ints(data.Conversations),
            data.LikesAllFood,
            data.LikesAllBeverages,
            data.Child,
            data.Hidden,
            null!);

    internal static LanguageBase NormalGuestText(NormalGuestData data) =>
        new(data.Name ?? "", data.Description ?? "");

    internal static SpecialGuest SpecialGuest(SpecialGuestData data)
    {
        var template = Values<SpecialGuest>(DatabaseInject.Member(typeof(DataBaseCharacter), "SpecialGuest")).FirstOrDefault()
            ?? throw new InvalidOperationException("A stock special guest is required before a new one can be generated.");
        return (SpecialGuest)InvokeLongest(typeof(SpecialGuest), parameter => parameter.Name switch
        {
            "id" => data.Id,
            "stringId" => data.Label ?? "",
            "fundRange" => new Vector2Int(data.FundMin, data.FundMax),
            "hateFoodTag" => Ints(data.HateFoodTags),
            "likeFoodTag" => Refs(Weights(data.LikeFood)),
            "likeBevTag" => Refs(Weights(data.LikeBeverages)),
            _ => Read(template, parameter.Name ?? ""),
        });
    }

    internal static GuestProfilePair Visual(SpecialGuestData data, string root)
    {
        var portraits = Load(data.Portraits, root);
        return PortraitSprites.Attach(
            data.Id,
            portraits,
            data.NotebookFace,
            data.PositiveSpellFace ?? -1,
            data.NegativeSpellFace ?? -1,
            Skins(data, root));
    }

    internal static void PlaceSpawns(IReadOnlyList<SpecialGuestData> guests)
    {
        var dictionary = DatabaseInject.Member(typeof(DataBaseCore), "Izakayas");
        if (dictionary is null)
            return;
        var indexer = DatabaseInject.Indexer(dictionary);
        foreach (var guest in guests)
        {
            if (guest.Spawns is null)
                continue;
            foreach (var spawn in guest.Spawns)
            {
                object? stored;
                try
                {
                    stored = indexer.GetValue(dictionary, [spawn.IzakayaId]);
                }
                catch (TargetInvocationException)
                {
                    continue;
                }

                if (stored is not Izakaya izakaya)
                    continue;
                var pool = izakaya.SpecialGuestPool;
                var count = pool?.Length ?? 0;
                var next = new Il2CppReferenceArray<Izakaya.SpecialGuestGroup>(count + 1);
                for (var i = 0; i < count; i++)
                    next[i] = pool![i];
                next[count] = Spawn(guest.Id, spawn);
                izakaya.SpecialGuestPool = next;
            }
        }
    }

    internal static NPC Npc(NpcData data)
    {
        var places = data.Places ?? [];
        var destinations = new NPC.Destination[places.Length];
        for (var i = 0; i < places.Length; i++)
        {
            var place = places[i];
            destinations[i] = new NPC.Destination
            {
                spawnMarker = place.Marker ?? "",
                stayTime = new Vector2Int(place.StayFrom, place.StayUntil),
                initialDialogPackIDs = Strings(place.Dialogs),
                interactiveAreaSize = place.InteractSize,
                // The paid build of 4.4.0e has no switch condition field on this struct (the game
                // project source does), so a place's Switch value has no target table entry here.
            };
        }

        return new NPC
        {
            key = data.Key ?? "",
            identity = new SchedulerNode.Character((SceneDirector.Identity)data.Kind, data.CharacterId),
            offByDefault = data.OffByDefault,
            showTime = new Vector2Int(data.ShowFrom, data.ShowUntil),
            restTime = new Vector2Int(data.RestFrom, data.RestUntil),
            possibleDestinations = destinations,
        };
    }

    internal static DialogPackage Dialog(DialogData data)
    {
        var template = Values<DialogPackage>(DatabaseInject.Member(typeof(DataBaseDay), "allDialogPackages")).FirstOrDefault()
            ?? throw new InvalidOperationException("A stock dialog is required before a new one can be generated.");
        var package = UnityEngine.Object.Instantiate(template);
        package.name = data.Name ?? "";
        var lines = data.Lines ?? [];
        WriteLines(package, lines);
        DialogScripts.Remember(package.name, lines);
        return package;
    }

    internal static Merchant Merchant(MerchantData data, Func<string, DialogPackage?> find)
    {
        return new Merchant
        {
            key = data.Key ?? "",
            welcomeDialogPackage = Packages(data.WelcomeDialogs, find),
            nullDialogPackage = Packages(data.EmptyDialogs, find),
            priceMultiplierRange = new Vector2(data.PriceMin, data.PriceMax),
            leastSellNum = data.LeastSellCount,
            merchandiseCollection = Offers(data.Offers),
        };
    }

    internal static DialogPackage? FindDialog(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        var dictionary = DatabaseInject.Member(typeof(DataBaseDay), "allDialogPackages");
        if (dictionary is null)
            return null;
        try
        {
            return DatabaseInject.Indexer(dictionary).GetValue(dictionary, [name]) as DialogPackage;
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    private static Sellable MakeSellable(int id, int baseValue, int level, int[]? tags, int[]? banned, Sellable.SellableType type, bool collab) =>
        new(id, baseValue, level, Ints(tags), Ints(banned), type, new Il2CppSystem.Collections.Generic.List<int>(), collab);

    private static ObjectLanguageBase Text(string? name, string? description, string? picture, string root) =>
        new(name ?? "", description ?? "", string.IsNullOrWhiteSpace(picture) ? null! : SpriteFiles.Load(root, picture));

    private static Izakaya.SpecialGuestGroup Spawn(int guestId, GuestSpawn spawn) =>
        new(guestId, spawn.Probability, spawn.OnlyAfterUnlock, spawn.OnlyWhenPlaceRecorded);

    private static SpecialGuest.WeightedTag[] Weights(TagWeight[]? tags)
    {
        if (tags is null || tags.Length == 0)
            return [];
        var weights = new SpecialGuest.WeightedTag[tags.Length];
        for (var i = 0; i < tags.Length; i++)
            weights[i] = new SpecialGuest.WeightedTag(tags[i].TagId, tags[i].Weight);
        return weights;
    }

    private static CharacterSkinSets Skins(SpecialGuestData data, string root)
    {
        var skins = ScriptableObject.CreateInstance<CharacterSkinSets>();
        skins.Initialize(CompactPixel(data.Body, data.Eyes, root), new Il2CppReferenceArray<CharacterSpriteSetCompact>(0), new Il2CppReferenceArray<CharacterSpriteSetCompact>(0));
        return skins;
    }

    private static CharacterSpriteSetCompact EmptyPixel()
    {
        var pixel = ScriptableObject.CreateInstance<CharacterSpriteSetCompact>();
        var empty = AsRefs([]);
        pixel.Initialize(empty, true, empty, false, 1f, 0f, false, 0f, false, 1f, new Il2CppReferenceArray<CharacterSpriteSetCompact.RemovableTrimProperty>(0), empty, empty, 0f, 0f);
        return pixel;
    }

    private static void WriteLines(DialogPackage package, DialogLine[] lines)
    {
        var property = AccessTools.Property(typeof(DialogPackage), "dialogMeta");
        var field = property is null ? AccessTools.Field(typeof(DialogPackage), "dialogMeta") : null;
        var memberType = property?.PropertyType ?? field?.FieldType
            ?? throw new MissingMemberException(typeof(DialogPackage).FullName, "dialogMeta");
        var current = property is not null ? property.GetValue(package) : field!.GetValue(package);
        var sample = LineAt(current, 0);
        var created = Activator.CreateInstance(memberType, lines.Length)!;
        var setter = memberType.GetMethod("set_Item")
            ?? throw new MissingMethodException(memberType.FullName, "set_Item");
        for (var i = 0; i < lines.Length; i++)
            setter.Invoke(created, [i, Rewrite(sample, lines[i], i)]);
        if (property is not null)
            property.SetValue(package, created);
        else
            field!.SetValue(package, created);
    }

    private static DialogMeta LineAt(object? array, int index)
    {
        if (array is null)
            throw new InvalidOperationException("A stock dialog is required before a new one can be generated.");
        var length = (int)(array.GetType().GetProperty("Length")?.GetValue(array) ?? 0);
        if (length <= index)
            throw new InvalidOperationException("A stock dialog is required before a new one can be generated.");
        return (DialogMeta)array.GetType().GetMethod("get_Item")!.Invoke(array, [index])!;
    }

    private static DialogMeta Rewrite(DialogMeta meta, DialogLine line, int index)
    {
        meta.dialogId = index;
        var speaker = meta.speakerIdentity;
        speaker.speakerType = (SpeakerIdentity.Identity)line.Speaker;
        speaker.speakerId = line.SpeakerId;
        speaker.speakerPortrayalVariationId = line.Portrait;
        meta.speakerIdentity = speaker;
        meta.speakerPosition = (Position)line.Side;
        return meta;
    }

    private static Il2CppReferenceArray<DialogPackage> Packages(string[]? names, Func<string, DialogPackage?> find)
    {
        names ??= [];
        var found = new List<DialogPackage>();
        foreach (var name in names)
        {
            var package = find(name);
            if (package is not null)
                found.Add(package);
        }

        return Refs(found);
    }

    private static Merchant.Merchandise[] Offers(MerchantOffer[]? offers)
    {
        offers ??= [];
        var goods = new Merchant.Merchandise[offers.Length];
        for (var i = 0; i < offers.Length; i++)
        {
            var offer = offers[i];
            goods[i] = new Merchant.Merchandise(
                new Product((Product.ProductType)offer.Kind, offer.ItemId, 1, offer.Label ?? ""),
                offer.Chance,
                new Vector2Int(offer.AmountMin, offer.AmountMax));
        }

        return goods;
    }

    private static Sprite[] Load(string[]? paths, string root)
    {
        if (paths is not { Length: > 0 })
            return [];
        var sprites = new Sprite[paths.Length];
        for (var i = 0; i < paths.Length; i++)
            sprites[i] = SpriteFiles.Load(root, paths[i]);
        return sprites;
    }

    private static Il2CppReferenceArray<Sprite> AsRefs(Sprite[] sprites)
    {
        var array = new Il2CppReferenceArray<Sprite>(sprites.Length);
        for (var i = 0; i < sprites.Length; i++)
            array[i] = sprites[i];
        return array;
    }

    private static Il2CppStructArray<int> Ints(int[]? values)
    {
        values ??= [];
        var array = new Il2CppStructArray<int>(values.Length);
        for (var i = 0; i < values.Length; i++)
            array[i] = values[i];
        return array;
    }

    private static Il2CppStringArray Strings(string[]? values)
    {
        values ??= [];
        var array = new Il2CppStringArray(values.Length);
        for (var i = 0; i < values.Length; i++)
            array[i] = values[i];
        return array;
    }

    private static Il2CppReferenceArray<T> Refs<T>(IReadOnlyList<T> values) where T : Il2CppObjectBase
    {
        var array = new Il2CppReferenceArray<T>(values.Count);
        for (var i = 0; i < values.Count; i++)
            array[i] = values[i];
        return array;
    }

    private static object InvokeLongest(Type type, Func<ParameterInfo, object?> argument)
    {
        var ctor = type.GetConstructors().OrderByDescending(ctor => ctor.GetParameters().Length).First();
        var parameters = ctor.GetParameters();
        var values = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            values[i] = argument(parameters[i]);
        return ctor.Invoke(values)!;
    }

    private static object? Read(object source, string name)
    {
        if (name == "specialGuestExtraDialogData")
            name = "m_SpecialGuestExtraDialogDataAsset";
        var type = source.GetType();
        var property = AccessTools.Property(type, name);
        if (property is not null)
            return property.GetValue(source);
        var field = AccessTools.Field(type, name);
        if (field is not null)
            return field.GetValue(source);
        if (name.Length == 0)
            throw new MissingMemberException(type.FullName, name);
        var pascal = char.ToUpperInvariant(name[0]) + name[1..];
        property = AccessTools.Property(type, pascal);
        if (property is not null)
            return property.GetValue(source);
        throw new MissingMemberException(type.FullName, name);
    }

    private static IEnumerable<T> Values<T>(object? dictionary) where T : class
    {
        if (dictionary is null)
            yield break;
        if (dictionary.GetType().GetProperty("Values")?.GetValue(dictionary) is not System.Collections.IEnumerable values)
            yield break;
        foreach (var value in values)
        {
            if (value is T typed)
                yield return typed;
        }
    }
}

internal static class DialogScripts
{
    private static readonly Dictionary<string, string[]> Lines = new(StringComparer.Ordinal);

    internal static void Remember(string name, DialogLine[] lines)
    {
        var text = new string[lines.Length];
        for (var i = 0; i < lines.Length; i++)
            text[i] = lines[i].Text ?? "";
        Lines[name] = text;
    }

    internal static void Clear() => Lines.Clear();

    internal static void Fill(
        DialogPackage? package,
        ref Il2CppSystem.Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>> callback)
    {
        if (package is null || string.IsNullOrEmpty(package.name) || !Lines.TryGetValue(package.name, out var lines) || lines is null)
            return;
        var prior = callback;
        var captured = lines;
        callback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>>>(
            new Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>>(map =>
            {
                prior?.Invoke(map);
                for (var i = 0; i < captured.Length; i++)
                    map[i] = captured[i];
            }))!;
    }
}
