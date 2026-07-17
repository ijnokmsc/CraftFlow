using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using CraftFlow.Data.Models;
using CraftFlow.IPC;
using CraftFlow.Services;

namespace CraftFlow.UI;

/// <summary>
/// 制作进度弹窗。制作进行时显示，替代 MainWindow 以避免遮挡游戏画面。
/// 同时负责 Artisan 步骤推送的轮询逻辑。
///
/// 核心设计：每次只推送 1 个物品到 Artisan（CraftItem(recipeId, 1)），
/// 每完成一个物品调用 AdvanceOneItem() 更新进度条。
/// 这样 Artisan 每做完一个物品就返回 IsBusy=false，进度条能逐步更新。
///
/// 轮询状态机：
///   Idle（未开始）
///   → WaitBusy（已推送 1 个物品，等待 Artisan 变忙碌）
///   → WaitIdle（Artisan 忙碌中，等待完成当前物品）
///   → （返回 WaitBusy 推送下一物品，或 Done/Stopped 结束）
/// </summary>
public sealed class CraftProgressWindow : Window
{
    private readonly CraftProgressManager _progressManager;
    private readonly ArtisanIpcClient _artisanIpc;
    private readonly IPluginLog _log;

    // 轮询状态
    private List<CraftStep>? _pendingCraftSteps;
    private int _currentStepIdx;         // 当前正在处理的步骤索引（in _pendingCraftSteps）
    private int _currentItemIdx;         // 当前步骤中已完成的物品数
    private int _busyWaitFrames;         // 推送后等待 Artisan 变忙的帧数
    private int _artisanBusyFrames;      // Artisan 持续忙碌的帧数
    private bool _hasActiveCraft;        // Artisan 已确认接受任务并正在制作
    private PollState _pollState;

    private enum PollState
    {
        Idle,       // 无任务
        WaitBusy,   // 已推送，等待 Artisan 进入 busy 状态
        WaitIdle,   // Artisan 忙碌中，等待完成当前物品
        Stopping,   // 用户请求停止，等待 Artisan 空闲
    }

    // 推送后最多等待 N 帧确认 Artisan 变 busy（约 5 秒 @ 60fps）
    private const int WaitBusyMaxFrames = 300;
    // Artisan busy 超时帧数（约 10 分钟 @ 60fps，防止异常卡死）
    private const int BusyTimeoutFrames = 36000;

    private static readonly Vector4 ColorGreen  = new(0.2f, 0.9f, 0.2f, 1f);
    private static readonly Vector4 ColorOrange = new(0.9f, 0.6f, 0.2f, 1f);
    private static readonly Vector4 ColorRed    = new(1.0f, 0.3f, 0.3f, 1f);
    private static readonly Vector4 ColorGray   = new(0.6f, 0.6f, 0.6f, 1f);

    /// <summary>当制作流程结束（完成/停止）时触发，用于恢复主窗口。</summary>
    public event Action? CraftingEnded;

    public CraftProgressWindow(
        CraftProgressManager progressManager,
        ArtisanIpcClient artisanIpc,
        IPluginLog log)
        : base("CraftFlow 制作进度###CraftFlowProgressWindow",
               ImGuiWindowFlags.NoCollapse)
    {
        _progressManager = progressManager;
        _artisanIpc = artisanIpc;
        _log = log;

        Size = new Vector2(480, 440);
        SizeCondition = ImGuiCond.FirstUseEver;
        IsOpen = false;
    }

    /// <summary>
    /// 开始制作流程。由 MaterialListWidget.OnStartCrafting 回调触发。
    /// 此时 CraftProgressManager.Start() 已被调用，进度已初始化。
    /// 每次只推送 1 个物品，确保进度条能逐步更新。
    /// </summary>
    public void StartCrafting(List<CraftStep> steps)
    {
        _pendingCraftSteps = steps;
        _currentStepIdx = 0;
        _currentItemIdx = 0;
        _busyWaitFrames = 0;
        _artisanBusyFrames = 0;
        _hasActiveCraft = false;

        // 推送第一个步骤的第一个物品
        var first = steps[0];
        _artisanIpc.CraftItem((ushort)first.RecipeId, 1);
        _progressManager.MarkStarted();
        _pollState = PollState.WaitBusy;

        IsOpen = true;
        _log.Information($"[CraftProgress] 开始制作: {first.ItemName} (1/{first.Quantity}, 共 {steps.Count} 步)");
    }

