using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Plugin;
using CraftFlow.Data.Models;

namespace CraftFlow.Config;

/// <summary>
/// 插件配置类，通过 Dalamud PluginInterface 持久化。
/// </summary>
[Serializable]
public class PluginConfig : IPluginConfiguration
{
    private IDalamudPluginInterface _pluginInterface;

    /// <summary>
    /// 配置版本号，用于 Dalamud IPluginConfiguration 接口。
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// 默认版本筛选（如 7 表示仅显示 7.x 配方，0 表示全部）。
    /// </summary>
    public int DefaultVersion { get; set; } = 7;

    /// <summary>
    /// 主窗口位置。
    /// </summary>
    public Vector2 WindowPosition { get; set; } = Vector2.Zero;

    /// <summary>
    /// 主窗口是否锁定位置。
    /// </summary>
    public bool IsWindowLocked { get; set; } = false;

    /// <summary>
    /// 是否在材料清单中显示水晶/晶簇。默认不显示。
    /// </summary>
    public bool ShowCrystals { get; set; } = false;

    /// <summary>
    /// GBR 推送时是否只推送缺失材料（扣除背包已有量，含半成品）。
    /// </summary>
    public bool OnlyMissingMaterials { get; set; } = false;

    /// <summary>
    /// 计算已有材料时是否只计 HQ 物品。联动 OnlyMissingMaterials，
    /// 勾选后 NQ 物品不计入已有量，差额需用 HQ 补足。
    /// </summary>
    public bool HqOnly { get; set; } = false;

    /// <summary>
    /// 用户收藏的装备预设列表。
    /// </summary>
    public List<FavoritePreset> FavoritePresets { get; set; } = [];

    /// <summary>
    /// 制作进度（一键 Artisan 制作），持久化以支持崩溃恢复。
    /// </summary>
    public CraftProgress? CraftProgress { get; set; }

    /// <summary>
    /// 初始化配置实例并从持久化存储加载。
    /// </summary>
    /// <param name="pluginInterface">Dalamud 插件接口。</param>
    public PluginConfig(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        LoadSafe();
    }

    /// <summary>
    /// 无参构造器，供 Dalamud JSON 反序列化使用。
    /// </summary>
    public PluginConfig()
    {
        _pluginInterface = null!;
    }

    /// <summary>
    /// 将当前配置保存到持久化存储。
    /// </summary>
    public void Save()
    {
        if (_pluginInterface is null) return;
        _pluginInterface.SavePluginConfig(this);
    }

    /// <summary>
    /// 安全加载配置，反序列化失败时使用默认值不崩溃。
    /// </summary>
    private void LoadSafe()
    {
        try
        {
            var saved = _pluginInterface.GetPluginConfig() as PluginConfig;
            if (saved is not null) CopyFrom(saved);
        }
        catch (Exception ex)
        {
            // 反序列化失败（如模型变更导致旧 JSON 不兼容），使用默认值
            System.Diagnostics.Debug.WriteLine($"[CraftFlow] 配置加载失败，使用默认值: {ex.Message}");
        }
    }

    private void CopyFrom(PluginConfig saved)
    {
        DefaultVersion = saved.DefaultVersion;
        WindowPosition = saved.WindowPosition;
        IsWindowLocked = saved.IsWindowLocked;
        ShowCrystals = saved.ShowCrystals;
        OnlyMissingMaterials = saved.OnlyMissingMaterials;
        HqOnly = saved.HqOnly;
        FavoritePresets = saved.FavoritePresets ?? [];
        CraftProgress = saved.CraftProgress;
    }
}
