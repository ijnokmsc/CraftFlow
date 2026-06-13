using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using CraftFlow.Config;
using CraftFlow.Data.GameData;
using CraftFlow.Data.Models;
using CraftFlow.Services;
using CraftFlow.UI.Widgets;
using Lumina.Excel.Sheets;

namespace CraftFlow.UI.Tabs;

/// <summary>
/// 食物药品制作 Tab，参考 HQHelper ft-helper + EquipmentTab 布局。
/// 左面板：版本筛选 → 食物区 → 药品区，每项带数量步进器。
/// 右面板：材料清单汇总 + 操作按钮。
/// </summary>
public sealed class ConsumableTab
{
    private readonly BomExpander _bomExpander;
    private readonly MaterialAggregator _materialAggregator;
    private readonly CraftOrderCalculator _craftOrderCalculator;
    private readonly RecipeRepository _recipeRepo;
    private readonly MaterialListWidget _materialListWidget;
    private readonly PluginConfig _config;
    private readonly IPluginLog _log;

    private string _searchFilter = string.Empty;
    private readonly List<CraftTarget> _selectedItems = [];
    private BomNode? _bomResult;
    private List<MaterialEntry> _materialSummary = [];
    private List<CraftStep> _craftSteps = [];
    private bool _showTreeView = false;

    // 版本筛选
    private int _versionFilter = 0; // 0 = 全部
    private static readonly (int Ver, int Min, int Max)[] PatchRanges =
    [
        (70, 650, 699),
        (705, 700, 714),
        (71, 715, 734),
        (72, 735, 749),
        (73, 750, 769),
        (74, 770, 799),
    ];

    // 数量输入缓存
    private readonly Dictionary<uint, int> _quantityOverrides = [];
    private string _favName = string.Empty;
    private bool _showFavPopup;

    // 缓存
    private List<Item> _cachedFoods = [];
    private List<Item> _cachedMedicines = [];
    private bool _cacheInitialized = false;

    private static readonly Vector4 ColorFood     = new(0.95f, 0.75f, 0.40f, 1f);
    private static readonly Vector4 ColorMedicine = new(0.40f, 0.80f, 0.70f, 1f);

    public ConsumableTab(
        BomExpander bomExpander,
        MaterialAggregator materialAggregator,
        CraftOrderCalculator craftOrderCalculator,
        RecipeRepository recipeRepo,
        MaterialListWidget materialListWidget,
        PluginConfig config,
        IPluginLog log)
    {
        _bomExpander = bomExpander;
        _materialAggregator = materialAggregator;
        _craftOrderCalculator = craftOrderCalculator;
        _recipeRepo = recipeRepo;
        _materialListWidget = materialListWidget;
        _config = config;
        _log = log;
    }

    // ================================================================
    //  左面板
    // ================================================================

    public void DrawLeftPanel()
    {
        DrawVersionFilter();
        ImGui.Separator();

        // 搜索框
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("###ConsumableSearch", "搜索食物或药品...", ref _searchFilter, 100);
        ImGui.Separator();

        // 初始化缓存（全量加载一次）
        if (!_cacheInitialized)
        {
            _cachedFoods = _recipeRepo.GetConsumables(ConsumableCategory.Food);
            _cachedMedicines = _recipeRepo.GetConsumables(ConsumableCategory.Medicine);
            _cacheInitialized = true;
        }

        // 按版本筛选
        var foods = FilterByVersion(_cachedFoods);
        var medicines = FilterByVersion(_cachedMedicines);

        // 搜索筛选
        if (!string.IsNullOrWhiteSpace(_searchFilter))
        {
            var lower = _searchFilter.ToLowerInvariant();
            foods = foods.Where(i => i.Name.ToString().ToLowerInvariant().Contains(lower)).ToList();
            medicines = medicines.Where(i => i.Name.ToString().ToLowerInvariant().Contains(lower)).ToList();
        }

        ImGui.BeginChild("ConsumableList");

        DrawSectionHeader("🍲 食物", ColorFood, foods.Count);
        foreach (var item in foods) DrawItemRow(item);

        ImGui.Spacing();

        DrawSectionHeader("🧪 药品", ColorMedicine, medicines.Count);
        foreach (var item in medicines) DrawItemRow(item);

        ImGui.EndChild();
    }

