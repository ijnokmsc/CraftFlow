namespace CraftFlow.Data.Models;

/// <summary>
/// 装备项数据模型，表示一件可制作的装备。
/// 包含物品 ID、名称、槽位类型、装等、HQ 标记和数量。
/// </summary>
public sealed class EquipmentItem
{
    /// <summary>
    /// 物品 ID。
    /// </summary>
    public uint ItemId { get; set; }

    /// <summary>
    /// 物品名称。
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// 装备槽位类型。
    /// </summary>
    public EquipmentSlotType SlotType { get; set; }

    /// <summary>
    /// 装备等级（ItemLevel）。
    /// </summary>
    public int ItemLevel { get; set; }

    /// <summary>
    /// 是否为高品质（HQ）版本。
    /// </summary>
    public bool IsHq { get; set; }

    /// <summary>
    /// 是否为双手武器（主手与副手槽同时占用，无独立副手）。
    /// 由 EquipSlotCategory 的 MainHand/OffHand 字段判定：两者均非零即双手武器。
    /// </summary>
    public bool IsTwoHanded { get; set; }

    /// <summary>
    /// 选择数量。
    /// </summary>
    public int Quantity { get; set; } = 1;
}
