using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using CraftFlow.Data.GameData;
using CraftFlow.Data.Models;

namespace CraftFlow.Services;

/// <summary>
/// 一键添加整套装备逻辑服务。
/// 根据角色分组和槽位范围，调用 EquipmentRepository 获取最佳装备，
/// 转换为 CraftTarget 列表返回。
/// </summary>
public sealed class EquipmentSetService
{
    private readonly EquipmentRepository _equipRepo;
    private readonly RecipeRepository _recipeRepo;
    private readonly IPluginLog _log;

    /// <summary>
    /// 初始化 EquipmentSetService 实例。
    /// </summary>
    /// <param name="equipRepo">装备查询仓库。</param>
    /// <param name="recipeRepo">配方查询仓库。</param>
    /// <param name="log">插件日志。</param>
    public EquipmentSetService(EquipmentRepository equipRepo, RecipeRepository recipeRepo, IPluginLog log)
    {
        _equipRepo = equipRepo;
        _recipeRepo = recipeRepo;
        _log = log;
    }

    /// <summary>
    /// 添加主副手武器套装。
    /// </summary>
    /// <param name="role">角色分组。</param>
    /// <param name="classJobId">职业 ID。</param>
    /// <param name="quantity">每个装备的数量。</param>
    /// <param name="patchVersion">版本筛选（null 表示不筛选）。</param>
    /// <returns>制作目标列表。</returns>
    public List<CraftTarget> AddWeaponSet(RoleGroup role, uint classJobId, int quantity = 1, int? patchVersion = null)
    {
        var targets = new List<CraftTarget>();

        // 先取最佳主手武器；无主手则整组为空（某些职业/版本可能没有可制作主手）
        var mainHand = _equipRepo.GetBestInSlot(role, EquipmentSlotType.MainHand, hqOnly: true, classJobId: classJobId, patchVersion: patchVersion);
        if (mainHand is null)
        {
            _log.Debug($"AddWeaponSet({role.DisplayName}, {classJobId}): 无可用主手武器");
            return targets;
        }

        targets.Add(EquipmentItemToTarget(mainHand, quantity));

        // 仅当主手武器为单手（EquipSlotCategory.OffHand == 0）时才添加副手。
        // 双手武器（MainHand != 0 且 OffHand != 0）本身占满主手与副手两个槽，
        // 不存在独立的副手装备（如黑魔的法杖、武僧的拳套），强行加副手是 BUG。
        if (!mainHand.IsTwoHanded)
        {
            var offHand = _equipRepo.GetBestInSlot(role, EquipmentSlotType.OffHand, hqOnly: true, classJobId: classJobId, patchVersion: patchVersion);
            if (offHand is not null)
            {
                targets.Add(EquipmentItemToTarget(offHand, quantity));
            }
        }
        else
        {
            _log.Debug($"AddWeaponSet({role.DisplayName}, {classJobId}): 主手为双手武器，跳过副手");
        }

        _log.Debug($"AddWeaponSet({role.DisplayName}, {classJobId}): 添加 {targets.Count} 件武器");
        return targets;
    }

    /// <summary>
    /// 添加防具套装（头/身/手/腿/足）。
    /// </summary>
    /// <param name="role">角色分组。</param>
    /// <param name="classJobId">职业 ID。</param>
    /// <param name="quantity">每个装备的数量。</param>
    /// <param name="patchVersion">版本筛选（null 表示不筛选）。</param>
    /// <returns>制作目标列表。</returns>
    public List<CraftTarget> AddArmorSet(RoleGroup role, uint classJobId, int quantity = 1, int? patchVersion = null)
    {
        var targets = new List<CraftTarget>();
        var armorSlots = new[] { EquipmentSlotType.Head, EquipmentSlotType.Body, EquipmentSlotType.Hands, EquipmentSlotType.Legs, EquipmentSlotType.Feet };

        foreach (var slot in armorSlots)
        {
            var best = _equipRepo.GetBestInSlot(role, slot, hqOnly: true, classJobId: classJobId, patchVersion: patchVersion);
            if (best is not null)
            {
                targets.Add(EquipmentItemToTarget(best, quantity));
            }
        }

        _log.Debug($"AddArmorSet({role.DisplayName}, {classJobId}): 添加 {targets.Count} 件防具");
        return targets;
    }