    /// <summary>
    /// 版本筛选下拉框（与 EquipmentTab 风格一致）。
    /// </summary>
    private void DrawVersionFilter()
    {
        ImGui.Text("版本筛选:");
        ImGui.SameLine();

        string currentLabel = _versionFilter == 0 ? "全部" : FormatPatch(_versionFilter);

        if (ImGui.BeginCombo("###ConsumableVersion", currentLabel))
        {
            if (ImGui.Selectable("全部###Ver_all", _versionFilter == 0))
            {
                _versionFilter = 0;
            }

            foreach (var (ver, _, _) in PatchRanges)
            {
                bool isSelected = _versionFilter == ver;
                if (ImGui.Selectable($"{FormatPatch(ver)}###Ver_{ver}", isSelected))
                {
                    _versionFilter = ver;
                }
            }

            ImGui.EndCombo();
        }
    }

    /// <summary>
    /// 按版本过滤物品列表（通过 Item.LevelItem 判断）。
    /// </summary>
    private List<Item> FilterByVersion(List<Item> items)
    {
        if (_versionFilter == 0) return items;

        var range = PatchRanges.FirstOrDefault(r => r.Ver == _versionFilter);
        if (range == default) return items;

        return items.Where(item =>
        {
            var ilvl = item.LevelItem.IsValid ? (int)item.LevelItem.Value.RowId : 0;
            return ilvl >= range.Min && ilvl <= range.Max;
        }).ToList();
    }

    private static string FormatPatch(int v) => v >= 100 ? $"{v / 100}.{v % 100:D2}" : $"{v / 10}.{v % 10}";

    // ================================================================
    //  物品行
    // ================================================================

