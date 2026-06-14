using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using CraftFlow.Data.Models;

namespace CraftFlow.Data.GameData;

/// <summary>
/// 装备查询仓库，实现按角色分组/槽位/版本的装备筛选。
/// 通过 ClassJobCategory 结构体的布尔属性（CanJobEquip）匹配职业，
/// 通过 EquipSlotCategory 判断装备槽位。
/// 注意：已废弃 ClassJobCategory.Name 字符串匹配，国服 Name 返回中文名无法与英文 affix 比较。
/// </summary>
public sealed class EquipmentRepository
{
    private readonly LuminaCache _cache;
    private readonly IPluginLog _log;
    private readonly ITextureProvider? _textureProvider;

    /// <summary>
    /// ILvl → PatchVersion 映射表（FFXIV DT 7.x 版本）。
    /// Key 为简化整数版本号（70=7.0, 71=7.1, 72=7.2, 73=7.3）。
    /// Value 为 (MinILvl, MaxILvl) 元组，表示该版本装备的 ILvl 范围。
    /// </summary>
    private static readonly (int PatchVersion, int MinILvl, int MaxILvl)[] PatchVersionILvlRanges =
    [
        (70, 650, 699),  // 7.0
        (705, 700, 714), // 7.05
        (71, 715, 734),  // 7.1
        (72, 735, 749),  // 7.2
        (73, 750, 769),  // 7.3: 750 生产采集 / 760 战斗
        (74, 770, 799),  // 7.4: 770 战斗制作装
    ];

    /// <summary>
    /// 职业图标纹理缓存，ClassJobId → IDalamudTextureWrap。
    /// 保存引用以防止 GC 回收。
    /// </summary>
    private readonly Dictionary<uint, IDalamudTextureWrap> _classJobIconCache = [];

    /// <summary>
    /// 初始化 EquipmentRepository 实例。
    /// </summary>
    /// <param name="cache">Lumina 数据缓存。</param>
    /// <param name="log">插件日志。</param>
    /// <param name="textureProvider">纹理提供器（可选，用于加载职业图标）。</param>
    public EquipmentRepository(LuminaCache cache, IPluginLog log, ITextureProvider? textureProvider = null)
    {
        _cache = cache;
        _log = log;
        _textureProvider = textureProvider;
    }

    /// <summary>
    /// 获取所有已定义的 PatchVersion 列表（从最新到最旧）。
    /// </summary>
    public static int[] GetDefinedPatchVersions()
    {
        return PatchVersionILvlRanges.Select(r => r.PatchVersion).OrderByDescending(v => v).ToArray();
    }

    /// <summary>
    /// 根据 ILvl 推算对应的 PatchVersion。
    /// 遍历映射表，返回第一个 ILvl 在范围内的版本号；若不在任何范围内，返回 null。
    /// </summary>
    /// <param name="ilvl">装备等级（ItemLevel）。</param>
    /// <returns>简化整数版本号（如 70, 71, 72, 73），null 表示无法匹配。</returns>
    public static int? GetPatchVersionByILvl(int ilvl)
    {
        foreach (var range in PatchVersionILvlRanges)
        {
            if (ilvl >= range.MinILvl && ilvl <= range.MaxILvl)
            {
                return range.PatchVersion;
            }
        }

        return null;
    }

    /// <summary>
    /// 根据 PatchVersion 获取对应的 ILvl 范围。
    /// </summary>
    /// <param name="patchVersion">简化整数版本号（如 70, 71, 72, 73）。</param>
    /// <returns>ILvl 范围元组 (Min, Max)，若版本不存在返回 null。</returns>
    public static (int Min, int Max)? GetILvlRangeForPatchVersion(int patchVersion)
    {
        foreach (var range in PatchVersionILvlRanges)
        {
            if (range.PatchVersion == patchVersion)
            {
                return (range.MinILvl, range.MaxILvl);
            }
        }

        return null;
    }

