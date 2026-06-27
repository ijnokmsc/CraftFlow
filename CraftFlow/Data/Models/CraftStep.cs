namespace CraftFlow.Data.Models;

/// <summary>
/// 制作步骤，表示拓扑排序后的一个制作任务。
/// 子材料步骤排在成品步骤之前。
/// </summary>
public sealed class CraftStep
{
    /// <summary>
    /// 配方 RowId。
    /// </summary>
    public uint RecipeId { get; set; }

    /// <summary>
    /// 产出物品 ID。
    /// </summary>
    public uint ItemId { get; set; }

    /// <summary>
    /// 产出物品名称。
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// 制作数量。
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// 单次制作的产出数量（AmountResult）。1次制作=AmountResult个物品。
    /// 用于判断背包已有量是否可跳过本次制作。
    /// </summary>
    public int AmountResult { get; set; }

    /// <summary>
    /// 拓扑排序顺序号（0 = 最先制作）。
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 当前步骤状态。
    /// </summary>
    public StepStatus Status { get; set; } = StepStatus.Pending;
}
