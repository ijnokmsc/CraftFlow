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
/// 收藏品制作 Tab，按制作职业分类、二级按评分区间分组展示收藏品。
/// 左面板：职业图标按钮行（能工巧匠 + 8 职业图标）→ 评分区间分组 → 收藏品列表。
/// 右面板：材料清单汇总（上）+ 精简评分档位行（下）+ 一键 Artisan 制作。
/// </summary>
public sealed class CollectablesTab
{
    private readonly BomExpander _bomExpander;
    private readonly MaterialAggregator _materialAggregator;
    private readonly CraftOrderCalculator _craftOrderCalculator;
    private readonly RecipeRepository _recipeRepo;
    private readonly MaterialListWidget _materialListWidget;
    private readonly PluginConfig _config;
    private readonly IPluginLog _log;
    private readonly ItemIconService _itemIconService;
    private readonly JobIconService _jobIconService;

    private string _searchFilter = string.Empty;
    private int _selectedCraftType = -1; // -1=全部, 0-7=对应制作职业
    private ScripType? _scripFilter = null; // null=全部, Orange=仅橙票, Purple=仅紫票
    private readonly List<CollectibleTarget> _selectedItems = [];
    private BomNode? _bomResult;
    private List<MaterialEntry> _materialSummary = [];
    private List<CraftStep> _craftSteps = [];
    private bool _showTreeView = false;

    // 数量输入缓存
    private readonly Dictionary<uint, int> _turnOverrides = [];

    // 缓存
    private List<CollectibleInfo>? _allCollectibles;
    private bool _cacheInitialized = false;

    // CraftType RowId (0-7) → ClassJobId (8-15)
    private static readonly uint[] CraftTypeToClassJobId = [8, 9, 10, 11, 12, 13, 14, 15];

