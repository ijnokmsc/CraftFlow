using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using CraftFlow.Data.GameData;
using CraftFlow.Data.Models;

namespace CraftFlow.Services;

/// <summary>
/// 职业图标服务，严格参考 WrathCombo Icons.cs 实现。
/// 使用 GetFromFile(path) 方式加载图标（非 GetFromGameIcon）。
/// </summary>
public sealed class JobIconService : IDisposable
{
    private readonly ITextureProvider _textureProvider;
    private readonly EquipmentRepository _equipRepo;
    private readonly IPluginLog _log;

    // 缓存：只存 ISharedImmediateTexture，每次 Draw 时 GetWrapOrDefault()
    private readonly Dictionary<uint, ISharedImmediateTexture> _iconSharedCache = [];
    private readonly Dictionary<string, ISharedImmediateTexture> _roleIconSharedCache = [];

    /// <summary>
    /// 战斗角色分组专用图标 ID（来源 WrathCombo Role.IconID + offset）。
    /// </summary>
    private static readonly Dictionary<string, uint> CombatRoleIconIds = new()
    {
        { "Tank", 62581 },         // RoleBaseIconID + 1
        { "Healer", 62582 },       // RoleBaseIconID + 2
        { "Maiming DPS", 62584 },  // Melee (RoleBaseIconID + 4)
        { "Striking DPS", 62583 },
        { "Scouting DPS", 62585 },
        { "Ranged DPS", 62586 },   // RoleBaseIconID + 6
        { "Casting DPS", 62587 },  // Magic (RoleBaseIconID + 7)
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
    /// 获取职业图标（参考 WrathCombo GetTextureFromIconId 方式）。
    /// 使用 GetFromFile(path) 而非 GetFromGameIcon。
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
                shared = LoadIconTexture(iconId);
                if (shared is null) return new ImTextureID(0);
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
                shared = LoadIconTexture(iconId);
                if (shared is null) return new ImTextureID(0);
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

    /// <summary>
    /// 加载图标纹理（参考 WrathCombo GetTextureFromIconId 方式）。
    /// 使用 GetFromFile(path) 而非 GetFromGameIcon。
    /// </summary>
    private ISharedImmediateTexture? LoadIconTexture(uint iconId)
    {
        try
        {
            // 参考 WrathCombo：GameIconLookup(iconId, false, true)
            var lookup = new GameIconLookup(iconId, false, true);
            
            // 方式1：直接用 GetFromGameIcon（WrathCombo 的 fallback 路径）
            // 注：WrathCombo 主路径是 GetFromFile(resolvedPath)，但 GetFromGameIcon 也应该能用
            var shared = _textureProvider.GetFromGameIcon(lookup);
            return shared;
        }
        catch (Exception ex)
        {
            _log.Debug($"LoadIconTexture 失败 IconId={iconId}: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _iconSharedCache.Clear();
        _roleIconSharedCache.Clear();
    }
}
