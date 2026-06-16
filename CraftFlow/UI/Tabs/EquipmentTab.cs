using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using CraftFlow.Config;
using CraftFlow.Data.GameData;
using CraftFlow.Data.Models;
using CraftFlow.Services;
using CraftFlow.UI.Widgets;

namespace CraftFlow.UI.Tabs;

/// <summary>
/// 装备武器制作 Tab，提供三级选择流程和 BOM 展示功能。
/// 左面板：版本筛选 → 角色分组 → 职业选择 → 槽位分组展示 → 一键添加。
/// 右面板：制作列表 + 材料清单（带"显示水晶"切换） + 操作按钮。
/// </summary>
public sealed class EquipmentTab
{
    private readonly EquipmentRepository _equipRepo;
    private readonly EquipmentSetService _setService;
    private readonly BomExpander _bomExpander;
    private readonly MaterialAggregator _materialAggregator;
    private readonly CraftOrderCalculator _craftOrderCalculator;
    private readonly RecipeRepository _recipeRepo;
    private readonly MaterialListWidget _materialListWidget;
    private readonly EquipmentSlotGroupWidget _slotGroupWidget;
    private readonly QuickAddButtonsWidget _quickAddWidget;
    private readonly PluginConfig _config;
    private readonly IPluginLog _log;
    private readonly JobIconService _jobIconService;

    // 选中状态
    private RoleGroup? _selectedRoleGroup;
    private uint _selectedClassJobId;
    private readonly List<CraftTarget> _selectedItems = [];
    private string _loadedFavName = string.Empty; // 当前加载的收藏名称
    private Dictionary<EquipmentSlotType, List<EquipmentItem>> _groupedEquipment = [];
    private BomNode? _bomResult;
    private List<MaterialEntry> _materialSummary = [];
    private List<CraftStep> _craftSteps = [];
    private bool _showTreeView = false;

    // 版本筛选状态
    private int _versionFilter;

    // 动态版本列表缓存
    private List<(string Label, int Value)>? _cachedVersionList;
    private bool _versionListInitialized = false;

    // 收藏名称输入
    private string _favName = string.Empty;
    private bool _showFavPopup;

    /// <summary>
    /// 初始化 EquipmentTab 实例。
    /// </summary>
    public EquipmentTab(
        EquipmentRepository equipRepo,
        EquipmentSetService setService,
        BomExpander bomExpander,
        MaterialAggregator materialAggregator,
        CraftOrderCalculator craftOrderCalculator,
        RecipeRepository recipeRepo,
        MaterialListWidget materialListWidget,
        PluginConfig config,
        IPluginLog log,
        JobIconService jobIconService)
    {
        _equipRepo = equipRepo;
        _setService = setService;
        _bomExpander = bomExpander;
        _materialAggregator = materialAggregator;
        _craftOrderCalculator = craftOrderCalculator;
        _recipeRepo = recipeRepo;
        _materialListWidget = materialListWidget;
        _slotGroupWidget = new EquipmentSlotGroupWidget(log);
        _quickAddWidget = new QuickAddButtonsWidget(setService, log);
        _config = config;
        _log = log;
        _jobIconService = jobIconService;

        // 默认版本：0 表示全部，初始化时将延迟设置到最新版本
        _versionFilter = 0;
    }

    /// <summary>
    /// 绘制左面板（装备选择流程）。
    /// </summary>
    public void DrawLeftPanel()
    {
        DrawVersionFilter();
        ImGui.Separator();
        DrawJobSelector();
        ImGui.Separator();
        DrawQuickAddButtons();
        ImGui.Separator();
        DrawEquipmentBySlot();
    }

