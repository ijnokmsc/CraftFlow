using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using CraftFlow.Data.GameData;
using CraftFlow.Data.Models;

namespace CraftFlow.Services;

/// <summary>
/// 职业图标服务，所有图标直接从游戏资源加载。
/// 参考 WrathCombo：三参数 GameIconLookup，不缓存 wrap。
/// 战斗角色分组使用 RoleBaseIconID + offset（WrathCombo 来源），
/// 生产/采集分组无专用角色图标，使用第一个职业的图标。
/// </summary>
public sealed class JobIconService : IDisposable
{
    private readonly ITextureProvider _textureProvider;
    private readonly EquipmentRepository _equipRepo;
    private readonly IPluginLog _log;

    private readonly Dictionary<uint, ISharedImmediateTexture> _iconSharedCache = [];
    private readonly Dictionary<string, ISharedImmediateTexture> _roleIconSharedCache = [];

    /// <summary>
    /// 战斗角色分组专用图标 ID（来源 WrathCombo）。
    /// RoleBaseIconID = 62580，offset 对应 FFXIV ClassJob.Role 编号。
    /// 注意：游戏中没有生产(DoH)和采集(DoL)的专用角色图标。
    /// </summary>
    private static readonly Dictionary<string, uint> CombatRoleIconIds = new()
    {
        { "Tank", 62581 },         // Role=1
        { "Healer", 62582 },       // Role=4
        { "Maiming DPS", 62584 },  // Role=2
        { "Striking DPS", 62583 },
        { "Scouting DPS", 62585 },
        { "Ranged DPS", 62586 },   // Role=3
        { "Casting DPS", 62587 },  // Role=5
    };

    /// <summary>
    /// 生产/采集分组使用第一个职业的图标。
    /// </summary>
    private static readonly Dictionary<string, uint> DoHDoLRoleFirstJob = new()
    {
        { "Crafter", 8 },   // 刻木匠
        { "Gatherer", 16 }, // 采矿工
    };

    public JobIconService(ITextureProvider textureProvider, EquipmentRepository equipRepo, IPluginLog log)
    {
        _textureProvider = textureProvider;
        _equipRepo = equipRepo;
        _log = log;
    }

    /// <summary>
    /// 获取职业图标。不依赖 EquipmentRepository 的 wrap 缓存。
    /// </summary>
    public ImTextureID GetJobIcon(uint classJobId)
    {
        try
        {
            if (!_iconSharedCache.TryGetValue(classJobId, out var shared))
            {
                var iconId = _equipRepo.GetClassJobIcon(classJobId);
                if (iconId == 0)
                {
                    _log.Warning($"职业图标ID为0 ClassJobId={classJobId}");
                    return new ImTextureID(0);
                }

                _log.Debug($"加载职业图标 ClassJobId={classJobId} IconId={iconId}");
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
    /// 战斗角色使用专用角色图标（WrathCombo RoleBaseIconID + offset）。
    /// 生产/采集无专用角色图标，使用第一个职业的图标。
    /// </summary>
    public ImTextureID GetRoleGroupIcon(string englishName)
    {
        try
        {
            if (!_roleIconSharedCache.TryGetValue(englishName, out var shared))
            {
                uint iconId;

                // 战斗角色：使用专用角色图标
                if (CombatRoleIconIds.TryGetValue(englishName, out var combatIconId))
                {
                    iconId = combatIconId;
                }
                // 生产/采集：使用第一个职业的图标
                else if (DoHDoLRoleFirstJob.TryGetValue(englishName, out var firstJobId))
                {
                    iconId = _equipRepo.GetClassJobIcon(firstJobId);
                }
                else
                {
                    return new ImTextureID(0);
                }

                if (iconId == 0) return new ImTextureID(0);

                _log.Debug($"加载分组图标 {englishName} IconId={iconId}");
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