    // 评分区间分组
    private static readonly (int Min, int Max, string Label)[] ScoreBands =
    [
        (91, 100, "91-100"),
        (81, 90, "81-90"),
        (71, 80, "71-80"),
        (61, 70, "61-70"),
        (50, 60, "50-60"),
    ];

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
        MaterialListWidget materialListWidget,
        PluginConfig config,
        ItemIconService itemIconService,
        JobIconService jobIconService,
        IPluginLog log)
    {
        _bomExpander = bomExpander;
        _materialAggregator = materialAggregator;
        _craftOrderCalculator = craftOrderCalculator;
        _recipeRepo = recipeRepo;
        _materialListWidget = materialListWidget;
        _config = config;
        _itemIconService = itemIconService;
        _jobIconService = jobIconService;
        _log = log;
    }

    // ================================================================
    //  左面板
    // ================================================================

    public void DrawLeftPanel()
    {
        // === 第一排：8 个制作职业图标 | 橙票 | 紫票 过滤按钮（同一行，自适应尺寸）===
        float gap = 4f;
        float availW = ImGui.GetContentRegionAvail().X;
        // 预留：8个职业 + 1个"|"分隔符 + 2个票按钮 + 其中间距
        int totalSlots = CraftTypeToClassJobId.Length + 1 + 2; // 11 个元素
        float slotW = Math.Min(24f, (availW - gap * (totalSlots - 1)) / totalSlots);
        slotW = Math.Max(slotW, 14f);

        for (int i = 0; i < CraftTypeToClassJobId.Length; i++)
        {
            uint classJobId = CraftTypeToClassJobId[i];
            var icon = _jobIconService.GetJobIcon(classJobId);

            bool isSelected = (_selectedCraftType == i);
            if (isSelected)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.95f, 0.75f, 0.15f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1f, 0.85f, 0.3f, 1f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2f);
            }

            bool clicked = false;
            if (icon.Handle != 0)
                clicked = ImGui.ImageButton(icon, new Vector2(slotW, slotW));
            else
            {
                ImGui.SetNextItemWidth(slotW + 6);
                clicked = ImGui.Button($"{i}");
            }

            if (isSelected)
            {
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(2);
            }

            if (clicked)
                _selectedCraftType = _selectedCraftType == i ? -1 : i;

            // 最后一个图标后面跟分隔符或票按钮
            if (i < CraftTypeToClassJobId.Length - 1)
                ImGui.SameLine(0, gap);
        }

        // 分隔符 |
        ImGui.SameLine(0, gap);
        ImGui.Text("|");
        ImGui.SameLine(0, gap);

        // 橙票过滤按钮（巧手橙票 物品41784 → Icon 65110），尺寸与职业图标一致
        DrawScripFilterBtn(ScripType.OrangeScrip, 41784u, slotW);

        // 紫票过滤按钮（巧手紫票 物品33913 → Icon 65088），紧邻橙票，无分隔符，无文字
        ImGui.SameLine(0, gap);
        DrawScripFilterBtn(ScripType.PurpleScrip, 33913u, slotW);

        ImGui.Separator();

        // 搜索框
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("###CollectableSearch", "搜索收藏品...", ref _searchFilter, 100);
        ImGui.Separator();

        // 懒加载缓存（一次性加载全部）
        if (!_cacheInitialized)
        {
            _allCollectibles = _recipeRepo.GetAllCollectibles();
            _cacheInitialized = true;
        }

        if (_allCollectibles is null || _allCollectibles.Count == 0)
        {
            ImGui.BeginChild("CollectableList");
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "暂无收藏品数据");
            ImGui.EndChild();
            return;
        }

        var filtered = FilterItems(_allCollectibles);
        if (filtered.Count == 0)
        {
            ImGui.BeginChild("CollectableList");
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f),
                string.IsNullOrWhiteSpace(_searchFilter) ? "无匹配的收藏品" : $"未找到匹配 \"{_searchFilter}\" 的收藏品");
            ImGui.EndChild();
            return;
        }

        // === 评分区间分组（不再按职业折叠，职业已由图标行筛选）===
        ImGui.BeginChild("CollectableList");
        foreach (var (min, max, label) in ScoreBands)
        {
            var bandItems = filtered.Where(i => i.CollectableLevel >= min && i.CollectableLevel <= max).ToList();
            if (bandItems.Count == 0) continue;

            if (ImGui.CollapsingHeader($"{label} ({bandItems.Count})###Band_{min}_{max}", ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var item in bandItems)
                    DrawItemRow(item);
            }
        }

        // 不在任何区间的物品归入"其他"
        var otherItems = filtered.Where(i => i.CollectableLevel < ScoreBands[^1].Min).ToList();
        if (otherItems.Count > 0)
        {
            if (ImGui.CollapsingHeader($"其他 ({otherItems.Count})###Band_other"))
            {
                foreach (var item in otherItems)
                    DrawItemRow(item);
            }
        }

        ImGui.EndChild();
    }

    /// <summary>
    /// 工票类型过滤按钮：仅显示游戏图标，尺寸与职业图标一致（iconSize），互斥切换。
    /// itemId 为真实工票物品（橙=41784 巧手橙票 / 紫=33913 巧手紫票），走已验证可用的 GetItemIcon 路径。
    /// </summary>
    private void DrawScripFilterBtn(ScripType type, uint itemId, float iconSize)
    {
        bool isOn = _scripFilter == type;
        Vector4 color = ScripTypeColor(type);

        if (isOn)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(color.X, color.Y, color.Z, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(color.X, color.Y, color.Z, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2f);
        }

        var scripIcon = _itemIconService.GetItemIcon(itemId);
        bool clicked;
        if (scripIcon.Handle != 0)
            clicked = ImGui.ImageButton(scripIcon.Handle, new Vector2(iconSize, iconSize));
        else
            clicked = ImGui.Button($"{(type == ScripType.OrangeScrip ? "橙" : "紫")}###Filter_{type}", new Vector2(iconSize, iconSize));

        if (isOn)
        {
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(2);
        }

        if (clicked)
            _scripFilter = isOn ? null : type;
    }

    /// <summary>
    /// 按像素宽度截断文本，超出部分加省略号。
    /// </summary>
    private static string TruncateText(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0) return text ?? "";
        var size = ImGui.CalcTextSize(text);
        if (size.X <= maxWidth) return text;

        // 二分查找截断点
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (ImGui.CalcTextSize(text.AsSpan(0, mid).ToString() + "…").X <= maxWidth)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo > 0 ? text.AsSpan(0, lo).ToString() + "…" : "…";
    }

    /// <summary>
    /// 按职业图标筛选 + 搜索词过滤。
    /// </summary>
    private List<CollectibleInfo> FilterItems(List<CollectibleInfo> items)
    {
        // 职业筛选（-1=全部）
        if (_selectedCraftType >= 0)
            items = items.Where(i => i.CraftTypeId == (uint)_selectedCraftType).ToList();

        // 工票类型筛选（null=全部）
        if (_scripFilter.HasValue)
            items = items.Where(i => i.ScripType == _scripFilter.Value).ToList();

        // 搜索词过滤
        if (!string.IsNullOrWhiteSpace(_searchFilter))
        {
            var lower = _searchFilter.ToLowerInvariant();
            items = items.Where(i => i.ItemName.ToLowerInvariant().Contains(lower)).ToList();
        }

        return items;
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

        // 物品图标 + 名称（名称过长自动截断）
        var nameColor = isSelected ? new Vector4(0.2f, 0.9f, 0.2f, 1f) : new Vector4(1f, 1f, 1f, 1f);
        var icon = _itemIconService.GetItemIcon(info.ItemId);

        // 预留：图标20px + 等级~45px + 票数~45px + 步进器(~90px) + 间距
        float nameMaxW = ImGui.GetContentRegionAvail().X - 200f;
        if (nameMaxW < 40f) nameMaxW = 40f;
        string displayName = TruncateText(info.ItemName, nameMaxW);

        if (icon.Handle != 0)
        {
            ImGui.Image(icon, new Vector2(20, 20));
            ImGui.SameLine();
        }
        ImGui.TextColored(nameColor, displayName);

        // 等级
        if (info.CollectableLevel > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), $"{info.CollectableLevel}级");
        }

        // 最高档票数（彩色表示票类型，删除星级）
        if (info.ScoreThresholds.Count > 0)
        {
            var top = info.ScoreThresholds.OrderByDescending(t => t.MinScore).First();
            ImGui.SameLine();
            ImGui.TextColored(ScripTypeColor(info.ScripType), $"{top.ScripReward}票");
        }

        // 选中后显示次数步进器（同行紧凑）
        if (isSelected)
        {
            ImGui.SameLine();
            var target = _selectedItems.First(t => t.Info.ItemId == info.ItemId);
            int turns = target.Turns;

            if (ImGui.Button("-###Minus", new Vector2(18, 0)) && turns > 1) { turns--; UpdateTurns(info.ItemId, turns); }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(30);
            if (ImGui.InputInt("###Turns", ref turns, 0, 0)) { UpdateTurns(info.ItemId, turns); }
            ImGui.SameLine();
            if (ImGui.Button("+###Plus", new Vector2(18, 0))) { turns++; UpdateTurns(info.ItemId, turns); }
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

        // === 标题 + 操作按钮 ===
        int totalTurns = _selectedItems.Sum(t => t.Turns);
        ImGui.Text($"材料清单 ({_selectedItems.Count} 种收藏品, 共 {totalTurns} 次)");
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 120);
        if (ImGui.Button("清空###CollClearAll", new Vector2(50, 0)))
        {
            ClearSelection();
        }

        // === 视图 / 显示 / 缺失 选项分组（作用于材料清单）===
        ImGui.Spacing();
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

        // === 材料清单（复用共享 Widget）===
        // 评分档位作为 footer 传入 DrawMaterialPanel，渲染在滚动区与「制作」按钮栏之间
        if (_showTreeView && _bomResult is not null)
        {
            _materialListWidget.DrawTree(_bomResult);
            ImGui.Separator();
            DrawTicketCalcBlock();
        }
        else
        {
            float ticketBlockH = EstimateTicketBlockHeight();
            _materialListWidget.DrawMaterialPanel(
                _materialSummary, _craftSteps, ImGui.GetContentRegionAvail().Y, _bomResult,
                footer: DrawTicketCalcBlock, footerHeight: ticketBlockH);
        }
    }

    /// <summary>
    /// 估算「评分档位」块所需高度。汇总模式下最多 2 行（紫/橙各一）。
    /// </summary>
    private float EstimateTicketBlockHeight()
    {
        int lineCount = 0;
        if (_selectedItems.Any(t => t.Info.ScripType == ScripType.PurpleScrip && t.Info.ScoreThresholds.Count > 0))
            lineCount++;
        if (_selectedItems.Any(t => t.Info.ScripType == ScripType.OrangeScrip && t.Info.ScoreThresholds.Count > 0))
            lineCount++;
        return Math.Max(lineCount, 1) * 20f + 8f;
    }

    /// <summary>
    /// 根据工票类型返回显示颜色（橙票=橙、紫票=紫），用颜色区分节省空间。
    /// </summary>
    private static Vector4 ScripTypeColor(ScripType type) => type switch
    {
        ScripType.OrangeScrip => new Vector4(0.95f, 0.55f, 0.15f, 1f),  // 橙
        ScripType.PurpleScrip => new Vector4(0.7f, 0.45f, 0.95f, 1f),   // 紫
        _ => new Vector4(0.85f, 0.7f, 0.2f, 1f)
    };

    /// <summary>
    /// 汇总评分档位行：将所有选中收藏品的档位按票类型聚合为一行。
    /// 格式 "评分档位(紫): ★★★|xxx；★★|xxx；★|xxx" （无该类型则不显示）
    /// </summary>
    private void DrawTicketCalcBlock()
    {
        // 按 ScripType 分组汇总
        var purpleItems = _selectedItems.Where(t => t.Info.ScripType == ScripType.PurpleScrip && t.Info.ScoreThresholds.Count > 0).ToList();
        var orangeItems = _selectedItems.Where(t => t.Info.ScripType == ScripType.OrangeScrip && t.Info.ScoreThresholds.Count > 0).ToList();

        void DrawSummary(ScripType type, List<CollectibleTarget> items)
        {
            if (items.Count == 0) return;

            // 找出最多档位数，用于对齐星级显示
            int maxTiers = items.Max(i => i.Info.ScoreThresholds.Count);
            var parts = new List<string>();
            for (int tierIdx = 0; tierIdx < maxTiers; tierIdx++)
            {
                int grandTotal = 0;
                bool anyHasThisTier = false;
                foreach (var target in items)
                {
                    var sorted = target.Info.ScoreThresholds.OrderByDescending(t => t.MinScore).ToList();
                    if (tierIdx < sorted.Count)
                    {
                        int starsCount = sorted.Count - tierIdx;
                        if (starsCount <= 3) // 只取最高3星
                        {
                            grandTotal += sorted[tierIdx].ScripReward * target.Turns;
                            anyHasThisTier = true;
                        }
                    }
                }
                if (anyHasThisTier && grandTotal > 0)
                {
                    int starCount = Math.Min(maxTiers - tierIdx, 3);
                    string stars = new string('★', starCount);
                    parts.Add($"{stars}|{grandTotal}票");
                }
            }

            if (parts.Count > 0)
            {
                ImGui.TextColored(ScripTypeColor(type), $"评分档位: {string.Join("；", parts)}");
            }
        }

        DrawSummary(ScripType.PurpleScrip, purpleItems);
        DrawSummary(ScripType.OrangeScrip, orangeItems);
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
                if (_allCollectibles is not null)
                {
                    info = _allCollectibles.FirstOrDefault(i => i.ItemId == t.ItemId);
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
