using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using CraftFlow.Data.GameData;
using CraftFlow.Data.Models;

namespace CraftFlow.Services;

/// <summary>
/// BOM 递归展开服务，实现配方树的深度优先遍历。
/// 包含深度限制（10 层）和循环检测（HashSet 回溯）。
/// 注意：不使用缓存，因为缓存会导致子节点 Children 共享引用，
/// 当同一物品在不同分支以不同数量出现时，数量不会按新倍率缩放，
/// 导致 MaterialAggregator 聚合结果错误。FF14 配方树通常不超过 5-6 层，性能可接受。
/// </summary>
public sealed class BomExpander
{
    private const int MaxDepth = 10;

    private readonly RecipeRepository _recipeRepo;
    private readonly IPluginLog _log;

    /// <summary>
    /// 初始化 BomExpander 实例。
    /// </summary>
    /// <param name="recipeRepo">配方查询仓库。</param>
    /// <param name="log">插件日志。</param>
    public BomExpander(RecipeRepository recipeRepo, IPluginLog log)
    {
        _recipeRepo = recipeRepo;
        _log = log;
    }

    /// <summary>
    /// 展开指定物品的 BOM 树。
    /// </summary>
    /// <param name="itemId">目标物品 ID。</param>
    /// <param name="quantity">需求数量。</param>
    /// <returns>BOM 树根节点。</returns>
    public BomNode Expand(uint itemId, int quantity)
    {
        return ExpandRecursive(itemId, quantity, 0, new HashSet<uint>());
    }

    /// <summary>
    /// 递归展开 BOM 子树。
    /// </summary>
    /// <param name="itemId">当前物品 ID。</param>
    /// <param name="quantity">当前层级需求数量。</param>
    /// <param name="depth">当前递归深度。</param>
    /// <param name="visited">当前路径已访问的 ItemId 集合，用于循环检测。</param>
    /// <returns>BOM 子树节点。</returns>
    private BomNode ExpandRecursive(uint itemId, int quantity, int depth, HashSet<uint> visited)
    {
        // 循环检测：同一物品在当前路径中已出现
        if (!visited.Add(itemId))
        {
            _log.Warning($"BOM 循环检测: Item {itemId} 已在当前路径中 (深度 {depth})");
            return new BomNode
            {
                ItemId = itemId,
                ItemName = _recipeRepo.GetItemName(itemId),
                Quantity = quantity,
                Depth = depth,
                IsIncomplete = true
            };
        }

        // 深度限制
        if (depth >= MaxDepth)
        {
            _log.Warning($"BOM 深度超限: Item {itemId} 在第 {depth} 层");
            return new BomNode
            {
                ItemId = itemId,
                ItemName = _recipeRepo.GetItemName(itemId),
                Quantity = quantity,
                Depth = depth,
                IsIncomplete = true
            };
        }

        // 查找配方
        var recipe = _recipeRepo.FindRecipeByItem(itemId);
        var node = new BomNode
        {
            ItemId = itemId,
            ItemName = _recipeRepo.GetItemName(itemId),
            Quantity = quantity,
            Depth = depth,
            RecipeId = recipe?.RowId
        };

        if (recipe.HasValue)
        {
            // 配方产量（AmountResult）：多数配方=1，部分中间产品=3/5等
            // ingredient 数量是按"AmountResult 个产出"给出的，需按需折算
            int yield = recipe.Value.AmountResult > 0 ? recipe.Value.AmountResult : 1;
            node.AmountResult = yield;

            // 遍历 8 个材料槽位
            for (int i = 0; i < 8; i++)
            {
                var ingredientId = recipe.Value.Ingredient[i];
                var ingredientQty = recipe.Value.AmountIngredient[i];

                if (ingredientId.RowId == 0 || ingredientQty == 0)
                {
                    continue;
                }

                var childItemId = ingredientId.RowId;
                // 需要制作 ceil(quantity / yield) 次，每次消耗 ingredientQty 个材料
                var craftsNeeded = Math.Max(1, (int)Math.Ceiling((double)quantity / yield));
                var childQty = (int)ingredientQty * craftsNeeded;
                var childNode = ExpandRecursive(childItemId, childQty, depth + 1, new HashSet<uint>(visited));
                node.Children.Add(childNode);
            }
        }

        // 回溯：允许其他路径经过此节点
        visited.Remove(itemId);
        return node;
    }
}
