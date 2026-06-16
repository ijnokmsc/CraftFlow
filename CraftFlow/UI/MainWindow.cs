using System;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using CraftFlow.Config;
using CraftFlow.Data.GameData;
using CraftFlow.Data.Models;
using CraftFlow.IPC;
using CraftFlow.Services;
using CraftFlow.UI.Tabs;
using CraftFlow.UI.Widgets;

namespace CraftFlow.UI;

public sealed class MainWindow : Window
{
    private readonly Plugin _plugin;
    private readonly BomExpander _bomExpander;
    private readonly MaterialAggregator _materialAggregator;
    private readonly CraftOrderCalculator _craftOrderCalculator;
    private readonly RecipeRepository _recipeRepo;
    private readonly EquipmentRepository _equipRepo;
    private readonly EquipmentSetService _equipSetService;
    private readonly PresetService _presetService;
    private readonly PluginConfig _config;
    private readonly GbrIpcClient _gbrIpc;
    private readonly ArtisanIpcClient _artisanIpc;
    private readonly IpcAvailabilityChecker _ipcChecker;
    private readonly IPluginLog _log;
    private readonly LuminaCache _luminaCache;

    private readonly EquipmentTab _equipmentTab;
    private readonly ConsumableTab _consumableTab;
    private readonly FavoritesTab _favoritesTab;
    private readonly RecommendationTab _recommendationTab;
    private readonly CraftProgressManager _progressManager;
    private readonly CraftProgressWindow _craftProgressWindow;
    private readonly StatusBarWidget _statusBar;
    private readonly JobIconService _jobIconService;

    private TabType _currentTab = TabType.Equipment;

    public MainWindow(
        Plugin plugin,
        BomExpander bomExpander,
        MaterialAggregator materialAggregator,
        CraftOrderCalculator craftOrderCalculator,
        RecipeRepository recipeRepo,
        EquipmentRepository equipRepo,
        EquipmentSetService equipSetService,
        PresetService presetService,
        PluginConfig config,
        GbrIpcClient gbrIpc,
        ArtisanIpcClient artisanIpc,
        IpcAvailabilityChecker ipcChecker,
        CraftProgressManager progressManager,
        CraftProgressWindow craftProgressWindow,
        JobIconService jobIconService,
        LuminaCache luminaCache,
        IPluginLog log)
        : base("CraftFlow###CraftFlowMainWindow")
    {
        try
        {
            var ver = typeof(Plugin).Assembly.GetName().Version;
            WindowName = $"CraftFlow v{ver?.Major}.{ver?.Minor}.{ver?.Build}###CraftFlowMainWindow";
        }
        catch
        {
            // 如果读取版本失败，使用默认标题
        }
        _plugin = plugin;
        _bomExpander = bomExpander;
        _materialAggregator = materialAggregator;
        _craftOrderCalculator = craftOrderCalculator;
        _recipeRepo = recipeRepo;
        _equipRepo = equipRepo;
        _equipSetService = equipSetService;
        _presetService = presetService;
        _config = config;
        _gbrIpc = gbrIpc;
        _artisanIpc = artisanIpc;
        _ipcChecker = ipcChecker;
        _progressManager = progressManager;
        _craftProgressWindow = craftProgressWindow;
        _log = log;
        _luminaCache = luminaCache;
        _jobIconService = jobIconService;

        var materialListWidget = new MaterialListWidget(gbrIpc, artisanIpc, ipcChecker, _progressManager, config, log);
        // 设置制作开始回调：显示进度窗口，隐藏主窗口
        materialListWidget.OnStartCrafting = (steps) =>
        {
            _craftProgressWindow.StartCrafting(steps);
            IsOpen = false;
        };

        _equipmentTab = new EquipmentTab(
            equipRepo, equipSetService, bomExpander, materialAggregator, craftOrderCalculator,
            recipeRepo, materialListWidget, config, log, _jobIconService);

        _consumableTab = new ConsumableTab(
            bomExpander, materialAggregator, craftOrderCalculator, recipeRepo,
            materialListWidget, config, luminaCache, log);

        _favoritesTab = new FavoritesTab(presetService, () => _equipmentTab.GetSelectedTargets(), log);
        _favoritesTab.SetTargetLoadedCallback((targets, name) =>
        {
            bool isConsumable = targets.Any(t => t.Type == TargetType.Consumable);
            _equipmentTab.ClearSelection();
            _consumableTab.ClearSelection();
            if (isConsumable)
            {
                _consumableTab.AddTargets(targets);
                _currentTab = TabType.Consumable;
            }
            else
            {
                _equipmentTab.SetLoadedFavName(name);
                _equipmentTab.AddTargets(targets);
                _currentTab = TabType.Equipment;
            }
        });

        _recommendationTab = new RecommendationTab(presetService, () => _equipmentTab.GetSelectedTargets(), log);
        _recommendationTab.SetTargetLoadedCallback((targets, name) =>
        {
            _equipmentTab.ClearSelection();
            _consumableTab.ClearSelection();
            _equipmentTab.SetLoadedFavName(name);
            _equipmentTab.AddTargets(targets);
            _currentTab = TabType.Equipment;
        });

        _statusBar = new StatusBarWidget(gbrIpc, artisanIpc, log);

        Size = new Vector2(870, 610);
        SizeCondition = ImGuiCond.Once;
    }