    /// <summary>
    /// 恢复中断的制作流程。从 CraftProgressManager 中重建轮询状态。
    /// 恢复当前步骤的已完成物品数，从下一个未完成的物品继续。
    /// </summary>
    public void ResumeCrafting()
    {
        var p = _progressManager.Progress;
        if (p is null || p.IsComplete) return;

        // 从进度数据重建剩余步骤列表
        var remainingSteps = new List<CraftStep>();
        for (int i = p.CurrentStepIndex; i < p.Steps.Count; i++)
        {
            var s = p.Steps[i];
            remainingSteps.Add(new CraftStep
            {
                RecipeId = s.RecipeId,
                ItemId = s.ItemId,
                ItemName = s.ItemName,
                Quantity = s.Quantity,
                Order = s.Index,
                Status = StepStatus.Pending,
            });
        }

        if (remainingSteps.Count == 0) return;

        _pendingCraftSteps = remainingSteps;
        _busyWaitFrames = 0;
        _artisanBusyFrames = 0;
        _hasActiveCraft = false;

        // 恢复当前步骤的已完成物品数
        var currentStep = p.Steps[p.CurrentStepIndex];
        int completed = currentStep.CompletedQuantity;

        if (completed >= currentStep.Quantity)
        {
            // 当前步骤已完成，跳到下一步
            _currentStepIdx = 1;
            _currentItemIdx = 0;
        }
        else
        {
            _currentStepIdx = 0;
            _currentItemIdx = completed;
        }

        if (_currentStepIdx >= _pendingCraftSteps.Count)
        {
            // 没有剩余步骤
            _log.Warning("[CraftProgress] 没有剩余步骤可恢复");
            return;
        }

        // 推送下一个待制作物品
        var next = _pendingCraftSteps[_currentStepIdx];
        int nextItem = _currentItemIdx + 1;
        _artisanIpc.CraftItem((ushort)next.RecipeId, 1);
        _progressManager.MarkStarted();
        _pollState = PollState.WaitBusy;

        IsOpen = true;
        _log.Information($"[CraftProgress] 恢复制作: {next.ItemName} ({nextItem}/{next.Quantity}, 剩余 {remainingSteps.Count} 步)");
    }

