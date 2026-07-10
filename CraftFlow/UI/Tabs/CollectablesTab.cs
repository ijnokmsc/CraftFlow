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

namespace CraftFlow.UI.Tabs;

/// <summary>
/// 收藏品制作 Tab，按工票类型（橙/紫）+ 评分区间分组展示收藏品。
/// 左面板：工票筛选 → 评分区间分组 → 收藏品列表，每项带制作次数步进器。
/// 右面板：评分档位信息 + 材料清单汇总 + 一键 Artisan 制作。
/// </summary>
public sealed class CollectablesTab
{
    private readonly BomExpander _bomExpander;
    private readonly MaterialAggregator _materialAggregator;
    private readonly CraftOrderCalculator _craftOrderCalculator;
    private readonly RecipeRepository _recipeRepo;
    private readonly CollectibleCalculator _collectibleCalc;
    private readonly MaterialListWidget _materialListWidget;
    private readonly PluginConfig _config;
    private readonly IPluginLog _log;
    private readonly ItemIconService _itemIconService;

    private string _searchFilter = string.Empty;
    private int _scripFilter = 0; // 0=全部, 1=紫票(Purple), 2=橙票(Orange)
    private readonly List<CollectibleTarget> _selectedItems = [];
    private BomNode? _bomResult;
    private List<MaterialEntry> _materialSummary = [];
    private List<CraftStep> _craftSteps = [];
    private bool _showTreeView = false;

    // 数量输入缓存
    private readonly Dictionary<uint, int> _turnOverrides = [];

    // 缓存
    private Dictionary<ScripType, List<CollectibleInfo>>? _collectiblesCache;
    private bool _cacheInitialized = false;

    // 评分区间分组（与 Artisan UI 对齐）
    private static readonly (int Min, int Max, string Label)[] ScoreBands =
    [
        (91, 100, "91-100"),
        (81, 90, "81-90"),
        (71, 80, "71-80"),
        (61, 70, "61-70"),
        (50, 60, "50-60"),
    ];

    private static readonly Vector4 ColorPurpleScrip = new(0.6f, 0.4f, 0.9f, 1f);
    private static readonly Vector4 ColorOrangeScrip = new(0.9f, 0.6f, 0.2f, 1f);

    /// <summary>选中的收藏品目标（含制作次数）。</summary>
    private sealed class CollectibleTarget
    {
        public CollectibleInfo Info { get; set; } = null!;
        public int Turns { get; set; } = 1;
    }

    public CollectablesTab(
        BomExpander bomExpander,
        MaterialAggregator materialAggregator,
        CraftOrderCalculator craftOrderCalculator,
        RecipeRepository recipeRepo,
        CollectibleCalculator collectibleCalc,
        MaterialListWidget materialListWidget,
        PluginConfig config,
        ItemIconService itemIconService,
        IPluginLog log)
    {
        _bomExpander = bomExpander;
        _materialAggregator = materialAggregator;
        _craftOrderCalculator = craftOrderCalculator;
        _recipeRepo = recipeRepo;
        _collectibleCalc = collectibleCalc;
        _materialListWidget = materialListWidget;
        _config = config;
        _itemIconService = itemIconService;
        _log = log;
    }

    // ================================================================
    //  左面板
    // ================================================================

    public void DrawLeftPanel()
    {
        DrawScripFilter();
        ImGui.Separator();

        // 搜索框
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("###CollectableSearch", "搜索收藏品...", ref _searchFilter, 100);
        ImGui.Separator();

        // 懒加载缓存
        if (!_cacheInitialized)
        {
            _collectiblesCache = new()
            {
                [ScripType.PurpleScrip] = _recipeRepo.GetCollectibles(ScripType.PurpleScrip),
                [ScripType.OrangeScrip] = _recipeRepo.GetCollectibles(ScripType.OrangeScrip),
            };
            _cacheInitialized = true;
        }

        if (_collectiblesCache is null || _collectiblesCache.Count == 0)
        {
            ImGui.BeginChild("CollectableList");
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "暂无收藏品数据");
            ImGui.EndChild();
            return;
        }

        var purple = FilterItems(_collectiblesCache[ScripType.PurpleScrip]);
        var orange = FilterItems(_collectiblesCache[ScripType.OrangeScrip]);

        ImGui.BeginChild("CollectableList");

