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
    /// 初始化 MaterialAggregator 实例。
    /// </summary>
    /// <param name="recipeRepo">配方查询仓库，用于获取材料来源。</param>
    /// <param name="log">插件日志。</param>
    public MaterialAggregator(RecipeRepository recipeRepo, IPluginLog log)
    {
        _recipeRepo = recipeRepo;
        _cache = null!; // 兼容旧构造函数，LuminaCache 可通过 RecipeRepo 间接访问
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
    /// 优先按 ItemUICategory RowId==59 过滤，辅助按名称含"晶簇"/"Crystal"过滤。
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

        // 辅助按名称判断
        if (!string.IsNullOrEmpty(itemName))
        {
            var nameLower = itemName.ToLowerInvariant();
            if (nameLower.Contains("晶簇") || nameLower.Contains("crystal"))
            {
                return true;
            }
        }

        return false;
    }
}
