using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace CraftFlow.Services;

/// <summary>
/// 职业图标服务，所有图标直接从游戏资源加载，无需外部 PNG 文件。
/// 只缓存 ISharedImmediateTexture，每次 Draw 时通过 GetWrapOrDefault() 获取新鲜 wrap，
/// 避免 UnknownTextureWrap 被 Dalamud 内部纹理管理器回收导致的 ObjectDisposedException。
/// </summary>
public sealed class JobIconService : IDisposable
{
    private readonly ITextureProvider _textureProvider;
    private readonly IPluginLog _log;

    // 只缓存 Shared，不缓存 Wrap（Wrap 会被 Dalamud 内部回收）
    private readonly Dictionary<uint, ISharedImmediateTexture> _iconSharedCache = [];

    private readonly Dictionary<string, ISharedImmediateTexture> _roleIconSharedCache = [];

    /// <summary>
    /// 分组英文名 → 代表职业的 ClassJobId。
    /// </summary>
    private static readonly Dictionary<string, uint> RoleGroupRepresentativeJob = new()
    {
        { "Tank", 19 },        // 骑士
        { "Healer", 24 },      // 白魔法师
        { "Maiming DPS", 22 }, // 龙骑士
        { "Striking DPS", 20 },// 武僧
        { "Scouting DPS", 30 },// 忍者
        { "Ranged DPS", 23 },  // 吟游诗人
        { "Casting DPS", 25 }, // 黑魔法师
        { "Gatherer", 16 },    // 采矿工
        { "Crafter", 8 },      // 刻木匠
    };

    public JobIconService(ITextureProvider textureProvider, IPluginLog log)
    {
        _textureProvider = textureProvider;
        _log = log;
        _log.Debug("JobIconService 初始化: 纯游戏图标模式");
    }

    /// <summary>
    /// 获取职业图标，防御性处理 wrap dispose。
    /// </summary>
    public ImTextureID GetJobIcon(uint classJobId)
    {
        try
        {
            // 确保有 shared texture
            if (!_iconSharedCache.TryGetValue(classJobId, out var shared))
            {
                var iconId = GetClassJobIconId(classJobId);
                if (iconId == 0) return new ImTextureID(0);

                shared = _textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
                _iconSharedCache[classJobId] = shared;
            }

            var wrap = shared.GetWrapOrDefault();
            if (wrap is not null) return wrap.Handle;
        }
        catch (ObjectDisposedException)
        {
            // shared 已失效，移除缓存让下次重新加载
            _iconSharedCache.Remove(classJobId);
        }
        catch (Exception ex)
        {
            _log.Debug($"图标加载异常 ClassJobId={classJobId}: {ex.Message}");
        }

        return new ImTextureID(0);
    }

    /// <summary>
    /// 获取角色分组图标，防御性处理 wrap dispose。
    /// </summary>
    public ImTextureID GetRoleGroupIcon(string englishName)
    {
        try
        {
            if (!_roleIconSharedCache.TryGetValue(englishName, out var shared))
            {
                if (!RoleGroupRepresentativeJob.TryGetValue(englishName, out var repJobId))
                    return new ImTextureID(0);

                var iconId = GetClassJobIconId(repJobId);
                if (iconId == 0) return new ImTextureID(0);

                shared = _textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
                _roleIconSharedCache[englishName] = shared;
            }

            var wrap = shared.GetWrapOrDefault();
            if (wrap is not null) return wrap.Handle;
        }
        catch (ObjectDisposedException)
        {
            _roleIconSharedCache.Remove(englishName);
        }
        catch (Exception ex)
        {
            _log.Debug($"分组图标异常 {englishName}: {ex.Message}");
        }

        return new ImTextureID(0);
    }

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
    }
}
