using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using CraftFlow.Data.Models;
using CraftFlow.Helpers;

namespace CraftFlow.UI.Widgets;

/// <summary>
/// 装备槽位分组展示组件。
/// 按槽位分组展示装备列表，每个槽位带有 HQHelper 风格图标。
/// </summary>
public sealed class EquipmentSlotGroupWidget
{
    private readonly EquipmentSlotIcon _slotIcon;
    private readonly IPluginLog _log;

    /// <summary>
    /// 初始化 EquipmentSlotGroupWidget 实例。
    /// </summary>
    /// <param name="log">插件日志。</param>
    public EquipmentSlotGroupWidget(IPluginLog log)
    {
        _slotIcon = new EquipmentSlotIcon();
        _log = log;
    }

    /// <summary>
    /// 绘制槽位分组的装备列表。
    /// </summary>
    /// <param name="group">槽位分组定义。</param>
    /// <param name="groupedEquipment">按槽位分组的装备字典。</param>
    /// <param name="selectedItems">已选中的制作目标列表（可修改）。</param>
    /// <param name="onSelectionChanged">选中状态变化后的回调（如触发 RecalculateBom）。</param>
    public void Draw(EquipmentSlotGroup group, Dictionary<EquipmentSlotType, List<EquipmentItem>> groupedEquipment, List<CraftTarget> selectedItems, Action? onSelectionChanged = null)
    {
        // 配对展示各槽位的装备
        for (int i = 0; i < group.Slots.Length; i++)
        {
            var slot = group.Slots[i];
            if (!groupedEquipment.TryGetValue(slot, out var items) || items.Count == 0)
            {
                // 该槽位无装备，用灰色图标 + 文字显示空位
                _slotIcon.DrawIconOnly(slot);
                ImGui.SameLine();
                ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1f), $"{GetSlotDisplayName(slot)}: 无可制作装备");
                if (i < group.Slots.Length - 1) ImGui.SameLine(30);
                continue;
            }

            // 图标 + 槽位名称
            _slotIcon.DrawIconOnly(slot);
            ImGui.SameLine();
            string slotLabel = GetSlotDisplayName(slot);
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.85f, 1.0f, 1.0f), $" {slotLabel}:");

            // 显示该槽位的装备列表
            foreach (var item in items)
            {
                ImGui.PushID($"Equip_{item.ItemId}");

                bool isSelected = selectedItems.Any(t => t.ItemId == item.ItemId);

                // 选中复选框
                if (ImGui.Checkbox("###Select", ref isSelected))
                {
                    if (isSelected)
                    {
                        var existing = selectedItems.Find(t => t.ItemId == item.ItemId);
                        if (existing is null)
                        {
                            selectedItems.Add(new CraftTarget
                            {
                                ItemId = item.ItemId,
                                ItemName = item.ItemName,
                                Quantity = item.Quantity,
                                Type = TargetType.Equipment
                            });
                        }
                    }
                    else
                    {
                        selectedItems.RemoveAll(t => t.ItemId == item.ItemId);
                    }

                    onSelectionChanged?.Invoke();
                }

                ImGui.SameLine();

                // 装备名称 + ILvl + HQ 标记
                string hqLabel = item.IsHq ? " ★" : "";
                var nameColor = isSelected
                    ? new System.Numerics.Vector4(0.2f, 0.9f, 0.2f, 1f)
                    : new System.Numerics.Vector4(1f, 1f, 1f, 1f);
                ImGui.TextColored(nameColor, $"{item.ItemName}{hqLabel}");
                ImGui.SameLine();
                ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1f), $"IL{item.ItemLevel}");

                // 数量步进器
                ImGui.SameLine();
                var target = selectedItems.Find(t => t.ItemId == item.ItemId);
                int qty = target?.Quantity ?? item.Quantity;
                ImGui.SetNextItemWidth(70);
                if (ImGui.InputInt($"###Qty", ref qty, 1, 5))
                {
                    if (qty < 1) qty = 1;
                    if (target is not null)
                    {
                        target.Quantity = qty;
                    }

                    onSelectionChanged?.Invoke();
                }

                ImGui.PopID();
            }

            // 同行配对展示
            if (i < group.Slots.Length - 1)
            {
                ImGui.SameLine();
            }
        }

        ImGui.Spacing();
    }

    /// <summary>
    /// 获取槽位的中文显示名称。
    /// </summary>
    private static string GetSlotDisplayName(EquipmentSlotType slot) => slot switch
    {
        EquipmentSlotType.MainHand => "主手",
        EquipmentSlotType.OffHand => "副手",
        EquipmentSlotType.Head => "头",
        EquipmentSlotType.Body => "身",
        EquipmentSlotType.Hands => "手",
        EquipmentSlotType.Legs => "腿",
        EquipmentSlotType.Feet => "足",
        EquipmentSlotType.Ears => "耳",
        EquipmentSlotType.Neck => "颈",
        EquipmentSlotType.Wrists => "腕",
        EquipmentSlotType.Fingers => "指",
        _ => slot.ToString()
    };
}
