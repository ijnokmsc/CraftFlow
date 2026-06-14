using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace CraftFlow.Services;

/// <summary>
/// 职业图标服务：从外部 PNG 文件加载职业/分组图标纹理。
/// PNG 文件存放在 pluginDirectory/job-icons/ 和 pluginDirectory/role-icons/ 目录下。
/// 正确做法：缓存 ISharedImmediateTexture（维持引用），每帧通过 GetWrapOrDefault() 获取 wrap。
/// </summary>
public sealed class JobIconService : IDisposable
{
    private readonly ITextureProvider _textureProvider;
    private readonly string _jobIconsDir;
    private readonly string _roleIconsDir;
    private readonly IPluginLog _log;

    // 缓存 ISharedImmediateTexture（维持引用，让 Dalamud 管理异步加载生命周期）
    private readonly Dictionary<uint, ISharedImmediateTexture> _jobSharedCache = new();
    private readonly Dictionary<string, ISharedImmediateTexture> _roleSharedCache = new();

    /// <summary>
    /// ClassJobId → 文件名（不含扩展名，对应 job-icons/*.png）。
    /// </summary>
    private static readonly Dictionary<uint, string> JobIconFileNames = new()
    {
        { 8,  "carpenter" },      // 刻木匠 CRP
        { 9,  "blacksmith" },     // 锻铁匠 BSM
        { 10, "armorer" },       // 铸甲匠 ARM
        { 11, "goldsmith" },     // 雕金匠 GSM
        { 12, "leatherworker" }, // 制革匠 LTW
        { 13, "weaver" },        // 裁衣匠 WVR
        { 14, "alchemist" },     // 炼金术士 ALC
        { 15, "culinarian" },    // 烹调师 CUL
        { 16, "miner" },         // 采矿工 MIN
        { 17, "botanist" },      // 园艺工 BTN
        { 18, "fisher" },        // 捕鱼人 FSH
        { 19, "paladin" },       // 骑士 PLD
        { 20, "monk" },          // 武僧 MNK
        { 21, "warrior" },       // 战士 WAR
        { 22, "dragoon" },       // 龙骑士 DRG
        { 23, "bard" },          // 吟游诗人 BRD
        { 24, "whitemage" },     // 白魔法师 WHM
        { 25, "blackmage" },     // 黑魔法师 BLM
        { 27, "summoner" },      // 召唤师 SMN
        { 28, "scholar" },       // 学者 SCH
        { 30, "ninja" },         // 忍者 NIN
        { 31, "machinist" },     // 机工士 MCH
        { 32, "darkknight" },    // 暗黑骑士 DRK
        { 33, "astrologian" },   // 占星术士 AST
        { 34, "samurai" },       // 武士 SAM
        { 35, "redmage" },       // 赤魔法师 RDM
        { 36, "bluemage" },      // 青魔法师 BLU
        { 37, "gunbreaker" },    // 绝枪战士 GNB
        { 38, "dancer" },        // 舞者 DNC
        { 39, "reaper" },        // 钐镰客 RPR
        { 40, "sage" },          // 贤者 SGE
        { 41, "viper" },         // 蝰蛇剑士 VPR
        { 42, "pictomancer" },   // 绘灵法师 PCT
    };

    /// <summary>
    /// 分组英文名 → 角色图标文件名（对应 role-icons/*.png）。
    /// </summary>
    private static readonly Dictionary<string, string> RoleIconFileNames = new()
    {
        { "Tank",           "clear_tank" },
        { "Healer",         "clear_healer" },
        { "Maiming DPS",   "clear_dps" },
        { "Striking DPS",  "clear_dps" },
        { "Scouting DPS",   "clear_dps" },
        { "Ranged DPS",    "clear_ranged" },
        { "Casting DPS",   "clear_dps_magic" },
        { "Gatherer",       "clear_dol" },
        { "Crafter",        "clear_doh" },
    };

