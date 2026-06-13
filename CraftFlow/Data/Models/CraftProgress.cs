using System;
using System.Collections.Generic;

namespace CraftFlow.Data.Models;

/// <summary>
/// 制作进度，记录一键 Artisan 制作的当前状态。
/// 持久化到 PluginConfig，支持游戏崩溃后恢复。
/// </summary>
public sealed class CraftProgress
{
    /// <summary>进度版本号。</summary>
    public int Version { get; set; } = 2;

    /// <summary>制作是否已完成。</summary>
    public bool IsComplete { get; set; }

    /// <summary>是否请求停止（下次空闲时停止）。</summary>
    public bool RequestedStop { get; set; }

    /// <summary>所有步骤。</summary>
    public List<CraftProgressStep> Steps { get; set; } = [];

    /// <summary>当前步骤索引。</summary>
    public int CurrentStepIndex { get; set; }

    /// <summary>总步骤数。</summary>
    public int TotalSteps { get; set; }

    /// <summary>已完成步骤数。</summary>
    public int CompletedSteps { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>最后更新时间。</summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.Now;

    /// <summary>当前制作的 RecipeId。</summary>
    public uint CurrentRecipeId { get; set; }

    /// <summary>是否正在制作中。</summary>
    public bool IsActive => !IsComplete && Steps.Count > 0 && CurrentStepIndex < Steps.Count && !RequestedStop;
}

/// <summary>
/// 单个制作步骤。
/// </summary>
public sealed class CraftProgressStep
{
    /// <summary>在列表中的索引。</summary>
    public int Index { get; set; }

    /// <summary>配方 ID。</summary>
    public uint RecipeId { get; set; }

    /// <summary>物品 ID。</summary>
    public uint ItemId { get; set; }

    /// <summary>物品名称。</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>需要数量。</summary>
    public int Quantity { get; set; }

    /// <summary>已完成数量。</summary>
    public int CompletedQuantity { get; set; }

    /// <summary>是否已完成。</summary>
    public bool IsDone => CompletedQuantity >= Quantity;
}