    private void DrawSectionHeader(string label, Vector4 color, int count)
    {
        ImGui.TextColored(color, label);
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), $"({count})");
        ImGui.Separator();
    }

    private void DrawItemRow(Item item)
    {
        ImGui.PushID($"Consumable_{item.RowId}");

        bool isSelected = _selectedItems.Any(t => t.ItemId == item.RowId);

        if (ImGui.Checkbox("###Select", ref isSelected))
        {
            if (isSelected)
                _selectedItems.Add(new CraftTarget
                {
                    ItemId = item.RowId,
                    ItemName = item.Name.ToString(),
                    Quantity = _quantityOverrides.GetValueOrDefault(item.RowId, 1),
                    Type = TargetType.Consumable
                });
            else
                _selectedItems.RemoveAll(t => t.ItemId == item.RowId);
            RecalculateBom();
        }

        ImGui.SameLine();

        var nameColor = isSelected ? new Vector4(0.2f, 0.9f, 0.2f, 1f) : new Vector4(1f, 1f, 1f, 1f);
        ImGui.TextColored(nameColor, item.Name.ToString());

        // 显示 ILvl
        if (item.LevelItem.IsValid)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), $"IL{item.LevelItem.Value.RowId}");
        }

        // 选中后显示 +/- 步进器
        if (isSelected)
        {
            ImGui.SameLine();
            int qty = _quantityOverrides.GetValueOrDefault(item.RowId, 1);

            if (ImGui.Button("-###Minus") && qty > 1) { qty--; UpdateQty(item.RowId, qty); }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(45);
            if (ImGui.InputInt("###Qty", ref qty, 0, 0)) { UpdateQty(item.RowId, qty); }
            ImGui.SameLine();
            if (ImGui.Button("+###Plus")) { qty++; UpdateQty(item.RowId, qty); }
        }

        ImGui.PopID();
    }

    private void UpdateQty(uint itemId, int qty)
    {
        qty = Math.Max(1, qty);
        _quantityOverrides[itemId] = qty;
        var target = _selectedItems.FirstOrDefault(t => t.ItemId == itemId);
        if (target is not null) target.Quantity = qty;
        RecalculateBom();
    }

    // ================================================================
    //  右面板
    // ================================================================

    public void DrawRightPanel()
    {
        if (_selectedItems.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "请在左侧选择食物或药品");
            return;
        }

        int total = _selectedItems.Sum(t => t.Quantity);
        ImGui.Text($"材料清单 (共 {total} 件)");
        ImGui.SameLine();
        if (ImGui.Button("收藏###SaveConsFav"))
        {
            _favName = $"食物药品 {DateTime.Now:yyyyMMdd_HHmmss}";
            _showFavPopup = true;
        }
        ImGui.SameLine();
        ImGui.Checkbox("树视图", ref _showTreeView);
        ImGui.Separator();

        if (_showTreeView && _bomResult is not null)
            _materialListWidget.DrawTree(_bomResult);
        else
            _materialListWidget.DrawMaterialPanel(_materialSummary, _craftSteps, ImGui.GetContentRegionAvail().Y);

        DrawFavPopup();
    }

    public List<CraftTarget> GetSelectedTargets() => _selectedItems.ToList();

    /// <summary>从外部添加制作目标。</summary>
    public void AddTargets(List<CraftTarget> targets)
    {
        foreach (var t in targets)
        {
            var existing = _selectedItems.Find(x => x.ItemId == t.ItemId);
            if (existing is not null)
                existing.Quantity += t.Quantity;
            else
                _selectedItems.Add(new CraftTarget
                {
                    ItemId = t.ItemId, ItemName = t.ItemName,
                    Quantity = t.Quantity, Type = t.Type
                });
        }
        RecalculateBom();
    }

    /// <summary>清空已选项目。</summary>
    public void ClearSelection()
    {
        _selectedItems.Clear();
        _quantityOverrides.Clear();
        RecalculateBom();
    }

    private void RecalculateBom()
    {
        if (_selectedItems.Count == 0)
        {
            _bomResult = null; _materialSummary = []; _craftSteps = [];
            return;
        }

        var root = new BomNode { ItemId = 0, ItemName = "汇总", Quantity = 1, Depth = -1 };
        foreach (var t in _selectedItems)
            root.Children.Add(_bomExpander.Expand(t.ItemId, t.Quantity));

        _bomResult = root;
        _materialSummary = _materialAggregator.Aggregate(root);
        _craftSteps = _craftOrderCalculator.CalculateOrder(root);
    }

    /// <summary>收藏名称输入弹窗。</summary>
    private void DrawFavPopup()
    {
        if (_showFavPopup)
            ImGui.OpenPopup("保存收藏");

        if (ImGui.BeginPopupModal("保存收藏", ref _showFavPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("输入收藏名称:");
            ImGui.SetNextItemWidth(200);
            ImGui.InputTextWithHint("###FavNameInput", "收藏名称...", ref _favName, 50);
            if (ImGui.Button("确认保存"))
            {
                if (!string.IsNullOrWhiteSpace(_favName))
                {
                    SaveAsFavorite();
                    _showFavPopup = false;
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("取消"))
            {
                _showFavPopup = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private void SaveAsFavorite()
    {
        if (_selectedItems.Count == 0) return;
        var name = string.IsNullOrWhiteSpace(_favName) ? $"食物药品 {DateTime.Now:yyyyMMdd_HHmmss}" : _favName;
        var preset = new FavoritePreset
        {
            Name = name,
            Selections = _selectedItems.Select(t => new EquipmentSelection(t.ItemId, t.ItemName, t.Quantity)).ToList(),
            CreatedAt = DateTime.Now
        };
        var existing = _config.FavoritePresets.FindIndex(p => p.Name == name);
        if (existing >= 0) _config.FavoritePresets[existing] = preset;
        else _config.FavoritePresets.Add(preset);
        _config.Save();
        _favName = string.Empty;
        _log.Information($"已保存收藏: {name}");
    }
}
