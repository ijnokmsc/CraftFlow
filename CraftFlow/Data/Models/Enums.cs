namespace CraftFlow.Data.Models;

/// <summary>
/// 制作目标类型。
/// </summary>
public enum TargetType
{
    /// <summary>装备武器</summary>
    Equipment,

    /// <summary>食物药品</summary>
    Consumable,

    /// <summary>工票收藏品</summary>
    Collectible
}

/// <summary>
/// 材料来源类型。
/// </summary>
public enum MaterialSource
{
    /// <summary>可采集（矿/木/渔等）</summary>
    Gatherable,

    /// <summary>商店购买</summary>
    Purchasable,

    /// <summary>需要制作</summary>
    Craftable,

    /// <summary>怪物/副本掉落</summary>
    Drop,

    /// <summary>未知来源</summary>
    Unknown
}

/// <summary>
/// 制作步骤状态。
/// </summary>
public enum StepStatus
{
    /// <summary>等待中</summary>
    Pending,

    /// <summary>采集进行中</summary>
    Gathering,

    /// <summary>材料齐全，可制作</summary>
    ReadyToCraft,

    /// <summary>制作进行中</summary>
    Crafting,

    /// <summary>已完成</summary>
    Completed,

    /// <summary>失败</summary>
    Failed
}

/// <summary>
/// 工票类型。
/// </summary>
public enum ScripType
{
    /// <summary>巧手紫票</summary>
    PurpleScrip,

    /// <summary>巧手橙票</summary>
    OrangeScrip
}

/// <summary>
/// 主窗口 Tab 类型。
/// </summary>
public enum TabType
{
    /// <summary>装备武器 Tab</summary>
    Equipment,

    /// <summary>食物药品 Tab</summary>
    Consumable,

    /// <summary>手动收藏 Tab</summary>
    Favorites,

    /// <summary>推荐套装 Tab</summary>
    Recommendations,

    /// <summary>收藏品制作 Tab</summary>
    Collectables
}

/// <summary>
/// 消耗品类别。
/// </summary>
public enum ConsumableCategory
{
    /// <summary>食物</summary>
    Food,

    /// <summary>药品（Tincture 等）</summary>
    Medicine
}

/// <summary>
/// 装备槽位类型，对应 Item.EquipSlotCategory 的布尔字段。
/// </summary>
public enum EquipmentSlotType
{
    /// <summary>主手</summary>
    MainHand,

    /// <summary>副手</summary>
    OffHand,

    /// <summary>头部</summary>
    Head,

    /// <summary>身体</summary>
    Body,

    /// <summary>手部</summary>
    Hands,

    /// <summary>腿部</summary>
    Legs,

    /// <summary>足部</summary>
    Feet,

    /// <summary>耳饰</summary>
    Ears,

    /// <summary>颈饰</summary>
    Neck,

    /// <summary>腕饰</summary>
    Wrists,

    /// <summary>指环</summary>
    Fingers
}

/// <summary>
/// 一键添加的槽位范围类型。
/// </summary>
public enum AddSlotType
{
    /// <summary>仅主副手武器</summary>
    WeaponOnly,

    /// <summary>仅防具（头/身/手/腿/足）</summary>
    ArmorOnly,

    /// <summary>仅首饰（耳/颈/腕/指）</summary>
    AccessoryOnly,

    /// <summary>防具+首饰</summary>
    ArmorAndAccessory,

    /// <summary>整套（武器+防具+首饰）</summary>
    FullSet
}

/// <summary>
/// 装备槽位分组，用于 UI 配对展示。
/// </summary>
public record EquipmentSlotGroup(
    string DisplayName,
    EquipmentSlotType[] Slots
);

/// <summary>
/// 装备槽位分组静态定义。
/// </summary>
public static class EquipmentSlotGroups
{
    /// <summary>
    /// 槽位分组列表，用于 UI 配对显示。
    /// </summary>
    public static readonly EquipmentSlotGroup[] SlotGroups =
    [
        new("主手/副手", [EquipmentSlotType.MainHand, EquipmentSlotType.OffHand]),
        new("头/耳", [EquipmentSlotType.Head, EquipmentSlotType.Ears]),
        new("身/颈", [EquipmentSlotType.Body, EquipmentSlotType.Neck]),
        new("手/腕", [EquipmentSlotType.Hands, EquipmentSlotType.Wrists]),
        new("腿/指", [EquipmentSlotType.Legs, EquipmentSlotType.Fingers]),
        new("足", [EquipmentSlotType.Feet]),
    ];

    /// <summary>
    /// 装备大类分类定义：主副武器 / 防具 / 首饰。
    /// 每个大类包含一组槽位分组，用于 UI 分类展示。
    /// </summary>
    public static readonly (string Name, EquipmentSlotGroup[] Groups)[] EquipmentCategories =
    [
        ("主副武器", [
            new EquipmentSlotGroup("主手", [EquipmentSlotType.MainHand]),
            new EquipmentSlotGroup("副手", [EquipmentSlotType.OffHand]),
        ]),
        ("防具", [
            new EquipmentSlotGroup("头部", [EquipmentSlotType.Head]),
            new EquipmentSlotGroup("身体", [EquipmentSlotType.Body]),
            new EquipmentSlotGroup("手部", [EquipmentSlotType.Hands]),
            new EquipmentSlotGroup("腿部", [EquipmentSlotType.Legs]),
            new EquipmentSlotGroup("足部", [EquipmentSlotType.Feet]),
        ]),
        ("首饰", [
            new EquipmentSlotGroup("耳饰", [EquipmentSlotType.Ears]),
            new EquipmentSlotGroup("颈饰", [EquipmentSlotType.Neck]),
            new EquipmentSlotGroup("腕饰", [EquipmentSlotType.Wrists]),
            new EquipmentSlotGroup("指环", [EquipmentSlotType.Fingers]),
        ]),
    ];
}
