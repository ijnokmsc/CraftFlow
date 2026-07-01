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
    private readonly ItemIconService _itemIconService;

    private TabType _currentTab = TabType.Equipment;

    // 通知日志
    private readonly List<LogEntry> _notificationLog = new();
    private bool _logExpanded;
    private const int MaxLogEntries = 200;

    private static string GetVersionString()
    {
        try
        {
            var ver = typeof(Plugin).Assembly.GetName().Version;
            return $"v{ver?.Major}.{ver?.Minor}.{ver?.Build}";
        }
        catch
        {
            return "v?.?.?";
        }
    }

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
        CraftOrchestrator craftOrchestrator,
        CraftProgressWindow craftProgressWindow,
        JobIconService jobIconService,
        ItemIconService itemIconService,
        LuminaCache luminaCache,
        IPluginLog log)
        : base($"CraftFlow {GetVersionString()}###CraftFlowMainWindow")
    {
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
        _itemIconService = itemIconService;

        var materialListWidget = new MaterialListWidget(
            gbrIpc, artisanIpc, ipcChecker, craftOrchestrator, config, log, _itemIconService);
        // 订阅日志事件（解耦：Widget 不再反向持有 MainWindow 引用）
        materialListWidget.OnLog += (msg, level) => AddLog(msg, level);
        // 设置制作开始回调：显示进度窗口，隐藏主窗口
        materialListWidget.OnStartCrafting = (steps) =>
        {
            _craftProgressWindow.StartCrafting(steps);
            IsOpen = false;
        };

        _equipmentTab = new EquipmentTab(
            equipRepo, equipSetService, bomExpander, materialAggregator, craftOrderCalculator,
            recipeRepo, materialListWidget, config, log, _jobIconService, _itemIconService);

        _consumableTab = new ConsumableTab(
            bomExpander, materialAggregator, craftOrderCalculator, recipeRepo,
            materialListWidget, config, luminaCache, _itemIconService, log);

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
        DrawTabBar();
        DrawContentArea();
        DrawStatusBar();
        // DrawLogPanel(); // 通知日志面板已移除
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
        float contentHeight = ImGui.GetContentRegionAvail().Y - 55f;

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

    private void DrawLogPanel()
    {
        ImGui.Separator();

        bool expanded = ImGui.CollapsingHeader("通知日志", ImGuiTreeNodeFlags.DefaultOpen);
        if (expanded != _logExpanded)
        {
            _logExpanded = expanded;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("清空") && _notificationLog.Count > 0)
        {
            _notificationLog.Clear();
        }

        if (!_logExpanded) return;

        var availHeight = ImGui.GetContentRegionAvail().Y;
        var panelHeight = Math.Max(availHeight, 20f);

        ImGui.BeginChild("LogContent", new Vector2(0, panelHeight), true);
        foreach (var entry in _notificationLog)
        {
            var color = entry.Level switch
            {
                LogLevel.Success => new Vector4(0.3f, 0.9f, 0.3f, 1f),
                LogLevel.Warning => new Vector4(1f, 0.85f, 0.2f, 1f),
                LogLevel.Error => new Vector4(0.9f, 0.3f, 0.2f, 1f),
                _ => new Vector4(0.85f, 0.85f, 0.85f, 1f)
            };
            ImGui.TextColored(color, $"[{entry.Time:HH:mm:ss}] {entry.Message}");
        }
        if (_notificationLog.Count > 0)
            ImGui.SetScrollHereY(1.0f);
        ImGui.EndChild();
    }

    public void AddLog(string message, LogLevel level = LogLevel.Info)
    {
        _notificationLog.Add(new LogEntry(DateTime.Now, message, level));
        while (_notificationLog.Count > MaxLogEntries)
            _notificationLog.RemoveAt(0);
    }
}

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class LogEntry
{
    public DateTime Time { get; }
    public string Message { get; }
    public LogLevel Level { get; }

    public LogEntry(DateTime time, string message, LogLevel level)
    {
        Time = time;
        Message = message;
        Level = level;
    }
}
