using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using CraftFlow.Data.Models;

namespace CraftFlow.Data.GameData;

/// <summary>
/// 配方查询仓库，封装对 Lumina 数据的常用查询操作。
/// 依赖 LuminaCache 提供的预建索引实现高效查询。
/// </summary>
public sealed class RecipeRepository
{
    private readonly LuminaCache _cache;
    private readonly IPluginLog _log;

    /// <summary>
    /// 初始化 RecipeRepository 实例。
    /// </summary>
    /// <param name="cache">Lumina 数据缓存。</param>
    /// <param name="log">插件日志。</param>
    public RecipeRepository(LuminaCache cache, IPluginLog log)
    {
        _cache = cache;
        _log = log;
    }

    /// <summary>
    /// 根据 ItemId 查找物品的首个配方。
    /// 优先选择当前角色对应职业的配方；若无匹配，取 RowId 最小的配方。
    /// 若物品无配方，返回 null（表示叶节点/原材料）。
    /// </summary>
    /// <param name="itemId">目标物品 ID。</param>
    /// <returns>匹配的 Recipe，或 null。</returns>
    public Recipe? FindRecipeByItem(uint itemId)
    {
        if (!_cache.RecipeByResultItem.TryGetValue(itemId, out var recipes) || recipes.Count == 0)
        {
            return null;
        }

        // 取 RowId 最小（通常为最新版本或默认配方）
        return recipes.OrderBy(r => r.RowId).First();
    }

    /// <summary>
    /// 根据 ItemId 查找所有关联配方。
    /// 同一物品可能被不同职业/不同版本的配方产出。
    /// </summary>
    /// <param name="itemId">目标物品 ID。</param>
    /// <returns>配方列表，若无则返回空列表。</returns>
    public List<Recipe> FindRecipesByItem(uint itemId)
    {
        if (!_cache.RecipeByResultItem.TryGetValue(itemId, out var recipes))
        {
            return [];
        }

        return recipes;
    }

    /// <summary>
    /// 根据职业 ID 获取可制作的装备列表。
    /// </summary>
    /// <param name="classJobId">职业 ID（CraftType RowId，0 表示全部）。</param>
    /// <returns>该职业可制作的装备物品列表。</returns>
    [Obsolete("已被 EquipmentRepository 替代，保留以兼容现有代码")]
    public List<Item> GetEquipmentByClassJob(uint classJobId)
    {
        var result = new List<Item>();

        // 遍历所有配方，找到属于指定职业的配方
        foreach (var recipe in _cache.RecipeSheet.Values)
        {
            if (!recipe.CraftType.IsValid || recipe.ItemResult.Value.RowId == 0)
            {
                continue;
            }

            // 检查配方职业是否匹配（CraftType 关联 ClassJob）
            var craftType = recipe.CraftType.Value;
            if (craftType.RowId != classJobId && craftType.RowId != 0)
            {
                continue;
            }

            // 检查产出物品是否为装备类
            if (!_cache.ItemSheet.TryGetValue(recipe.ItemResult.Value.RowId, out var item))
            {
                continue;
            }

            // 装备特征：有 ClassJobUse 或属于装备分类
            if (item.ClassJobUse.RowId == 0 && item.EquipSlotCategory.RowId == 0)
            {
                continue;
            }

            result.Add(item);
        }

        _log.Debug($"GetEquipmentByClassJob({classJobId}): 找到 {result.Count} 件装备");
        return result;
    }

