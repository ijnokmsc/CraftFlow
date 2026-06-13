using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;

namespace CraftFlow.IPC;

/// <summary>
/// GatherBuddyReborn IPC 客户端，封装 GBR 暴露的 IPC Channel 调用。
/// 注意：GBR 不暴露 AddGatherable IPC，仅支持 Identify + AutoGather 控制。
/// 推送采集列表的降级方案为生成文本清单 + 可选启用 AutoGather。
/// 支持国际服 GatherBuddy 和国服 Owl.Buddy 两套 IPC 通道名。
/// </summary>
public sealed class GbrIpcClient : IIpcClient
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _log;

    /// <summary>
    /// 当前使用的 IPC 通道名前缀（"GatherBuddy." 或 "Owl.Buddy."）。
    /// </summary>
    private string _channelPrefix = "GatherBuddy.";

    private ICallGateSubscriber<int>? _versionSubscriber;
    private ICallGateSubscriber<string, uint>? _identifySubscriber;
    private ICallGateSubscriber<bool>? _isAutoGatherEnabledSubscriber;
    private ICallGateSubscriber<string>? _getAutoGatherStatusTextSubscriber;
    private ICallGateSubscriber<bool, object?>? _setAutoGatherEnabledSubscriber;
    private ICallGateSubscriber<bool>? _isAutoGatherWaitingSubscriber;
    private ICallGateSubscriber<Action>? _autoGatherWaitingEventSubscriber;
    private ICallGateSubscriber<Action<bool>>? _autoGatherEnabledChangedEventSubscriber;

    /// <summary>
    /// GBR 是否已安装且 IPC 可用。
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>
    /// 初始化 GbrIpcClient 实例。
    /// </summary>
    /// <param name="pluginInterface">Dalamud 插件接口。</param>
    /// <param name="log">插件日志。</param>
    public GbrIpcClient(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        _pluginInterface = pluginInterface;
        _log = log;
        IsAvailable = false;
    }

    /// <summary>
    /// 订阅 GBR IPC Channel。若 GBR 未安装，IsAvailable 保持 false。
    /// 依次尝试 GatherBuddyReborn（Reborn 分支）、GatherBuddy（原版）和
    /// Owl.Buddy（国服版）三套通道名。
    /// </summary>
    public void Subscribe()
    {
        // GatherBuddyReborn (FFXIV-CombatReborn 分支，当前主流版本)
        if (TrySubscribeWithPrefix("GatherBuddyReborn."))
        {
            return;
        }

        // 原版 GatherBuddy (Ottermandias)
        if (TrySubscribeWithPrefix("GatherBuddy."))
        {
            return;
        }

        // 国服 Owl.Buddy 通道
        if (TrySubscribeWithPrefix("Owl.Buddy."))
        {
            return;
        }

        IsAvailable = false;
        _log.Debug("GBR IPC 订阅失败，GBR/Owl.Buddy 可能未安装");
    }

    /// <summary>
    /// 使用指定通道前缀尝试订阅 GBR IPC。
    /// </summary>
    /// <param name="prefix">IPC 通道名前缀。</param>
    /// <returns>是否订阅成功。</returns>
    private bool TrySubscribeWithPrefix(string prefix)
    {
        try
        {
            _versionSubscriber = _pluginInterface.GetIpcSubscriber<int>($"{prefix}Version");
            _identifySubscriber = _pluginInterface.GetIpcSubscriber<string, uint>($"{prefix}Identify");
            _isAutoGatherEnabledSubscriber = _pluginInterface.GetIpcSubscriber<bool>($"{prefix}IsAutoGatherEnabled");
            _getAutoGatherStatusTextSubscriber = _pluginInterface.GetIpcSubscriber<string>($"{prefix}GetAutoGatherStatusText");
            _setAutoGatherEnabledSubscriber = _pluginInterface.GetIpcSubscriber<bool, object?>($"{prefix}SetAutoGatherEnabled");
            _isAutoGatherWaitingSubscriber = _pluginInterface.GetIpcSubscriber<bool>($"{prefix}IsAutoGatherWaiting");
            _autoGatherWaitingEventSubscriber = _pluginInterface.GetIpcSubscriber<Action>($"{prefix}AutoGatherWaiting");
            _autoGatherEnabledChangedEventSubscriber = _pluginInterface.GetIpcSubscriber<Action<bool>>($"{prefix}AutoGatherEnabledChanged");

            // 尝试调用 Version 验证 GBR 是否真正可用
            _versionSubscriber.InvokeFunc();
            _channelPrefix = prefix;
            IsAvailable = true;
            _log.Information($"GBR IPC 订阅成功（通道前缀: {prefix}），GBR 已安装");
            return true;
        }
        catch (IpcError)
        {
            IsAvailable = false;
            _log.Debug($"GBR IPC 订阅失败（通道前缀: {prefix}）");
            return false;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            _log.Debug($"GBR IPC 订阅异常（通道前缀: {prefix}）: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 用户主动触发重新检测 GBR IPC 可用性。
    /// 在 UI 按钮点击时调用，绕过延迟订阅的次数限制。
    /// </summary>
    /// <returns>重新检测后是否可用。</returns>
    public bool TryResubscribe()
    {
        IsAvailable = false;
        Subscribe();
        _log.Information($"GBR 重新检测结果: IsAvailable={IsAvailable}（通道前缀: {_channelPrefix}）");
        return IsAvailable;
    }

    /// <summary>
    /// 延迟订阅 GBR IPC Channel，使用 Framework.Update 帧计数退避重试。
    /// 解决插件构造函数中 GBR 可能尚未加载导致的时序问题。
    /// 退避策略：1/2/4/8...帧，最多 30 次重试。
    /// </summary>
    /// <param name="framework">Dalamud Framework 服务。</param>
    /// <param name="onSuccess">订阅成功后的回调（可选）。</param>
    public void DelayedSubscribe(IFramework framework, Action? onSuccess = null)
    {
        int retryCount = 0;
        const int maxRetries = 10;
        int nextRetryFrame = 1; // 下次重试的帧计数
        int currentFrame = 0;

        void OnFrameworkUpdate(IFramework fw)
        {
            currentFrame++;
            if (IsAvailable || retryCount >= maxRetries)
            {
                // 已成功或超过最大重试次数，注销事件
                fw.Update -= OnFrameworkUpdate;

                if (!IsAvailable)
                {
                    _log.Information($"GBR IPC 延迟订阅结束：已重试 {retryCount} 次，GBR 未安装");
                }

                return;
            }

            // 帧计数退避：仅到达指定帧数时才重试
            if (currentFrame < nextRetryFrame)
            {
                return;
            }

            retryCount++;
            _log.Debug($"GBR IPC 延迟订阅第 {retryCount} 次重试 (帧 {currentFrame})");

            try
            {
                Subscribe();
                if (IsAvailable)
                {
                    fw.Update -= OnFrameworkUpdate;
                    _log.Information("GBR IPC 延迟订阅成功");
                    onSuccess?.Invoke();
                }
            }
            catch
            {
                // 订阅异常，继续重试
            }

            // 指数退避：下次重试帧数翻倍
            nextRetryFrame = currentFrame + (1 << Math.Min(retryCount, 8));
        }

        framework.Update += OnFrameworkUpdate;
        _log.Information("GBR IPC 已注册延迟订阅，等待 GBR 加载");
    }

    /// <summary>
    /// 使用 GBR Identify 功能按名称查询可采集物品的 ItemId。
    /// </summary>
    /// <param name="text">物品名称（GBR 识别格式）。</param>
    /// <returns>匹配的 ItemId，0 表示未找到。</returns>
    public uint Identify(string text)
    {
        if (!IsAvailable || _identifySubscriber is null)
        {
            return 0;
        }

        try
        {
            return _identifySubscriber.InvokeFunc(text);
        }
        catch (IpcError e)
        {
            _log.Warning($"GBR.Identify IPC 调用失败: {e.Message}");
            IsAvailable = false;
            return 0;
        }
    }

    /// <summary>
    /// 获取 GBR 自动采集状态文本。
    /// </summary>
    /// <returns>状态文本字符串。</returns>
    public string GetAutoGatherStatus()
    {
        if (!IsAvailable || _getAutoGatherStatusTextSubscriber is null)
        {
            return "不可用";
        }

        try
        {
            return _getAutoGatherStatusTextSubscriber.InvokeFunc();
        }
        catch (IpcError e)
        {
            _log.Warning($"GBR.GetAutoGatherStatusText IPC 调用失败: {e.Message}");
            IsAvailable = false;
            return "错误";
        }
    }

    /// <summary>
    /// 启用或禁用 GBR 自动采集。
    /// </summary>
    /// <param name="enabled">true 启用，false 禁用。</param>
    public void SetAutoGatherEnabled(bool enabled)
    {
        if (!IsAvailable || _setAutoGatherEnabledSubscriber is null)
        {
            return;
        }

        try
        {
            _setAutoGatherEnabledSubscriber.InvokeAction(enabled);
            _log.Information($"GBR 自动采集已{(enabled ? "启用" : "禁用")}");
        }
        catch (IpcError e)
        {
            _log.Warning($"GBR.SetAutoGatherEnabled IPC 调用失败: {e.Message}");
            IsAvailable = false;
        }
    }

    /// <summary>
    /// 查询 GBR 自动采集是否已启用。
    /// </summary>
    /// <returns>自动采集启用状态。</returns>
    public bool IsAutoGatherEnabled()
    {
        if (!IsAvailable || _isAutoGatherEnabledSubscriber is null)
        {
            return false;
        }

        try
        {
            return _isAutoGatherEnabledSubscriber.InvokeFunc();
        }
        catch (IpcError e)
        {
            _log.Warning($"GBR.IsAutoGatherEnabled IPC 调用失败: {e.Message}");
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// 查询 GBR 自动采集是否在等待中。
    /// </summary>
    /// <returns>是否在等待。</returns>
    public bool IsAutoGatherWaiting()
    {
        if (!IsAvailable || _isAutoGatherWaitingSubscriber is null)
        {
            return false;
        }

        try
        {
            return _isAutoGatherWaitingSubscriber.InvokeFunc();
        }
        catch (IpcError e)
        {
            _log.Warning($"GBR.IsAutoGatherWaiting IPC 调用失败: {e.Message}");
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// 获取 GBR 版本号。
    /// </summary>
    /// <returns>GBR 版本号，-1 表示不可用。</returns>
    public int GetVersion()
    {
        if (!IsAvailable || _versionSubscriber is null)
        {
            return -1;
        }

        try
        {
            return _versionSubscriber.InvokeFunc();
        }
        catch (IpcError e)
        {
            _log.Warning($"GBR.Version IPC 调用失败: {e.Message}");
            IsAvailable = false;
            return -1;
        }
    }

    /// <summary>
    /// 释放 IPC 资源。
    /// </summary>
    public void Dispose()
    {
        IsAvailable = false;
        _versionSubscriber = null;
        _identifySubscriber = null;
        _isAutoGatherEnabledSubscriber = null;
        _getAutoGatherStatusTextSubscriber = null;
        _setAutoGatherEnabledSubscriber = null;
        _isAutoGatherWaitingSubscriber = null;
        _autoGatherWaitingEventSubscriber = null;
        _autoGatherEnabledChangedEventSubscriber = null;
        _log.Debug("GBR IPC 资源已释放");
    }
}
