namespace CraftFlow.Data.Models;

/// <summary>
/// 制作目标，表示用户选择的一个待制作物品及其数量。
/// </summary>
public sealed class CraftTarget
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
    /// 制作数量。
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// 目标类型（装备/消耗品/收藏品）。
    /// </summary>
    public TargetType Type { get; set; }
}
