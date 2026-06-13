using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using CraftFlow.Data.Models;
using CraftFlow.Services;

namespace CraftFlow.UI.Widgets;

/// <summary>
/// 一键添加按钮组组件。
/// 提供 5 种快捷按钮：主副手、防具、首饰、防具+首饰、整套。
/// 点击后调用 EquipmentSetService 对应方法，通过回调通知 EquipmentTab。
/// </summary>
public sealed class QuickAddButtonsWidget
{
    private readonly EquipmentSetService _setService;
    private readonly IPluginLog _log;

    /// <summary>
    /// 初始化 QuickAddButtonsWidget 实例。
    /// </summary>
    /// <param name="setService">装备套装服务。</param>
    /// <param name="log">插件日志。</param>
    public QuickAddButtonsWidget(EquipmentSetService setService, IPluginLog log)
    {
        _setService = setService;
        _log = log;
    }

    /// <summary>
    /// 绘制一键添加按钮组。
    /// </summary>
    /// <param name="roleGroup">当前选中的角色分组（null 时按钮禁用）。</param>
    /// <param name="classJobId">当前选中的职业 ID。</param>
    /// <param name="patchVersion">当前版本筛选（null 表示不筛选）。</param>
    /// <param name="onAdded">添加完成后的回调，参数为新增的 CraftTarget 列表。</param>
    public void Draw(RoleGroup? roleGroup, uint classJobId, int? patchVersion, Action<List<CraftTarget>> onAdded)
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.85f, 1.0f, 1.0f), "一键添加:");
        ImGui.Separator();

        // 如果未选中角色分组，禁用所有按钮
        bool disabled = roleGroup is null;
        if (disabled)
        {
            ImGui.BeginDisabled();
        }

        // 按钮定义：(显示名称, AddSlotType)
        var buttons = new (string Label, AddSlotType Type)[]
        {
            ("主副手###QuickAdd_Weapon", AddSlotType.WeaponOnly),
            ("防具###QuickAdd_Armor", AddSlotType.ArmorOnly),
            ("首饰###QuickAdd_Accessory", AddSlotType.AccessoryOnly),
            ("防具+首饰###QuickAdd_ArmorAccessory", AddSlotType.ArmorAndAccessory),
            ("整套###QuickAdd_FullSet", AddSlotType.FullSet),
        };

        for (int i = 0; i < buttons.Length; i++)
        {
            if (i > 0 && i % 3 != 0) ImGui.SameLine();
            if (i == 3) ImGui.NewLine();

            if (ImGui.Button(buttons[i].Label))
            {
                if (roleGroup is not null)
                {
                    var targets = _setService.AddByType(roleGroup, classJobId, buttons[i].Type, 1, patchVersion);
                    if (targets.Count > 0)
                    {
                        onAdded(targets);
                        _log.Information($"一键添加 {buttons[i].Type}: {targets.Count} 件装备");
                    }
                    else
                    {
                        _log.Warning($"一键添加 {buttons[i].Type}: 未找到匹配装备");
                    }
                }
            }
        }

        if (disabled)
        {
            ImGui.EndDisabled();
        }
    }
}