    /// <summary>
    /// 根据消耗品类别获取食物或药品列表。
    /// 按配方职业筛选 + ItemUICategory 名称/ID 匹配。
    /// 参考 HQHelper hqdata.json 的 meals/medicines 分类。
    /// </summary>
    public List<Item> GetConsumables(ConsumableCategory category)
    {
        uint targetCraftType = category == ConsumableCategory.Food ? 7u : 6u;
        var result = new List<Item>();

        foreach (var recipe in _cache.RecipeSheet.Values)
        {
            if (!recipe.ItemResult.IsValid || recipe.ItemResult.Value.RowId == 0) continue;
            if (!recipe.CraftType.IsValid || recipe.CraftType.Value.RowId != targetCraftType) continue;
            if (!_cache.ItemSheet.TryGetValue(recipe.ItemResult.Value.RowId, out var item)) continue;
            if (!item.ItemUICategory.IsValid) continue;

            var cat = item.ItemUICategory.Value;
            uint catId = cat.RowId;
            string catName = cat.Name.ToString().ToLowerInvariant();

            bool matches;
            if (category == ConsumableCategory.Food)
                matches = IsFood(catId, catName);
            else
                matches = IsMedicine(catId, catName);

            if (!matches) continue;
            result.Add(item);
        }

        result = result.GroupBy(i => i.RowId).Select(g => g.First()).ToList();
        _log.Debug($"GetConsumables({category}): {result.Count} 个");
        return result;
    }

    /// <summary>
    /// 判断是否为食物分类：按名称或已知 ID 范围匹配。
    /// </summary>
    private static bool IsFood(uint catId, string catName)
    {
        // 按名称匹配（多语言兼容）
        if (catName.Contains("meal") || catName.Contains("food") ||
            catName.Contains("料理") || catName.Contains("食物"))
            return true;

        // 按已知 ID 范围匹配（7.x Dawntrail）
        // 食物分类 RowId: 44 45 46
        return catId is 44 or 45 or 46;
    }

    /// <summary>
    /// 判断是否为药品分类：按名称或已知 ID 范围匹配。
    /// </summary>
    private static bool IsMedicine(uint catId, string catName)
    {
        // 按名称匹配（多语言兼容）
        if (catName.Contains("medicine") || catName.Contains("tincture") ||
            catName.Contains("potion") || catName.Contains("药品") ||
            catName.Contains("爆发药") || catName.Contains("药水") || catName.Contains("药剂"))
            return true;

        // 按已知 ID 范围匹配（7.x Dawntrail）
        // 药品分类 RowId: 47 48 49
        return catId is 47 or 48 or 49;
    }