    public override void Draw()
    {
        // 不再有进度面板的特殊处理 — 进度窗口是独立的弹窗
        DrawTabBar();
        DrawContentArea();
        DrawStatusBar();
    }

    private void DrawTabBar()
    {
        var tabNames = new[] { " ⚔ 装备武器", " ♨ 食物药品", " ★ 收藏清单", " ▶ 推荐套装" };
        var tabTypes = new[] { TabType.Equipment, TabType.Consumable, TabType.Favorites, TabType.Recommendations };

        for (int i = 0; i < tabNames.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            bool isSelected = _currentTab == tabTypes[i];
            if (isSelected)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.8f, 1.0f));

            if (ImGui.Button($"{tabNames[i]}###Tab_{tabTypes[i]}"))
                _currentTab = tabTypes[i];

            if (isSelected)
                ImGui.PopStyleColor();
        }
        ImGui.Separator();
    }

    private void DrawContentArea()
    {
        float leftWidth = ImGui.GetContentRegionAvail().X * 0.45f;
        float rightWidth = ImGui.GetContentRegionAvail().X * 0.53f;
        float contentHeight = ImGui.GetContentRegionAvail().Y - 35f;

        ImGui.BeginChild("LeftPanel", new Vector2(leftWidth, contentHeight), true);
        switch (_currentTab)
        {
            case TabType.Equipment:
                _equipmentTab.DrawLeftPanel();
                break;
            case TabType.Consumable:
                _consumableTab.DrawLeftPanel();
                break;
            case TabType.Favorites:
                _favoritesTab.DrawLeftPanel();
                break;
            case TabType.Recommendations:
                _recommendationTab.DrawLeftPanel();
                break;
        }
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("RightPanel", new Vector2(rightWidth, contentHeight), true);
        switch (_currentTab)
        {
            case TabType.Equipment:
                _equipmentTab.DrawRightPanel();
                break;
            case TabType.Consumable:
                _consumableTab.DrawRightPanel();
                break;
            case TabType.Favorites:
                _favoritesTab.DrawRightPanel();
                break;
            case TabType.Recommendations:
                _recommendationTab.DrawRightPanel();
                break;
        }
        ImGui.EndChild();
    }

    private void DrawStatusBar()
    {
        ImGui.Separator();

        // 暂停进度信息压缩在一行：按钮自带总进度，Tooltip 显示物品详情
        if (_progressManager.HasIncompleteProgress)
        {
            var p = _progressManager.Progress!;
            var step = _progressManager.GetCurrentStep();

            if (ImGui.Button($"▶ 继续 ({p.CompletedSteps}/{p.TotalSteps})"))
            {
                _craftProgressWindow.ResumeCrafting();
                IsOpen = false;
            }
            if (step is not null && ImGui.IsItemHovered())
            {
                var tooltip = step.Quantity > 1
                    ? $"{step.ItemName} ({step.CompletedQuantity}/{step.Quantity})"
                    : step.ItemName;
                ImGui.SetTooltip($"已暂停: {tooltip}");
            }
            ImGui.SameLine();

            if (ImGui.Button("✕ 放弃"))
            {
                _progressManager.Clear();
            }
            ImGui.SameLine();
        }

        _statusBar.Draw();
    }
}
