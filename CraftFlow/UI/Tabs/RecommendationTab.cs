using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using CraftFlow.Data.Models;
using CraftFlow.Services;

namespace CraftFlow.UI.Tabs;

/// <summary>
/// 推荐套装 Tab，显示内置推荐队列。
/// 每个推荐可[加载]到装备 Tab 或[收藏]到收藏清单。
/// </summary>
public sealed class RecommendationTab
{
    private readonly PresetService _presetService;
    private readonly IPluginLog _log;
    private readonly Func<List<CraftTarget>> _getCraftTargets;
    private Action<List<CraftTarget>, string>? _onTargetsLoaded;

    private string _loadNotification = string.Empty;
    private float _loadNotificationTimer;

    private static readonly System.Numerics.Vector4 ColorHighlight = new(0.7f, 0.85f, 1.0f, 1.0f);
    private static readonly System.Numerics.Vector4 ColorGray     = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly System.Numerics.Vector4 ColorGreen    = new(0.2f, 0.8f, 0.2f, 1f);

    public RecommendationTab(PresetService presetService, Func<List<CraftTarget>> getCraftTargets, IPluginLog log)
    {
        _presetService = presetService;
        _getCraftTargets = getCraftTargets;
        _log = log;
    }

    public void SetTargetLoadedCallback(Action<List<CraftTarget>, string> onTargetsLoaded)
    {
        _onTargetsLoaded = onTargetsLoaded;
    }

    public void DrawLeftPanel()
    {
        DrawLoadNotification();
        ImGui.TextColored(ColorHighlight, "▶ 推荐套装队列");
        ImGui.Separator();

        var presets = _presetService.GetBuiltInPresets();
        foreach (var preset in presets)
        {
            ImGui.PushID($"RecPreset_{preset.DisplayName}");
            ImGui.Text($"★ {preset.DisplayName}");
            ImGui.SameLine();
            ImGui.TextColored(ColorGray, $"IL{preset.ItemLevel}");
            if (preset.IsHq)
            {
                ImGui.SameLine();
                ImGui.TextColored(ColorGreen, "HQ");
            }

            if (ImGui.Button("加载###LoadRec"))
            {
                LoadPreset(preset);
            }
            ImGui.SameLine();
            if (ImGui.Button("收藏###SaveRec"))
            {
                _presetService.SavePresetAsFavorite(preset);
                ShowNotification($"已收藏: {preset.DisplayName}");
            }
            ImGui.PopID();
        }

        ImGui.Separator();
        ImGui.TextColored(ColorGray, "提示: 当前仅包含 HQ 装备推荐");
    }

    public void DrawRightPanel()
    {
        ImGui.TextColored(ColorGray, "选择推荐套装以加载到装备 Tab");
        ImGui.Spacing();
        ImGui.TextWrapped("推荐套装基于当前版本动态查询最佳 HQ 装备。点击'收藏'可保存到收藏清单。");
    }

    private void DrawLoadNotification()
    {
        if (_loadNotificationTimer > 0)
        {
            ImGui.TextColored(ColorGreen, _loadNotification);
            _loadNotificationTimer -= ImGui.GetIO().DeltaTime;
        }
    }

    private void LoadPreset(PresetEntry preset)
    {
        var targets = _presetService.LoadPresetToTargets(preset);
        if (targets.Count > 0)
        {
            _onTargetsLoaded?.Invoke(targets, preset.DisplayName);
            ShowNotification($"已加载: {preset.DisplayName} ({targets.Count}件)");
        }
        else
        {
            ShowNotification($"加载失败: {preset.DisplayName} 无可用装备");
        }
    }

    private void ShowNotification(string msg)
    {
        _loadNotification = msg;
        _loadNotificationTimer = 3.0f;
    }
}
