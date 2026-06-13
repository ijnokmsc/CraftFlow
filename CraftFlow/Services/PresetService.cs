using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using CraftFlow.Config;
using CraftFlow.Data.GameData;
using CraftFlow.Data.Models;

namespace CraftFlow.Services;

/// <summary>
/// 收藏/推荐预设管理服务。
/// 提供内置推荐队列查询、用户收藏 CRUD、预设动态加载等功能。
/// </summary>
public sealed class PresetService
{
    private readonly EquipmentRepository _equipRepo;
    private readonly PluginConfig _config;
    private readonly IPluginLog _log;

    /// <summary>
    /// 初始化 PresetService 实例。
    /// </summary>
    /// <param name="equipRepo">装备查询仓库。</param>
    /// <param name="config">插件配置。</param>
    /// <param name="log">插件日志。</param>
    public PresetService(EquipmentRepository equipRepo, PluginConfig config, IPluginLog log)
    {
        _equipRepo = equipRepo;
        _config = config;
        _log = log;
    }

    /// <summary>
    /// 获取内置推荐套装列表。
    /// </summary>
    /// <returns>内置推荐 PresetEntry 数组。</returns>
    public PresetEntry[] GetBuiltInPresets()
    {
        return PresetEntryDefinitions.BuiltInPresets;
    }

    /// <summary>
    /// 获取用户收藏的预设列表。
    /// </summary>
    /// <returns>收藏预设列表。</returns>
    public List<FavoritePreset> GetFavoritePresets()
    {
        return _config.FavoritePresets;
    }

    /// <summary>
    /// 保存收藏预设到配置。
    /// </summary>
    /// <param name="preset">收藏预设。</param>
    public void SaveFavorite(FavoritePreset preset)
    {
        // 检查是否已存在同名收藏
        var existing = _config.FavoritePresets.FindIndex(p => p.Name == preset.Name);
        if (existing >= 0)
        {
            _config.FavoritePresets[existing] = preset;
        }
        else
        {
            _config.FavoritePresets.Add(preset);
        }

        _config.Save();
        _log.Information($"已保存收藏预设: {preset.Name}");
    }

    /// <summary>
    /// 删除指定名称的收藏预设。
    /// </summary>
    /// <param name="name">收藏名称。</param>
    public void DeleteFavorite(string name)
    {
        var removed = _config.FavoritePresets.RemoveAll(p => p.Name == name);
        if (removed > 0)
        {
            _config.Save();
            _log.Information($"已删除收藏预设: {name}");
        }
    }

    /// <summary>
    /// 重命名收藏预设。
    /// </summary>
    /// <param name="oldName">旧名称。</param>
    /// <param name="newName">新名称。</param>
    public void RenameFavorite(string oldName, string newName)
    {
        var preset = _config.FavoritePresets.Find(p => p.Name == oldName);
        if (preset is not null)
        {
            preset.Name = newName;
            _config.Save();
            _log.Information($"已重命名收藏预设: {oldName} → {newName}");
        }
    }

