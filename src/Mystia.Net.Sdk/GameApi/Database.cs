using Mystia;

namespace Mystia.Data;

public enum CookerKind
{
    Empty,
    Pot,
    Grill,
    Fryer,
    Steamer,
    CuttingBoard,
}

public enum CharacterKind
{
    Special,
    Normal,
}

public enum SpeakerKind
{
    Self,
    Special,
    Normal,
    Unknown,
}

public enum DialogSide
{
    Left,
    Right,
}

public enum GoodsKind
{
    Food,
    Ingredient,
    Beverage,
    Money,
    Mission,
    Item,
    Recipe,
    Izakaya,
    Cooker,
    Partner,
    Badge,
    Trophy,
}

public struct IngredientData
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public int Level { get; set; }

    public int BaseValue { get; set; }

    public int Prefix { get; set; }

    public int[]? Tags { get; set; }

    public string? Picture { get; set; }
}

public struct FoodData
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public int Level { get; set; }

    public int BaseValue { get; set; }

    public int[]? Tags { get; set; }

    public int[]? BannedTags { get; set; }

    public bool Collab { get; set; }

    public string? Picture { get; set; }
}

public struct BeverageData
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public int Level { get; set; }

    public int BaseValue { get; set; }

    public int[]? Tags { get; set; }

    public int[]? BannedTags { get; set; }

    public bool Collab { get; set; }

    public string? Picture { get; set; }
}

public struct RecipeData
{
    public int Id { get; set; }

    public int FoodId { get; set; }

    public CookerKind Cooker { get; set; }

    public float Seconds { get; set; }

    public int[]? Ingredients { get; set; }
}

public struct CookerData
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public CookerKind Kind { get; set; }

    public int Series { get; set; }

    public string? Picture { get; set; }
}

public struct GuestCrowd
{
    public int[]? GuestIds { get; set; }

    public int Weight { get; set; }
}

public struct GuestSpawn
{
    public int IzakayaId { get; set; }

    public int GuestId { get; set; }

    public float Probability { get; set; }

    public bool OnlyAfterUnlock { get; set; }

    public bool OnlyWhenPlaceRecorded { get; set; }
}

public struct IzakayaData
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public int FundMin { get; set; }

    public int FundMax { get; set; }

    public float GuestGapMin { get; set; }

    public float GuestGapMax { get; set; }

    public float SpecialGuestGap { get; set; }

    public int Music { get; set; }

    public string? Map { get; set; }

    public int Tables { get; set; }

    public int CookTables { get; set; }

    public GuestCrowd[]? Crowds { get; set; }

    public GuestSpawn[]? SpecialGuests { get; set; }
}

public struct ItemData
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? Picture { get; set; }
}

public struct BadgeData
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? Picture { get; set; }
}

public struct NormalGuestData
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public float FundMultiplier { get; set; }

    public int Evaluation { get; set; }

    public int[]? FoodTags { get; set; }

    public int[]? BeverageTags { get; set; }

    public int[]? Conversations { get; set; }

    public bool LikesAllFood { get; set; }

    public bool LikesAllBeverages { get; set; }

    public bool Child { get; set; }

    public bool Hidden { get; set; }
}

public struct TagWeight
{
    public int TagId { get; set; }

    public int Weight { get; set; }
}

public struct GuestRequestLine
{
    public int TagId { get; set; }

    public string? Line { get; set; }
}

public struct SpecialGuestData
{
    public int Id { get; set; }

    public string? Label { get; set; }

    public string? Name { get; set; }

    public string? Description1 { get; set; }

    public string? Description2 { get; set; }

    public string? Description3 { get; set; }

    public int FundMin { get; set; }

    public int FundMax { get; set; }

    public int[]? HateFoodTags { get; set; }

    public TagWeight[]? LikeFood { get; set; }

    public TagWeight[]? LikeBeverages { get; set; }

    public GuestRequestLine[]? FoodRequests { get; set; }

    public GuestRequestLine[]? BeverageRequests { get; set; }

    public string[]? Evaluations { get; set; }

    public string[]? Conversations { get; set; }

    public string[]? Portraits { get; set; }

    public int NotebookFace { get; set; }

    public int? PositiveSpellFace { get; set; }

    public int? NegativeSpellFace { get; set; }

    public string[]? Body { get; set; }

    public string[]? Eyes { get; set; }

    public GuestSpawn[]? Spawns { get; set; }
}

public struct NpcPlace
{
    public string? Marker { get; set; }

    public int StayFrom { get; set; }

    public int StayUntil { get; set; }

    public string[]? Dialogs { get; set; }

    public float InteractSize { get; set; }

    public string? Switch { get; set; }
}

public struct NpcData
{
    public string? Key { get; set; }

    public CharacterKind Kind { get; set; }

    public int CharacterId { get; set; }

    public bool OffByDefault { get; set; }

    public int ShowFrom { get; set; }

    public int ShowUntil { get; set; }

    public int RestFrom { get; set; }

    public int RestUntil { get; set; }

    public NpcPlace[]? Places { get; set; }
}

public struct DialogLine
{
    public SpeakerKind Speaker { get; set; }

    public int SpeakerId { get; set; }

    public int Portrait { get; set; }

    public DialogSide Side { get; set; }

    public string? Text { get; set; }
}

public struct DialogData
{
    public string? Name { get; set; }

    public DialogLine[]? Lines { get; set; }
}

public struct MerchantOffer
{
    public GoodsKind Kind { get; set; }

    public int ItemId { get; set; }

    public string? Label { get; set; }

    public float Chance { get; set; }

    public int AmountMin { get; set; }

    public int AmountMax { get; set; }
}

public struct MerchantData
{
    public string? Key { get; set; }

    public string[]? WelcomeDialogs { get; set; }

    public string[]? EmptyDialogs { get; set; }

    public float PriceMin { get; set; }

    public float PriceMax { get; set; }

    public int LeastSellCount { get; set; }

    public MerchantOffer[]? Offers { get; set; }
}

[AutoWire]
public interface IDatabaseExtension
{
    void OnInjectIngredients(List<IngredientData> ingredients) { }

    void OnInjectFoods(List<FoodData> foods) { }

    void OnInjectBeverages(List<BeverageData> beverages) { }

    void OnInjectRecipes(List<RecipeData> recipes) { }

    void OnInjectCookers(List<CookerData> cookers) { }

    void OnInjectIzakayas(List<IzakayaData> izakayas) { }

    void OnInjectItems(List<ItemData> items) { }

    void OnInjectBadges(List<BadgeData> badges) { }

    void OnInjectNormalGuests(List<NormalGuestData> normalGuests) { }

    void OnInjectSpecialGuests(List<SpecialGuestData> specialGuests) { }

    void OnInjectNpcs(List<NpcData> npcs) { }

    void OnInjectDialogs(List<DialogData> dialogs) { }

    void OnInjectMerchants(List<MerchantData> merchants) { }
}
