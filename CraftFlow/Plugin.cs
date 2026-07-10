using System;
using Dalamud.Game.Command;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using CraftFlow.Commands;
using CraftFlow.Config;
using CraftFlow.Data.GameData;
using CraftFlow.IPC;
using CraftFlow.Services;
using CraftFlow.UI;

namespace CraftFlow;

/// <summary>
/// CraftFlow 插件入口，实现 IDalamudPlugin 接口。
/// Dalamud API 15 使用 [PluginService] 静态属性注入替代构造函数注入。
/// 框架通过 Source Generator 自动填充标记了 [PluginService] 的属性。
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    // API 15: 通过 [PluginService] 静态属性注入 Dalamud 核心服务
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    private readonly LuminaCache _luminaCache;
    private readonly RecipeRepository _recipeRepo;
    private readonly EquipmentRepository _equipRepo;
    private readonly BomExpander _bomExpander;
    private readonly MaterialAggregator _materialAggregator;
    private readonly CraftOrderCalculator _craftOrderCalculator;
    private readonly EquipmentSetService _equipSetService;
    private readonly PresetService _presetService;
    private readonly GbrIpcClient _gbrIpc;
    private readonly ArtisanIpcClient _artisanIpc;
    private readonly IpcAvailabilityChecker _ipcChecker;
    private readonly PluginConfig _config;
    private readonly CraftProgressManager _progressManager;
    private readonly CraftOrchestrator _craftOrchestrator;
    private readonly CollectibleCalculator _collectibleCalculator;
    private readonly CraftProgressWindow _craftProgressWindow;
    private readonly JobIconService _jobIconService;
    private readonly ItemIconService _itemIconService;
    private readonly MainWindow _mainWindow;
    private readonly WindowSystem _windowSystem;
    private readonly Action _openMainUiHandler;
    private readonly Action _openConfigUiHandler;
    private readonly Action _craftingEndedHandler;
    private readonly PluginCommands _commands;

    /// <summary>
    /// 无参构造函数，Dalamud API 15 通过 [PluginService] 注入依赖。
    /// 在构造函数执行时，所有 [PluginService] 属性已被框架填充。
    /// </summary>
    public Plugin()
    {
        // 配置 — 使用静态 PluginInterface 属性
        _config = new PluginConfig(PluginInterface);

        // 数据层 — 使用静态 DataManager / Log 属性
        _luminaCache = new LuminaCache(DataManager, Log);
        _luminaCache.Init();
        _recipeRepo = new RecipeRepository(_luminaCache, Log);
        _equipRepo = new EquipmentRepository(_luminaCache, Log);

        // 业务逻辑层
        _bomExpander = new BomExpander(_recipeRepo, Log);
        _materialAggregator = new MaterialAggregator(_recipeRepo, _luminaCache, Log);
        _craftOrderCalculator = new CraftOrderCalculator(Log);
        _equipSetService = new EquipmentSetService(_equipRepo, _recipeRepo, Log);
        _presetService = new PresetService(_equipRepo, _config, Log);

        // IPC 层
        _gbrIpc = new GbrIpcClient(PluginInterface, Log);
        _artisanIpc = new ArtisanIpcClient(PluginInterface, Log);
        _ipcChecker = new IpcAvailabilityChecker(_gbrIpc, _artisanIpc);

        // 延迟订阅 IPC — 使用 Framework.Update 帧计数退避，避免时序问题
        _gbrIpc.DelayedSubscribe(Framework);
        DelayedSubscribeArtisan();

        // 制作进度管理器（从 MainWindow 中提升）
        _progressManager = new CraftProgressManager(_config, Log);

        // 制作编排服务（从 MaterialListWidget.CraftWithArtisan 提取，P4 重构）
        _craftOrchestrator = new CraftOrchestrator(_config, Log, _progressManager, _artisanIpc);

        // 收藏品计算服务（收藏品制作 Tab 专用）
        _collectibleCalculator = new CollectibleCalculator(_recipeRepo, _bomExpander, Log);

        // 职业图标服务（外部 PNG 文件）
        // API 15: PluginInterface.AssemblyLocation → 插件 DLL 路径 → 其所在目录即为插件目录
        var pluginDir = Path.GetDirectoryName(PluginInterface.AssemblyLocation.FullName) ?? "";
        Log.Information($"[CraftFlow] PluginDirectory={pluginDir}");
        _jobIconService = new JobIconService(TextureProvider, pluginDir, Log);

        _itemIconService = new ItemIconService(TextureProvider, _luminaCache, Log);

        // 进度弹窗（在 MainWindow 之前创建，因为 MainWindow 需要引用它）
        _craftProgressWindow = new CraftProgressWindow(_progressManager, _artisanIpc, Log);

        // UI 层 - WindowSystem 管理 MainWindow 的 ImGui.Begin/End 生命周期
        _mainWindow = new MainWindow(
            this,
            _bomExpander,
            _materialAggregator,
            _craftOrderCalculator,
            _recipeRepo,
            _equipRepo,
            _equipSetService,
            _presetService,
            _config,
            _gbrIpc,
            _artisanIpc,
            _ipcChecker,
            _progressManager,
            _craftOrchestrator,
            _collectibleCalculator,
            _craftProgressWindow,
            _jobIconService,
            _itemIconService,
            _luminaCache,
            Log
        );
        // CraftingEnded 在 _mainWindow 赋值后再订阅，避免 CS8602 空引用警告
        _craftingEndedHandler = () => _mainWindow.IsOpen = true;
        _craftProgressWindow.CraftingEnded += _craftingEndedHandler;

        _windowSystem = new WindowSystem("CraftFlow_WindowSystem");
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_craftProgressWindow);
        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _openMainUiHandler = () => _mainWindow.IsOpen = true;
        PluginInterface.UiBuilder.OpenMainUi += _openMainUiHandler;
        _openConfigUiHandler = () => _mainWindow.IsOpen = true;
        PluginInterface.UiBuilder.OpenConfigUi += _openConfigUiHandler;

        // 命令 — 使用静态 CommandManager 属性
        _commands = new PluginCommands(
            CommandManager,
            _mainWindow,
            Log
        );

        Log.Information("CraftFlow 插件初始化完成");
    }

    /// <summary>
    /// 获取 GBR IPC 客户端实例。
    /// </summary>
    public GbrIpcClient GbrIpc => _gbrIpc;

    /// <summary>
    /// 获取 Artisan IPC 客户端实例。
    /// </summary>
    public ArtisanIpcClient ArtisanIpc => _artisanIpc;

    /// <summary>
    /// 获取配方仓库实例。
    /// </summary>
    public RecipeRepository RecipeRepo => _recipeRepo;

    /// <summary>
    /// 获取装备仓库实例。
    /// </summary>
    public EquipmentRepository EquipRepo => _equipRepo;

    /// <summary>
    /// 获取插件配置实例。
    /// </summary>
    public PluginConfig Config => _config;

    /// <summary>
    /// Artisan IPC 延迟订阅，使用与 GBR 相同的帧计数退避策略。
    /// </summary>
    private void DelayedSubscribeArtisan()
    {
        int retryCount = 0;
        const int maxRetries = 10;
        int nextRetryFrame = 1;
        int currentFrame = 0;

        void OnFrameworkUpdate(IFramework fw)
        {
            currentFrame++;
            if (_artisanIpc.IsAvailable || retryCount >= maxRetries)
            {
                fw.Update -= OnFrameworkUpdate;

                if (!_artisanIpc.IsAvailable)
                {
                    Log.Information($"Artisan IPC 延迟订阅结束：已重试 {retryCount} 次，Artisan 未安装");
                }

                return;
            }

            if (currentFrame < nextRetryFrame)
            {
                return;
            }

            retryCount++;
            Log.Debug($"Artisan IPC 延迟订阅第 {retryCount} 次重试 (帧 {currentFrame})");

            try
            {
                _artisanIpc.Subscribe();
            }
            catch
            {
                // 订阅异常，继续重试
            }

            nextRetryFrame = currentFrame + (1 << Math.Min(retryCount, 8));
        }

        Framework.Update += OnFrameworkUpdate;
        Log.Information("Artisan IPC 已注册延迟订阅，等待 Artisan 加载");
    }

    /// <summary>
    /// 释放所有资源，注销命令和 IPC 订阅。
    /// </summary>
    public void Dispose()
    {
        _commands.Dispose();
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= _openMainUiHandler;
        PluginInterface.UiBuilder.OpenConfigUi -= _openConfigUiHandler;
        _craftProgressWindow.CraftingEnded -= _craftingEndedHandler;
        _jobIconService.Dispose();
        _artisanIpc.Dispose();
        _gbrIpc.Dispose();
        Log.Information("CraftFlow 插件已卸载");
    }
}
