using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace CraftFlow.Services;

/// <summary>
/// 职业图标服务，负责加载 Companion 风格职业图标和角色分组图标（PNG 文件）。
/// - 职业图标：job-icons/ 子目录（优先），失败回退到游戏内职业图标
/// - 分组图标：role-icons/ 子目录（来自 HQHelper misc 图标集）
/// </summary>
public sealed class JobIconService : IDisposable
{
    private readonly ITextureProvider _textureProvider;
    private readonly string _pluginDirectory;
    private readonly string _jobIconDir;
    private readonly string _roleIconDir;
    private readonly IPluginLog _log;

    // companion PNG 纹理缓存（SharedImmediateTexture 防止 GC）
    private readonly Dictionary<uint, ISharedImmediateTexture> _iconSharedCache = [];
    // 游戏图标纹理缓存
    private readonly Dictionary<uint, IDalamudTextureWrap> _gameIconCache = [];
    // 分组图标纹理缓存
    private readonly Dictionary<string, ISharedImmediateTexture> _roleIconSharedCache = [];

    /// <summary>
    /// Companion 图标文件名映射表（ClassJobId → 文件名不含扩展名）。
    /// </summary>
    private static readonly Dictionary<uint, string> ClassJobToCompanionIcon = new()
    {
        // 防护职业
        { 19, "paladin" }, { 21, "warrior" }, { 32, "darkknight" }, { 37, "gunbreaker" },
        // 治疗职业
        { 24, "whitemage" }, { 28, "scholar" }, { 33, "astrologian" }, { 40, "sage" },
        // 制敌DPS
        { 22, "dragoon" }, { 39, "reaper" },
        // 强袭DPS
        { 20, "monk" }, { 34, "samurai" },
        // 游击DPS
        { 30, "ninja" }, { 41, "viper" },
        // 远敏DPS
        { 23, "bard" }, { 31, "machinist" }, { 38, "dancer" },
        // 法系DPS
        { 25, "blackmage" }, { 27, "summoner" }, { 35, "redmage" }, { 42, "pictomancer" },
        // 生产职业
        { 8, "carpenter" }, { 9, "blacksmith" }, { 10, "armorer" }, { 11, "goldsmith" },
        { 12, "leatherworker" }, { 13, "weaver" }, { 14, "alchemist" }, { 15, "culinarian" },
        // 采集职业
        { 16, "miner" }, { 17, "botanist" }, { 18, "fisher" },
    };

    /// <summary>
    /// 角色分组图标映射表（EnglishName → 文件名）。
    /// </summary>
    private static readonly Dictionary<string, string> RoleGroupToIconFile = new()
    {
        { "Tank", "clear_tank" },
        { "Healer", "clear_healer" },
        { "Maiming DPS", "clear_dps" },
        { "Striking DPS", "clear_dps" },
        { "Scouting DPS", "clear_dps" },
        { "Ranged DPS", "clear_ranged" },
        { "Casting DPS", "clear_dps_magic" },
        { "Gatherer", "clear_dol" },
        { "Crafter", "clear_doh" },
    };

    public JobIconService(ITextureProvider textureProvider, string pluginDirectory, IPluginLog log)
    {
        _textureProvider = textureProvider;
        _pluginDirectory = pluginDirectory;
        _log = log;

        _jobIconDir = Path.Combine(pluginDirectory, "job-icons");
        _roleIconDir = Path.Combine(pluginDirectory, "role-icons");
        _log.Debug($"JobIconService 初始化: dir={pluginDirectory}");
        _log.Debug($"  jobIcons: {_jobIconDir} exists={Directory.Exists(_jobIconDir)} files={(Directory.Exists(_jobIconDir) ? Directory.GetFiles(_jobIconDir, "*.png").Length : 0)}");
        _log.Debug($"  roleIcons: {_roleIconDir} exists={Directory.Exists(_roleIconDir)} files={(Directory.Exists(_roleIconDir) ? Directory.GetFiles(_roleIconDir, "*.png").Length : 0)}");
    }