    /// <summary>
    /// 绘制右面板（材料清单和操作按钮）。
    /// </summary>
    public void DrawRightPanel()
    {
        if (_selectedItems.Count == 0)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1f), "请在左侧选择装备以查看材料清单");
            return;
        }

        // === 行1：标题 + 操作按钮 ===
        ImGui.Text($"材料清单 ({_selectedItems.Count} 件装备)");
        if (!string.IsNullOrEmpty(_loadedFavName))
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), $"({_loadedFavName})");
        }
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 120);
        if (ImGui.Button("收藏###SaveEquipFav", new Vector2(50, 0)))
        {
            _favName = $"装备 {DateTime.Now:yyyyMMdd_HHmmss}";
            _showFavPopup = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("清空###ClearAll", new Vector2(50, 0)))
        {
            _selectedItems.Clear();
            RecalculateBom();
        }

        // === 行2：视图 / 显示 / 缺失 选项分组 ===
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "视图");
        ImGui.SameLine();
        ImGui.Checkbox("树视图###Equip_ShowTreeView", ref _showTreeView);
        ImGui.SameLine(0, 16);

        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "显示");
        ImGui.SameLine();
        bool showCrystals = _config.ShowCrystals;
        if (ImGui.Checkbox("水晶###Equip_ShowCrystals", ref showCrystals))
        {
            _config.ShowCrystals = showCrystals;
            _config.Save();
            RecalculateBom();
        }
        ImGui.SameLine(0, 16);

        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "缺失");
        ImGui.SameLine();
        bool onlyMissing = _config.OnlyMissingMaterials;
        if (ImGui.Checkbox("仅缺失材料###Equip_OnlyMissing", ref onlyMissing))
        {
            _config.OnlyMissingMaterials = onlyMissing;
            _config.Save();
            RecalculateBom();
        }
        ImGui.SameLine();
        if (!_config.OnlyMissingMaterials) ImGui.BeginDisabled();
        bool hqOnly = _config.HqOnly;
        if (ImGui.Checkbox("仅计HQ###Equip_HqOnly", ref hqOnly))
        {
            _config.HqOnly = hqOnly;
            _config.Save();
            RecalculateBom();
        }
        if (!_config.OnlyMissingMaterials) ImGui.EndDisabled();

        ImGui.Separator();

        if (_showTreeView && _bomResult is not null)
        {
            _materialListWidget.DrawTree(_bomResult);
        }
        else
        {
            _materialListWidget.DrawMaterialPanel(_materialSummary, _craftSteps, ImGui.GetContentRegionAvail().Y, _bomResult);
        }

        DrawFavPopup();
    }

    /// <summary>
    /// 获取当前选中的制作目标列表。
    /// </summary>
    public List<CraftTarget> GetSelectedTargets() => _selectedItems.ToList();

    /// <summary>设置当前加载的收藏/推荐名称。</summary>
    public void SetLoadedFavName(string name) => _loadedFavName = name;

    /// <summary>
    /// 从外部添加制作目标（如从收藏推荐 Tab 加载）。
    /// </summary>
    /// <param name="targets">要添加的制作目标列表。</param>
    public void AddTargets(List<CraftTarget> targets)
    {
        foreach (var target in targets)
        {
            // 去重：如果已存在相同 ItemId，增加数量
            var existing = _selectedItems.Find(t => t.ItemId == target.ItemId);
            if (existing is not null)
            {
                existing.Quantity += target.Quantity;
            }
            else
            {
                _selectedItems.Add(new CraftTarget
                {
                    ItemId = target.ItemId,
                    ItemName = target.ItemName,
                    Quantity = target.Quantity,
                    Type = target.Type
                });
            }
        }

        RecalculateBom();
    }

    /// <summary>清空已选项目。</summary>
    public void ClearSelection()
    {
        _selectedItems.Clear();
        RecalculateBom();
    }

    /// <summary>
    /// 获取动态版本列表，从游戏数据中提取实际存在的版本。
    /// </summary>
    private List<(string Label, int Value)> GetVersionList()
    {
        if (_versionListInitialized && _cachedVersionList is not null)
        {
            return _cachedVersionList;
        }

        var versions = new List<(string Label, int Value)> { ("全部", 0) };

        // 从 EquipmentRepository 获取实际存在的版本列表
        var patchVersions = _equipRepo.GetAvailablePatchVersions();

        // 按版本号升序排列后添加到列表
        foreach (var pv in patchVersions.OrderBy(v => v))
        {
            string label = FormatPatchVersion(pv);
            versions.Add((label, pv));
        }

        _cachedVersionList = versions;
        _versionListInitialized = true;

        return versions;
    }

    /// <summary>
    /// 格式化版本号为显示字符串。
    /// 例如 70 → "7.0", 71 → "7.1", 72 → "7.2"
    /// </summary>
    private static string FormatPatchVersion(int patchVersion)
    {
        if (patchVersion >= 100)
        {
            int major = patchVersion / 100;
            int minor = patchVersion % 100;
            return $"{major}.{minor:D2}";
        }
        int maj = patchVersion / 10;
        int min = patchVersion % 10;
        return $"{maj}.{min}";
    }

    /// <summary>
    /// 绘制版本筛选下拉框。
    /// 动态获取游戏中的版本列表，默认选中最新版本。
    /// </summary>
    private void DrawVersionFilter()
    {
        var versions = GetVersionList();

        ImGui.Text("版本筛选:");
        ImGui.SameLine();

        string currentLabel = versions.FirstOrDefault(v => v.Value == _versionFilter).Label ?? "全部";

        if (ImGui.BeginCombo("###VersionFilter", currentLabel))
        {
            for (int i = 0; i < versions.Count; i++)
            {
                bool isSelected = _versionFilter == versions[i].Value;
                if (ImGui.Selectable($"{versions[i].Label}###Ver_{i}", isSelected))
                {
                    _versionFilter = versions[i].Value;
                    RefreshEquipmentList();
                }
            }

            ImGui.EndCombo();
        }
    }

    /// <summary>
    /// 绘制角色分组 + 职业图标选择器（参考 HQHelper JobPanel 风格）。
    /// 每个角色分组一行：[分组图标] 职业图标按钮×N。
    /// </summary>
    private void DrawJobSelector()
    {
        var groups = RoleGroupDefinitions.RoleGroups;
        var iconSize = new Vector2(26, 26);

        for (int gi = 0; gi < groups.Length; gi++)
        {
            var group = groups[gi];
            var isGroupActive = _selectedRoleGroup == group;

            ImGui.PushID($"Group_{gi}");

            // 分组图标（16×16）
            var roleIcon = _jobIconService.GetRoleGroupIcon(group.EnglishName);
            if (roleIcon.Handle != 0)
            {
                ImGui.Image(roleIcon, new Vector2(16, 16));
                ImGui.SameLine(0, 4);
            }

            // 分组名称标签
            var labelColor = isGroupActive
                ? new Vector4(0.4f, 0.8f, 1.0f, 1.0f)
                : new Vector4(0.7f, 0.7f, 0.7f, 1.0f);
            ImGui.TextColored(labelColor, group.DisplayName);
            ImGui.SameLine(0, 6);

            // 每个职业一个图标按钮
            for (int ji = 0; ji < group.Jobs.Length; ji++)
            {
                var job = group.Jobs[ji];
                var isJobSelected = _selectedClassJobId == job.ClassJobId;

                ImGui.PushID($"Job_{job.ClassJobId}");

                if (isJobSelected)
                {
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2.0f);
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.45f, 0.75f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.35f, 0.55f, 0.85f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 1.0f, 0.3f, 1.0f));
                }

                var jobIcon = _jobIconService.GetJobIcon(job.ClassJobId);
                bool clicked;
                if (jobIcon.Handle != 0)
                {
                    clicked = ImGui.ImageButton(jobIcon, iconSize);
                }
                else
                {
                    clicked = ImGui.Button($"{job.Name[0]}###FallbackJob_{job.ClassJobId}", iconSize);
                }

                if (clicked)
                    SelectJob(job.ClassJobId, group);

                if (isJobSelected)
                {
                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor(3);
                }

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(job.Name);

                ImGui.PopID();

                if (ji < group.Jobs.Length - 1)
                    ImGui.SameLine(0, 2);
            }

            ImGui.PopID();
        }
    }

    /// <summary>
    /// 选择角色分组（不改变当前职业）。
    /// </summary>
    private void SelectRoleGroup(RoleGroup group)
    {
        _selectedRoleGroup = group;
        if (group.Jobs.Length > 0 && !group.Jobs.Any(j => j.ClassJobId == _selectedClassJobId))
        {
            _selectedClassJobId = group.Jobs[0].ClassJobId;
        }

        AutoSelectBestVersion(group);
        RefreshEquipmentList();
    }

    /// <summary>
    /// 选择职业并刷新装备列表，同时更新角色分组和联动版本。
    /// </summary>
    private void SelectJob(uint classJobId, RoleGroup? group = null)
    {
        _selectedClassJobId = classJobId;

        // 更新角色分组（从图标点击时传入，或根据 classJobId 反查）
        if (group is not null)
        {
            _selectedRoleGroup = group;
        }
        else
        {
            // 反查所属角色分组
            var found = RoleGroupDefinitions.RoleGroups
                .FirstOrDefault(g => g.Jobs.Any(j => j.ClassJobId == classJobId));
            if (found is not null)
                _selectedRoleGroup = found;
        }

        // 联动：切换职业时自动选择该职业有装备的最佳版本
        if (_selectedRoleGroup is not null)
        {
            AutoSelectBestVersion(_selectedRoleGroup);
        }

        RefreshEquipmentList();
    }

    /// <summary>
    /// 绘制装备按大类分类展示（主副武器 / 防具 / 首饰）。
    /// 每个大类使用可折叠的树节点，展开后显示各槽位的装备列表。
    /// </summary>
    private void DrawEquipmentBySlot()
    {
        if (_selectedRoleGroup is null || _selectedClassJobId == 0)
        {
            return;
        }

        if (_groupedEquipment.Count == 0)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1f), "该职业无可制作装备");
            return;
        }

        foreach (var (categoryName, slotGroups) in EquipmentSlotGroups.EquipmentCategories)
        {
            bool hasAnyItem = false;
            foreach (var group in slotGroups)
            {
                foreach (var slot in group.Slots)
                {
                    if (_groupedEquipment.TryGetValue(slot, out var items) && items.Count > 0)
                    {
                        hasAnyItem = true;
                        break;
                    }
                }
                if (hasAnyItem) break;
            }

            if (!hasAnyItem) continue;

            bool isOpen = ImGui.TreeNodeEx($"{categoryName}###Category_{categoryName}",
                ImGuiTreeNodeFlags.DefaultOpen);

            if (isOpen)
            {
                foreach (var group in slotGroups)
                {
                    _slotGroupWidget.Draw(group, _groupedEquipment, _selectedItems, () => RecalculateBom());
                }
                ImGui.TreePop();
            }
        }
    }

    /// <summary>
    /// 绘制一键添加按钮区。
    /// </summary>
    private void DrawQuickAddButtons()
    {
        int? patchVersion = _versionFilter > 0 ? _versionFilter : null;
        _quickAddWidget.Draw(_selectedRoleGroup, _selectedClassJobId, patchVersion, targets =>
        {
            AddTargets(targets);
        });
    }

    /// <summary>
    /// 刷新装备列表，根据当前选中的角色分组和职业重新查询。
    /// </summary>
    private void RefreshEquipmentList()
    {
        if (_selectedRoleGroup is null || _selectedClassJobId == 0)
        {
            _groupedEquipment = [];
            return;
        }

        int? patchVersion = _versionFilter > 0 ? _versionFilter : null;
        _groupedEquipment = _equipRepo.GetEquipmentGroupedBySlot(_selectedRoleGroup, _selectedClassJobId, patchVersion);

        // 不再清除已选物品——切换职业应保留跨职业制作清单
        // var validItemIds = _groupedEquipment.Values.SelectMany(list => list).Select(e => e.ItemId).ToHashSet();
        // _selectedItems.RemoveAll(t => !validItemIds.Contains(t.ItemId));

        RecalculateBom();
    }

    /// <summary>
    /// 自动选择当前职业分组的最佳装备版本。
    /// 使用 GetAvailablePatchVersions（已排除 7.05），从最新版本开始检测。
    /// </summary>
    private void AutoSelectBestVersion(RoleGroup role)
    {
        if (_selectedClassJobId == 0) return;

        // 使用装备版本列表（不含 7.05）
        var versions = _equipRepo.GetAvailablePatchVersions().OrderByDescending(v => v).ToList();
        foreach (var ver in versions)
        {
            var slotData = _equipRepo.GetEquipmentGroupedBySlot(role, _selectedClassJobId, ver);
            if (slotData.Values.Any(list => list.Count > 0))
            {
                _versionFilter = ver;
                _cachedVersionList = null;
                _versionListInitialized = false;
                return;
            }
        }
        _versionFilter = 0;
    }

    /// <summary>
    /// 重新计算 BOM 和材料汇总。
    /// </summary>
    private void RecalculateBom()
    {
        if (_selectedItems.Count == 0)
        {
            _bomResult = null;
            _materialSummary = [];
            _craftSteps = [];
            return;
        }

        // 对每个选中物品展开 BOM，合并到统一树
        var combinedRoot = new BomNode
        {
            ItemId = 0,
            ItemName = "汇总",
            Quantity = 1,
            Depth = -1
        };

        foreach (var target in _selectedItems)
        {
            var bomTree = _bomExpander.Expand(target.ItemId, target.Quantity);
            combinedRoot.Children.Add(bomTree);
        }

        _bomResult = combinedRoot;
        _materialSummary = _materialAggregator.Aggregate(combinedRoot, _config.ShowCrystals);
        _craftSteps = _craftOrderCalculator.CalculateOrder(combinedRoot);
    }

    /// <summary>
    /// 将当前选择保存为收藏清单。
    /// </summary>
    private void SaveAsFavorite()
    {
        if (_selectedItems.Count == 0) return;

        var name = string.IsNullOrWhiteSpace(_favName)
            ? $"装备 {DateTime.Now:yyyyMMdd_HHmmss}" : _favName;

        var preset = new FavoritePreset
        {
            Name = name,
            Selections = _selectedItems.Select(t => new EquipmentSelection(t.ItemId, t.ItemName, t.Quantity)).ToList(),
            CreatedAt = DateTime.Now
        };

        var existing = _config.FavoritePresets.FindIndex(p => p.Name == name);
        if (existing >= 0)
            _config.FavoritePresets[existing] = preset;
        else
            _config.FavoritePresets.Add(preset);
        _config.Save();
        _favName = string.Empty;
        _log.Information($"已保存装备收藏: {name} ({_selectedItems.Count}件)");
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
}
