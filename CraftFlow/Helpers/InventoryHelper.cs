using System;
using System.Collections.Generic;
using System.Linq;
using CraftFlow.Data.Models;

namespace CraftFlow.Helpers;

/// <summary>
/// 背包物品数量查询辅助类。
/// 使用 FFXIVClientStructs 读取游戏内存中的背包数据。
/// </summary>
public static class InventoryHelper
{
    /// <summary>
    /// 获取指定物品在背包中的总数量（不含装备栏），HQ+NQ 合计。
    /// GetInventoryItemCount(id, false) 在某些 Dalamud 版本中只计 NQ，
    /// 因此分开查询 NQ 和 HQ 后相加。
    /// </summary>
    public static unsafe int GetItemCount(uint itemId)
    {
        try
        {
            var inventory = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
            if (inventory == null) return 0;
            return (int)inventory->GetInventoryItemCount(itemId, false, false, false)
                 + (int)inventory->GetInventoryItemCount(itemId, true, false, false);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// 获取指定物品在背包中的数量，可指定是否只计 HQ。
    /// </summary>
    /// <param name="itemId">物品 ID。</param>
    /// <param name="isHq">true 只计 HQ；false 计全部（HQ+NQ，分别查询后相加）。</param>
    public static unsafe int GetItemCount(uint itemId, bool isHq)
    {
        try
        {
            var inventory = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
            if (inventory == null) return 0;
            if (isHq)
                return (int)inventory->GetInventoryItemCount(itemId, true, false, false);
            else
                return (int)inventory->GetInventoryItemCount(itemId, false, false, false)
                     + (int)inventory->GetInventoryItemCount(itemId, true, false, false);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// 为材料列表添加背包数量信息（总数，HQ+NQ）。
    /// 返回每个材料条目对应的 (背包拥有量, 差额, 是否齐全)。
    /// </summary>
    public static List<(MaterialEntry Entry, int Owned, int Deficit, bool Complete)> CheckInventory(
        List<MaterialEntry> materials)
    {
        return CheckInventory(materials, false);
    }

    /// <summary>
    /// 为材料列表添加背包数量信息，可指定是否只计 HQ。
    /// </summary>
    /// <param name="materials">材料列表。</param>
    /// <param name="isHq">true: 只计 HQ；false: 计 HQ+NQ。</param>
    public static List<(MaterialEntry Entry, int Owned, int Deficit, bool Complete)> CheckInventory(
        List<MaterialEntry> materials, bool isHq)
    {
        return materials.Select(m =>
        {
            var owned = GetItemCount(m.ItemId, isHq);
            var deficit = owned >= 0 ? Math.Max(0, m.TotalRequired - owned) : m.TotalRequired;
            var complete = owned >= m.TotalRequired;
            return (m, owned, deficit, complete);
        }).ToList();
    }

    /// <summary>
    /// 基于 BOM 树计算有效材料需求，考虑背包中已有半成品（中间产品）。
    /// 遍历 BOM 树时，如果某个非叶节点（半成品）在背包中已有库存，
    /// 则按比例减少其下级材料的需求量。
    /// 跨分支共享的中间产物库存会被追踪消费，避免不同分支重复扣减同一批库存。
    /// </summary>
    /// <param name="root">BOM 树根节点。</param>
    /// <param name="hqOnly">是否只计 HQ 物品为已有。</param>
    /// <returns>以 ItemId 为键的有效需求量字典。</returns>
    public static Dictionary<uint, int> CalculateEffectiveNeeds(BomNode root, bool hqOnly)
    {
        var needs = new Dictionary<uint, int>();
        var inventoryConsumed = new Dictionary<uint, int>();
        WalkForEffectiveNeeds(root, needs, hqOnly, 1.0, inventoryConsumed);
        return needs;
    }

    /// <summary>
    /// 递归遍历 BOM 树计算有效需求。
    /// 对非叶节点（半成品），检查背包已有量并按比例缩减下级需求。
    /// inventoryConsumed 跨分支追踪已消费的中间产物库存，防止组合 BOM 树中
    /// 不同分支的同一中间产物重复扣减同一批库存。
    /// </summary>
    private static void WalkForEffectiveNeeds(BomNode node, Dictionary<uint, int> needs,
        bool hqOnly, double scale, Dictionary<uint, int> inventoryConsumed)
    {
        // 根节点（ItemId == 0）：直接递归子节点
        if (node.ItemId == 0)
        {
            foreach (var child in node.Children)
                WalkForEffectiveNeeds(child, needs, hqOnly, 1.0, inventoryConsumed);
            return;
        }

        // 成品/顶层节点（Depth == 0）：最终产物，不扣成品背包库存，直接递归子节点
        // 只有中间产物（半成品）的库存才应该抵扣下级材料需求。
        if (node.Depth == 0)
        {
            foreach (var child in node.Children)
                WalkForEffectiveNeeds(child, needs, hqOnly, 1.0, inventoryConsumed);
            return;
        }

        // 叶节点：原材料，累积到有效需求
        if (node.IsLeaf)
        {
            int qty = Math.Max(1, (int)Math.Ceiling(node.Quantity * scale));
            if (needs.TryGetValue(node.ItemId, out int existing))
                needs[node.ItemId] = existing + qty;
            else
                needs[node.ItemId] = qty;
            return;
        }

        // 非叶节点（Depth > 0）：半成品（中间产品），检查背包已有量（扣除前序分支已消费量）
        int totalNeeded = Math.Max(1, (int)Math.Ceiling(node.Quantity * scale));
        int owned = GetItemCount(node.ItemId, hqOnly);

        // 扣除其他分支已消费的库存，防止共享中间产物被重复扣减
        if (owned > 0 && inventoryConsumed.TryGetValue(node.ItemId, out int alreadyConsumed))
        {
            owned = Math.Max(0, owned - alreadyConsumed);
        }

        int stillNeeded = owned < 0 ? totalNeeded : Math.Max(0, totalNeeded - owned);

        // 记录本次消费量（含已由前序分支消费量）
        int consumedThisBranch = totalNeeded - stillNeeded;
        if (consumedThisBranch > 0)
        {
            if (inventoryConsumed.TryGetValue(node.ItemId, out int prev))
                inventoryConsumed[node.ItemId] = prev + consumedThisBranch;
            else
                inventoryConsumed[node.ItemId] = consumedThisBranch;
        }

        if (stillNeeded == 0)
            return; // 已有量（扣除已消费后）充足，跳过整棵子树

        // 按缩减比例递归处理下级材料
        double childScale = scale * ((double)stillNeeded / totalNeeded);
        foreach (var child in node.Children)
            WalkForEffectiveNeeds(child, needs, hqOnly, childScale, inventoryConsumed);
    }

    /// <summary>
    /// 计算采集材料的差额列表（用于 GBR 推送）。
    /// 结合 BOM 树半成品库存计算有效需求，再减去叶节点背包已有量，
    /// 仅返回差额 > 0 的采集类材料。
    /// </summary>
    /// <param name="root">BOM 树根节点。</param>
    /// <param name="materials">聚合后的材料清单（用于获取 Source 信息）。</param>
    /// <param name="hqOnly">是否只计 HQ 物品为已有。</param>
    /// <returns>以 ItemId 为键的差额字典（仅采集材料、差额 > 0）。</returns>
    public static Dictionary<uint, int> CalculateGatherableDeficit(
        BomNode root, List<MaterialEntry> materials, bool hqOnly)
    {
        var effectiveNeeds = CalculateEffectiveNeeds(root, hqOnly);
        var result = new Dictionary<uint, int>();

        foreach (var mat in materials)
        {
            if (mat.Source != MaterialSource.Gatherable) continue;
            if (!effectiveNeeds.TryGetValue(mat.ItemId, out int need)) continue;

            int owned = GetItemCount(mat.ItemId, hqOnly);
            int deficit = owned < 0 ? need : Math.Max(0, need - owned);
            if (deficit > 0)
                result[mat.ItemId] = deficit;
        }

        return result;
    }
}
