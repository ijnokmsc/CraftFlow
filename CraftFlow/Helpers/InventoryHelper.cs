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
    /// 获取指定物品在背包中的总数量（不含装备栏）。
    /// </summary>
    public static unsafe int GetItemCount(uint itemId)
    {
        try
        {
            var inventory = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
            if (inventory == null) return 0;
            return (int)inventory->GetInventoryItemCount(itemId, false, false, false);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// 为材料列表添加背包数量信息。
    /// 返回每个材料条目对应的 (背包拥有量, 差额, 是否齐全)。
    /// </summary>
    public static List<(MaterialEntry Entry, int Owned, int Deficit, bool Complete)> CheckInventory(
        List<MaterialEntry> materials)
    {
        return materials.Select(m =>
        {
            var owned = GetItemCount(m.ItemId);
            var deficit = owned >= 0 ? Math.Max(0, m.TotalRequired - owned) : m.TotalRequired;
            var complete = owned >= m.TotalRequired;
            return (m, owned, deficit, complete);
        }).ToList();
    }
}
