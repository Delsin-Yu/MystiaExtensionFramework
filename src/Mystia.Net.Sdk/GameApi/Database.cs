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

    /// <summary>玩家自身。仅数据面需要区分来源时使用；值不与游戏内 <c>Identity</c> 枚举逐值对应，桥接按名称转换。</summary>
    Self,
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

/// <summary>角色朝向。取值与顺序同游戏内 <c>DayScenePlayerInputGenerator.CharacterRotation</c>。</summary>
public enum CharacterRotationKind
{
    Down,
    Left,
    Up,
    Right,
    Null = -1,
}

/// <summary>
/// 对话行内动作的类型。取值与顺序同游戏内 <c>DialogPannel</c> 的 <c>ActionType</c>，便于桥接按值转换。
/// 数据面当前只承载 BG/CG/Sound/Branch/Goto/End，其余成员保留以保持枚举值一致。
/// </summary>
public enum DialogActionKind
{
    ForegroundCleaning,
    BG,
    CG,
    Sound,
    CameraShake,
    Branch,
    Goto,
    End,
    PlayBGM,
    PauseResumeBGM,
    StopBGM,
    Null,
    TutorialSFX,
    SwitchBranch,
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

public record struct IngredientData
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

public record struct FoodData
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

public record struct BeverageData
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

public record struct RecipeData
{
    public int Id { get; set; }

    public int FoodId { get; set; }

    public CookerKind Cooker { get; set; }

    public float Seconds { get; set; }

    public int[]? Ingredients { get; set; }
}

public record struct CookerData
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public CookerKind Kind { get; set; }

    public int Series { get; set; }

    public string? Picture { get; set; }
}

public record struct GuestCrowd
{
    public int[]? GuestIds { get; set; }

    public int Weight { get; set; }
}

public record struct GuestSpawn
{
    public int IzakayaId { get; set; }

    public int GuestId { get; set; }

    public float Probability { get; set; }

    public bool OnlyAfterUnlock { get; set; }

    public bool OnlyWhenPlaceRecorded { get; set; }
}

public record struct IzakayaData
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

public record struct ItemData
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? Picture { get; set; }
}

public record struct BadgeData
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? Picture { get; set; }
}

public record struct NormalGuestData
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

public record struct TagWeight
{
    public int TagId { get; set; }

    public int Weight { get; set; }
}

public record struct GuestRequestLine
{
    public int TagId { get; set; }

    public string? Line { get; set; }

    public bool Enable { get; set; }
}

public record struct SpecialGuestKizunaData
{
    /// <summary>羁绊 1 级升级前置事件（事件节点 label）。</summary>
    public string? PrerequisiteEvent1 { get; set; }

    /// <summary>羁绊 2 级升级前置事件（事件节点 label）。</summary>
    public string? PrerequisiteEvent2 { get; set; }

    /// <summary>羁绊 3 级升级前置事件（事件节点 label）。</summary>
    public string? PrerequisiteEvent3 { get; set; }

    /// <summary>羁绊 4 级升级前置事件（事件节点 label）。</summary>
    public string? PrerequisiteEvent4 { get; set; }

    /// <summary>羁绊 1 级首句，对话包名。</summary>
    public string[]? Welcome1 { get; set; }

    /// <summary>羁绊 2 级首句，对话包名。</summary>
    public string[]? Welcome2 { get; set; }

    /// <summary>羁绊 3 级首句，对话包名。</summary>
    public string[]? Welcome3 { get; set; }

    /// <summary>羁绊 4 级首句，对话包名。</summary>
    public string[]? Welcome4 { get; set; }

    /// <summary>羁绊 5 级首句，对话包名。</summary>
    public string[]? Welcome5 { get; set; }

    /// <summary>羁绊 1 级闲聊，对话包名。</summary>
    public string[]? ChatData1 { get; set; }

    /// <summary>羁绊 2 级闲聊，对话包名。</summary>
    public string[]? ChatData2 { get; set; }

    /// <summary>羁绊 3 级闲聊，对话包名。</summary>
    public string[]? ChatData3 { get; set; }

    /// <summary>羁绊 4 级闲聊，对话包名。</summary>
    public string[]? ChatData4 { get; set; }

    /// <summary>羁绊 5 级闲聊，对话包名。</summary>
    public string[]? ChatData5 { get; set; }

    /// <summary>羁绊 2 级邀约成功，对话包名。</summary>
    public string[]? InviteSucceed2 { get; set; }

