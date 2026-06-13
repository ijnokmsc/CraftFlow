using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using CraftFlow.Data.GameData;

namespace CraftFlow.Services;

/// <summary>
/// 职业图标服务，所有图标直接从游戏资源加载。
/// 委托 EquipmentRepository.GetClassJobIcon() 获取正确的图标 ID（Lumina 反射 + 回退映射）。
/// 只缓存 ISharedImmediateTexture，每次 Draw 时通过 GetWrapOrDefault() 获取新鲜 wrap。
/// </summary>
public sealed class JobIconService : IDisposable
{
    private readonly ITextureProvider _textureProvider;
    private readonly EquipmentRepository _equipRepo;
    private readonly IPluginLog _log;

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

    public JobIconService(ITextureProvider textureProvider, EquipmentRepository equipRepo, IPluginLog log)
    {
        _textureProvider = textureProvider;
        _equipRepo = equipRepo;
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
            if (!_iconSharedCache.TryGetValue(classJobId, out var shared))
            {
                // 委托 EquipmentRepository 获取正确图标 ID（Lumina 反射 + 回退）
                var iconId = _equipRepo.GetClassJobIcon(classJobId);
                if (iconId == 0) return new ImTextureID(0);

                shared = _textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
                _iconSharedCache[classJobId] = shared;
            }

            var wrap = shared.GetWrapOrDefault();
            if (wrap is not null) return wrap.Handle;
        }
        catch (ObjectDisposedException)
        {
            _iconSharedCache.Remove(classJobId);
        }
        catch (Exception ex)
        {
            _log.Debug($"图标加载异常 ClassJobId={classJobId}: {ex.Message}");
        }

        return new ImTextureID(0);
    }

    /// <summary>
    /// 获取角色分组图标。
    /// </summary>
    public ImTextureID GetRoleGroupIcon(string englishName)
    {
        try
        {
            if (!_roleIconSharedCache.TryGetValue(englishName, out var shared))
            {
                if (!RoleGroupRepresentativeJob.TryGetValue(englishName, out var repJobId))
                    return new ImTextureID(0);

                var iconId = _equipRepo.GetClassJobIcon(repJobId);
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

    public void Dispose()
    {
        _iconSharedCache.Clear();
        _roleIconSharedCache.Clear();
    }
}
