using System;

namespace CraftFlow.IPC;

/// <summary>
/// IPC 客户端抽象接口，定义 Dalamud IPC 插件客户端的通用行为。
/// 所有外部插件 IPC 客户端（GBR、Artisan）均实现此接口。
/// </summary>
public interface IIpcClient : IDisposable
{
    /// <summary>
    /// 目标插件是否已安装且 IPC 可用。
    /// 在 Subscribe 时检测，IPC 调用失败时自动置为 false。
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 订阅目标插件的 IPC Channel。
    /// 应在插件初始化时调用，若目标插件未安装则 IsAvailable 保持 false。
    /// </summary>
    void Subscribe();

    /// <summary>
    /// 释放 IPC 订阅资源。
    /// </summary>
    new void Dispose();
}
