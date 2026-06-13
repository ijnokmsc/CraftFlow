using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina;
using Lumina.Data;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace CraftFlow.Data.GameData;

/// <summary>
/// Lumina 数据缓存与索引构建器。
/// 在插件初始化时加载所需的游戏数据 Sheet，并预建常用索引字典以提高查询性能。
/// </summary>
public sealed class LuminaCache
{
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _log;

    /// <summary>
    /// Item Sheet 数据，以 RowId 为键。
    /// </summary>
    public Dictionary<uint, Item> ItemSheet { get; private set; } = [];

    /// <summary>
    /// Recipe Sheet 数据，以 RowId 为键。
    /// </summary>
    public Dictionary<uint, Recipe> RecipeSheet { get; private set; } = [];

    /// <summary>
    /// Recipe 按产出物品 ItemId 分组的索引。
    /// 同一物品可能被多个配方产出（不同职业/不同版本）。
    /// </summary>
    public Dictionary<uint, List<Recipe>> RecipeByResultItem { get; private set; } = [];

    /// <summary>
    /// 收藏品商店物品数据，用于工票计算。
    /// 注意：CollectablesShopItem 是子行(Sheet)，需用 GetSubrowExcelSheet 加载。
    /// 存储为 RowId → 子行列表 的映射。
    /// </summary>
    public Dictionary<uint, List<CollectablesShopItem>> CollectablesShopItemSheet { get; private set; } = [];

    /// <summary>
    /// 收藏品评分精炼数据，仅包含收集度阈值(Low/Mid/High Collectability)。
    /// </summary>
    public Dictionary<uint, CollectablesShopRefine> CollectablesShopRefineSheet { get; private set; } = [];

    /// <summary>
    /// 收藏品工票奖励数据，包含 Currency/LowReward/MidReward/HighReward。
    /// 在 Dalamud 15/Lumina 新版中，工票奖励从此表获取（旧版在 CollectablesShopRefine）。
    /// </summary>
    public Dictionary<uint, CollectablesShopRewardScrip> CollectablesShopRewardScripSheet { get; private set; } = [];

    /// <summary>
    /// 职业数据，用于装备分类。
    /// </summary>
    public Dictionary<uint, ClassJob> ClassJobSheet { get; private set; } = [];

    /// <summary>
    /// 物品 UI 分类数据，用于装备/消耗品分类。
    /// </summary>
    public Dictionary<uint, ItemUICategory> ItemUICategorySheet { get; private set; } = [];

    /// <summary>
    /// ClassJobCategory Sheet 数据，以 RowId 为键。
    /// 用于装备词缀匹配（fending/healing/maiming 等）。
    /// </summary>
    public Dictionary<uint, ClassJobCategory> ClassJobCategorySheet { get; private set; } = [];

    /// <summary>
    /// EquipSlotCategory Sheet 数据，以 RowId 为键。
    /// 用于判断装备槽位类型（主手/副手/头/身等）。
    /// </summary>
    public Dictionary<uint, EquipSlotCategory> EquipSlotCategorySheet { get; private set; } = [];

    /// <summary>
    /// ClassJobCategory 按 Name 字段的索引，用于词缀匹配。
    /// Key 为 ClassJobCategory.Name（如 "fending"、"healing"），Value 为 RowId。
    // Reserved for future use
    /// </summary>
    public Dictionary<string, uint> ClassJobCategoryByName { get; private set; } = [];

    /// <summary>
    /// GatheringItem Sheet 数据，以 RowId 为键。
    /// 用于判断物品是否为可采集物。
    /// </summary>
    public Dictionary<uint, GatheringItem> GatheringItemSheet { get; private set; } = [];

    /// <summary>
    /// 可采集物品 ID 集合，从 GatheringItem 表构建的索引。
    /// 用于快速判断物品是否为采集物。
    /// </summary>
    public HashSet<uint> GatherableItemIds { get; private set; } = [];