    /// <summary>羁绊 3 级邀约成功，对话包名。</summary>
    public string[]? InviteSucceed3 { get; set; }

    /// <summary>羁绊 4 级邀约成功，对话包名。</summary>
    public string[]? InviteSucceed4 { get; set; }

    /// <summary>羁绊 5 级邀约成功，对话包名。</summary>
    public string[]? InviteSucceed5 { get; set; }

    /// <summary>羁绊 2 级邀约失败，对话包名。</summary>
    public string[]? InviteFailed2 { get; set; }

    /// <summary>羁绊 3 级邀约失败，对话包名。</summary>
    public string[]? InviteFailed3 { get; set; }

    /// <summary>羁绊 4 级邀约失败，对话包名。</summary>
    public string[]? InviteFailed4 { get; set; }

    /// <summary>羁绊 3 级点单食材，对话包名（源包字段拼写为 RequestIngerdient）。</summary>
    public string[]? RequestIngredient3 { get; set; }

    /// <summary>羁绊 4 级点单食材，对话包名（源包字段拼写为 RequestIngerdient）。</summary>
    public string[]? RequestIngredient4 { get; set; }

    /// <summary>羁绊 5 级点单食材，对话包名（源包字段拼写为 RequestIngerdient）。</summary>
    public string[]? RequestIngredient5 { get; set; }

    /// <summary>羁绊 4 级点单饮品，对话包名。</summary>
    public string[]? RequestBeverage4 { get; set; }

    /// <summary>羁绊 5 级点单饮品，对话包名。</summary>
    public string[]? RequestBeverage5 { get; set; }

    /// <summary>羁绊 5 级委托，对话包名。</summary>
    public string[]? Commision5 { get; set; }

    /// <summary>羁绊 5 级委托完成，对话包名。</summary>
    public string[]? CommisionFinish5 { get; set; }
}

public record struct SpecialGuestData
{
    public int Id { get; set; }

    public string? Label { get; set; }

    /// <summary>角色类型（特殊/普通/自身）。</summary>
    public CharacterKind Kind { get; set; }

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

    /// <summary>是否为特殊稀客（笔记本与刷客规则上区别于普通稀客）。</summary>
    public bool IsParticular { get; set; }

    /// <summary>是否为联动角色。</summary>
    public bool IsCollabCharacter { get; set; }

    /// <summary>是否在笔记本图鉴中隐藏。</summary>
    public bool HideInAlbum { get; set; }

    /// <summary>委托区域 label（源包与游戏侧字段拼写均为 CommisionAreaLabel）。</summary>
    public string? CommisionAreaLabel { get; set; }

    /// <summary>该稀客在白天场景的固定刷新点；为空表示不落点。</summary>
    public SpawnMarkerData? SpawnMarker { get; set; }

    /// <summary>羁绊（Kizuna）对话与事件配置；为空表示该客人无羁绊数据。</summary>
    public SpecialGuestKizunaData? Kizuna { get; set; }
}

public record struct NpcPlace
{
    public string? Marker { get; set; }

    public int StayFrom { get; set; }

    public int StayUntil { get; set; }

    public string[]? Dialogs { get; set; }

    public float InteractSize { get; set; }

    public string? Switch { get; set; }
}

public record struct NpcData
{
    public string? Key { get; set; }

    public CharacterKind Kind { get; set; }

    public int CharacterId { get; set; }

    /// <summary>白天场景显示名（写入日间场景 NPC 名表）。</summary>
    public string? Name { get; set; }

    /// <summary>白天场景描述文本。</summary>
    public string? Description { get; set; }

    public bool OffByDefault { get; set; }

    public int ShowFrom { get; set; }

    public int ShowUntil { get; set; }

    public int RestFrom { get; set; }

    public int RestUntil { get; set; }

    public NpcPlace[]? Places { get; set; }
}

public record struct DialogBranchOptionData
{
    /// <summary>选项文本。</summary>
    public string? Text { get; set; }

    /// <summary>目标对话序号，从 1 开始；对话行数 + 1 表示结束当前对话包。</summary>
    public int Jump { get; set; }

    /// <summary>选项价格；为空表示不收费。</summary>
    public int? Price { get; set; }
}

public record struct DialogActionData
{
    public DialogActionKind ActionType { get; set; }

    /// <summary>CG/BG 动作的图像，相对模组目录的路径。</summary>
    public string? Sprite { get; set; }