    /// <summary>
    /// 获取全部收藏品配方列表（不分工票类型），并按职业填充分类信息。
    /// 收藏品名称统一加"收藏用"前缀，便于按职业分类展示。
    /// </summary>
    /// <returns>收藏品信息列表。</returns>
    public List<CollectibleInfo> GetAllCollectibles()
    {
        var result = new List<CollectibleInfo>();
        int totalSubrows = 0, skippedNoItem = 0, skippedNoTiers = 0, skippedNoRecipe = 0, skippedBySeries = 0;

        _log.Debug($"GetAllCollectibles(): CollectablesShopItemSheet 共 {_cache.CollectablesShopItemSheet.Count} 个 RowId");

        foreach (var (rowId, subrows) in _cache.CollectablesShopItemSheet)
        {
            // 遍历子行（同一 RowId 下可能有多个收藏品变体）
            foreach (var shopItem in subrows)
            {
                totalSubrows++;

                bool hasRefine = shopItem.CollectablesShopRefine.IsValid;
                bool hasRewardScrip = shopItem.CollectablesShopRewardScrip.IsValid;

                if (!shopItem.Item.IsValid || shopItem.Item.Value.RowId == 0)
                {
                    skippedNoItem++;
                    _log.Debug($"  SKIP[无Item] RowId={rowId} Item.IsValid={shopItem.Item.IsValid}");
                    continue;
                }

                var itemId = shopItem.Item.Value.RowId;
                var rawName = _cache.GetItemName(itemId);

                // 仅保留制作职业收藏品（游戏内命名为"收藏用…"），排除：
                //  - 旧版"面向收藏家的…"/"收藏家…"系列（Endwalker 及更早）
                //  - "改良用…"采集职业收藏品（不属于制作职业，不应出现在制作 Tab）
                // 注意：ItemSeries.Name 在收藏品数据上恒为空，不能用资料片系列名过滤。
                if (!IsCraftCollectable(rawName))
                {
                    skippedBySeries++;
                    _log.Debug($"  SKIP[非制作收藏品] RowId={rowId} Item={itemId}({rawName})");
                    continue;
                }

                // 获取评分档位
                var scoreTiers = GetScoreTiersForCollectable(shopItem);
                if (scoreTiers.Count == 0)
                {
                    skippedNoTiers++;
                    _log.Debug($"  SKIP[无档位] RowId={rowId} Item={itemId}({rawName}) RefineValid={hasRefine} RewardScripValid={hasRewardScrip}");
                    continue;
                }

                // 查找关联配方
                var recipe = FindRecipeByItem(itemId);
                if (recipe is null)
                {
                    skippedNoRecipe++;
                    _log.Debug($"  SKIP[无配方] RowId={rowId} Item={itemId}({rawName})");
                    continue;
                }

                // 获取收藏品等级（配方的 RecipeLevelTable.ClassJobLevel，用于评分区间分组）
                int collectableLevel = recipe.Value.RecipeLevelTable.IsValid
                    ? recipe.Value.RecipeLevelTable.Value.ClassJobLevel
                    : 0;

                // 制作职业（CraftType RowId → 名称）
                uint craftTypeId = recipe.Value.CraftType.IsValid ? recipe.Value.CraftType.Value.RowId : 0;
                string craftTypeName = craftTypeId != 0 ? _cache.GetCraftTypeName(craftTypeId) : "未知";

                // 名称：原始名可能已含"收藏用"前缀（游戏数据），避免重复添加
                string displayName = rawName.StartsWith("收藏用") ? rawName : "收藏用" + rawName;

                result.Add(new CollectibleInfo
                {
                    RecipeId = recipe.Value.RowId,
                    ItemId = itemId,
                    ItemName = displayName,
                    ScripType = DetermineScripType(shopItem),
                    ScoreThresholds = scoreTiers,
                    CollectableLevel = collectableLevel,
                    CraftTypeId = craftTypeId,
                    CraftTypeName = craftTypeName
                });
            }
        }

        _log.Debug($"GetAllCollectibles(): 原始子行={totalSubrows}, 无效Item={skippedNoItem}, 非制作收藏品={skippedBySeries}, 无评分档位={skippedNoTiers}, 无配方={skippedNoRecipe} → 最终={result.Count} 个收藏品");

        // 详细诊断日志：逐条输出以便与 Artisan 对比
        foreach (var c in result.OrderBy(c => c.CollectableLevel).ThenBy(c => c.CraftTypeId))
        {
            _log.Debug($"  [{c.CraftTypeName}] Lv{c.CollectableLevel} | {c.ItemName}(Item={c.ItemId}) | Recipe={c.RecipeId} | {c.ScripType} | 档位数={c.ScoreThresholds.Count}");
        }

        return result;
    }

    /// <summary>
    /// 判定物品是否属于制作职业收藏品。
    /// 游戏内制作收藏品统一以"收藏用"开头（如"收藏用雪松长弓"）；
    /// "改良用…"为采集职业收藏品，"面向收藏家的…"/"收藏家…"为旧版系列，均排除。
    /// 注意：ItemSeries.Name 在收藏品数据上恒为空，不能据此按资料片过滤。
    /// </summary>
    private bool IsCraftCollectable(string itemName)
    {
        return itemName.StartsWith("收藏用");
    }

    /// <summary>
    /// 根据工票类型获取收藏品配方列表（含评分/票数映射）。
    /// 已废弃按工票分类的 UI；保留以便兼容，内部委托 GetAllCollectibles 后过滤。
    /// </summary>
    /// <param name="scripType">工票类型（橙票/紫票）。</param>
    /// <returns>收藏品信息列表。</returns>
    public List<CollectibleInfo> GetCollectibles(ScripType scripType)
    {
        return GetAllCollectibles().Where(i => i.ScripType == scripType).ToList();
    }