    /// <summary>
    /// 动态加载推荐预设为 CraftTarget 列表。
    /// 通过词缀解析 RoleGroup，区分防具/首饰词缀匹配，武器按职业逐个查找。
    /// </summary>
    /// <param name="preset">推荐预设条目。</param>
    /// <returns>制作目标列表。</returns>
    public List<CraftTarget> LoadPresetToTargets(PresetEntry preset)
    {
        var targets = new List<CraftTarget>();

        // 通过词缀查找对应的 RoleGroup（含完整 Jobs 列表）
        var equipRole = RoleGroupDefinitions.GetByAffix(preset.EquipmentAffix);
        var accessoryRole = RoleGroupDefinitions.GetByAffix(preset.AccessoryAffix);

        // 通过 ItemLevel 推算版本号，用于 ILvl 范围过滤
        var patchVersion = EquipmentRepository.GetPatchVersionByILvl(preset.ItemLevel);

        // 防具和首饰槽位：每个槽位一件
        var armorSlots = new[]
        {
            EquipmentSlotType.Head, EquipmentSlotType.Body, EquipmentSlotType.Hands,
            EquipmentSlotType.Legs, EquipmentSlotType.Feet,
            EquipmentSlotType.Ears, EquipmentSlotType.Neck, EquipmentSlotType.Wrists, EquipmentSlotType.Fingers
        };

        foreach (var slot in armorSlots)
        {
            // 首饰槽位用 AccessoryAffix 对应的 RoleGroup，防具槽位用 EquipmentAffix
            var role = _equipRepo.IsAccessorySlot(slot) ? accessoryRole : equipRole;
            if (role is null) continue;

            var best = _equipRepo.GetBestInSlot(role, slot, preset.IsHq, patchVersion: patchVersion);
            if (best is not null)
            {
                targets.Add(new CraftTarget
                {
                    ItemId = best.ItemId,
                    ItemName = best.ItemName,
                    Quantity = 1,
                    Type = TargetType.Equipment
                });
            }
        }

        // 武器槽位：按职业逐个查找，避免只返回一件通用武器
        if (preset.IncludeWeapon && equipRole is not null)
        {
            var addedWeaponIds = new HashSet<uint>();
            foreach (var (jobId, _) in equipRole.Jobs)
            {
                var weapon = _equipRepo.GetBestInSlot(equipRole, EquipmentSlotType.MainHand, preset.IsHq, jobId, patchVersion);
                if (weapon is not null && addedWeaponIds.Add(weapon.ItemId))
                {
                    targets.Add(new CraftTarget
                    {
                        ItemId = weapon.ItemId,
                        ItemName = weapon.ItemName,
                        Quantity = 1,
                        Type = TargetType.Equipment
                    });
                }
            }

            // 也检查副手
            var offHand = _equipRepo.GetBestInSlot(equipRole, EquipmentSlotType.OffHand, preset.IsHq, patchVersion: patchVersion);
            if (offHand is not null && addedWeaponIds.Add(offHand.ItemId))
            {
                targets.Add(new CraftTarget
                {
                    ItemId = offHand.ItemId,
                    ItemName = offHand.ItemName,
                    Quantity = 1,
                    Type = TargetType.Equipment
                });
            }
        }

        _log.Debug($"LoadPresetToTargets({preset.DisplayName}): 加载 {targets.Count} 件装备");
        return targets;
    }

    /// <summary>
    /// 将用户收藏预设转换为 CraftTarget 列表。
    /// 直接从保存的 EquipmentSelection 转换，无需动态查询。
    /// </summary>
    /// <param name="preset">收藏预设。</param>
    /// <returns>制作目标列表。</returns>
    public List<CraftTarget> LoadPresetToTargets(FavoritePreset preset)
    {
        var targets = preset.Selections.Select(s => new CraftTarget
        {
            ItemId = s.ItemId,
            ItemName = s.ItemName,
            Quantity = s.Quantity,
            Type = TargetType.Equipment
        }).ToList();

        _log.Debug($"LoadPresetToTargets({preset.Name}): 加载 {targets.Count} 件装备");
        return targets;
    }

    /// <summary>
    /// 将推荐预设保存为收藏预设。
    /// 动态查询当前最佳装备，保存为 EquipmentSelection 列表。
    /// </summary>
    /// <param name="preset">推荐预设条目。</param>
    public void SavePresetAsFavorite(PresetEntry preset)
    {
        var targets = LoadPresetToTargets(preset);
        var favorite = new FavoritePreset
        {
            Name = preset.DisplayName,
            Selections = targets.Select(t => new EquipmentSelection(t.ItemId, t.ItemName, t.Quantity)).ToList(),
            CreatedAt = DateTime.Now
        };

        SaveFavorite(favorite);
    }
}
