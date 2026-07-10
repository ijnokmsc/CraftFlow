namespace CraftFlow.Data.Models;

/// <summary>
/// 收藏品信息，包含评分区间与票数奖励映射。
/// </summary>
public sealed class CollectibleInfo
{
    /// <summary>
    /// 关联配方的 RowId。
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
    /// 工票类型（橙票/紫票）。
    /// </summary>
    public ScripType ScripType { get; set; }

    /// <summary>
    /// 评分区间列表，每个区间包含最低评分和对应票数奖励。
    /// </summary>
    public List<ScoreTier> ScoreThresholds { get; set; } = [];

    /// <summary>
    /// 收藏品等级（结果物品的 LevelItem），用于 91-100/81-90 评分区间分组。
    /// </summary>
    public int CollectableLevel { get; set; }

    /// <summary>
    /// 制作职业 ID（CraftType RowId），用于按职业分类。
    /// </summary>
    public uint CraftTypeId { get; set; }

    /// <summary>
    /// 制作职业名称（中文，如 木工/锻冶），用于分组标题显示。
    /// </summary>
    public string CraftTypeName { get; set; } = string.Empty;
}

/// <summary>
/// 评分档位，表示一个评分区间及对应的工票奖励。
/// </summary>
public sealed class ScoreTier
{
    /// <summary>
    /// 该档位的最低评分。
    /// </summary>
    public int MinScore { get; set; }

    /// <summary>
    /// 达到此评分时获得的工票数量。
    /// </summary>
    public int ScripReward { get; set; }
}