    /// <summary>
    /// 判断材料的来源类型。
    /// 优先级：Craftable > Gatherable > Purchasable > Drop > Unknown。
    /// 对于叶节点（原材料），Craftable 通常不成立，实际优先级为 Gatherable > Purchasable > Drop。
    /// 注意：Drop 判断为排除法 — 物品存在但无法制作/采集/购买时，推断为怪物/副本掉落。
    /// </summary>
    /// <param name="itemId">物品 ID。</param>
    /// <returns>材料来源类型。</returns>
    public MaterialSource GetMaterialSource(uint itemId)
    {
        if (!_cache.ItemSheet.TryGetValue(itemId, out var item))
        {
            return MaterialSource.Unknown;
        }

        // 如果物品有配方，则为 Craftable
        if (_cache.RecipeByResultItem.ContainsKey(itemId))
        {
            return MaterialSource.Craftable;
        }

        // 判断是否可采集：通过 GatheringItem 表 + ItemUICategory 回退
        if (IsGatherableItem(item))
        {
            return MaterialSource.Gatherable;
        }

        // 判断是否可购买：检查是否有商店价格（PriceMid 为商店售价）
        if (item.PriceMid > 0 || item.PriceLow > 0)
        {
            return MaterialSource.Purchasable;
        }

        // 物品存在但无法制作/采集/购买 → 推断为怪物/副本掉落
        return MaterialSource.Drop;
    }

    /// <summary>
    /// 获取物品名称。
    /// </summary>
    /// <param name="itemId">物品 ID。</param>
    /// <returns>物品名称。</returns>
    public string GetItemName(uint itemId)
    {
        return _cache.GetItemName(itemId);
    }

    /// <summary>
    /// 获取收藏品评分档位列表。
    /// 在 Dalamud 15/Lumina 新版中：
    /// - CollectablesShopRefine 仅包含收集度阈值（Low/Mid/High Collectability）
    /// - 工票奖励从 CollectablesShopRewardScrip 获取
    /// </summary>
    private List<ScoreTier> GetScoreTiersForCollectable(CollectablesShopItem shopItem)
    {
        var tiers = new List<ScoreTier>();

        // 从 CollectablesShopRefine 获取收集度阈值
        if (!shopItem.CollectablesShopRefine.IsValid)
        {
            return tiers;
        }

        var refine = shopItem.CollectablesShopRefine.Value;

        // 从 CollectablesShopRewardScrip 获取工票奖励
        var rewardScrip = shopItem.CollectablesShopRewardScrip.IsValid
            ? shopItem.CollectablesShopRewardScrip.Value
            : default(CollectablesShopRewardScrip?);

        // Low 档
        if (refine.LowCollectability > 0)
        {
            tiers.Add(new ScoreTier
            {
                MinScore = refine.LowCollectability,
                ScripReward = rewardScrip.HasValue ? rewardScrip.Value.LowReward : 0
            });
        }

        // Mid 档
        if (refine.MidCollectability > 0)
        {
            tiers.Add(new ScoreTier
            {
                MinScore = refine.MidCollectability,
                ScripReward = rewardScrip.HasValue ? rewardScrip.Value.MidReward : 0
            });
        }

        // High 档
        if (refine.HighCollectability > 0)
        {
            tiers.Add(new ScoreTier
            {
                MinScore = refine.HighCollectability,
                ScripReward = rewardScrip.HasValue ? rewardScrip.Value.HighReward : 0
            });
        }

        return tiers;
    }

    /// <summary>
    /// 根据 CollectablesShopItem 判断工票类型。
    /// 通过 CollectablesShopRewardScrip.Currency 字段判断（已用真实游戏数据验证）：
    ///   Currency=6 → 巧手橙票（橙票），对应 Lv100 收藏品
    ///   Currency=2/4/7 → 巧手紫票（紫票），对应其余等级（Lv50-99 等）
    /// 注意：不能用 ">= 2" 这种阈值，否则会把 Lv100 的橙票（Currency=6）误判为紫票。
    /// </summary>
    private ScripType DetermineScripType(CollectablesShopItem shopItem)
    {
        if (shopItem.CollectablesShopRewardScrip.IsValid)
        {
            int currency = (int)shopItem.CollectablesShopRewardScrip.Value.Currency;
            if (currency == 6)
                return ScripType.OrangeScrip;
        }

        return ScripType.PurpleScrip;
    }