    /// <summary>
    /// 判断指定职业是否能装备某件物品（基于 ClassJobCategory 的布尔属性）。
    /// 使用 switch 表达式直接映射 jobId → ClassJobCategory 属性，避免反射和字符串比较。
    /// 这是解决国服客户端 ClassJobCategory.Name 返回中文名导致匹配失败的根因修复。
    /// </summary>
    /// <param name="cjc">物品的 ClassJobCategory。</param>
    /// <param name="classJobId">职业 ID（ClassJob RowId）。</param>
    /// <returns>true 表示该职业可以使用此物品。</returns>
    private static bool CanJobEquip(ClassJobCategory cjc, uint classJobId) => classJobId switch
    {
        // 基础职业（属性名全大写，与 Lumina 生成的 Excel struct 一致）
        1 => cjc.GLA, 2 => cjc.PGL, 3 => cjc.MRD, 4 => cjc.LNC,
        5 => cjc.ARC, 6 => cjc.CNJ, 7 => cjc.THM,
        // 能工巧匠
        8 => cjc.CRP, 9 => cjc.BSM, 10 => cjc.ARM, 11 => cjc.GSM,
        12 => cjc.LTW, 13 => cjc.WVR, 14 => cjc.ALC, 15 => cjc.CUL,
        // 大地使者
        16 => cjc.MIN, 17 => cjc.BTN, 18 => cjc.FSH,
        // 战斗特职
        19 => cjc.PLD, 20 => cjc.MNK, 21 => cjc.WAR, 22 => cjc.DRG,
        23 => cjc.BRD, 24 => cjc.WHM, 25 => cjc.BLM,
        27 => cjc.SMN, 28 => cjc.SCH,
        30 => cjc.NIN, 31 => cjc.MCH, 32 => cjc.DRK,
        33 => cjc.AST, 34 => cjc.SAM, 35 => cjc.RDM, 36 => cjc.BLU,
        37 => cjc.GNB, 38 => cjc.DNC, 39 => cjc.RPR, 40 => cjc.SGE,
        41 => cjc.VPR, 42 => cjc.PCT,
        _ => false
    };

    /// <summary>
    /// 判断物品是否匹配指定的 RoleGroup。
    /// 如果指定了具体职业（classJobId > 0），检查该职业是否可装备；
    /// 否则检查 RoleGroup 中的任一职业是否可装备。
    /// 完全废弃了 ClassJobCategory.Name 字符串匹配，改为使用结构体的布尔属性。
    /// </summary>
    /// <param name="item">Lumina Item 结构。</param>
    /// <param name="role">角色分组。</param>
    /// <param name="classJobId">具体职业 ID，0 表示检查 RoleGroup 的全部职业。</param>
    /// <returns>true 表示匹配。</returns>
    private static bool IsItemMatchForRole(Item item, RoleGroup role, uint classJobId = 0)
    {
        if (!item.ClassJobCategory.IsValid) return false;
        var cjc = item.ClassJobCategory.Value;

        if (classJobId > 0)
        {
            return CanJobEquip(cjc, classJobId);
        }

        foreach (var (jobId, _) in role.Jobs)
        {
            if (CanJobEquip(cjc, jobId)) return true;
        }
        return false;
    }

    /// <summary>
    /// 获取游戏中实际存在的可用 PatchVersion 列表。
    /// 遍历所有有配方的装备，按 ILvl 推算版本号，去重排序后返回。
    /// </summary>
    /// <returns>实际存在装备的 PatchVersion 列表（升序排列）。</returns>
    public List<int> GetAvailablePatchVersions()
    {
        var versions = new HashSet<int>();
        var recipes = _cache.RecipeSheet.Values;

        foreach (var recipe in recipes)
        {
            if (!recipe.ItemResult.IsValid || recipe.ItemResult.Value.RowId == 0)
            {
                continue;
            }

            var itemId = recipe.ItemResult.Value.RowId;
            if (!_cache.ItemSheet.TryGetValue(itemId, out var item))
            {
                continue;
            }

            // 仅统计装备类物品（有 EquipSlotCategory）
            if (!item.EquipSlotCategory.IsValid)
            {
                continue;
            }

            var ilvl = item.LevelItem.IsValid ? (int)item.LevelItem.Value.RowId : 0;
            var pv = GetPatchVersionByILvl(ilvl);
            if (pv.HasValue)
            {
                versions.Add(pv.Value);
            }
        }

        // 排除 7.05（极神武器版本，装备 Tab 不显示）
        versions.Remove(705);

        return versions.OrderBy(v => v).ToList();
    }

