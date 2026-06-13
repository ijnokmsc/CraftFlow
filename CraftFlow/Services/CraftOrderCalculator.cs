using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using CraftFlow.Data.Models;

namespace CraftFlow.Services;

/// <summary>
/// 制作顺序计算服务，对 BOM 树进行拓扑排序。
/// 确保子材料步骤排在成品步骤之前，保证制作依赖关系正确。
/// </summary>
public sealed class CraftOrderCalculator
{
    private readonly IPluginLog _log;

    /// <summary>
    /// 初始化 CraftOrderCalculator 实例。
    /// </summary>
    /// <param name="log">插件日志。</param>
    public CraftOrderCalculator(IPluginLog log)
    {
        _log = log;
    }

    /// <summary>
    /// 根据 BOM 树计算拓扑有序的制作步骤列表。
    /// 叶节点（原材料）不生成制作步骤，仅非叶节点（需制作的中间产品/成品）生成步骤。
    /// </summary>
    /// <param name="root">BOM 树根节点。</param>
    /// <returns>按拓扑排序的制作步骤列表。</returns>
    public List<CraftStep> CalculateOrder(BomNode root)
    {
        var sorted = TopologicalSort(root);
        var steps = new List<CraftStep>();

        for (int i = 0; i < sorted.Count; i++)
        {
            var node = sorted[i];
            if (node.RecipeId.HasValue && !node.IsLeaf)
            {
                steps.Add(new CraftStep
                {
                    RecipeId = node.RecipeId.Value,
                    ItemId = node.ItemId,
                    ItemName = node.ItemName,
                    Quantity = node.Quantity,
                    Order = i,
                    Status = StepStatus.Pending
                });
            }
        }

        // 合并同 ItemId 的步骤：当共用中间产物出现在多个分支时，合并为一条步骤
        steps = MergeDuplicateSteps(steps);

        // 重新编号 Order，使序号连续
        for (int i = 0; i < steps.Count; i++)
        {
            steps[i].Order = i;
        }

        _log.Debug($"CraftOrderCalculator: 生成 {steps.Count} 个制作步骤");
        return steps;
    }

    /// <summary>
    /// 对 BOM 树进行后序遍历拓扑排序。
    /// 子节点排在父节点之前，确保先制作子材料。
    /// 注意：使用 BomNode 引用去重而非 ItemId，确保两件装备共用同一种
    /// 中间产物时，两个分支的数量都能保留，不会丢失。
    /// </summary>
    /// <param name="root">BOM 树根节点。</param>
    /// <returns>拓扑有序的节点列表。</returns>
    private List<BomNode> TopologicalSort(BomNode root)
    {
        var result = new List<BomNode>();
        var visited = new HashSet<BomNode>();

        TopologicalSortVisit(root, result, visited);
        return result;
    }

    /// <summary>
    /// 递归后序遍历访问节点。
    /// 使用 BomNode 引用去重而非 ItemId，原因：
    /// 合并 BOM 树中，两件装备可能共用同一种中间产物（相同 ItemId
    /// 但不同 BomNode 实例）。如果用 ItemId 去重，第二个分支的节点
    /// 会被跳过，其数量丢失，导致 CraftStep 量化错误。
    /// </summary>
    /// <param name="node">当前访问节点。</param>
    /// <param name="result">排序结果列表。</param>
    /// <param name="visited">已访问的 BomNode 引用集合。</param>
    private void TopologicalSortVisit(BomNode node, List<BomNode> result, HashSet<BomNode> visited)
    {
        if (!visited.Add(node))
        {
            return; // 同一个引用已访问过，跳过
        }

        // 先访问子节点（后序遍历）
        foreach (var child in node.Children)
        {
            TopologicalSortVisit(child, result, visited);
        }

        result.Add(node);
    }

    /// <summary>
    /// 合并同 ItemId 的制作步骤，将多次出现的同一中间产物合并为一条步骤，
    /// 数量求和，保留最早出现的 Order。
    /// 修复场景：两件装备共用同一种半成品时，两个分支各有一个步骤，
    /// 合并后 Artisan 一次制作全部数量，更高效且符合预期。
    /// </summary>
    /// <param name="steps">合并前的制作步骤列表。</param>
    /// <returns>合并后的制作步骤列表。</returns>
    private static List<CraftStep> MergeDuplicateSteps(List<CraftStep> steps)
    {
        if (steps.Count <= 1) return steps;

        var merged = new List<CraftStep>();
        // 按 RecipeId 分组（同一配方即同一物品）
        var groups = steps.GroupBy(s => s.RecipeId);

        foreach (var group in groups)
        {
            var list = group.ToList();
            if (list.Count == 1)
            {
                merged.Add(list[0]);
            }
            else
            {
                // 同一配方出现多次：数量求和，保留最早出现的 Order
                merged.Add(new CraftStep
                {
                    RecipeId = list[0].RecipeId,
                    ItemId = list[0].ItemId,
                    ItemName = list[0].ItemName,
                    Quantity = list.Sum(s => s.Quantity),
                    Order = list.Min(s => s.Order),
                    Status = StepStatus.Pending
                });
            }
        }

        // 按 Order 重新排序
        merged.Sort((a, b) => a.Order.CompareTo(b.Order));
        return merged;
    }
}
