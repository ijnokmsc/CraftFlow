using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using CraftFlow.Config;
using CraftFlow.Data.Models;
using CraftFlow.Helpers;
using CraftFlow.IPC;

namespace CraftFlow.Services;

/// <summary>
/// 制作编排服务，封装一键 Artisan 制作的业务逻辑。
///
/// 职责：
/// 1. 材料缺口校验（考虑半成品 effective needs 扣减）
/// 2. 制作步骤过滤（已有半成品跳过或减少制作次数）
/// 3. 启动进度管理器 + 开启 Artisan 耐力
///
/// 不负责：
/// - UI 渲染（由 MaterialListWidget 处理）
/// - 主窗口隐藏/进度窗口显示（由调用方通过回调处理）
///
/// 从 MaterialListWidget.CraftWithArtisan 提取（P4 重构）。
/// </summary>
public sealed class CraftOrchestrator
{
    private readonly PluginConfig _config;
    private readonly IPluginLog _log;
    private readonly CraftProgressManager _progressManager;
    private readonly ArtisanIpcClient _artisanIpc;

    public CraftOrchestrator(
        PluginConfig config,
        IPluginLog log,
        CraftProgressManager progressManager,
        ArtisanIpcClient artisanIpc)
    {
        _config = config;
        _log = log;
        _progressManager = progressManager;
        _artisanIpc = artisanIpc;
    }

    /// <summary>
    /// 尝试启动一键制作。
    ///
    /// 流程：
    /// 1. 校验步骤非空且 Artisan 可用
    /// 2. 校验材料充足（若传入 materials）
    /// 3. 过滤步骤：跳过已充足的半成品，调整不足的制作次数
    /// 4. 启动进度管理器 + 开启耐力
    ///
    /// </summary>
    /// <param name="steps">拓扑排序后的制作步骤列表。</param>
    /// <param name="materials">材料清单（可为 null，表示不校验材料）。</param>
    /// <param name="bomRoot">BOM 树根节点（仅当启用 OnlyMissingMaterials 时用于 effective needs 计算）。</param>
    /// <returns>过滤后待执行的步骤列表；若中止返回 null。</returns>
    public List<CraftStep>? TryStartCraft(List<CraftStep> steps, List<MaterialEntry>? materials, BomNode? bomRoot)
    {
        if (steps.Count == 0)
        {
            _log.Information("无制作步骤");
            return null;
        }

        if (!_artisanIpc.IsAvailable)
        {
            _log.Warning("Artisan 不可用");
            return null;
        }

        if (materials is not null && materials.Count > 0)
        {
            if (!ValidateMaterialsSufficient(materials, bomRoot))
            {
                return null;
            }
        }

        var filteredSteps = FilterStepsByInventory(steps);

        _progressManager.Start(filteredSteps);
        _artisanIpc.SetEnduranceStatus(true);

        return filteredSteps;
    }

    /// <summary>
    /// 校验材料是否充足。考虑两种模式：
    /// - OnlyMissingMaterials + bomRoot：使用 effective needs（含半成品扣减）再扣除背包库存
    /// - 其他：直接用 CheckInventory 检查原材料缺口
    /// </summary>
    /// <returns>true 表示充足；false 表示不足（已记录日志）。</returns>
    private bool ValidateMaterialsSufficient(List<MaterialEntry> materials, BomNode? bomRoot)
    {
        bool useHq = _config.OnlyMissingMaterials && _config.HqOnly;
        List<(MaterialEntry Entry, int Deficit)> missing;

        if (_config.OnlyMissingMaterials && bomRoot is not null)
        {
            // 使用有效需求（含半成品扣减），再扣除原材料背包库存
            var effectiveNeeds = InventoryHelper.CalculateEffectiveNeeds(bomRoot, useHq);
            missing = materials
                .Where(m => effectiveNeeds.TryGetValue(m.ItemId, out int need) && need > 0)
                .Select(m =>
                {
                    int owned = InventoryHelper.GetItemCount(m.ItemId, false);
                    int deficit = Math.Max(0, effectiveNeeds[m.ItemId] - owned);
                    return (m, deficit);
                })
                .Where(x => x.deficit > 0)
                .ToList();
        }
        else
        {
            var inv = InventoryHelper.CheckInventory(materials, useHq);
            missing = inv.Where(i => i.Deficit > 0)
                .Select(i => (i.Entry, i.Deficit))
                .ToList();
        }

        if (missing.Count > 0)
        {
            var names = string.Join(", ", missing.Take(3).Select(m => $"{m.Entry.ItemName} 缺{m.Deficit}"));
            if (missing.Count > 3) names += $" 等{missing.Count}种";
            _log.Warning($"材料不足，无法开始制作: {names}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 根据背包已有量过滤制作步骤：
    /// - 已有量 >= 需求总量：跳过该步骤
    /// - 已有量 < 需求总量：减少制作次数（向上取整）
    /// - 已有量 <= 0：保持原样
    /// </summary>
    private List<CraftStep> FilterStepsByInventory(List<CraftStep> steps)
    {
        return steps
            .Select(s =>
            {
                int owned = InventoryHelper.GetItemCount(s.ItemId, false);
                if (owned <= 0) return (Step: s, Skip: false);

                int yield = s.AmountResult > 0 ? s.AmountResult : 1;
                int totalProduced = s.Quantity * yield;
                if (owned >= totalProduced)
                {
                    _log.Information($"跳过制作 {s.ItemName}（已有 {owned}，需要 {totalProduced}）");
                    return (Step: s, Skip: true);
                }

                int stillNeeded = totalProduced - owned;
                s.Quantity = Math.Max(1, (int)Math.Ceiling((double)stillNeeded / yield));
                _log.Information($"调整制作 {s.ItemName}：需要 {stillNeeded} 件，制作 {s.Quantity} 次 (yield={yield})");
                return (Step: s, Skip: false);
            })
            .Where(x => !x.Skip)
            .Select(x => x.Step)
            .ToList();
    }
}