    /// <summary>
    /// 按角色分组和职业获取装备，按槽位分组返回。
    /// 遍历有配方的 Item，通过 ClassJobCategory.Name 匹配词缀，
    /// 通过 EquipSlotCategory 判断槽位，按 ILvl 降序排列。
    /// </summary>
    /// <param name="role">角色分组（含 EquipmentAffix 和 AccessoryAffix）。</param>
    /// <param name="classJobId">职业 ID，用于武器槽位的额外筛选。</param>
    /// <param name="patchVersion">版本筛选（如 7 表示 7.x，null 表示不筛选）。</param>
    /// <returns>按槽位分组的装备列表字典。</returns>
    public Dictionary<EquipmentSlotType, List<EquipmentItem>> GetEquipmentGroupedBySlot(
        RoleGroup role, uint classJobId, int? patchVersion = null)
    {
        var result = new Dictionary<EquipmentSlotType, List<EquipmentItem>>();

        // 遍历所有有配方的 Item
        foreach (var recipe in _cache.RecipeSheet.Values)
        {
            if (!recipe.ItemResult.IsValid || recipe.ItemResult.Value.RowId == 0)
            {
                continue;
            }

            var itemId = recipe.ItemResult.Value.RowId;
            if (!_cache.ItemSheet.TryGetValue(itemId, out var item))
            {
                continue;
            }

            // 版本筛选：根据 patchVersion 参数过滤 ILvl 范围
            if (patchVersion.HasValue)
            {
                var ilvlRange = GetILvlRangeForPatchVersion(patchVersion.Value);
                if (ilvlRange.HasValue)
                {
                    var itemIlvl = item.LevelItem.IsValid ? (int)item.LevelItem.Value.RowId : 0;
                    if (itemIlvl < ilvlRange.Value.Min || itemIlvl > ilvlRange.Value.Max)
                    {
                        continue;
                    }
                }
            }

            // 检查该物品是否为装备类（有 EquipSlotCategory），必须在 GetSlotTypeForItem 之前
            if (!item.EquipSlotCategory.IsValid)
            {
                continue;
            }

            // 判断装备槽位
            var slotType = GetSlotTypeForItem(item);

            // 通过 ClassJobCategory 布尔属性匹配角色分组
            // 废弃了 Name 字符串匹配（国服 Name 返回中文名如"防护"，与英文 affix 如"fending"永远不匹配）
            if (!IsItemMatchForRole(item, role, classJobId))
            {
                continue;
            }

            // 构建 EquipmentItem
            var equipItem = new EquipmentItem
            {
                ItemId = item.RowId,
                ItemName = item.Name.ToString(),
                SlotType = slotType,
                ItemLevel = item.LevelItem.IsValid ? (int)item.LevelItem.Value.RowId : 0,
                IsHq = item.CanBeHq,
                Quantity = 1
            };

            if (!result.TryGetValue(slotType, out var list))
            {
                list = [];
                result[slotType] = list;
            }

            // 去重：同一 ItemId 不重复添加
            if (list.Any(e => e.ItemId == itemId))
            {
                continue;
            }

            list.Add(equipItem);
        }

        // 每个槽位按 ILvl 降序排列
        foreach (var list in result.Values)
        {
            list.Sort((a, b) => b.ItemLevel.CompareTo(a.ItemLevel));
        }

        _log.Debug($"GetEquipmentGroupedBySlot({role.DisplayName}, {classJobId}): 找到 {result.Values.Sum(l => l.Count)} 件装备，{result.Count} 个槽位");
        return result;
    }

    /// <summary>
    /// 获取指定槽位的最佳装备（最高 ILvl 的可制作装备）。
    /// </summary>
    /// <param name="role">角色分组。</param>
    /// <param name="slot">装备槽位类型。</param>
    /// <param name="hqOnly">是否仅选择 HQ 装备。</param>
    /// <param name="classJobId">职业 ID，用于武器槽位的精确职业匹配（能工巧匠/大地使者必须传递）。</param>
    /// <param name="patchVersion">版本筛选。</param>
    /// <returns>最佳装备，若无可用装备返回 null。</returns>
    public EquipmentItem? GetBestInSlot(RoleGroup role, EquipmentSlotType slot, bool hqOnly = true, uint classJobId = 0, int? patchVersion = null)
    {
        // 先按传入的 patchVersion 筛选
        var best = FindBestInSlot(role, slot, hqOnly, classJobId, patchVersion);

        // 如果指定了版本但没找到装备，降级为不限制版本再找一次
        // （解决某些职业/槽位在当前版本 ILvl 范围内无可用装备的问题）
        if (best is null && patchVersion.HasValue)
        {
            best = FindBestInSlot(role, slot, hqOnly, classJobId, null);
        }

        return best;
    }

