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
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mystia.Data;
using UnityEngine;

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
                switchConditionLabel = place.Switch ?? "",
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
        var fallback = DataBaseCharacter.FallbackCompactPixel;
        CharacterSpriteSetCompact pixel;
        if (data.Body is not { Length: > 0 } && data.Eyes is not { Length: > 0 })
        {
            pixel = fallback ?? EmptyPixel();
        }
        else
        {
            pixel = ScriptableObject.CreateInstance<CharacterSpriteSetCompact>();
            var body = data.Body is { Length: > 0 } ? AsRefs(Load(data.Body, root)) : fallback?.MainSprite ?? AsRefs([]);
            var eyes = data.Eyes is { Length: > 0 } ? AsRefs(Load(data.Eyes, root)) : fallback?.EyeSprite ?? AsRefs([]);
            if (fallback is null)
                pixel.Initialize(body, true, eyes, false, 1f, 0f, false, 0f, false, 1f, new Il2CppReferenceArray<CharacterSpriteSetCompact.RemovableTrimProperty>(0), AsRefs([]), AsRefs([]), 0f, 0f);
            else
                pixel.Initialize(body, fallback.DoNotUseEyeSprite, eyes, fallback.HasPrebakedShadow, fallback.AnimationSpeedMultiplier, fallback.ExtraYOffset, fallback.IsHina, fallback.RotatePerTime, fallback.DoNotHaveStepVFX, fallback.MoveSpeedMultiplier, fallback.RemovableTrims, fallback.TrimSpritesDisplayFront, fallback.TrimSpritesDisplayBack, fallback.TrimFrontSpriteFrameSpeed, fallback.TrimBackSpriteFrameSpeed);
        }

        skins.Initialize(pixel, new Il2CppReferenceArray<CharacterSpriteSetCompact>(0), new Il2CppReferenceArray<CharacterSpriteSetCompact>(0));
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
