using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace CraftFlow.Services;

/// <summary>
/// 职业图标服务，负责加载 Companion 风格职业图标（PNG 文件）。
/// 图标文件位于插件资源目录的 job-icons/ 子目录。
/// </summary>
public sealed class JobIconService : IDisposable
{
    private readonly ITextureProvider _textureProvider;
    private readonly string _jobIconDir;
    private readonly IPluginLog _log;
    private readonly Dictionary<uint, IDalamudTextureWrap> _iconCache = [];
    private readonly Dictionary<uint, ImTextureID> _iconHandleCache = [];

    /// <summary>
    /// Companion 图标文件名映射表（ClassJobId → 文件名不含扩展名）。
    /// 文件名格式与 HQHelper 的 public/image/game-job/companion/ 一致（小写英文名）。
    /// </summary>
    private static readonly Dictionary<uint, string> ClassJobToCompanionIcon = new()
    {
        // 防护职业
        { 19, "paladin" },      // 骑士
        { 21, "warrior" },       // 战士
        { 32, "darkknight" },    // 暗黑骑士
        { 37, "gunbreaker" },    // 绝枪战士
        // 治疗职业
        { 24, "whitemage" },     // 白魔法师
        { 28, "scholar" },        // 学者
        { 33, "astrologian" },    // 占星术士
        { 40, "sage" },           // 贤者
        // 制敌DPS（枪剑师）
        { 22, "dragoon" },        // 龙骑士
        { 39, "reaper" },         // 钐镰客
        // 强袭DPS（格斗家）
        { 20, "monk" },           // 武僧
        { 34, "samurai" },        // 武士
        // 游击DPS（双剑师）
        { 30, "ninja" },          // 忍者
        { 41, "viper" },          // 蝰蛇剑士
        // 远敏DPS（弓箭手）
        { 23, "bard" },           // 吟游诗人
        { 31, "machinist" },      // 机工士
        { 38, "dancer" },         // 舞者
        // 法系DPS（咒术师）
        { 25, "blackmage" },      // 黑魔法师
        { 27, "summoner" },       // 召唤师
        { 35, "redmage" },        // 赤魔法师
        { 42, "pictomancer" },    // 绘灵法师
        // 能工巧匠（生产职业）
        { 8, "carpenter" },       // 刻木匠
        { 9, "blacksmith" },       // 锻铁匠
        { 10, "armorer" },         // 铸甲匠
        { 11, "goldsmith" },      // 雕金匠
        { 12, "leatherworker" },   // 制革匠
        { 13, "weaver" },          // 裁衣匠
        { 14, "alchemist" },      // 炼金术士
        { 15, "culinarian" },      // 烹调师
        // 大地使者（采集职业）
        { 16, "miner" },          // 采矿工
        { 17, "botanist" },       // 园艺工
        { 18, "fisher" },         // 捕鱼人
    };

    /// <summary>
    /// 初始化 JobIconService 实例。
    /// </summary>
    /// <param name="textureProvider">纹理提供器。</param>
    /// <param name="pluginAssemblyLocation">插件程序集路径，用于定位资源目录。</param>
    /// <param name="log">插件日志。</param>
    public JobIconService(ITextureProvider textureProvider, string pluginAssemblyLocation, IPluginLog log)
    {
        _textureProvider = textureProvider;
        _log = log;

        // 插件 DLL 所在目录 + job-icons/
        _jobIconDir = Path.Combine(Path.GetDirectoryName(pluginAssemblyLocation)!, "job-icons");
        _log.Debug($"JobIconService: 图标目录 = {_jobIconDir}");

        // 预检：目录是否存在
        if (!Directory.Exists(_jobIconDir))
        {
            _log.Warning($"JobIconService: 图标目录不存在: {_jobIconDir}");
        }
    }

    /// <summary>
    /// 获取 Companion 风格职业图标的纹理。
    /// </summary>
    /// <param name="classJobId">职业 ID。</param>
    /// <returns>图标纹理，若无法加载返回 null。</returns>
    public IDalamudTextureWrap? GetCompanionIcon(uint classJobId)
    {
        // 检查缓存
        if (_iconCache.TryGetValue(classJobId, out var cached))
        {
            return cached;
        }

        // 查找文件名
        if (!ClassJobToCompanionIcon.TryGetValue(classJobId, out var fileName))
        {
            return null;
        }

        var filePath = Path.Combine(_jobIconDir, fileName + ".png");
        if (!File.Exists(filePath))
        {
            _log.Debug($"Companion 图标文件不存在: {filePath}");
            return null;
        }

        try
        {
            var sharedTexture = _textureProvider.GetFromFile(filePath);
            var iconTexture = sharedTexture.GetWrapOrDefault();

            if (iconTexture is not null)
            {
                _iconCache[classJobId] = iconTexture;
                return iconTexture;
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"加载 Companion 图标失败 ClassJobId={classJobId} File={filePath}: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 获取 Companion 风格职业图标的 ImGui 句柄。
    /// 内部通过反射获取 ImGuiHandle（IDalamudTextureWrap 接口未暴露此属性）。
    /// </summary>
    /// <param name="classJobId">职业 ID。</param>
    /// <returns>ImGui 纹理句柄，0 表示不可用。</returns>
    public ImTextureID GetCompanionIconHandle(uint classJobId)
    {
        if (_iconHandleCache.TryGetValue(classJobId, out var handle))
        {
            return handle;
        }

        var wrap = GetCompanionIcon(classJobId);
        if (wrap is null)
        {
            return 0;
        }

        // 通过反射获取 ImGuiHandle（兼容不同 Dalamud 版本）
        try
        {
            var prop = wrap.GetType().GetProperty("ImGuiHandle");
            if (prop is not null)
            {
                var value = prop.GetValue(wrap);
                if (value is not null)
                {
                    var h = new ImTextureID((nint)value);
                    _iconHandleCache[classJobId] = h;
                    return h;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"获取 Companion 图标句柄失败 ClassJobId={classJobId}: {ex.Message}");
        }

        return 0;
    }

    /// <summary>
    /// 释放所有缓存的纹理。
    /// </summary>
    public void Dispose()
    {
        foreach (var wrap in _iconCache.Values)
        {
            wrap.Dispose();
        }
        _iconCache.Clear();
    }
}