    public override void Draw()
    {
        // 先执行轮询（状态机驱动）
        PollArtisanSteps();

        var p = _progressManager.Progress;
        if (p is null)
        {
            CloseAndNotify();
            return;
        }

        // ── 标题区 ──────────────────────────────────
        ImGui.TextColored(ColorGreen, "⏳ 制作进度");
        ImGui.Separator();

        // ── 当前步骤（物品级进度） ────────────────────
        var step = _progressManager.GetCurrentStep();
        if (step is not null)
        {
            if (step.Quantity > 1)
            {
                int current = step.CompletedQuantity + (_pollState == PollState.WaitIdle ? 1 : 0);
                ImGui.Text($"正在制作: {step.ItemName}  ({Math.Min(current, step.Quantity)}/{step.Quantity})");
            }
            else
            {
                ImGui.Text($"正在制作: {step.ItemName}");
            }
        }
        else if (p.IsComplete)
        {
            ImGui.TextColored(ColorGreen, "✅ 全部制作完成！");
        }

        // ── 总进度条 ────────────────────────────────
        var pct = _progressManager.Percent;
        ImGui.Text($"总进度: {p.CompletedSteps} / {p.TotalSteps} (次)");
        ImGui.ProgressBar(pct, new Vector2(ImGui.GetContentRegionAvail().X, 22),
                          $"{p.CompletedSteps}/{p.TotalSteps}");

        // ── 制作清单进度表格（半成品 + 成品） ─────────
        // 仅当步骤数 >= 2 时用表格展示完整流程；单步骤时顶部「正在制作」+ 总进度条已足够，
        // 避免孤零零一张 1 行表格 + 汇总行（冗余）。
        float reserveBottom = 64f; // 状态文字 + 停止按钮预留高度
        float listH = ImGui.GetContentRegionAvail().Y - reserveBottom;
        if (p.Steps.Count >= 2 && listH > 50)
        {
            ImGui.BeginChild("StepListChild", new Vector2(-1, listH), true);
            DrawStepProgressTable();
            ImGui.EndChild();
        }

        // ── 状态文字 ─────────────────────────────────
        if (_pollState == PollState.Stopping || p.RequestedStop)
        {
            ImGui.TextColored(ColorOrange, "正在停止… 等待 Artisan 完成当前物品");
        }
        else if (_pollState == PollState.WaitBusy)
        {
            ImGui.TextColored(ColorOrange, $"等待 Artisan 接受任务… ({_busyWaitFrames})");
        }

        // ── 停止按钮 ─────────────────────────────────
        bool isStopping = _pollState == PollState.Stopping || p.RequestedStop;
        if (isStopping) ImGui.BeginDisabled();

        if (ImGui.Button("停止制作###StopCraft"))
        {
            _pollState = PollState.Stopping;
            _progressManager.Stop();
            // 真正通知 Artisan 停止
            _artisanIpc.SetStopRequest(true);
            _artisanIpc.SetEnduranceStatus(false);
            _log.Information("[CraftProgress] 用户请求停止制作（已通知 Artisan）");
        }

        if (isStopping) ImGui.EndDisabled();

        // ── 自动关闭判断 ──────────────────────────────
        // 1. 正常完成：IsComplete=true
        // 2. 停止完成：状态机回到 Idle 且无待推送步骤
        if (p.IsComplete || (_pollState == PollState.Idle && _pendingCraftSteps is null))
        {
            CloseAndNotify();
        }
    }

