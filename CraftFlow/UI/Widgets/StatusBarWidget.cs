using System;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using CraftFlow.IPC;

namespace CraftFlow.UI.Widgets;

/// <summary>
/// 底部状态栏组件，显示 Artisan/GBR/库存状态。
/// </summary>
public sealed class StatusBarWidget
{
    private readonly GbrIpcClient _gbrIpc;
    private readonly ArtisanIpcClient _artisanIpc;
    private readonly IPluginLog _log;

    private string _artisanStatus = "未知";
    private string _gbrStatus = "未知";

    /// <summary>
    /// 初始化 StatusBarWidget 实例。
    /// </summary>
    /// <param name="gbrIpc">GBR IPC 客户端。</param>
    /// <param name="artisanIpc">Artisan IPC 客户端。</param>
    /// <param name="log">插件日志。</param>
    public StatusBarWidget(GbrIpcClient gbrIpc, ArtisanIpcClient artisanIpc, IPluginLog log)
    {
        _gbrIpc = gbrIpc;
        _artisanIpc = artisanIpc;
        _log = log;
    }

    /// <summary>
    /// 绘制状态栏。
    /// 每次绘制时更新 IPC 状态信息。
    /// </summary>
    public void Draw()
    {
        // 更新 Artisan 状态
        if (_artisanIpc.IsAvailable)
        {
            bool isBusy = _artisanIpc.IsBusy();
            _artisanStatus = isBusy ? "制作中" : "就绪";
        }
        else
        {
            _artisanStatus = "未安装";
        }

        // 更新 GBR 状态
        if (_gbrIpc.IsAvailable)
        {
            bool isEnabled = _gbrIpc.IsAutoGatherEnabled();
            bool isWaiting = _gbrIpc.IsAutoGatherWaiting();
            _gbrStatus = isEnabled
                ? (isWaiting ? "等待中" : "采集中")
                : "就绪";
        }
        else
        {
            _gbrStatus = "未安装";
        }

        // 渲染状态栏
        float availWidth = ImGui.GetContentRegionAvail().X;

        // Artisan 状态
        var artisanColor = _artisanStatus switch
        {
            "就绪" => new System.Numerics.Vector4(0.2f, 0.8f, 0.2f, 1f),
            "制作中" => new System.Numerics.Vector4(0.8f, 0.8f, 0.2f, 1f),
            "未安装" => new System.Numerics.Vector4(0.8f, 0.4f, 0.4f, 1f),
            _ => new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1f)
        };
        ImGui.TextColored(artisanColor, $"Artisan: {_artisanStatus}");

        // GBR 状态
        ImGui.SameLine();
        var gbrColor = _gbrStatus switch
        {
            "就绪" => new System.Numerics.Vector4(0.2f, 0.8f, 0.2f, 1f),
            "采集中" => new System.Numerics.Vector4(0.2f, 0.6f, 0.8f, 1f),
            "等待中" => new System.Numerics.Vector4(0.8f, 0.8f, 0.2f, 1f),
            "未安装" => new System.Numerics.Vector4(0.8f, 0.4f, 0.4f, 1f),
            _ => new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1f)
        };
        ImGui.TextColored(gbrColor, $"GBR: {_gbrStatus}");

        // 库存状态（P1 功能，占位显示）
        ImGui.SameLine();
        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1f), "库存: 未加载");
    }
}