    /// <summary>Sound 动作的音效，相对模组目录的路径。</summary>
    public string? Sound { get; set; }

    /// <summary>Branch 动作的选项。</summary>
    public DialogBranchOptionData[]? Options { get; set; }

    /// <summary>Goto/End 动作的目标对话序号，从 1 开始；为空表示不指定。</summary>
    public int? Index { get; set; }

    /// <summary>End 动作的原版退出码。</summary>
    public int? ExitCode { get; set; }

    /// <summary>是否设置资源（BG/CG 置 false 表示清空当前图）。</summary>
    public bool ShouldSet { get; set; }
}

public record struct DialogLine
{
    public SpeakerKind Speaker { get; set; }

    public int SpeakerId { get; set; }

    public int Portrait { get; set; }

    public DialogSide Side { get; set; }

    public string? Text { get; set; }

    /// <summary>该行的行内动作，按顺序执行。</summary>
    public DialogActionData[]? Actions { get; set; }

    /// <summary>是否在前景层说话（说话人拉到前景）。</summary>
    public bool IsSpeakInForeground { get; set; }

    /// <summary>是否压暗背景。</summary>
    public bool IsDark { get; set; }

    /// <summary>文本中是否替换说话人姓名。</summary>
    public bool UseNameInText { get; set; }

    /// <summary>是否使用行内自定义立绘。</summary>
    public bool UseOverrideSprite { get; set; }

    /// <summary>行内自定义立绘，相对模组目录的路径。</summary>
    public string? OverrideSprite { get; set; }
}

public record struct DialogData
{
    public string? Name { get; set; }

    public DialogLine[]? Lines { get; set; }
}

public record struct MerchantOffer
{
    public GoodsKind Kind { get; set; }

    public int ItemId { get; set; }

    public string? Label { get; set; }

    public float Chance { get; set; }

    public int AmountMin { get; set; }

    public int AmountMax { get; set; }
}

public record struct MerchantData
{
    public string? Key { get; set; }

    public string[]? WelcomeDialogs { get; set; }

    public string[]? EmptyDialogs { get; set; }

    public float PriceMin { get; set; }

    public float PriceMax { get; set; }

    public int LeastSellCount { get; set; }

    public MerchantOffer[]? Offers { get; set; }
}

public record struct ClothesData
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    /// <summary>服装图标/预览图，相对模组目录的路径。运行时立绘走 IPortraitProvider。</summary>
    public string? Picture { get; set; }

    public int IzakayaSkinIndex { get; set; }

    /// <summary>源包与游戏侧字段拼写均为 IzkayaHorizontalOffset。</summary>
    public float IzkayaHorizontalOffset { get; set; }

    public float NotebookHorizontalOffset { get; set; }

    public float NotebookVerticalOffset { get; set; }

    public float NotebookTitleHorizontalOffset { get; set; }

    public float NotebookTitleVerticalOffset { get; set; }

    /// <summary>像素集主体帧，相对模组目录的路径数组。</summary>
    public string[]? Body { get; set; }

    /// <summary>像素集眼睛帧，相对模组目录的路径数组。</summary>
    public string[]? Eyes { get; set; }
}

public record struct SpellData
{
    /// <summary>符卡 id。原版按此 id 索引语言、符卡实例与宣言立绘。</summary>
    public int Id { get; set; }

    /// <summary>符卡/角色标识。</summary>
    public string? Label { get; set; }

    /// <summary>拥有该符卡的角色 id。</summary>
    public int CharacterId { get; set; }

    /// <summary>正面符卡名（游戏语言表第 0 项的 Name）。</summary>
    public string? Name { get; set; }

    /// <summary>正面符卡说明（游戏语言表第 0 项的 Description）。</summary>
    public string? Description { get; set; }

    /// <summary>负面符卡名（游戏语言表第 1 项的 Name）。</summary>
    public string? NegativeName { get; set; }

    /// <summary>负面符卡说明（游戏语言表第 1 项的 Description）。</summary>
    public string? NegativeDescription { get; set; }

    /// <summary>宣言立绘，相对模组目录的路径。</summary>
    public string? Portrait { get; set; }

    /// <summary>正面符卡立绘，相对模组目录的路径。</summary>
    public string? PositivePortrait { get; set; }

    /// <summary>负面符卡立绘，相对模组目录的路径。</summary>
    public string? NegativePortrait { get; set; }

