using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using CraftFlow.Data.GameData;
using CraftFlow.Data.Models;

namespace CraftFlow.Services;

/// <summary>
/// 材料汇总聚合服务，将 BOM 树的叶节点按 ItemId 合并。
/// 遍历 BOM 树所有叶节点，聚合相同材料的总需求量并标注来源类型。
/// 支持可选的水晶/晶簇过滤功能。
/// </summary>
public sealed class MaterialAggregator
{
    private readonly RecipeRepository _recipeRepo;
    private readonly LuminaCache _cache;
    private readonly IPluginLog _log;

    /// <summary>
    /// 水晶/晶簇的 ItemUICategory RowId。
    /// </summary>
    private const uint CrystalUICategoryId = 59;

    /// <summary>
    /// 已知水晶/晶簇的英文名称集合（精确匹配，用于无 LuminaCache 时的回退判断）。
    /// 使用精确集合匹配代替 Contains("crystal")，避免误杀 Crystal Glass / Crystal Ring 等含 crystal 的非晶簇物品。
    /// </summary>
    private static readonly HashSet<string> KnownCrystalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // 水晶（Crystal）
        "Wind Crystal", "Fire Crystal", "Ice Crystal", "Earth Crystal",
        "Lightning Crystal", "Water Crystal",
        // 晶簇（Cluster）
        "Wind Cluster", "Fire Cluster", "Ice Cluster", "Earth Cluster",
        "Lightning Cluster", "Water Cluster",
    };

    /// <summary>
    /// 初始化 MaterialAggregator 实例（无 LuminaCache）。
    /// 水晶/晶簇过滤仅依靠 KnownCrystalNames 集合精确匹配，无法使用 ItemUICategory 判断。
    /// </summary>
    /// <param name="recipeRepo">配方查询仓库，用于获取材料来源。</param>
    /// <param name="log">插件日志。</param>
    public MaterialAggregator(RecipeRepository recipeRepo, IPluginLog log)
    {
        _recipeRepo = recipeRepo;
        _cache = null!; // 兼容无 LuminaCache 场景，IsCrystalOrCluster 回退到 KnownCrystalNames 集合
        _log = log;
    }

    /// <summary>
    /// 初始化 MaterialAggregator 实例（含 LuminaCache，用于水晶过滤）。
    /// </summary>
    /// <param name="recipeRepo">配方查询仓库。</param>
    /// <param name="cache">Lumina 数据缓存。</param>
    /// <param name="log">插件日志。</param>
    public MaterialAggregator(RecipeRepository recipeRepo, LuminaCache cache, IPluginLog log)
    {
        _recipeRepo = recipeRepo;
        _cache = cache;
        _log = log;
    }

    /// <summary>
    /// 聚合 BOM 树的材料需求，输出扁平化材料清单。
    /// 仅汇总叶节点（原材料），非叶节点（中间产品）不包含在结果中。
    /// </summary>
    /// <param name="root">BOM 树根节点。</param>
    /// <param name="showCrystals">是否显示水晶/晶簇。默认 false（过滤）。</param>
    /// <returns>去重聚合后的材料条目列表。</returns>
    public List<MaterialEntry> Aggregate(BomNode root, bool showCrystals = false)
    {
        var result = new Dictionary<uint, MaterialEntry>();
        WalkLeaves(root, result, showCrystals);

        var list = result.Values.ToList();
        list.Sort((a, b) => a.Source != b.Source
            ? a.Source.CompareTo(b.Source)
            : string.Compare(a.ItemName, b.ItemName, StringComparison.Ordinal));

        _log.Debug($"MaterialAggregator: 聚合了 {list.Count} 种材料 (showCrystals={showCrystals})");
        return list;
    }

    /// <summary>
    /// 基于制作步骤聚合材料需求（单一事实来源，与 Artisan 实际消耗一致）。
    /// 遍历每个步骤的配方，仅统计"非可制作"的叶材料（原材料），
    /// 其消耗量 = 材料量 × 制作次数。可制作的中间产物由其自身的步骤负责统计，
    /// 因此每种叶材料只被统计一次（合并后的制作次数）。
    ///
    /// 与 <see cref="Aggregate(BomNode, bool)"/> 的区别：旧方法遍历 BomExpander 逐目标
    /// 独立展开的树，对共享中间产物在每个分支分别 ceil 再求和，导致装备套装等共享
    /// 材料链时多算（ceil 不可加）。本方法直接复用 CraftOrderCalculator 已经合并、
    /// 且换算为制作次数的步骤，保证清单 == 实际消耗。
    /// </summary>
    /// <param name="steps">拓扑排序、合并、已换算为制作次数的步骤列表。</param>
    /// <param name="showCrystals">是否显示水晶/晶簇。默认 false（过滤）。</param>
    /// <returns>去重聚合后的材料条目列表。</returns>
    public List<MaterialEntry> AggregateFromSteps(List<CraftStep> steps, bool showCrystals = false)
    {
        var result = new Dictionary<uint, MaterialEntry>();

        foreach (var step in steps)
        {
            var recipe = _recipeRepo.FindRecipeById(step.RecipeId);
            if (recipe is null) continue;

            int crafts = step.Quantity; // 已是制作次数 = ceil(物品数 / yield)
            for (int i = 0; i < 8; i++)
            {
                var ingId = recipe.Value.Ingredient[i].RowId;
                var ingAmt = recipe.Value.AmountIngredient[i];
                if (ingId == 0 || ingAmt == 0) continue;

                // 水晶/晶簇过滤
                if (!showCrystals && IsCrystalOrCluster(ingId, _recipeRepo.GetItemName(ingId)))
                    continue;

                // 仅统计叶材料（非可制作）；可制作中间产物由自身步骤统计，避免重复计数
                if (_recipeRepo.FindRecipeByItem(ingId).HasValue)
                    continue;

                int consumed = ingAmt * crafts;

                // 诊断：打印每个步骤对叶材料的消耗（定位"多余物品"来源）
                _log.Information($"[AggregateFromSteps] 步骤 {step.ItemName} 消耗叶材料 " +
                    $"{_recipeRepo.GetItemName(ingId)}×{consumed} (ItemId={ingId})");

                if (result.TryGetValue(ingId, out var existing))
                {
                    existing.TotalRequired += consumed;
                }
                else
                {
                    result[ingId] = new MaterialEntry
                    {
                        ItemId = ingId,
                        ItemName = _recipeRepo.GetItemName(ingId),
                        TotalRequired = consumed,
                        Source = _recipeRepo.GetMaterialSource(ingId),
                        IsHqRequired = false
                    };
                }
            }
        }

        var list = result.Values.ToList();
        list.Sort((a, b) => a.Source != b.Source
            ? a.Source.CompareTo(b.Source)
            : string.Compare(a.ItemName, b.ItemName, StringComparison.Ordinal));

        _log.Debug($"MaterialAggregator: 从步骤聚合了 {list.Count} 种材料 (showCrystals={showCrystals})");
        return list;
    }

    /// <summary>
    /// 深度优先遍历 BOM 树的叶节点，按 ItemId 聚合材料数量。
    /// </summary>
    /// <param name="node">当前遍历的节点。</param>
    /// <param name="result">聚合结果字典，以 ItemId 为键。</param>
    /// <param name="showCrystals">是否显示水晶/晶簇。</param>
    private void WalkLeaves(BomNode node, Dictionary<uint, MaterialEntry> result, bool showCrystals)
    {
        if (node.IsLeaf || node.Children.Count == 0)
        {
            // 水晶/晶簇过滤：如果不过滤且该材料是水晶，则跳过
            if (!showCrystals && IsCrystalOrCluster(node.ItemId, node.ItemName))
            {
                return;
            }

            // 诊断：打印每个叶节点的来源（父节点名称）
            _log.Information($"[MaterialAggregator] 叶节点: {node.ItemName}×{node.Quantity} (ItemId={node.ItemId}, Depth={node.Depth})");

            // 叶节点：原材料，聚合到结果中
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

        // 非叶节点：递归遍历子节点
        foreach (var child in node.Children)
        {
            WalkLeaves(child, result, showCrystals);
        }
    }

    /// <summary>
    /// 判断材料是否为水晶/晶簇。
    /// 优先按 ItemUICategory RowId==59 过滤，回退按 KnownCrystalNames 集合精确匹配。
    /// </summary>
    /// <param name="itemId">物品 ID。</param>
    /// <param name="itemName">物品名称。</param>
    /// <returns>是否为水晶/晶簇。</returns>
    private bool IsCrystalOrCluster(uint itemId, string itemName)
    {
        // 优先按 ItemUICategory RowId 判断
        if (_cache is not null && _cache.ItemSheet.TryGetValue(itemId, out var item))
        {
            if (item.ItemUICategory.IsValid && item.ItemUICategory.Value.RowId == CrystalUICategoryId)
            {
                return true;
            }
        }

        // 回退按已知名称集合精确匹配（避免 Contains("crystal") 误杀 Crystal Glass 等）
        if (!string.IsNullOrEmpty(itemName) && KnownCrystalNames.Contains(itemName))
        {
            return true;
        }

        return false;
    }
}