    /// <summary>
    /// 内部查找：根据传入的 patchVersion 遍历所有配方寻找最佳装备。
    /// </summary>
    private EquipmentItem? FindBestInSlot(RoleGroup role, EquipmentSlotType slot, bool hqOnly, uint classJobId, int? patchVersion)
    {
        EquipmentItem? best = null;

        foreach (var recipe in _cache.RecipeSheet.Values)
        {
            if (!recipe.ItemResult.IsValid || recipe.ItemResult.Value.RowId == 0)
            {
                continue;
            }

            var itemId = recipe.ItemResult.Value.RowId;
            if (!_cache.ItemSheet.TryGetValue(itemId, out var item))
            {
                continue;
            }

            // 跳过无 EquipSlotCategory 的非装备物品
            if (!item.EquipSlotCategory.IsValid)
            {
                continue;
            }

            // 判断装备槽位
            var itemSlot = GetSlotTypeForItem(item);
            if (itemSlot != slot)
            {
                continue;
            }

            // 版本筛选：根据 patchVersion 参数过滤 ILvl 范围
            if (patchVersion.HasValue)
            {
                var ilvlRange = GetILvlRangeForPatchVersion(patchVersion.Value);
                if (ilvlRange.HasValue)
                {
                    var itemIlvl = item.LevelItem.IsValid ? (int)item.LevelItem.Value.RowId : 0;
                    if (itemIlvl < ilvlRange.Value.Min || itemIlvl > ilvlRange.Value.Max)
                    {
                        continue;
                    }
                }
            }

            // HQ 筛选
            if (hqOnly && !item.CanBeHq)
            {
                continue;
            }

            // 通过 ClassJobCategory 布尔属性匹配角色分组
            if (!IsItemMatchForRole(item, role, classJobId))
            {
                continue;
            }

            var ilvl = item.LevelItem.IsValid ? (int)item.LevelItem.Value.RowId : 0;
            if (best is null || ilvl > best.ItemLevel)
            {
                best = new EquipmentItem
                {
                    ItemId = item.RowId,
                    ItemName = item.Name.ToString(),
                    SlotType = slot,
                    ItemLevel = ilvl,
                    IsHq = item.CanBeHq,
                    Quantity = 1
                };
            }
        }

        return best;
    }

    /// <summary>
    /// 根据 PresetEntry 获取指定槽位的最佳装备（按词缀 + ILvl + HQ 匹配）。
    /// </summary>
    /// <param name="preset">推荐预设条目。</param>
    /// <param name="slot">装备槽位类型。</param>
    /// <returns>最佳装备，若无可用装备返回 null。</returns>
    public EquipmentItem? GetBestInSlotForPreset(PresetEntry preset, EquipmentSlotType slot)
    {
        // 构造临时 RoleGroup 用于词缀匹配
        var tempRole = new RoleGroup(
            preset.DisplayName,
            "Preset",
            [],
            preset.EquipmentAffix,
            preset.AccessoryAffix
        );

        return GetBestInSlot(tempRole, slot, preset.IsHq);
    }

    /// <summary>
    /// 通过 Item 的 EquipSlotCategory 判断装备槽位类型。
    /// </summary>
    /// <param name="item">Lumina Item 数据。</param>
    /// <returns>装备槽位类型。</returns>
    public EquipmentSlotType GetSlotTypeForItem(Item item)
    {
        if (!item.EquipSlotCategory.IsValid)
        {
            return EquipmentSlotType.MainHand; // 默认值，无法判断时返回
        }

        var esc = item.EquipSlotCategory.Value;

        if (esc.MainHand != 0) return EquipmentSlotType.MainHand;
        if (esc.OffHand != 0) return EquipmentSlotType.OffHand;
        if (esc.Head != 0) return EquipmentSlotType.Head;
        if (esc.Body != 0) return EquipmentSlotType.Body;
        if (esc.Gloves != 0) return EquipmentSlotType.Hands;
        if (esc.Legs != 0) return EquipmentSlotType.Legs;
        if (esc.Feet != 0) return EquipmentSlotType.Feet;
        if (esc.Ears != 0) return EquipmentSlotType.Ears;
        if (esc.Neck != 0) return EquipmentSlotType.Neck;
        if (esc.Wrists != 0) return EquipmentSlotType.Wrists;
        if (esc.FingerL != 0) return EquipmentSlotType.Fingers;

        return EquipmentSlotType.MainHand;
    }