    /// <summary>
    /// 获取所有制作职业列表（CraftType RowId → 名称映射）。
    /// 用于装备 Tab 的职业筛选下拉框。
    /// </summary>
    /// <returns>职业 ID 到名称的映射字典。</returns>
    public Dictionary<uint, string> GetCraftClassJobs()
    {
        var result = new Dictionary<uint, string>();
        var seenCraftTypes = new HashSet<uint>();

        foreach (var recipe in _cache.RecipeSheet.Values)
        {
            if (!recipe.CraftType.IsValid)
            {
                continue;
            }

            var craftTypeId = recipe.CraftType.Value.RowId;
            if (craftTypeId == 0 || !seenCraftTypes.Add(craftTypeId))
            {
                continue;
            }

            var craftTypeName = _cache.GetCraftTypeName(craftTypeId);
            if (!string.IsNullOrEmpty(craftTypeName))
            {
                result[craftTypeId] = craftTypeName;
            }
        }

        return result;
    }

    /// <summary>
    /// 判断物品 UI 分类是否为食物类。
    /// 优先通过 ItemUICategory 名称判断，回退到 ID 范围。
    /// </summary>
    private bool IsFoodCategory(uint uiCategoryId)
    {
        // 通过名称判断
        if (_cache.ItemUICategorySheet.TryGetValue(uiCategoryId, out var cat))
        {
            var name = cat.Name.ToString().ToLowerInvariant();
            if (name.Contains("meal") || name.Contains("food") || name.Contains("料理") || name.Contains("食物"))
                return true;
        }

        // 回退：已知食物分类 ID（7.x Dawntrail）
        return uiCategoryId is 44 or 45 or 46;
    }

    /// <summary>
    /// 判断物品 UI 分类是否为药品类。
    /// 优先通过 ItemUICategory 名称判断，回退到 ID 范围。
    /// </summary>
    private bool IsMedicineCategory(uint uiCategoryId)
    {
        // 通过名称判断
        if (_cache.ItemUICategorySheet.TryGetValue(uiCategoryId, out var cat))
        {
            var name = cat.Name.ToString().ToLowerInvariant();
            if (name.Contains("medicine") || name.Contains("tincture") || name.Contains("potion")
                || name.Contains("药品") || name.Contains("爆发药"))
                return true;
        }

        // 回退：已知药品分类 ID（7.x Dawntrail）
        return uiCategoryId is 47 or 48 or 49;
    }

    /// <summary>
    /// 判断物品是否为可采集物品。
    /// 优先通过 Lumina GatheringItem 表判断（准确），回退到 ItemUICategory 分类判断。
    /// 注意：GatheringItem 索引覆盖了绝大多数采集物，ID 范围回退仅在极少数边缘情况下触发。
    /// </summary>
    /// <remarks>
    /// 版本维护提醒：以下硬编码的 ItemUICategory RowId 范围基于 7.x Dawntrail。
    /// 当新资料片（8.x+）发布后，如果新增采集物分类，需要同步更新此范围。
    /// 验证方法：在游戏中查询新采集物的 ItemUICategory，确认其 RowId 是否已在范围内。
    /// </remarks>
    private bool IsGatherableItem(Item item)
    {
        // 优先通过 GatheringItem 表判断：如果物品是采集点的产出物，则为可采集
        if (_cache.GatherableItemIds.Contains(item.RowId))
        {
            return true;
        }

        // 回退：通过 ItemUICategory 判断
        if (!item.ItemUICategory.IsValid) return false;
        var catId = item.ItemUICategory.Value.RowId;

        // === 版本相关硬编码开始 (7.x Dawntrail) ===
        // 采集材料常见分类 ID：
        //   3-18: 矿石/木材/纤维/皮革等原材料
        //  20-32: 金属锭/石材/染料等
        //     38: 鱼类
        //  58-62: 采集物（兼容旧范围）
        // === 版本相关硬编码结束 ===
        return (catId >= 3 && catId <= 18)
            || (catId >= 20 && catId <= 32)
            || catId == 38
            || (catId >= 58 && catId <= 62);
    }
}
