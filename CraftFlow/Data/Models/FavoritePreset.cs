namespace CraftFlow.Data.Models;

/// <summary>
/// 装备选择项，用于收藏预设中保存的单个装备条目。
/// 必须是 class（非 record），否则 JSON 反序列化因缺少无参构造器而失败。
/// </summary>
public sealed class EquipmentSelection
{
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }

    public EquipmentSelection() { }

    public EquipmentSelection(uint itemId, string itemName, int quantity)
    {
        ItemId = itemId;
        ItemName = itemName;
        Quantity = quantity;
    }
}

/// <summary>
/// 用户收藏预设，包含一组装备选择项和元信息。
/// 持久化到 PluginConfig.FavoritePresets。
/// </summary>
public sealed class FavoritePreset
{
    /// <summary>
    /// 收藏名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 装备选择列表。
    /// </summary>
    public List<EquipmentSelection> Selections { get; set; } = [];

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 内置推荐套装条目，用于推荐队列的动态查询。
/// 通过装备词缀 + 装等 + HQ 标记动态查询最佳装备，避免硬编码 ItemId。
/// </summary>
public record PresetEntry(
    string DisplayName,
    string EquipmentAffix,
    string AccessoryAffix,
    int ItemLevel,
    bool IsHq,
    bool IncludeWeapon
);

/// <summary>
/// 内置推荐套装列表静态定义。
/// </summary>
public static class PresetEntryDefinitions
{
    /// <summary>
    /// 9 个内置推荐套装。
    /// </summary>
    public static readonly PresetEntry[] BuiltInPresets =
    [
        new("770HQ 坦克通用（含武器）", "fending", "fending", 770, true, true),
        new("770HQ 治疗通用", "healing", "healing", 770, true, true),
        new("770HQ 制敌DPS通用（含武器）", "maiming", "slaying", 770, true, true),
        new("770HQ 强袭DPS通用（含武器）", "striking", "slaying", 770, true, true),
        new("770HQ 游击DPS通用（含武器）", "scouting", "aiming", 770, true, true),
        new("770HQ 远敏DPS通用", "aiming", "aiming", 770, true, true),
        new("770HQ 法系DPS通用（含武器）", "casting", "casting", 770, true, true),
        new("750HQ 生产套", "crafting", "crafting", 750, true, true),
        new("750HQ 采集套", "gathering", "gathering", 750, true, true),
    ];
}
