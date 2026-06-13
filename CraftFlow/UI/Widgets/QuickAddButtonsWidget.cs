using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using CraftFlow.Data.Models;
using CraftFlow.Services;

namespace CraftFlow.UI.Widgets;

/// <summary>
/// 一键添加按钮组组件。
/// 5 个快捷按钮压缩到一排，使用短标签 + 间距区分。
/// </summary>
public sealed class QuickAddButtonsWidget
{
    private readonly EquipmentSetService _setService;
    private readonly IPluginLog _log;

    public QuickAddButtonsWidget(EquipmentSetService setService, IPluginLog log)
    {
        _setService = setService;
        _log = log;
    }

    public void Draw(RoleGroup? roleGroup, uint classJobId, int? patchVersion, Action<List<CraftTarget>> onAdded)
    {
        bool disabled = roleGroup is null;
        if (disabled)
            ImGui.BeginDisabled();

        var buttons = new (string Label, AddSlotType Type, string Tooltip)[]
        {
            ("武器", AddSlotType.WeaponOnly, "添加主副手武器"),
            ("防具", AddSlotType.ArmorOnly, "添加全部防具"),
            ("首饰", AddSlotType.AccessoryOnly, "添加全部首饰"),
            ("防+首", AddSlotType.ArmorAndAccessory, "添加防具+首饰"),
            ("整套", AddSlotType.FullSet, "添加全部装备"),
        };

        for (int i = 0; i < buttons.Length; i++)
        {
            var btn = buttons[i];
            if (ImGui.Button($"{btn.Label}###QA_{btn.Type}"))
            {
                if (roleGroup is not null)
                {
                    var targets = _setService.AddByType(roleGroup, classJobId, btn.Type, 1, patchVersion);
                    if (targets.Count > 0)
                    {
                        onAdded(targets);
                        _log.Information($"一键添加 {btn.Type}: {targets.Count} 件装备");
                    }
                    else
                    {
                        _log.Warning($"一键添加 {btn.Type}: 未找到匹配装备");
                    }
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(btn.Tooltip);
            if (i < buttons.Length - 1)
                ImGui.SameLine();
        }

        if (disabled)
            ImGui.EndDisabled();
    }
}
