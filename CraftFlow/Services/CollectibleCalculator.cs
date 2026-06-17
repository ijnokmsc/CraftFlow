using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using CraftFlow.Data.GameData;
using CraftFlow.Data.Models;

namespace CraftFlow.Services;

/// <summary>
/// 工票计算服务，根据收藏品评分档位计算制作次数和材料需求。
/// </summary>
public sealed class CollectibleCalculator
{
    private readonly RecipeRepository _recipeRepo;
    private readonly BomExpander _bomExpander;
    private readonly IPluginLog _log;

    /// <summary>
    /// 初始化 CollectibleCalculator 实例。
    /// </summary>
    /// <param name="recipeRepo">配方查询仓库。</param>
    /// <param name="bomExpander">BOM 展开器，用于递归展开收藏品配方的完整材料树。</param>
    /// <param name="log">插件日志。</param>
    public CollectibleCalculator(RecipeRepository recipeRepo, BomExpander bomExpander, IPluginLog log)
    {
        _recipeRepo = recipeRepo;
        _bomExpander = bomExpander;
        _log = log;
    }

    /// <summary>
    /// 计算达到目标票数所需的制作次数。
    /// 根据目标评分档位确定单次票数产出，然后计算总制作次数。
    /// </summary>
    /// <param name="info">收藏品信息（含评分档位）。</param>
    /// <param name="targetScrips">目标票数。</param>
    /// <param name="targetScore">目标评分（用于确定票数档位）。</param>
    /// <returns>需要制作的次数（向上取整）。</returns>
    public int CalculateCraftCount(CollectibleInfo info, int targetScrips, int targetScore)
    {
        var tier = FindBestTier(info, targetScore);
        if (tier is null || tier.ScripReward <= 0)
        {
            _log.Warning($"收藏品 {info.ItemName} 未找到评分 {targetScore} 对应的票数档位");
            return 0;
        }

        int craftCount = (int)Math.Ceiling((double)targetScrips / tier.ScripReward);
        _log.Debug($"CollectibleCalculator: 目标 {targetScrips} 票，单次 {tier.ScripReward} 票，需制作 {craftCount} 次");
        return craftCount;
    }

    /// <summary>
    /// 根据收藏品和制作次数计算所需材料。
    /// 使用 BomExpander 递归展开收藏品配方的完整 BOM 树，按制作次数计算材料倍率。
    /// 与 EquipmentTab/ConsumableTab 一致：中间产品被继续展开，最终只汇总叶节点（原材料）。
    /// </summary>
    /// <param name="info">收藏品信息。</param>
    /// <param name="craftCount">制作次数。</param>
    /// <returns>材料汇总列表（含倍率）。</returns>
    public List<MaterialEntry> CalculateMaterials(CollectibleInfo info, int craftCount)
    {
        if (craftCount <= 0)
        {
            return [];
        }

        // 查找收藏品配方
        var recipe = _recipeRepo.FindRecipeByItem(info.ItemId);
        if (!recipe.HasValue)
        {
            _log.Warning($"收藏品 {info.ItemName} 无对应配方");
            return [];
        }

        // 计算总产出量：制作次数 × 配方单次产量
        int yield = recipe.Value.AmountResult > 0 ? recipe.Value.AmountResult : 1;
        int totalOutput = craftCount * yield;

        // 使用 BomExpander 递归展开完整 BOM 树（含中间产品的子配方）
        var bomRoot = _bomExpander.Expand(info.ItemId, totalOutput);

        // 遍历 BOM 树叶节点，按 ItemId 聚合材料
        var materials = new Dictionary<uint, MaterialEntry>();
        WalkLeaves(bomRoot, materials);

        var result = new List<MaterialEntry>(materials.Values);
        result.Sort((a, b) => string.Compare(a.ItemName, b.ItemName, StringComparison.Ordinal));

        _log.Debug($"CollectibleCalculator: 计算 {craftCount} 次制作，需要 {result.Count} 种材料");
        return result;
    }

    /// <summary>
    /// 深度优先遍历 BOM 树的叶节点，按 ItemId 聚合材料数量。
    /// 与 MaterialAggregator.WalkLeaves 逻辑一致，但不做水晶过滤。
    /// </summary>
    private void WalkLeaves(BomNode node, Dictionary<uint, MaterialEntry> result)
    {
        if (node.IsLeaf || node.Children.Count == 0)
        {
            if (result.TryGetValue(node.ItemId, out var existing))
            {
                existing.TotalRequired += node.Quantity;
            }
            else
            {
                result[node.ItemId] = new MaterialEntry
                {
                    ItemId = node.ItemId,
                    ItemName = node.ItemName,
                    TotalRequired = node.Quantity,
                    Source = _recipeRepo.GetMaterialSource(node.ItemId),
                    IsHqRequired = false
                };
            }

            return;
        }

        // 非叶节点：递归遍历子节点（中间产品继续展开）
        foreach (var child in node.Children)
        {
            WalkLeaves(child, result);
        }
    }

    /// <summary>
    /// 根据目标评分找到最佳评分档位。
    /// 返回不超过目标评分的最高档位。
    /// </summary>
    /// <param name="info">收藏品信息。</param>
    /// <param name="targetScore">目标评分。</param>
    /// <returns>匹配的评分档位，或 null。</returns>
    public ScoreTier? FindBestTier(CollectibleInfo info, int targetScore)
    {
        ScoreTier? bestTier = null;

        foreach (var tier in info.ScoreThresholds)
        {
            if (tier.MinScore <= targetScore)
            {
                if (bestTier is null || tier.MinScore > bestTier.MinScore)
                {
                    bestTier = tier;
                }
            }
        }

        // 若目标评分低于所有档位，取最低档位
        if (bestTier is null && info.ScoreThresholds.Count > 0)
        {
            bestTier = info.ScoreThresholds[0];
        }

        return bestTier;
    }
}