    /// <summary>宣言立绘的水平 pivot（贴图内归一化坐标）；与 PortraitPivotY 同为 0 表示未指定，由桥接取默认值。</summary>
    public float PortraitPivotX { get; set; }

    /// <summary>宣言立绘的垂直 pivot（贴图内归一化坐标）；与 PortraitPivotX 同为 0 表示未指定，由桥接取默认值。</summary>
    public float PortraitPivotY { get; set; }
}

public record struct BuffData
{
    public int Id { get; set; }

    public string? Name { get; set; }

    /// <summary>说明文本中的 $a、$b 等占位符由使用方在注册时替换。</summary>
    public string? Description { get; set; }

    /// <summary>图标，相对模组目录的路径。</summary>
    public string? Picture { get; set; }
}

public record struct SpawnMarkerData
{
    /// <summary>所属地图 label。</summary>
    public string? MapLabel { get; set; }

    /// <summary>点位名（原版同一地图内唯一）。</summary>
    public string? Label { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public CharacterRotationKind Rotation { get; set; }
}

public record struct DayMapData
{
    public int Id { get; set; }

    /// <summary>地图 label，也是 DataBaseDay 与地图语言表的键。</summary>
    public string? Label { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    /// <summary>所在区域的父地图 label；为空表示自身即区域根。</summary>
    public string? Parent { get; set; }

    /// <summary>
    /// 地图本体资源引用（相对模组目录的路径）。地图的引擎对象（Tilemap、相机、内存 GameObject）
    /// 不在数据面表达，仍由模组构建；此字段仅供需要地址引用的一方使用。
    /// </summary>
    public string? MapAsset { get; set; }

    /// <summary>默认刷新点名，须出现在 SpawnMarkers 中。</summary>
    public string? DefaultSpawnMarker { get; set; }

    /// <summary>地图内采集点 label。</summary>
    public string[]? Collectables { get; set; }

    public SpawnMarkerData[]? SpawnMarkers { get; set; }

    public int[]? Level1IzakayaIds { get; set; }

    public int[]? Level2IzakayaIds { get; set; }

    public int[]? Level3IzakayaIds { get; set; }
}

/// <summary>节点奖励。字段面与资源包 rewards/postRewards 一致。</summary>
public record struct SchedulerRewardData
{
    /// <summary>游戏内 Reward.RewardType。</summary>
    public int RewardType { get; set; }

    /// <summary>目标标识（如稀客 stringId）。</summary>
    public string? RewardId { get; set; }

    /// <summary>游戏内 Reward.ObjectType；仅 GiveItem 等按物品发放的奖励使用。</summary>
    public int? ObjectType { get; set; }

    /// <summary>数值载荷（如 GiveItem 的物品 id 列表）。</summary>
    public int[]? RewardIntArray { get; set; }
}

/// <summary>节点触发日。字段面与资源包 TriggerConfig 的时间部分一致。</summary>
public record struct SchedulerDayData
{
    /// <summary>游戏内 SchedulerNode.Day.DayType（Relative/Absolute）。</summary>
    public int DayType { get; set; }

    /// <summary>游戏内 SchedulerNode.Day.CalculateType（Constant/Random）。</summary>
    public int CalcType { get; set; }

    /// <summary>Constant 时的天数。</summary>
    public int Day { get; set; }

    /// <summary>Random 时的天数下界。</summary>
    public int DayRangeMin { get; set; }

    /// <summary>Random 时的天数上界。</summary>
    public int DayRangeMax { get; set; }
}

/// <summary>节点触发条件。字段面与资源包 TriggerConfig 一致。</summary>
public record struct SchedulerTriggerData
{
    /// <summary>游戏内 SchedulerNode.Trigger.TriggerType。</summary>
    public int TriggerType { get; set; }

    /// <summary>触发标识（角色标识、区域 label 等，随 TriggerType 变化）。</summary>
    public string? TriggerId { get; set; }

    public SchedulerDayData? Time { get; set; }
}

/// <summary>
/// 节点播放的事件。数据面只承载对话包名；Timeline 等引擎对象仍由模组自行提供，不在此表达。
/// </summary>
public record struct SchedulerEventData
{
    /// <summary>游戏内 SchedulerNode.Event.EventType（Null/Timeline/Dialog）。</summary>
    public int EventType { get; set; }

