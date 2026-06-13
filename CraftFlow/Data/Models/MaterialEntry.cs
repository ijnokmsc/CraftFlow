namespace CraftFlow.Data.Models;

/// <summary>
/// 材料汇总条目，表示 BOM 展开后聚合的单一材料。
/// 同一 ItemId 的材料会被合并，总需求量累加。
/// </summary>
public sealed class MaterialEntry
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
    /// 总需求数量（所有层级合计）。
    /// </summary>
    public int TotalRequired { get; set; }

    /// <summary>
    /// 材料来源类型。
    /// </summary>
    public MaterialSource Source { get; set; } = MaterialSource.Unknown;

    /// <summary>
    /// 是否需要高品质（HQ）材料。
    /// </summary>
    public bool IsHqRequired { get; set; }
}
