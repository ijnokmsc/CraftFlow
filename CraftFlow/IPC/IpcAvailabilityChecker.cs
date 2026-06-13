using Dalamud.Plugin.Services;

namespace CraftFlow.IPC;

/// <summary>
/// IPC 可用性检测器，检查 GBR 和 Artisan 插件是否已安装且 IPC 可用。
/// 在用户点击操作按钮前重新检测，确保状态最新。
/// 依赖外部的延迟订阅机制（GbrIpcClient.DelayedSubscribe），不再在构造函数内立即重试。
/// </summary>
public sealed class IpcAvailabilityChecker
{
    private readonly GbrIpcClient _gbrIpc;
    private readonly ArtisanIpcClient _artisanIpc;

    /// <summary>
    /// 初始化 IpcAvailabilityChecker 实例。
    /// </summary>
    /// <param name="gbrIpc">GBR IPC 客户端。</param>
    /// <param name="artisanIpc">Artisan IPC 客户端。</param>
    public IpcAvailabilityChecker(GbrIpcClient gbrIpc, ArtisanIpcClient artisanIpc)
    {
        _gbrIpc = gbrIpc;
        _artisanIpc = artisanIpc;
    }

    /// <summary>
    /// 检测 GatherBuddyReborn 是否已安装且 IPC 可用。
    /// 直接使用 GbrIpcClient 的 IsAvailable 状态。
    /// </summary>
    /// <returns>GBR 是否可用。</returns>
    public bool IsGbrAvailable()
    {
        return _gbrIpc.IsAvailable;
    }

    /// <summary>
    /// 检测 Artisan 是否已安装且 IPC 可用。
    /// 直接使用 ArtisanIpcClient 的 IsAvailable 状态。
    /// </summary>
    /// <returns>Artisan 是否可用。</returns>
    public bool IsArtisanAvailable()
    {
        return _artisanIpc.IsAvailable;
    }
}
