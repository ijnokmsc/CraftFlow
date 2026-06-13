using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using CraftFlow.Config;
using CraftFlow.Data.Models;

namespace CraftFlow.Services;

/// <summary>
/// 制作进度管理器。单一数据源，每次推送后持久化。
/// </summary>
public sealed class CraftProgressManager
{
    private readonly PluginConfig _config;
    private readonly IPluginLog _log;

    public CraftProgressManager(PluginConfig config, IPluginLog log)
    {
        _config = config;
        _log = log;
    }

    public CraftProgress? Progress => _config.CraftProgress;
    public bool HasIncompleteProgress => _config.CraftProgress is { IsActive: true };

    /// <summary>用 CraftStep 列表创建新进度。</summary>
    public void Start(List<CraftStep> steps)
    {
        // TotalSteps 改为"总物品数"（各步骤 Quantity 之和），而非步骤数。
        // 这样"做 3 个同样地东西"合并成 1 步(Quantity=3)时，
        // TotalSteps=3，进度条 0/3 → 3/3，符合用户直觉。
        int totalItems = steps.Sum(s => s.Quantity);
        _config.CraftProgress = new CraftProgress
        {
            Steps = steps.Select((s, i) => new CraftProgressStep
            {
                Index = i,
                RecipeId = s.RecipeId,
                ItemId = s.ItemId,
                ItemName = s.ItemName,
                Quantity = s.Quantity,
            }).ToList(),
            TotalSteps = totalItems,
            CurrentStepIndex = 0,
            CompletedSteps = 0,
            CreatedAt = DateTime.Now,
        };
        Save();
        _log.Information($"制作进度已创建: {steps.Count} 个步骤, 共 {totalItems} 个物品");
    }

    /// <summary>标记当前步骤开始推送。</summary>
    public void MarkStarted()
    {
        var p = _config.CraftProgress;
        if (p is null || !p.IsActive) return;
        if (p.CurrentStepIndex < p.Steps.Count)
            p.CurrentRecipeId = p.Steps[p.CurrentStepIndex].RecipeId;
        p.LastUpdatedAt = DateTime.Now;
        Save();
    }

    /// <summary>
    /// 完成 1 个物品。
    /// 当前步骤全部完成后自动推进到下一步。
    /// 与 TotalSteps（总物品数）对齐，确保进度条逐步更新。
    /// </summary>
    public void AdvanceOneItem()
    {
        var p = _config.CraftProgress;
        if (p is null || p.IsComplete) return;

        if (p.CurrentStepIndex < p.Steps.Count)
        {
            var step = p.Steps[p.CurrentStepIndex];
            step.CompletedQuantity++;
            p.CompletedSteps++;

            if (step.IsDone)
            {
                p.CurrentStepIndex++;
                p.CurrentRecipeId = 0;
                if (p.CurrentStepIndex >= p.Steps.Count)
                {
                    p.IsComplete = true;
                    _log.Information($"制作已全部完成: {p.CompletedSteps}/{p.TotalSteps}");
                }
            }
        }
        p.LastUpdatedAt = DateTime.Now;
        Save();
    }

    /// <summary>当前步骤完成，推到下一步（整步推进，兼容旧逻辑）。</summary>
    public void Advance()
    {
        var p = _config.CraftProgress;
        if (p is null || !p.IsActive) return;

        // 标记刚刚完成的步骤（CurrentStepIndex 指向刚完成的步骤）
        // 按物品数累计 CompletedSteps，与 TotalSteps（总物品数）对齐
        if (p.CurrentStepIndex < p.Steps.Count)
        {
            var finished = p.Steps[p.CurrentStepIndex];
            finished.CompletedQuantity = finished.Quantity;
            p.CompletedSteps += finished.Quantity;
        }
        p.CurrentStepIndex++;
        p.CurrentRecipeId = 0;
        p.LastUpdatedAt = DateTime.Now;

        if (p.CurrentStepIndex >= p.Steps.Count)
        {
            p.IsComplete = true;
            _log.Information("制作已全部完成");
        }
        Save();
    }

    /// <summary>获取当前步骤。</summary>
    public CraftProgressStep? GetCurrentStep()
    {
        var p = _config.CraftProgress;
        if (p is null || !p.IsActive) return null;
        return p.CurrentStepIndex < p.Steps.Count ? p.Steps[p.CurrentStepIndex] : null;
    }

    /// <summary>停止（不改变步骤索引，可恢复）。</summary>
    public void Stop()
    {
        var p = _config.CraftProgress;
        if (p is null) return;
        p.RequestedStop = true;
        _log.Information($"制作暂停请求: {p.CompletedSteps}/{p.TotalSteps}");
    }

    /// <summary>完成停止（Artisan 空闲后调用）。</summary>
    public void ConfirmStopped()
    {
        var p = _config.CraftProgress;
        if (p is null) return;
        p.RequestedStop = false;
        p.CurrentRecipeId = 0;
        Save();
        _log.Information($"制作已暂停: {p.CompletedSteps}/{p.TotalSteps}");
    }

    /// <summary>标记完成。</summary>
    public void Complete()
    {
        if (_config.CraftProgress is null) return;
        _config.CraftProgress.IsComplete = true;
        _config.CraftProgress.RequestedStop = false;
        Save();
    }

    /// <summary>清除进度。</summary>
    public void Clear()
    {
        _config.CraftProgress = null;
        Save();
    }

    public float Percent => _config.CraftProgress is { TotalSteps: > 0 } p ? (float)p.CompletedSteps / p.TotalSteps : 0;

    /// <summary>强制保存进度到配置文件。</summary>
    public void ForceSave() => Save();

    public string Summary
    {
        get
        {
            var p = _config.CraftProgress;
            if (p is null) return "";
            var s = GetCurrentStep();
            var name = s?.ItemName ?? "无";
            return $"{name} ({p.CompletedSteps}/{p.TotalSteps})";
        }
    }

    private void Save() => _config.Save();
}
