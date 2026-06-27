namespace CraftFlow.Data.Models;

/// <summary>
/// BOM 树节点，表示配方展开后的一个材料层级。
/// 递归结构：每个节点可包含子节点（下级材料），叶节点表示原材料。
/// </summary>
public sealed class BomNode
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
    /// 需求数量（含上级倍率）。
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// 在 BOM 树中的深度层级（0 = 根节点）。
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// 对应配方的 RowId，null 表示无配方（原材料/叶节点）。
    /// </summary>
    public uint? RecipeId { get; set; }

    /// <summary>
    /// 子节点列表（下级材料）。
    /// </summary>
    public List<BomNode> Children { get; set; } = [];

    /// <summary>
    /// 是否为叶节点（无子节点的原材料）。
    /// </summary>
    public bool IsLeaf => Children.Count == 0;

    /// <summary>
    /// 配方产出数量（AmountResult）。用于判断背包已有是否可跳过制作。
    /// 0 或无配方时为默认值。
    /// </summary>
    public int AmountResult { get; set; }

    /// <summary>
    /// 是否因循环引用或深度超限而标记为不完整。
    /// </summary>
    public bool IsIncomplete { get; set; }
}