    /// <summary>
    /// 添加首饰套装（耳/颈/腕/指）。
    /// </summary>
    /// <param name="role">角色分组。</param>
    /// <param name="classJobId">职业 ID。</param>
    /// <param name="quantity">每个装备的数量。</param>
    /// <param name="patchVersion">版本筛选（null 表示不筛选）。</param>
    /// <returns>制作目标列表。</returns>
    public List<CraftTarget> AddAccessorySet(RoleGroup role, uint classJobId, int quantity = 1, int? patchVersion = null)
    {
        var targets = new List<CraftTarget>();
        var accessorySlots = new[] { EquipmentSlotType.Ears, EquipmentSlotType.Neck, EquipmentSlotType.Wrists, EquipmentSlotType.Fingers };

        foreach (var slot in accessorySlots)
        {
            var best = _equipRepo.GetBestInSlot(role, slot, hqOnly: true, classJobId: classJobId, patchVersion: patchVersion);
            if (best is not null)
            {
                targets.Add(EquipmentItemToTarget(best, quantity));
            }
        }

        _log.Debug($"AddAccessorySet({role.DisplayName}, {classJobId}): 添加 {targets.Count} 件首饰");
        return targets;
    }

    /// <summary>
    /// 添加防具+首饰套装。
    /// </summary>
    /// <param name="role">角色分组。</param>
    /// <param name="classJobId">职业 ID。</param>
    /// <param name="quantity">每个装备的数量。</param>
    /// <param name="patchVersion">版本筛选（null 表示不筛选）。</param>
    /// <returns>制作目标列表。</returns>
    public List<CraftTarget> AddArmorAndAccessorySet(RoleGroup role, uint classJobId, int quantity = 1, int? patchVersion = null)
    {
        var targets = new List<CraftTarget>();
        targets.AddRange(AddArmorSet(role, classJobId, quantity, patchVersion));
        targets.AddRange(AddAccessorySet(role, classJobId, quantity, patchVersion));
        return targets;
    }

    /// <summary>
    /// 添加整套装备（武器+防具+首饰）。
    /// </summary>
    /// <param name="role">角色分组。</param>
    /// <param name="classJobId">职业 ID。</param>
    /// <param name="quantity">每个装备的数量。</param>
    /// <param name="patchVersion">版本筛选（null 表示不筛选）。</param>
    /// <returns>制作目标列表。</returns>
    public List<CraftTarget> AddFullSet(RoleGroup role, uint classJobId, int quantity = 1, int? patchVersion = null)
    {
        var targets = new List<CraftTarget>();
        targets.AddRange(AddWeaponSet(role, classJobId, quantity, patchVersion));
        targets.AddRange(AddArmorSet(role, classJobId, quantity, patchVersion));
        targets.AddRange(AddAccessorySet(role, classJobId, quantity, patchVersion));
        return targets;
    }

    /// <summary>
    /// 统一入口：根据 AddSlotType 添加对应范围的装备套装。
    /// </summary>
    /// <param name="role">角色分组。</param>
    /// <param name="classJobId">职业 ID。</param>
    /// <param name="type">槽位范围类型。</param>
    /// <param name="quantity">每个装备的数量。</param>
    /// <param name="patchVersion">版本筛选（null 表示不筛选）。</param>
    /// <returns>制作目标列表。</returns>
    public List<CraftTarget> AddByType(RoleGroup role, uint classJobId, AddSlotType type, int quantity = 1, int? patchVersion = null)
    {
        return type switch
        {
            AddSlotType.WeaponOnly => AddWeaponSet(role, classJobId, quantity, patchVersion),
            AddSlotType.ArmorOnly => AddArmorSet(role, classJobId, quantity, patchVersion),
            AddSlotType.AccessoryOnly => AddAccessorySet(role, classJobId, quantity, patchVersion),
            AddSlotType.ArmorAndAccessory => AddArmorAndAccessorySet(role, classJobId, quantity, patchVersion),
            AddSlotType.FullSet => AddFullSet(role, classJobId, quantity, patchVersion),
            _ => []
        };
    }

    /// <summary>
    /// 将 EquipmentItem 转换为 CraftTarget。
    /// </summary>
    private CraftTarget EquipmentItemToTarget(EquipmentItem item, int quantity)
    {
        return new CraftTarget
        {
            ItemId = item.ItemId,
            ItemName = item.ItemName,
            Quantity = quantity,
            Type = TargetType.Equipment
        };
    }
}
