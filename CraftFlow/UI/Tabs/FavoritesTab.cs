using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using CraftFlow.Data.Models;
using CraftFlow.Services;

namespace CraftFlow.UI.Tabs;

/// <summary>
/// 手动收藏 Tab，仅显示用户手动添加的清单。
/// 支持加载到装备 Tab、重命名、删除。
/// </summary>
public sealed class FavoritesTab
{
    private readonly PresetService _presetService;
    private readonly IPluginLog _log;
    private readonly Func<List<CraftTarget>> _getCraftTargets;
    private Action<List<CraftTarget>, string>? _onTargetsLoaded;

    private string _renamingFrom = string.Empty;
    private string _renamingTo = string.Empty;
    private bool _isRenaming;
    private string _newPresetName = string.Empty;
    private string _notification = string.Empty;
    private float _notificationTimer;

    private static readonly Vector4 ColorTitle   = new(0.7f, 0.85f, 1.0f, 1f);
    private static readonly Vector4 ColorGray    = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 ColorGreen   = new(0.2f, 0.9f, 0.2f, 1f);
    private static readonly Vector4 ColorRedBg   = new(0.6f, 0.2f, 0.2f, 1f);

    public FavoritesTab(PresetService presetService, Func<List<CraftTarget>> getCraftTargets, IPluginLog log)
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
        DrawNotification();

        // 保存当前选择
        DrawSaveSection();

        ImGui.Separator();

        // 收藏列表
        DrawFavoriteList();
    }

    public void DrawRightPanel()
    {
        ImGui.TextColored(ColorGray, "选择收藏清单加载到装备 Tab");
        ImGui.Spacing();
        ImGui.TextWrapped("可从装备 Tab 或食物药品 Tab 保存当前选择为收藏。");
    }

    private void DrawNotification()
    {
        if (_notificationTimer > 0)
        {
            ImGui.TextColored(ColorGreen, _notification);
            _notificationTimer -= ImGui.GetIO().DeltaTime;
        }
    }

    private void DrawSaveSection()
    {
        ImGui.TextColored(ColorTitle, "保存当前选择为收藏:");
        ImGui.InputTextWithHint("###NewFavName", "输入收藏名称...", ref _newPresetName, 50);
        ImGui.SameLine();
        if (ImGui.Button("保存###SaveFav"))
        {
            if (string.IsNullOrWhiteSpace(_newPresetName))
            {
                ShowNotification("请输入收藏名称");
                return;
            }
            var targets = _getCraftTargets();
            if (targets.Count == 0)
            {
                ShowNotification("请先在装备/食物 Tab 中选择物品");
                return;
            }
            SaveCurrentSelection(targets);
        }
    }

    private void DrawFavoriteList()
    {
        ImGui.TextColored(ColorTitle, "我的收藏:");
        ImGui.Separator();

        var favorites = _presetService.GetFavoritePresets();
        if (favorites.Count == 0)
        {
            ImGui.TextColored(ColorGray, "暂无收藏");
            return;
        }

        for (int i = favorites.Count - 1; i >= 0; i--)
        {
            var fav = favorites[i];
            ImGui.PushID($"Fav_{fav.Name}_{i}");

            ImGui.Text($"♥ {fav.Name}");
            ImGui.SameLine();
            ImGui.TextColored(ColorGray, $"({fav.Selections.Count}件 {fav.CreatedAt:yyyy-MM-dd})");

            // 加载
            if (ImGui.Button("加载###LoadFav"))
            {
                var targets = _presetService.LoadPresetToTargets(fav);
                if (targets.Count > 0)
                {
                    _onTargetsLoaded?.Invoke(targets, fav.Name);
                    ShowNotification($"已加载: {fav.Name} ({targets.Count}件)");
                }
                else ShowNotification($"加载失败: {fav.Name}");
            }

            ImGui.SameLine();

            // 重命名
            if (_isRenaming && _renamingFrom == fav.Name)
            {
                ImGui.PushItemWidth(120);
                if (ImGui.InputText("###RenameInput", ref _renamingTo, 50, ImGuiInputTextFlags.EnterReturnsTrue))
                    ConfirmRename(fav.Name);
                ImGui.PopItemWidth();
                ImGui.SameLine();
                if (ImGui.Button("确认###RenameConfirm"))
                    ConfirmRename(fav.Name);
            }
            else
            {
                if (ImGui.Button("重命名###RenameFav"))
                {
                    _renamingFrom = fav.Name;
                    _renamingTo = fav.Name;
                    _isRenaming = true;
                }
            }

            ImGui.SameLine();

            // 删除
            ImGui.PushStyleColor(ImGuiCol.Button, ColorRedBg);
            if (ImGui.Button("删除###DeleteFav"))
            {
                _presetService.DeleteFavorite(fav.Name);
                ShowNotification($"已删除: {fav.Name}");
            }
            ImGui.PopStyleColor();

            ImGui.PopID();
        }
    }

    private void ConfirmRename(string oldName)
    {
        if (!string.IsNullOrWhiteSpace(_renamingTo) && _renamingTo != oldName)
        {
            _presetService.RenameFavorite(oldName, _renamingTo);
            ShowNotification($"已重命名: {oldName} → {_renamingTo}");
        }
        _isRenaming = false;
    }

    private void SaveCurrentSelection(List<CraftTarget> targets)
    {
        var name = string.IsNullOrWhiteSpace(_newPresetName)
            ? $"收藏 {DateTime.Now:yyyyMMdd_HHmmss}" : _newPresetName;
        var preset = new FavoritePreset
        {
            Name = name,
            Selections = targets.Select(t => new EquipmentSelection(t.ItemId, t.ItemName, t.Quantity)).ToList(),
            CreatedAt = DateTime.Now
        };
        _presetService.SaveFavorite(preset);
        _newPresetName = string.Empty;
        ShowNotification($"已保存收藏: {name}");
    }

    private void ShowNotification(string msg)
    {
        _notification = msg;
        _notificationTimer = 3.0f;
    }
}