    /// <summary>
    /// 初始化 LuminaCache 实例。
    /// </summary>
    /// <param name="dataManager">Dalamud 数据管理器。</param>
    /// <param name="log">插件日志。</param>
    public LuminaCache(IDataManager dataManager, IPluginLog log)
    {
        _dataManager = dataManager;
        _log = log;
    }

    /// <summary>
    /// 加载所有需要的 Lumina Sheet 并构建索引。
    /// 应在插件初始化时调用一次。
    /// </summary>
    public void Init()
    {
        _log.Information("LuminaCache 开始加载数据...");

        // 加载 Item Sheet
        var itemSheet = _dataManager.GetExcelSheet<Item>();
        if (itemSheet is not null)
        {
            ItemSheet = itemSheet.ToDictionary(row => row.RowId, row => row);
            _log.Information($"已加载 {ItemSheet.Count} 条 Item 数据");
        }
        else
        {
            _log.Warning("无法加载 Item Sheet");
        }

        // 加载 Recipe Sheet 并构建 RecipeByResultItem 索引
        var recipeSheet = _dataManager.GetExcelSheet<Recipe>();
        if (recipeSheet is not null)
        {
            RecipeSheet = recipeSheet.ToDictionary(row => row.RowId, row => row);

            RecipeByResultItem = recipeSheet
                .Where(r => r.ItemResult.IsValid && r.ItemResult.Value.RowId != 0)
                .GroupBy(r => r.ItemResult.Value.RowId)
                .ToDictionary(g => g.Key, g => g.ToList());

            _log.Information($"已加载 {RecipeSheet.Count} 条 Recipe 数据，构建 {RecipeByResultItem.Count} 条 Item→Recipe 索引");
        }
        else
        {
            _log.Warning("无法加载 Recipe Sheet");
        }

        // 加载收藏品商店数据（子行 Sheet）
        var collectablesShopItemSheet = _dataManager.GetSubrowExcelSheet<CollectablesShopItem>();
        if (collectablesShopItemSheet is not null)
        {
            // 子行 Sheet：每个 RowId 可能有多个子行
            var dict = new Dictionary<uint, List<CollectablesShopItem>>();
            foreach (var row in collectablesShopItemSheet)
            {
                var subrows = row.ToList();
                if (subrows.Count > 0)
                {
                    dict[row.First().RowId] = subrows;
                }
            }
            CollectablesShopItemSheet = dict;
            _log.Information($"已加载 {CollectablesShopItemSheet.Count} 条 CollectablesShopItem 数据");
        }
        else
        {
            _log.Warning("无法加载 CollectablesShopItem Sheet");
        }

        // 加载收藏品评分精炼数据
        var collectablesShopRefineSheet = _dataManager.GetExcelSheet<CollectablesShopRefine>();
        if (collectablesShopRefineSheet is not null)
        {
            CollectablesShopRefineSheet = collectablesShopRefineSheet.ToDictionary(row => row.RowId, row => row);
            _log.Information($"已加载 {CollectablesShopRefineSheet.Count} 条 CollectablesShopRefine 数据");
        }
        else
        {
            _log.Warning("无法加载 CollectablesShopRefine Sheet");
        }

        // 加载收藏品工票奖励数据
        var collectablesShopRewardScripSheet = _dataManager.GetExcelSheet<CollectablesShopRewardScrip>();
        if (collectablesShopRewardScripSheet is not null)
        {
            CollectablesShopRewardScripSheet = collectablesShopRewardScripSheet.ToDictionary(row => row.RowId, row => row);
            _log.Information($"已加载 {CollectablesShopRewardScripSheet.Count} 条 CollectablesShopRewardScrip 数据");
        }
        else
        {
            _log.Warning("无法加载 CollectablesShopRewardScrip Sheet");
        }

        // 加载职业数据
        var classJobSheet = _dataManager.GetExcelSheet<ClassJob>();
        if (classJobSheet is not null)
        {
            ClassJobSheet = classJobSheet.ToDictionary(row => row.RowId, row => row);
            _log.Information($"已加载 {ClassJobSheet.Count} 条 ClassJob 数据");
        }
        else
        {
            _log.Warning("无法加载 ClassJob Sheet");
        }

        // 加载物品 UI 分类数据
        var itemUICategorySheet = _dataManager.GetExcelSheet<ItemUICategory>();
        if (itemUICategorySheet is not null)
        {
            ItemUICategorySheet = itemUICategorySheet.ToDictionary(row => row.RowId, row => row);
            _log.Information($"已加载 {ItemUICategorySheet.Count} 条 ItemUICategory 数据");
        }
        else
        {
            _log.Warning("无法加载 ItemUICategory Sheet");
        }

        // 加载 ClassJobCategory Sheet 并构建 Name → RowId 索引
        var classJobCategorySheet = _dataManager.GetExcelSheet<ClassJobCategory>();
        if (classJobCategorySheet is not null)
        {
            ClassJobCategorySheet = classJobCategorySheet.ToDictionary(row => row.RowId, row => row);

            // 构建 Name → RowId 索引，用于词缀匹配
            ClassJobCategoryByName = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in ClassJobCategorySheet.Values)
            {
                var name = category.Name.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    ClassJobCategoryByName[name] = category.RowId;
                }
            }