        if (purple.Count > 0)
        {
            DrawScoreBandGroup(purple, "巧手紫票", ColorPurpleScrip);
        }
        if (orange.Count > 0 && purple.Count > 0)
        {
            ImGui.Spacing();
        }
        if (orange.Count > 0)
        {
            DrawScoreBandGroup(orange, "巧手橙票", ColorOrangeScrip);
        }

        if (purple.Count == 0 && orange.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f),
                string.IsNullOrWhiteSpace(_searchFilter) ? "无匹配的收藏品" : $"未找到匹配 \"{_searchFilter}\" 的收藏品");
        }

        ImGui.EndChild();
    }

    /// <summary>
    /// 工票类型筛选下拉框。
    /// </summary>
    private void DrawScripFilter()
    {
        ImGui.Text("工票:");
        ImGui.SameLine();

        string currentLabel = _scripFilter switch
        {
            0 => "全部",
            1 => "紫票",
            2 => "橙票",
            _ => "全部"
        };

        ImGui.SetNextItemWidth(65);
        if (ImGui.BeginCombo("###CollectableScripFilter", currentLabel))
        {
            if (ImGui.Selectable("全部###Scrip_all", _scripFilter == 0)) _scripFilter = 0;
            if (ImGui.Selectable("紫票###Scrip_purple", _scripFilter == 1)) _scripFilter = 1;
            if (ImGui.Selectable("橙票###Scrip_orange", _scripFilter == 2)) _scripFilter = 2;
            ImGui.EndCombo();
        }
    }

    /// <summary>
    /// 按工票类型和搜索词过滤列表。
    /// </summary>
    private List<CollectibleInfo> FilterItems(List<CollectibleInfo> items)
    {
        // 工票类型过滤（0=全部不过滤）
        if (_scripFilter != 0)
        {
            var filterType = _scripFilter == 1 ? ScripType.PurpleScrip : ScripType.OrangeScrip;
            items = items.Where(i => i.ScripType == filterType).ToList();
        }

        // 搜索词过滤
        if (!string.IsNullOrWhiteSpace(_searchFilter))
        {
            var lower = _searchFilter.ToLowerInvariant();
            items = items.Where(i => i.ItemName.ToLowerInvariant().Contains(lower)).ToList();
        }

        return items;
    }

    /// <summary>
    /// 按评分区间分组的列表渲染。
    /// </summary>
    private void DrawScoreBandGroup(List<CollectibleInfo> items, string title, Vector4 color)
    {
        foreach (var (min, max, label) in ScoreBands)
        {
            var bandItems = items.Where(i => i.CollectableLevel >= min && i.CollectableLevel <= max).ToList();
            if (bandItems.Count == 0) continue;

            ImGui.PushStyleColor(ImGuiCol.Text, color);
            if (ImGui.CollapsingHeader($"{label} ({bandItems.Count})###Band_{min}_{max}"))
            {
                ImGui.PopStyleColor();
                foreach (var item in bandItems)
                    DrawItemRow(item);
            }
            else
            {
                ImGui.PopStyleColor();
            }
        }

        // 不在任何区间的物品归入"其他"
        var otherItems = items.Where(i => i.CollectableLevel < ScoreBands[^1].Min).ToList();
        if (otherItems.Count > 0)
        {
            ImGui.TextColored(color, $"{title}: 其他 ({otherItems.Count})");
            ImGui.Separator();
            foreach (var item in otherItems)
                DrawItemRow(item);
        }
    }

    /// <summary>
    /// 单个收藏品行渲染。
    /// </summary>
    private void DrawItemRow(CollectibleInfo info)
    {
        ImGui.PushID($"Coll_{info.ItemId}");

        bool isSelected = _selectedItems.Any(t => t.Info.ItemId == info.ItemId);

        if (ImGui.Checkbox("###Select", ref isSelected))
        {
            if (isSelected)
            {
                _selectedItems.Add(new CollectibleTarget
                {
                    Info = info,
                    Turns = _turnOverrides.GetValueOrDefault(info.ItemId, 1)
                });
            }
            else
            {
                _selectedItems.RemoveAll(t => t.Info.ItemId == info.ItemId);
            }
            RecalculateBom();
        }

        ImGui.SameLine();

        // 物品图标 + 名称
        var nameColor = isSelected ? new Vector4(0.2f, 0.9f, 0.2f, 1f) : new Vector4(1f, 1f, 1f, 1f);
        var icon = _itemIconService.GetItemIcon(info.ItemId);
        if (icon.Handle != 0)
        {
            ImGui.Image(icon, new Vector2(20, 20));
            ImGui.SameLine();
        }
        ImGui.TextColored(nameColor, info.ItemName);

        // 等级
        if (info.CollectableLevel > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), $"{info.CollectableLevel}级");
        }

        // 星级（根据评分档位数量）
        if (info.ScoreThresholds.Count > 0)
        {
            ImGui.SameLine();
            string stars = new string('★', Math.Min(info.ScoreThresholds.Count, 3));
            ImGui.TextColored(new Vector4(0.85f, 0.7f, 0.2f, 1f), stars);
        }

        // 选中后显示次数步进器
        if (isSelected)
        {
            ImGui.SameLine();
            var target = _selectedItems.First(t => t.Info.ItemId == info.ItemId);
            int turns = target.Turns;

            if (ImGui.Button("-###Minus") && turns > 1) { turns--; UpdateTurns(info.ItemId, turns); }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(45);
            if (ImGui.InputInt("###Turns", ref turns, 0, 0)) { UpdateTurns(info.ItemId, turns); }
            ImGui.SameLine();
            if (ImGui.Button("+###Plus")) { turns++; UpdateTurns(info.ItemId, turns); }
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "次");
        }

        ImGui.PopID();
    }

    private void UpdateTurns(uint itemId, int turns)
    {
        turns = Math.Max(1, turns);
        _turnOverrides[itemId] = turns;
        var target = _selectedItems.FirstOrDefault(t => t.Info.ItemId == itemId);
        if (target is not null) target.Turns = turns;
        RecalculateBom();
    }

    // ================================================================
    //  右面板
    // ================================================================

    public void DrawRightPanel()
    {
        if (_selectedItems.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "请在左侧选择收藏品");
            return;
        }

        // === 行1：标题 + 操作按钮 ===
        int totalTurns = _selectedItems.Sum(t => t.Turns);
        ImGui.Text($"材料清单 ({_selectedItems.Count} 种收藏品, 共 {totalTurns} 次)");
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 120);
        if (ImGui.Button("清空###CollClearAll", new Vector2(50, 0)))
        {
            ClearSelection();
        }

        // === 收藏品特有信息块 ===
        ImGui.Spacing();
        DrawCollectibleInfoBlock();
        ImGui.Separator();

        // === 行2：视图 / 显示 / 缺失 选项分组 ===
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "视图");
        ImGui.SameLine();
        ImGui.Checkbox("树视图###Coll_ShowTreeView", ref _showTreeView);
        ImGui.SameLine(0, 16);

        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "显示");
        ImGui.SameLine();
        bool showCrystals = _config.ShowCrystals;
        if (ImGui.Checkbox("水晶###Coll_ShowCrystals", ref showCrystals))
        {
            _config.ShowCrystals = showCrystals;
            _config.Save();
            RecalculateBom();
        }
        ImGui.SameLine(0, 16);

        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "缺失");
        ImGui.SameLine();
        bool onlyMissing = _config.OnlyMissingMaterials;
        if (ImGui.Checkbox("仅缺失材料###Coll_OnlyMissing", ref onlyMissing))
        {
            _config.OnlyMissingMaterials = onlyMissing;
            _config.Save();
            RecalculateBom();
        }
        ImGui.SameLine();
        if (!_config.OnlyMissingMaterials) ImGui.BeginDisabled();
        bool hqOnly = _config.HqOnly;
        if (ImGui.Checkbox("仅计HQ###Coll_HqOnly", ref hqOnly))
        {
            _config.HqOnly = hqOnly;
            _config.Save();
            RecalculateBom();
        }
        if (!_config.OnlyMissingMaterials) ImGui.EndDisabled();

        ImGui.Separator();

        // 材料面板（复用共享 Widget）
        if (_showTreeView && _bomResult is not null)
            _materialListWidget.DrawTree(_bomResult);
        else
            _materialListWidget.DrawMaterialPanel(
                _materialSummary, _craftSteps, ImGui.GetContentRegionAvail().Y, _bomResult);
    }

    /// <summary>
    /// 渲染收藏品特有信息块：评分档位、票数奖励、推荐目标分数。
    /// </summary>
    private void DrawCollectibleInfoBlock()
    {
        foreach (var target in _selectedItems)
        {
            var info = target.Info;
            var tierColor = info.ScripType == ScripType.PurpleScrip ? ColorPurpleScrip : ColorOrangeScrip;

            // 物品名行
            ImGui.TextColored(tierColor, info.ItemName);

            if (info.CollectableLevel > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), $"{info.CollectableLevel}级 | {target.Turns}次");
            }

            // 评分档位表
            if (info.ScoreThresholds.Count > 0)
            {
                if (ImGui.BeginTable($"Tiers_{info.ItemId}", 3,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
                {
                    ImGui.TableSetupColumn("最低评分", ImGuiTableColumnFlags.WidthFixed, 70);
                    ImGui.TableSetupColumn("票数", ImGuiTableColumnFlags.WidthFixed, 55);
                    ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();

                    foreach (var tier in info.ScoreThresholds.OrderByDescending(t => t.MinScore))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text($"{tier.MinScore}");
                        ImGui.TableNextColumn();
                        ImGui.TextColored(tierColor, $"{tier.ScripReward}");
                        ImGui.TableNextColumn();
                        ImGui.Text("票");

                        // 「按此档计算次数」按钮
                        ImGui.TableNextColumn();
                        ImGui.PushID($"TierBtn_{info.ItemId}_{tier.MinScore}");
                        if (ImGui.SmallButton("算次数###CalcTurns"))
                        {
                            int craftCount = _collectibleCalc.CalculateCraftCount(
                                info, tier.ScripReward * 3, tier.MinScore);
                            if (craftCount > 0)
                            {
                                target.Turns = craftCount;
                                _turnOverrides[info.ItemId] = craftCount;
                                RecalculateBom();
                            }
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip($"按目标{tier.ScripReward*1}票反推所需制作次数\n(使用 CollectibleCalculator)");
                        ImGui.PopID();
                    }

                    ImGui.EndTable();
                }
            }
        }
    }

    // ================================================================
    //  对外接口
    // ================================================================

    public List<CraftTarget> GetSelectedTargets() =>
        _selectedItems.Select(t => new CraftTarget
        {
            ItemId = t.Info.ItemId,
            ItemName = t.Info.ItemName,
            Quantity = t.Turns,
            Type = TargetType.Collectible
        }).ToList();

    /// <summary>从外部添加制作目标。</summary>
    public void AddTargets(List<CraftTarget> targets)
    {
        foreach (var t in targets)
        {
            var existing = _selectedItems.Find(x => x.Info.ItemId == t.ItemId);
            if (existing is not null)
            {
                existing.Turns += t.Quantity;
            }
            else
            {
                // 从缓存查找 CollectibleInfo
                CollectibleInfo? info = null;
                if (_collectiblesCache is not null)
                {
                    foreach (var list in _collectiblesCache.Values)
                    {
                        info = list.FirstOrDefault(i => i.ItemId == t.ItemId);
                        if (info is not null) break;
                    }
                }

                if (info is not null)
                {
                    _selectedItems.Add(new CollectibleTarget { Info = info, Turns = t.Quantity });
                }
                else
                {
                    _log.Warning($"AddTargets: 找不到 ItemId={t.ItemId} 的收藏品信息，跳过");
                }
            }
        }
        RecalculateBom();
    }

    /// <summary>清空已选项目。</summary>
    public void ClearSelection()
    {
        _selectedItems.Clear();
        _turnOverrides.Clear();
        RecalculateBom();
    }

    // ================================================================
    //  BOM 计算
    // ================================================================

    private void RecalculateBom()
    {
        if (_selectedItems.Count == 0)
        {
            _bomResult = null;
            _materialSummary = [];
            _craftSteps = [];
            return;
        }

        var root = new BomNode { ItemId = 0, ItemName = "汇总", Quantity = 1, Depth = -1 };
        foreach (var t in _selectedItems)
        {
            // 获取配方产量
            int yield = 1;
            var recipe = _recipeRepo.FindRecipeByItem(t.Info.ItemId);
            if (recipe.HasValue && recipe.Value.AmountResult > 0)
                yield = recipe.Value.AmountResult;

            // 制作次数 × 单次产量 = 总产出量
            root.Children.Add(_bomExpander.Expand(t.Info.ItemId, t.Turns * yield));
        }

        _bomResult = root;
        _materialSummary = _materialAggregator.Aggregate(root, _config.ShowCrystals);
        _craftSteps = _craftOrderCalculator.CalculateOrder(root);
    }
}