    /// <summary>
    /// 绘制制作清单进度表格：列出本次制作的所有步骤（半成品 + 成品），
    /// 显示每步的制作次数进度与状态（进行中/完成/等待），当前步高亮并自动滚动到可见。
    /// 底部追加总进度汇总行。
    /// </summary>
    private void DrawStepProgressTable()
    {
        var p = _progressManager.Progress;
        if (p is null || p.Steps.Count == 0) return;

        // 表格标志：可滚动 + 行背景 + 内水平边框 + 列宽可调 + 自动调整列宽以适应内容
        var flags = ImGuiTableFlags.RowBg
                  | ImGuiTableFlags.BordersInnerH
                  | ImGuiTableFlags.Resizable
                  | ImGuiTableFlags.ScrollY          // 允许垂直滚动（长列表）
                  | ImGuiTableFlags.SizingFixedFit;  // 列宽固定，溢出滚动

        if (ImGui.BeginTable("CraftStepTbl", 3, flags))
        {
            // 固定列宽，物品名自适应剩余空间
            ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableSetupColumn("进度(次)", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("状态", ImGuiTableColumnFlags.WidthFixed, 64);
            ImGui.TableHeadersRow();

            int scrollToIdx = p.IsComplete ? -1 : p.CurrentStepIndex;

            for (int i = 0; i < p.Steps.Count; i++)
            {
                var s = p.Steps[i];
                bool isCurrent = i == p.CurrentStepIndex && !p.IsComplete;
                bool isDone = s.IsDone;

                ImGui.TableNextRow();

                // 当前行背景色：进行中=淡橙，完成=淡绿，等待=默认
                if (isCurrent)
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.7f, 0.3f, 0.18f)));
                else if (isDone)
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.8f, 0.2f, 0.12f)));

                // ── 物品名 ──────────────────────────────
                ImGui.TableNextColumn();
                string prefix = isCurrent ? "▶ " : (isDone ? "✓ " : "  ");
                ImGui.Text($"{prefix}{s.ItemName}");

                // ── 进度 ────────────────────────────────
                ImGui.TableNextColumn();

                // 当前正在制作的步骤，显示含"正在做的那一个"的预估进度
                int displayCompleted = s.CompletedQuantity;
                if (isCurrent && _pollState == PollState.WaitIdle)
                    displayCompleted = Math.Min(s.CompletedQuantity + 1, s.Quantity);

                if (isDone)
                    ImGui.TextColored(ColorGreen, $"{displayCompleted}/{s.Quantity}");
                else if (isCurrent)
                    ImGui.TextColored(ColorOrange, $"{displayCompleted}/{s.Quantity}");
                else
                    ImGui.TextColored(ColorGray, $"0/{s.Quantity}");

                // ── 状态 ────────────────────────────────
                ImGui.TableNextColumn();
                if (isDone)
                    ImGui.TextColored(ColorGreen, "已完成");
                else if (isCurrent)
                    ImGui.TextColored(ColorOrange, "制作中");
                else
                    ImGui.TextColored(ColorGray, "未制作");

                // 自动滚动：将当前步骤保持在可视区域中央附近
                if (i == scrollToIdx)
                    ImGui.SetScrollHereY(0.35f); // 滚到可视区约 35% 处（偏上，能看到前几步）
            }

            // ── 底部汇总行 ─────────────────────────────
            ImGui.TableNextRow();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.2f, 0.6f)));

            ImGui.TableNextColumn();
            ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.5f, 1f), "📋 总进度");

            ImGui.TableNextColumn();
            float totalPct = p.TotalSteps > 0 ? (float)p.CompletedSteps / p.TotalSteps : 0;
            var barColor = p.IsComplete ? ColorGreen : (totalPct > 0.5f ? ColorOrange : ColorRed);
            ImGui.ProgressBar(totalPct, new Vector2(-1, 0), $"{p.CompletedSteps}/{p.TotalSteps}");

            ImGui.TableNextColumn();
            if (p.IsComplete)
                ImGui.TextColored(ColorGreen, "已完成");
            else if (_pollState == PollState.Stopping || p.RequestedStop)
                ImGui.TextColored(ColorOrange, "停止中");
            else
                ImGui.TextColored(ColorOrange, "制作中");

            ImGui.EndTable();
        }
    }

    private void CloseAndNotify()
    {
        // 防止重复触发
        if (!IsOpen) return;
        IsOpen = false;
        _pendingCraftSteps = null;
        _pollState = PollState.Idle;
        _hasActiveCraft = false;
        // 清理 Artisan 状态，避免影响下次制作
        _artisanIpc.SetStopRequest(false);
        _artisanIpc.SetEnduranceStatus(false);
        // 强制保存进度到配置（确保崩溃恢复能读取最新状态）
        _progressManager.ForceSave();
        CraftingEnded?.Invoke();
        _log.Information("[CraftProgress] 进度窗口关闭，已通知主窗口恢复");
    }

    /// <summary>
    /// Artisan 步骤推送状态机。每帧调用一次。
    /// 核心逻辑：每次只推送 1 个物品，每完成 1 个调用 AdvanceOneItem()。
    ///
    /// 状态转换：
    ///   WaitBusy  → IsBusy=true  → WaitIdle（Artisan 确认接受任务）
    ///   WaitBusy  → 超时(300帧) → Stopping
    ///   WaitIdle  → IsBusy=false → AdvanceOneItem + 推送下一物品(或下一步) → WaitBusy
    ///   WaitIdle  → IsBusy=false + 全部完成 → Complete → Idle
    ///   WaitIdle  → 超时         → Stopping
    ///   Stopping  → IsBusy=false → [AdvanceOneItem if 有物品在制作] → ConfirmStopped → Idle
    /// </summary>
    private void PollArtisanSteps()
    {
        if (_pendingCraftSteps is null || _pollState == PollState.Idle) return;

        var isBusy = _artisanIpc.IsBusy();

        switch (_pollState)
        {
            case PollState.WaitBusy:
                if (isBusy)
                {
                    // Artisan 已接受任务并开始制作
                    _busyWaitFrames = 0;
                    _artisanBusyFrames = 0;
                    _hasActiveCraft = true;
                    _pollState = PollState.WaitIdle;
                    _log.Debug("[CraftProgress] Artisan 已进入忙碌状态");
                }
                else
                {
                    _busyWaitFrames++;
                    if (_busyWaitFrames > WaitBusyMaxFrames)
                    {
                        _pollState = PollState.Stopping;
                        _progressManager.Stop();
                        _log.Warning($"[CraftProgress] Artisan 超过 {WaitBusyMaxFrames} 帧未响应，自动停止");
                    }
                }
                break;

            case PollState.WaitIdle:
                if (!isBusy)
                {
                    // Artisan 完成当前物品，记录进度
                    _artisanBusyFrames = 0;
                    _progressManager.AdvanceOneItem();
                    _currentItemIdx++;

                    // 检查当前步骤是否全部完成
                    var step = _pendingCraftSteps[_currentStepIdx];
                    bool stepDone = _currentItemIdx >= step.Quantity;

                    if (_progressManager.Progress?.IsComplete == true)
                    {
                        // 全部完成
                        _pendingCraftSteps = null;
                        _pollState = PollState.Idle;
                        _log.Information("[CraftProgress] 全部步骤已完成");
                    }
                    else if (stepDone)
                    {
                        // 当前步骤完成，推进到下一步
                        _currentStepIdx++;
                        _currentItemIdx = 0;
                        _hasActiveCraft = false;

                        if (_currentStepIdx >= _pendingCraftSteps.Count)
                        {
                            _pendingCraftSteps = null;
                            _pollState = PollState.Idle;
                            if (_progressManager.Progress?.IsComplete != true)
                                _progressManager.Complete();
                            _log.Information("[CraftProgress] 全部步骤已完成");
                        }
                        else
                        {
                            // 推送下一步的第一个物品
                            var next = _pendingCraftSteps[_currentStepIdx];
                            _artisanIpc.CraftItem((ushort)next.RecipeId, 1);
                            _progressManager.MarkStarted();
                            _busyWaitFrames = 0;
                            _pollState = PollState.WaitBusy;
                            _log.Information($"[CraftProgress] 推送: {next.ItemName} (1/{next.Quantity})");
                        }
                    }
                    else
                    {
                        // 同一步骤还有剩余物品，继续推送
                        _hasActiveCraft = false;
                        _artisanIpc.CraftItem((ushort)step.RecipeId, 1);
                        _busyWaitFrames = 0;
                        _pollState = PollState.WaitBusy;
                        _log.Debug($"[CraftProgress] 继续: {step.ItemName} ({_currentItemIdx}/{step.Quantity})");
                    }
                }
                else
                {
                    _artisanBusyFrames++;
                    if (_artisanBusyFrames > BusyTimeoutFrames)
                    {
                        _pollState = PollState.Stopping;
                        _progressManager.Stop();
                        _log.Warning($"[CraftProgress] Artisan 忙碌超时（>{BusyTimeoutFrames} 帧），自动停止");
                    }
                }
                break;

            case PollState.Stopping:
                if (!isBusy)
                {
                    // Artisan 已空闲
                    if (_hasActiveCraft)
                    {
                        // 有物品正在制作中，记录最后完成的物品
                        _progressManager.AdvanceOneItem();
                        _currentItemIdx++;
                        _hasActiveCraft = false;
                    }

                    // 确认停止，重置 Artisan 侧的停止请求
                    _artisanIpc.SetStopRequest(false);
                    _progressManager.ConfirmStopped();
                    _pendingCraftSteps = null;
                    _pollState = PollState.Idle;
                    _log.Information($"[CraftProgress] 制作已停止: {_progressManager.Progress?.CompletedSteps}/{_progressManager.Progress?.TotalSteps}");
                    // 下一帧 Draw() 里的关闭判断会触发 CloseAndNotify
                }
                break;
        }
    }
}