            _log.Information($"已加载 {ClassJobCategorySheet.Count} 条 ClassJobCategory 数据，构建 {ClassJobCategoryByName.Count} 条 Name 索引");
        }
        else
        {
            _log.Warning("无法加载 ClassJobCategory Sheet");
        }

        // 加载 EquipSlotCategory Sheet
        var equipSlotCategorySheet = _dataManager.GetExcelSheet<EquipSlotCategory>();
        if (equipSlotCategorySheet is not null)
        {
            EquipSlotCategorySheet = equipSlotCategorySheet.ToDictionary(row => row.RowId, row => row);
            _log.Information($"已加载 {EquipSlotCategorySheet.Count} 条 EquipSlotCategory 数据");
        }
        else
        {
            _log.Warning("无法加载 EquipSlotCategory Sheet");
        }

        // 加载 GatheringItem Sheet 并构建可采集物品索引
        var gatheringItemSheet = _dataManager.GetExcelSheet<GatheringItem>();
        if (gatheringItemSheet is not null)
        {
            GatheringItemSheet = gatheringItemSheet.ToDictionary(row => row.RowId, row => row);
            GatherableItemIds = [.. gatheringItemSheet
                .Where(gi => gi.Item.RowId != 0)
                .Select(gi => gi.Item.RowId)
                .Distinct()];
            _log.Information($"已加载 {GatheringItemSheet.Count} 条 GatheringItem 数据，索引 {GatherableItemIds.Count} 种可采集物品");
        }
        else
        {
            _log.Warning("无法加载 GatheringItem Sheet");
        }

        _log.Information("LuminaCache 数据加载完成");
    }

    /// <summary>
    /// 获取物品名称，若数据缺失则返回占位文本。
    /// </summary>
    /// <param name="itemId">物品 ID。</param>
    /// <returns>物品名称字符串。</returns>
    public string GetItemName(uint itemId)
    {
        if (ItemSheet.TryGetValue(itemId, out var item))
        {
            return item.Name.ToString();
        }

        return $"未知物品({itemId})";
    }

    /// <summary>
    /// 根据 CraftType RowId 获取制作职业名称。
    /// 8 个制作职业的 CraftType → 名称映射。
    /// </summary>
    /// <param name="craftTypeId">CraftType RowId。</param>
    /// <returns>职业名称字符串。</returns>
    public string GetCraftTypeName(uint craftTypeId)
    {
        // 8 个制作职业的 CraftType → ClassJob 映射
        // 使用 Lumina 的 ClassJobSheet 获取本地化名称
        // 简化映射：
        var craftTypeNames = new Dictionary<uint, string>
        {
            [0] = "刻木匠", [1] = "锻铁匠", [2] = "铸甲匠", [3] = "雕金匠",
            [4] = "制革匠", [5] = "裁缝匠", [6] = "炼金术士", [7] = "烹调师"
        };
        return craftTypeNames.GetValueOrDefault(craftTypeId) ?? "未知";
    }
}