    public JobIconService(ITextureProvider textureProvider, string pluginDirectory, IPluginLog log)
    {
        _textureProvider = textureProvider;
        _jobIconsDir  = Path.Combine(pluginDirectory, "job-icons");
        _roleIconsDir = Path.Combine(pluginDirectory, "role-icons");
        _log = log;

        _log.Information($"[JobIconService] 初始化 pluginDir={pluginDirectory}");
        _log.Information($"[JobIconService] jobIconsDir={_jobIconsDir} exists={Directory.Exists(_jobIconsDir)}");
        _log.Information($"[JobIconService] roleIconsDir={_roleIconsDir} exists={Directory.Exists(_roleIconsDir)}");
        if (Directory.Exists(_jobIconsDir))
            _log.Information($"[JobIconService] job-icons 文件数={Directory.GetFiles(_jobIconsDir, "*.png").Length}");
        if (Directory.Exists(_roleIconsDir))
            _log.Information($"[JobIconService] role-icons 文件数={Directory.GetFiles(_roleIconsDir, "*.png").Length}");
    }

    /// <summary>
    /// 获取职业图标纹理 ID。若纹理尚未加载完成返回 0，下一帧重试。
    /// </summary>
    public ImTextureID GetJobIcon(uint classJobId)
    {
        if (classJobId == 0)
            return new ImTextureID(0);

        try
        {
            // 确保 ISharedImmediateTexture 已创建（只创建一次）
            if (!_jobSharedCache.TryGetValue(classJobId, out var shared))
            {
                shared = CreateJobShared(classJobId);
                if (shared == null)
                    return new ImTextureID(0);
                _jobSharedCache[classJobId] = shared;
            }

            // 每帧通过 shared 获取 wrap；纹理加载完成后 GetWrapOrDefault() 返回非 null
            var wrap = shared.GetWrapOrDefault();
            if (wrap == null)
                return new ImTextureID(0);

            return wrap.Handle;
        }
        catch (Exception ex)
        {
            _log.Debug($"职业图标获取异常 ClassJobId={classJobId}: {ex.Message}");
            return new ImTextureID(0);
        }
    }

    /// <summary>
    /// 获取角色分组图标纹理 ID。
    /// </summary>
    public ImTextureID GetRoleGroupIcon(string englishName)
    {
        try
        {
            if (!_roleSharedCache.TryGetValue(englishName, out var shared))
            {
                shared = CreateRoleShared(englishName);
                if (shared == null)
                    return new ImTextureID(0);
                _roleSharedCache[englishName] = shared;
            }

            var wrap = shared.GetWrapOrDefault();
            if (wrap == null)
                return new ImTextureID(0);

            return wrap.Handle;
        }
        catch (Exception ex)
        {
            _log.Debug($"分组图标获取异常 {englishName}: {ex.Message}");
            return new ImTextureID(0);
        }
    }

    private ISharedImmediateTexture? CreateJobShared(uint classJobId)
    {
        if (!JobIconFileNames.TryGetValue(classJobId, out var fileName))
        {
            _log.Debug($"未找到职业图标映射 ClassJobId={classJobId}");
            return null;
        }

        var path = Path.Combine(_jobIconsDir, $"{fileName}.png");
        if (!File.Exists(path))
        {
            _log.Debug($"职业图标文件不存在: {path}");
            return null;
        }

        _log.Debug($"[JobIconService] CreateJobShared ClassJobId={classJobId} path={path}");
        return _textureProvider.GetFromFile(path);
    }

    private ISharedImmediateTexture? CreateRoleShared(string englishName)
    {
        if (!RoleIconFileNames.TryGetValue(englishName, out var fileName))
        {
            _log.Debug($"未找到分组图标映射 {englishName}");
            return null;
        }

        var path = Path.Combine(_roleIconsDir, $"{fileName}.png");
        if (!File.Exists(path))
        {
            _log.Debug($"分组图标文件不存在: {path}");
            return null;
        }

        _log.Debug($"[JobIconService] CreateRoleShared {englishName} path={path}");
        return _textureProvider.GetFromFile(path);
    }

    public void Dispose()
    {
        // ISharedImmediateTexture 不需要手动 Dispose；
        // 清空字典释放引用，让 GC 回收。
        _jobSharedCache.Clear();
        _roleSharedCache.Clear();
    }
}