    /// <summary>目标对话包名（DialogData.Name）。</summary>
    public string? DialogPackage { get; set; }
}

/// <summary>任务完成条件。字段面与资源包 finishConditions 一致。</summary>
public record struct MissionFinishConditionData
{
    /// <summary>游戏内 MissionNode.FinishCondition.ConditionType。</summary>
    public int ConditionType { get; set; }

    public int? Amount { get; set; }

    public int? Tag { get; set; }

    public int[]? Tags { get; set; }

    /// <summary>游戏内 Sellable.SellableType。</summary>
    public int? SellableType { get; set; }

    /// <summary>条件目标标识（角色标识等）。</summary>
    public string? Label { get; set; }

    /// <summary>游戏内 Product.ProductType。</summary>
    public int? ProductType { get; set; }

    public int? ProductId { get; set; }

    public int? ProductAmount { get; set; }
}

public record struct MissionNodeData
{
    /// <summary>节点 label，也是调度器表与图连接的键。</summary>
    public string? Label { get; set; }

    public string? DebugLabel { get; set; }

    /// <summary>任务名（写入 DataBaseLanguage.Missions 的 Name）。</summary>
    public string? Name { get; set; }

    /// <summary>任务说明（写入 DataBaseLanguage.Missions 的 Description）。</summary>
    public string? Description { get; set; }

    /// <summary>游戏内 SchedulerNode.SchedulerType（Main/Side/Kitsuna）。</summary>
    public int MissionType { get; set; }

    /// <summary>任务发起人（角色标识）。</summary>
    public string? Sender { get; set; }

    /// <summary>任务接收人（角色标识；游戏侧字段拼写为 reciever）。</summary>
    public string? Receiver { get; set; }

    public SchedulerRewardData[]? Rewards { get; set; }

    public SchedulerRewardData[]? PostRewards { get; set; }

    public MissionFinishConditionData[]? FinishConditions { get; set; }

    public SchedulerEventData? MissionFinishEvent { get; set; }

    public SchedulerEventData? MissionFailedEvent { get; set; }

    public SchedulerTriggerData? MissionTimeLimit { get; set; }

    public bool IsTimedMission { get; set; }

    /// <summary>游戏内 MissionNode.MissionFailedAction（BackToMainMenu/Rewind/None）。</summary>
    public int MissionFailedAction { get; set; }

    /// <summary>图连接：本节点的前置节点 label。</summary>
    public string[]? PreNodes { get; set; }

    /// <summary>图连接：后置任务节点 label。</summary>
    public string[]? PostMissions { get; set; }

    /// <summary>图连接：演出完成后的后置任务节点 label。</summary>
    public string[]? PostMissionsAfterPerformance { get; set; }

    /// <summary>图连接：后置事件节点 label。</summary>
    public string[]? PostEvents { get; set; }
}

public record struct EventNodeData
{
    /// <summary>节点 label，也是调度器表与图连接的键。</summary>
    public string? Label { get; set; }

    public string? DebugLabel { get; set; }

    /// <summary>事件名。原版事件节点没有独立语言表项，此字段仅为数据面对称保留。</summary>
    public string? Name { get; set; }

    /// <summary>事件说明。原版事件节点没有独立语言表项，此字段仅为数据面对称保留。</summary>
    public string? Description { get; set; }

    /// <summary>触发条件与播放内容。</summary>
    public SchedulerEventData? ScheduledEvent { get; set; }

    /// <summary>触发条件；与 ScheduledEvent.EventType 配对使用。</summary>
    public SchedulerTriggerData? Trigger { get; set; }

    public SchedulerRewardData[]? Rewards { get; set; }

    public SchedulerRewardData[]? PostRewards { get; set; }

    /// <summary>图连接：本节点的前置节点 label。</summary>
    public string[]? PreNodes { get; set; }

    /// <summary>图连接：后置任务节点 label。</summary>
    public string[]? PostMissions { get; set; }

    /// <summary>图连接：演出完成后的后置任务节点 label。</summary>
    public string[]? PostMissionsAfterPerformance { get; set; }

    /// <summary>图连接：后置事件节点 label。</summary>
    public string[]? PostEvents { get; set; }
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

    void OnInjectClothes(List<ClothesData> clothes) { }

    void OnInjectSpells(List<SpellData> spells) { }

    void OnInjectBuffs(List<BuffData> buffs) { }

    void OnInjectMissionNodes(List<MissionNodeData> nodes) { }

    void OnInjectEventNodes(List<EventNodeData> nodes) { }

    void OnInjectDayMaps(List<DayMapData> maps) { }
}
