using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using CraftFlow.Data.GameData;

namespace CraftFlow.Services;

/// <summary>
/// 职业图标服务，所有图标直接从游戏资源加载。
/// 参考 WrathCombo 的实现：使用 GameIconLookup(iconId, false, false) 三参数，
/// 缓存 ISharedImmediateTexture，每次 Draw 时 GetWrapOrDefault() 获取新鲜 wrap。
/// </summary>
public sealed class JobIconService : IDisposable
{
    private readonly ITextureProvider _textureProvider;
    private readonly EquipmentRepository _equipRepo;
    private readonly IPluginLog _log;

    private readonly Dictionary<uint, ISharedImmediateTexture> _iconSharedCache = [];
    private readonly Dictionary<string, ISharedImmediateTexture> _roleIconSharedCache = [];

    /// <summary>
    /// 角色分组专用图标 ID（WrathCombo 参考来源）。
    /// </summary>
    private static readonly Dictionary<string, uint> RoleGroupIconIds = new()
    {
        { "Tank", 62581 },
        { "Healer", 62582 },
        { "Maiming DPS", 62584 },
        { "Striking DPS", 62583 },
        { "Scouting DPS", 62585 },
        { "Ranged DPS", 62586 },
        { "Casting DPS", 62587 },
        { "Gatherer", 62589 },
        { "Crafter", 62588 },
    };

    public JobIconService(ITextureProvider textureProvider, EquipmentRepository equipRepo, IPluginLog log)
    {
        _textureProvider = textureProvider;
        _equipRepo = equipRepo;
        _log = log;
    }

    /// <summary>
    /// 获取职业图标。参考 WrathCombo 使用三参数 GameIconLookup。
    /// </summary>
    public ImTextureID GetJobIcon(uint classJobId)
    {
        try
        {
            // 优先用 EquipmentRepository 已有的缓存方法
            var wrap = _equipRepo.GetClassJobIconTexture(classJobId);
            if (wrap is not null)
                return wrap.Handle;
        }
        catch (ObjectDisposedException)
        {
            // EquipmentRepository 的缓存可能失效，走自己的路径
        }
        catch (Exception ex)
        {
            _log.Debug($"EquipmentRepository 图标失败 ClassJobId={classJobId}: {ex.Message}");
        }

        // 回退：自己加载
        try
        {
            if (!_iconSharedCache.TryGetValue(classJobId, out var shared))
            {
                var iconId = _equipRepo.GetClassJobIcon(classJobId);
                if (iconId == 0) return new ImTextureID(0);

                shared = _textureProvider.GetFromGameIcon(new GameIconLookup(iconId, false, false));
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
    /// 使用 WrathCombo 发现的 RoleBaseIconID (62580) + offset 映射。
    /// </summary>
    public ImTextureID GetRoleGroupIcon(string englishName)
    {
        try
        {
            if (!_roleIconSharedCache.TryGetValue(englishName, out var shared))
            {
                if (!RoleGroupIconIds.TryGetValue(englishName, out var iconId))
                    return new ImTextureID(0);

                shared = _textureProvider.GetFromGameIcon(new GameIconLookup(iconId, false, false));
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
