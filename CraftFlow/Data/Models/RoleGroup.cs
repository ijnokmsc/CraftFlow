namespace CraftFlow.Data.Models;

/// <summary>
/// 角色分组定义，用于装备 Tab 的三级选择流程。
/// 每个分组包含一组职业、防具词缀和首饰词缀，用于 Lumina ClassJobCategory 匹配。
/// </summary>
public record RoleGroup(
    string DisplayName,
    string EnglishName,
    (uint ClassJobId, string Name)[] Jobs,
    string EquipmentAffix,
    string AccessoryAffix
);

/// <summary>
/// 9 个角色分组静态定义，供全项目引用。
/// 装备词缀用于防具类槽位（头/身/手/腿/足），首饰词缀用于首饰类槽位（耳/颈/腕/指）。
/// </summary>
public static class RoleGroupDefinitions
{
    /// <summary>
    /// 全部 9 个角色分组。
    /// </summary>
    public static readonly RoleGroup[] RoleGroups =
    [
        new("防护职业", "Tank",
            [(19, "骑士"), (21, "战士"), (32, "暗黑骑士"), (37, "绝枪战士")],
            "fending", "fending"),

        new("治疗职业", "Healer",
            [(24, "白魔法师"), (28, "学者"), (33, "占星术士"), (40, "贤者")],
            "healing", "healing"),

        new("制敌DPS", "Maiming DPS",
            [(22, "龙骑士"), (39, "钐镰客")],
            "maiming", "slaying"),

        new("强袭DPS", "Striking DPS",
            [(20, "武僧"), (34, "武士")],
            "striking", "slaying"),

        new("游击DPS", "Scouting DPS",
            [(30, "忍者"), (41, "蝰蛇剑士")],
            "scouting", "aiming"),

        new("远敏DPS", "Ranged DPS",
            [(23, "吟游诗人"), (31, "机工士"), (38, "舞者")],
            "aiming", "aiming"),

        new("法系DPS", "Casting DPS",
            [(25, "黑魔法师"), (27, "召唤师"), (35, "赤魔法师"), (42, "绘灵法师")],
            "casting", "casting"),

        new("大地使者", "Gatherer",
            [(16, "采矿工"), (17, "园艺工"), (18, "捕鱼人")],
            "gathering", "gathering"),

        new("能工巧匠", "Crafter",
            [(8, "刻木匠"), (9, "锻铁匠"), (10, "铸甲匠"), (11, "雕金匠"),
             (12, "制革匠"), (13, "裁衣匠"), (14, "炼金术士"), (15, "烹调师")],
            "crafting", "crafting"),
    ];

    /// <summary>
    /// 根据装备词缀或首饰词缀查找对应的 RoleGroup。
    /// 用于推荐套装的动态装备查询，将 affix 字符串映射到含 Jobs 列表的完整 RoleGroup。
    /// </summary>
    /// <param name="affix">装备词缀（如 "fending"、"slaying"）。</param>
    /// <returns>匹配的 RoleGroup，若未找到返回 null。</returns>
    public static RoleGroup? GetByAffix(string affix) =>
        RoleGroups.FirstOrDefault(r => r.EquipmentAffix == affix || r.AccessoryAffix == affix);
}