    /// <summary>
    /// 获取职业图标的 ImTextureID。
    /// 优先加载 companion PNG，失败则回退到游戏内职业图标。
    /// </summary>
    public ImTextureID GetJobIcon(uint classJobId)
    {
        // 1. 尝试 companion PNG
        if (ClassJobToCompanionIcon.TryGetValue(classJobId, out var fileName))
        {
            var filePath = Path.Combine(_jobIconDir, fileName + ".png");

            if (_iconSharedCache.TryGetValue(classJobId, out var cached))
            {
                var wrap = cached.GetWrapOrDefault();
                if (wrap is not null) return wrap.Handle;
            }

            if (File.Exists(filePath))
            {
                try
                {
                    var shared = _textureProvider.GetFromFile(filePath);
                    var wrap = shared.GetWrapOrDefault();
                    if (wrap is not null)
                    {
                        _iconSharedCache[classJobId] = shared;
                        _log.Debug($"Companion 图标加载成功: ClassJobId={classJobId} file={fileName} Handle={wrap.Handle}");
                        return wrap.Handle;
                    }
                    _log.Debug($"Companion 图标 wrap 为 null: {filePath}");
                }
                catch (Exception ex)
                {
                    _log.Debug($"Companion 图标加载异常: {ex.Message}");
                }
            }
            else
            {
                _log.Debug($"Companion 图标文件不存在: {filePath}");
            }
        }

        // 2. 回退到游戏内图标
        return GetGameIcon(classJobId);
    }

    /// <summary>
    /// 获取游戏内职业图标。
    /// </summary>
    private ImTextureID GetGameIcon(uint classJobId)
    {
        if (_gameIconCache.TryGetValue(classJobId, out var cached))
            return cached.Handle;

        var iconId = GetClassJobIconId(classJobId);
        if (iconId == 0) return new ImTextureID(0);

        try
        {
            var shared = _textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
            var wrap = shared.GetWrapOrDefault();
            if (wrap is not null)
            {
                _gameIconCache[classJobId] = wrap;
                return wrap.Handle;
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"游戏图标加载失败 ClassJobId={classJobId}: {ex.Message}");
        }

        return new ImTextureID(0);
    }

    /// <summary>
    /// 获取角色分组图标的 ImTextureID。
    /// </summary>
    public ImTextureID GetRoleGroupIcon(string englishName)
    {
        if (_roleIconSharedCache.TryGetValue(englishName, out var cached))
        {
            var wrap = cached.GetWrapOrDefault();
            if (wrap is not null) return wrap.Handle;
        }

        if (!RoleGroupToIconFile.TryGetValue(englishName, out var fileName))
            return new ImTextureID(0);

        var filePath = Path.Combine(_roleIconDir, fileName + ".png");

        if (File.Exists(filePath))
        {
            try
            {
                var shared = _textureProvider.GetFromFile(filePath);
                var wrap = shared.GetWrapOrDefault();
                if (wrap is not null)
                {
                    _roleIconSharedCache[englishName] = shared;
                    return wrap.Handle;
                }
            }
            catch (Exception ex)
            {
                _log.Debug($"分组图标加载异常: {ex.Message}");
            }
        }

        return new ImTextureID(0);
    }

    /// <summary>
    /// 职业图标 ID 映射（战斗职业用职业水晶，生产/采集用工具图标）。
    /// </summary>
    private static uint GetClassJobIconId(uint classJobId) => classJobId switch
    {
        // 坦克
        19 => 62401, 21 => 62402, 32 => 62403, 37 => 62404,
        // 治疗
        24 => 62501, 28 => 62502, 33 => 62503, 40 => 62504,
        // 近战/远程/法系
        22 => 62301, 20 => 62302, 30 => 62303, 34 => 62304,
        23 => 62305, 31 => 62306, 25 => 62307, 27 => 62308,
        // 扩展
        35 => 62310, 39 => 62312, 41 => 62313, 42 => 62314,
        // 生产
        8 => 26038, 9 => 26039, 10 => 26040, 11 => 26041,
        12 => 26042, 13 => 26043, 14 => 26044, 15 => 26045,
        // 采集
        16 => 26046, 17 => 26047, 18 => 26048,
        _ => 0,
    };

    public void Dispose()
    {
        _iconSharedCache.Clear();
        _roleIconSharedCache.Clear();
        foreach (var wrap in _gameIconCache.Values)
            wrap.Dispose();
        _gameIconCache.Clear();
    }
}
