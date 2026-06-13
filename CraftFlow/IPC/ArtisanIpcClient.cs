using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;

namespace CraftFlow.IPC;

/// <summary>
/// Artisan IPC 客户端，封装 Artisan 暴露的 IPC Channel 调用。
/// 核心推送方式：Artisan.CraftItem(ushort recipeId, int amount)，使用 Endurance 模式制作。
/// 注意：Artisan 不暴露创建/编辑制作列表的 IPC。
/// </summary>
public sealed class ArtisanIpcClient : IIpcClient
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _log;

    private ICallGateSubscriber<ushort, int, object?>? _craftItemSubscriber;
    private ICallGateSubscriber<bool>? _isBusySubscriber;
    private ICallGateSubscriber<bool>? _isListRunningSubscriber;
    private ICallGateSubscriber<bool>? _isListPausedSubscriber;
    private ICallGateSubscriber<bool, object?>? _setListPauseSubscriber;
    private ICallGateSubscriber<bool>? _getStopRequestSubscriber;
    private ICallGateSubscriber<bool, object?>? _setStopRequestSubscriber;
    private ICallGateSubscriber<bool>? _getEnduranceStatusSubscriber;
    private ICallGateSubscriber<bool, object?>? _setEnduranceStatusSubscriber;
    private ICallGateSubscriber<Dictionary<int, string>>? _getListsSubscriber;
    private ICallGateSubscriber<int, object?>? _startListByIdSubscriber;

    /// <summary>
    /// Artisan 是否已安装且 IPC 可用。
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>
    /// 初始化 ArtisanIpcClient 实例。
    /// </summary>
    /// <param name="pluginInterface">Dalamud 插件接口。</param>
    /// <param name="log">插件日志。</param>
    public ArtisanIpcClient(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        _pluginInterface = pluginInterface;
        _log = log;
        IsAvailable = false;
    }

    /// <summary>
    /// 订阅 Artisan IPC Channel。若 Artisan 未安装，IsAvailable 保持 false。
    /// </summary>
    public void Subscribe()
    {
        try
        {
            _craftItemSubscriber = _pluginInterface.GetIpcSubscriber<ushort, int, object?>("Artisan.CraftItem");
            _isBusySubscriber = _pluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy");
            _isListRunningSubscriber = _pluginInterface.GetIpcSubscriber<bool>("Artisan.IsListRunning");
            _isListPausedSubscriber = _pluginInterface.GetIpcSubscriber<bool>("Artisan.IsListPaused");
            _setListPauseSubscriber = _pluginInterface.GetIpcSubscriber<bool, object?>("Artisan.SetListPause");
            _getStopRequestSubscriber = _pluginInterface.GetIpcSubscriber<bool>("Artisan.GetStopRequest");
            _setStopRequestSubscriber = _pluginInterface.GetIpcSubscriber<bool, object?>("Artisan.SetStopRequest");
            _getEnduranceStatusSubscriber = _pluginInterface.GetIpcSubscriber<bool>("Artisan.GetEnduranceStatus");
            _setEnduranceStatusSubscriber = _pluginInterface.GetIpcSubscriber<bool, object?>("Artisan.SetEnduranceStatus");
            _getListsSubscriber = _pluginInterface.GetIpcSubscriber<Dictionary<int, string>>("Artisan.GetLists");
            _startListByIdSubscriber = _pluginInterface.GetIpcSubscriber<int, object?>("Artisan.StartListById");

            // 尝试调用 IsBusy 验证 Artisan 是否真正可用
            _isBusySubscriber.InvokeFunc();
            IsAvailable = true;
            _log.Information("Artisan IPC 订阅成功，Artisan 已安装");
        }
        catch (IpcError)
        {
            IsAvailable = false;
            _log.Debug("Artisan IPC 订阅失败，Artisan 可能未安装");
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            _log.Debug($"Artisan IPC 订阅异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 用户主动触发重新检测 Artisan IPC 可用性。
    /// 在 UI 按钮点击时调用，绕过延迟订阅的次数限制。
    /// </summary>
    /// <returns>重新检测后是否可用。</returns>
    public bool TryResubscribe()
    {
        IsAvailable = false;
        Subscribe();
        _log.Information($"Artisan 重新检测结果: IsAvailable={IsAvailable}");
        return IsAvailable;
    }

    /// <summary>
    /// 向 Artisan 推送一个制作任务。
    /// 使用 Endurance 模式，Artisan 会自动制作指定数量后停止。
    /// </summary>
    /// <param name="recipeId">配方 RowId（ushort）。</param>
    /// <param name="amount">制作数量。</param>
    public void CraftItem(ushort recipeId, int amount)
    {
        if (!IsAvailable || _craftItemSubscriber is null)
        {
            _log.Warning("Artisan 不可用，无法推送制作任务");
            return;
        }

        try
        {
            _craftItemSubscriber.InvokeAction(recipeId, amount);
            _log.Information($"Artisan.CraftItem: 已推送 RecipeId={recipeId}, Amount={amount}");
        }
        catch (IpcError e)
        {
            _log.Warning($"Artisan.CraftItem IPC 调用失败: {e.Message}");
            IsAvailable = false;
        }
    }

    /// <summary>
    /// 查询 Artisan 是否正在忙碌（制作中）。
    /// </summary>
    /// <returns>是否忙碌。</returns>
    public bool IsBusy()
    {
        if (!IsAvailable || _isBusySubscriber is null)
        {
            return false;
        }

        try
        {
            return _isBusySubscriber.InvokeFunc();
        }
        catch (IpcError e)
        {
            _log.Warning($"Artisan.IsBusy IPC 调用失败: {e.Message}");
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// 查询 Artisan 制作列表是否正在运行。
    /// </summary>
    /// <returns>是否运行中。</returns>
    public bool IsListRunning()
    {
        if (!IsAvailable || _isListRunningSubscriber is null)
        {
            return false;
        }

        try
        {
            return _isListRunningSubscriber.InvokeFunc();
        }
        catch (IpcError e)
        {
            _log.Warning($"Artisan.IsListRunning IPC 调用失败: {e.Message}");
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// 查询 Artisan 制作列表是否暂停。
    /// </summary>
    /// <returns>是否暂停。</returns>
    public bool IsListPaused()
    {
        if (!IsAvailable || _isListPausedSubscriber is null)
        {
            return false;
        }

        try
        {
            return _isListPausedSubscriber.InvokeFunc();
        }
        catch (IpcError e)
        {
            _log.Warning($"Artisan.IsListPaused IPC 调用失败: {e.Message}");
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// 暂停或恢复 Artisan 制作列表。
    /// </summary>
    /// <param name="pause">true 暂停，false 恢复。</param>
    public void SetListPause(bool pause)
    {
        if (!IsAvailable || _setListPauseSubscriber is null)
        {
            return;
        }

        try
        {
            _setListPauseSubscriber.InvokeAction(pause);
        }
        catch (IpcError e)
        {
            _log.Warning($"Artisan.SetListPause IPC 调用失败: {e.Message}");
            IsAvailable = false;
        }
    }

    /// <summary>
    /// 获取 Artisan 停止请求状态。
    /// </summary>
    /// <returns>是否已请求停止。</returns>
    public bool GetStopRequest()
    {
        if (!IsAvailable || _getStopRequestSubscriber is null)
        {
            return false;
        }

        try
        {
            return _getStopRequestSubscriber.InvokeFunc();
        }
        catch (IpcError e)
        {
            _log.Warning($"Artisan.GetStopRequest IPC 调用失败: {e.Message}");
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// 设置 Artisan 停止请求。
    /// </summary>
    /// <param name="stop">true 请求停止，false 取消停止。</param>
    public void SetStopRequest(bool stop)
    {
        if (!IsAvailable || _setStopRequestSubscriber is null)
        {
            return;
        }

        try
        {
            _setStopRequestSubscriber.InvokeAction(stop);
        }
        catch (IpcError e)
        {
            _log.Warning($"Artisan.SetStopRequest IPC 调用失败: {e.Message}");
            IsAvailable = false;
        }
    }

    /// <summary>
    /// 获取 Artisan Endurance 模式状态。
    /// </summary>
    /// <returns>Endurance 是否启用。</returns>
    public bool GetEnduranceStatus()
    {
        if (!IsAvailable || _getEnduranceStatusSubscriber is null)
        {
            return false;
        }

        try
        {
            return _getEnduranceStatusSubscriber.InvokeFunc();
        }
        catch (IpcError e)
        {
            _log.Warning($"Artisan.GetEnduranceStatus IPC 调用失败: {e.Message}");
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// 设置 Artisan Endurance 模式。
    /// </summary>
    /// <param name="enabled">true 启用，false 禁用。</param>
    public void SetEnduranceStatus(bool enabled)
    {
        if (!IsAvailable || _setEnduranceStatusSubscriber is null)
        {
            return;
        }

        try
        {
            _setEnduranceStatusSubscriber.InvokeAction(enabled);
        }
        catch (IpcError e)
        {
            _log.Warning($"Artisan.SetEnduranceStatus IPC 调用失败: {e.Message}");
            IsAvailable = false;
        }
    }

    /// <summary>
    /// 获取 Artisan 已有的制作列表。
    /// </summary>
    /// <returns>列表字典（ID → 名称），不可用时返回空字典。</returns>
    public Dictionary<int, string> GetLists()
    {
        if (!IsAvailable || _getListsSubscriber is null)
        {
            return new Dictionary<int, string>();
        }

        try
        {
            return _getListsSubscriber.InvokeFunc();
        }
        catch (IpcError e)
        {
            _log.Warning($"Artisan.GetLists IPC 调用失败: {e.Message}");
            IsAvailable = false;
            return new Dictionary<int, string>();
        }
    }

    /// <summary>
    /// 按 ID 启动 Artisan 制作列表。
    /// </summary>
    /// <param name="listId">制作列表 ID。</param>
    public void StartListById(int listId)
    {
        if (!IsAvailable || _startListByIdSubscriber is null)
        {
            return;
        }

        try
        {
            _startListByIdSubscriber.InvokeAction(listId);
            _log.Information($"Artisan.StartListById: 已启动列表 ID={listId}");
        }
        catch (IpcError e)
        {
            _log.Warning($"Artisan.StartListById IPC 调用失败: {e.Message}");
            IsAvailable = false;
        }
    }

    /// <summary>
    /// 释放 IPC 资源。
    /// </summary>
    public void Dispose()
    {
        IsAvailable = false;
        _craftItemSubscriber = null;
        _isBusySubscriber = null;
        _isListRunningSubscriber = null;
        _isListPausedSubscriber = null;
        _setListPauseSubscriber = null;
        _getStopRequestSubscriber = null;
        _setStopRequestSubscriber = null;
        _getEnduranceStatusSubscriber = null;
        _setEnduranceStatusSubscriber = null;
        _getListsSubscriber = null;
        _startListByIdSubscriber = null;
        _log.Debug("Artisan IPC 资源已释放");
    }
}