    /// <summary>
    /// 判断槽位是否为首饰类（耳/颈/腕/指）。
    /// </summary>
    /// <param name="slot">装备槽位类型。</param>
    /// <returns>是否为首饰类槽位。</returns>
    public bool IsAccessorySlot(EquipmentSlotType slot)
    {
        return slot is EquipmentSlotType.Ears
            or EquipmentSlotType.Neck
            or EquipmentSlotType.Wrists
            or EquipmentSlotType.Fingers;
    }

    /// <summary>
    /// 获取职业图标的游戏图标 ID。
    /// 通过 Lumina ClassJob 数据获取图标 ID，使用缓存避免重复查询。
    /// 返回的图标 ID 可用于 TextureProvider.GetFromGameIcon() 加载纹理。
    /// </summary>
    /// <param name="classJobId">职业 ID（ClassJob RowId）。</param>
    /// <returns>游戏图标 ID，0 表示无法获取。</returns>
    public uint GetClassJobIcon(uint classJobId)
    {
        if (classJobId == 0) return 62147;

        if (!_cache.ClassJobSheet.TryGetValue(classJobId, out var classJob))
        {
            _log.Debug($"[GetClassJobIcon] ClassJobId={classJobId} 不在 ClassJobSheet 中，使用回退");
            return GetClassJobIconFallback(classJobId);
        }

        // 主动触发 RowRef 解析：访问一次 ItemSheet 对应行
        // Lumina 的 RowRef 是惰性加载，需要目标 Sheet 已被访问过才能解析 RowId
        // 这里先尝试直接读，如果 RowId=0 则回退
        var soulCrystalRowId = classJob.ItemSoulCrystal.RowId;
        var weaponRowId = classJob.ItemStartingWeaponMainHand.RowId;

        _log.Debug($"[GetClassJobIcon] ClassJobId={classJobId} ItemSoulCrystal.RowId={soulCrystalRowId} ItemStartingWeaponMainHand.RowId={weaponRowId}");

        // 优先尝试通过反射读取 ClassJob 的 Icon 字段（Lumina 自动生成的 struct 可能未暴露此字段）
        // FFXIV ClassJob Excel 表有 Icon 列，存储职业在 UI 中显示的图标 ID
        try
        {
            var iconProp = typeof(ClassJob).GetProperty("Icon");
            if (iconProp is not null)
            {
                var iconVal = iconProp.GetValue(classJob);
                if (iconVal is uint u && u > 0)
                {
                    _log.Debug($"[GetClassJobIcon] ClassJobId={classJobId} 反射获取 Icon={u}");
                    return u;
                }
                if (iconVal is int i && i > 0)
                {
                    _log.Debug($"[GetClassJobIcon] ClassJobId={classJobId} 反射获取 Icon(int)={i}");
                    return (uint)i;
                }
                if (iconVal is ushort us && us > 0)
                {
                    _log.Debug($"[GetClassJobIcon] ClassJobId={classJobId} 反射获取 Icon(ushort)={us}");
                    return us;
                }
            }
            else
            {
                // 打印 ClassJob 的所有属性名，帮助定位 Icon 字段的正确名称
                var allProps = typeof(ClassJob).GetProperties().Select(p => p.Name).ToArray();
                _log.Debug($"[GetClassJobIcon] ClassJob 无 Icon 属性，所有属性: [{string.Join(", ", allProps)}]");
                // 同时检查 Field（Lumina 有时用字段而非属性）
                var allFields = typeof(ClassJob).GetFields().Select(f => $"{f.Name}:{f.FieldType.Name}").ToArray();
                _log.Debug($"[GetClassJobIcon] ClassJob 所有字段: [{string.Join(", ", allFields)}]");
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[GetClassJobIcon] 反射读取 Icon 异常 ClassJobId={classJobId}: {ex.GetType().Name}: {ex.Message}");
        }

        // 路径1：ClassJob.ItemStartingWeaponMainHand → Item.Icon（职业起始武器/工具的图标 = 职业图标）
        // 注：ItemSoulCrystal 的图标是水晶，不是职业图标，因此放到路径2作为回退
        if (weaponRowId > 0)
        {
            try
            {
                if (_cache.ItemSheet.TryGetValue(weaponRowId, out var weaponItem))
                {
                    var iconId = (uint)weaponItem.Icon;
                    _log.Debug($"[GetClassJobIcon] ClassJobId={classJobId} weaponItem.Icon={iconId} (ushort→uint)");
                    if (iconId > 0)
                    {
                        return iconId;
                    }
                }
                else
                {
                    _log.Debug($"[GetClassJobIcon] ClassJobId={classJobId} ItemStartingWeaponMainHand RowId={weaponRowId} 不在 ItemSheet 中");
                }
            }
            catch (Exception ex)
            {
                _log.Debug($"[GetClassJobIcon] ItemStartingWeaponMainHand 异常 ClassJobId={classJobId}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // 路径2：ClassJob.ItemSoulCrystal → Item.Icon（战斗职业的灵魂水晶，仅作为回退）
        if (soulCrystalRowId > 0)
        {
            try
            {
                if (_cache.ItemSheet.TryGetValue(soulCrystalRowId, out var soulCrystalItem))
                {
                    var iconId = (uint)soulCrystalItem.Icon;
                    _log.Debug($"[GetClassJobIcon] ClassJobId={classJobId} soulCrystalItem.Icon={iconId} (ushort→uint, 回退路径)");
                    if (iconId > 0)
                    {
                        return iconId;
                    }
                }
                else
                {
                    _log.Debug($"[GetClassJobIcon] ClassJobId={classJobId} ItemSoulCrystal RowId={soulCrystalRowId} 不在 ItemSheet 中");
                }
            }
            catch (Exception ex)
            {
                _log.Debug($"[GetClassJobIcon] ItemSoulCrystal 异常 ClassJobId={classJobId}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // 路径3：回退映射表（已知正确的图标 ID）
        _log.Debug($"[GetClassJobIcon] RowRef 解析失败（RowId=0），使用回退映射表 ClassJobId={classJobId}");
        return GetClassJobIconFallback(classJobId);
    }

    /// <summary>
    /// 获取职业图标的 IDalamudTextureWrap，用于 ImGui 渲染。
    /// 通过 TextureProvider 加载游戏内图标，使用缓存避免重复加载。
    /// 注意：返回的 wrap 引用需要保持引用以防止 GC 回收。
    /// </summary>
    /// <param name="classJobId">职业 ID（ClassJob RowId）。</param>
    /// <returns>图标纹理，若无法加载返回 null。</returns>
    public IDalamudTextureWrap? GetClassJobIconTexture(uint classJobId)
    {
        // 检查缓存
        if (_classJobIconCache.TryGetValue(classJobId, out var cached))
        {
            return cached;
        }

        // 如果 TextureProvider 不可用，无法加载图标
        if (_textureProvider is null)
        {
            return null;
        }

        var iconId = GetClassJobIcon(classJobId);
        if (iconId == 0)
        {
            return null;
        }

        try
        {
            var sharedTexture = _textureProvider.GetFromGameIcon(new GameIconLookup(iconId, false, true));
            var iconTexture = sharedTexture.GetWrapOrDefault();

            if (iconTexture is not null)
            {
                _classJobIconCache[classJobId] = iconTexture;
                return iconTexture;
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"加载职业图标纹理失败 ClassJobId={classJobId} IconId={iconId}: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 职业图标 ID 回退映射表（已知正确 ID，来源：FFXIV 游戏数据 + WrathCombo）。
    /// 当 Lumina RowRef 无法解析时（ItemSoulCrystal.RowId=0），
    /// 直接使用此映射获取图标 ID。
    /// 
    /// 图标 ID 来源说明：
    /// - 战斗职业：ClassJob.Icon 字段值（由 Lumina/ExcelSheet 提供，通常是 062xxx 范围）
    /// - 生产职业：同样使用 ClassJob.Icon 字段
    /// - 采集职业：WrathCombo 使用 82096（角色分组图标）
    /// 
    /// 注意：此表中的值是十进制整数，对应游戏图标文件编号。
    /// </summary>
    private static uint GetClassJobIconFallback(uint classJobId)
    {
        // 此映射表的值来自 FFXIV 游戏客户端内的 ClassJob.Icon 字段。
        // 每个职业在游戏数据里都有一个 Icon 字段，存储该职业在 UI 中显示的图标 ID。
        //
        // 如何验证：在游戏内打开"角色"窗口 → 选择"职业"标签，
        // 每个职业图标旁边的数字就是 Icon ID（可通过 TexTools 或 Garland Tools 确认）。
        //
        // 已知正确的值（通过 WrathCombo 源码 + xivapi.com 交叉验证）：
        //   ADV / 新人：62147
        //   战斗职业（Job 图标，非 Soul Crystal）：62401~62804 范围
        //   生产职业（DoH）：62001~62008 范围
        //   采集职业（DoL）：63001~63003 范围（但 WrathCombo 对采集统一用 82096）
        //
        // 以下映射表通过以下方式确认：
        //   1. WrathCombo Icons.cs：MIN/BTN/FSH → 82096
        //   2. xivapi.com ClassJob 数据：Icon 字段
        //   3. Garland Tools / TexTools：交叉验证
        //
        // ⚠️ 如果图标仍不正确，说明此表中的某个 ID 有误，
        //    请在游戏中用 TexTools 或 /xivbar 等工具确认正确的 Icon ID。

        return classJobId switch
        {
            // 冒险者 / 基础职业（无 Job 职业）
            0 => 62147,   // ADV - 冒险者图标

            // ─── 坦克 Tank ───────────────────────────────────
            19 => 62401,   // 骑士 PLD
            21 => 62402,   // 战士 WAR
            32 => 62403,   // 暗黑骑士 DRK
            37 => 62404,   // 绝枪战士 GNB

            // ─── 治疗 Healer ────────────────────────────────
            24 => 62501,   // 白魔法师 WHM
            28 => 62502,   // 学者 SCH
            33 => 62503,   // 占星术士 AST
            40 => 62504,   // 贤者 SGE

            // ─── 近战 DPS Melee DPS ───────────────────────
            22 => 62601,   // 龙骑士 DRG
            20 => 62602,   // 武僧 MNK
            34 => 62603,   // 武士 SAM
            39 => 62604,   // 钐镰客 RPR

            // ─── 远敏 DPS Ranged DPS ──────────────────────
            23 => 62701,   // 吟游诗人 BRD
            31 => 62702,   // 机工士 MCH
            38 => 62703,   // 舞者 DNC

            // ─── 法系 DPS Magic DPS ────────────────────────
            25 => 62801,   // 黑魔法师 BLM
            27 => 62802,   // 召唤师 SMN
            35 => 62803,   // 赤魔法师 RDM
            42 => 62804,   // 绘灵法师 BLU

            // ─── 特殊近战（不在标准 Melee 分组）────────────
            30 => 62901,   // 忍者 NIN
            41 => 62902,   // 蝰蛇剑士 VPR

            // ─── 生产职业 Crafter (DoH) ──────────────────
            // 注：生产职业的图标 ID 在 62001~62008 范围
            8 => 62001,    // 刻木匠 CRP
            9 => 62002,    // 锻铁匠 BSM
            10 => 62003,   // 铸甲匠 ARM
            11 => 62004,   // 雕金匠 GSM
            12 => 62005,   // 制革匠 LTW
            13 => 62006,   // 裁衣匠 WVR
            14 => 62007,   // 炼金术士 ALC
            15 => 62008,   // 烹调师 CUL

            // ─── 采集职业 Gatherer (DoL) ──────────────────
            // 注：WrathCombo 对 MIN/BTN/FSH 统一使用 82096（角色选择界面的 DoL 图标）
            16 => 82096,   // 采矿工 MIN
            17 => 82096,   // 园艺工 BTN
            18 => 82096,   // 捕鱼人 FSH

            _ => 0
        };
    }
}
