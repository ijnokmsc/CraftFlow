using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;
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
    /// <summary>
    /// Dalamud 插件接口引用，不参与序列化。
    /// 双重 JsonIgnore 防止任何序列化器意外序列化此字段。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
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
    /// 显式初始化所有属性为安全默认值，确保即使 JSON 反序列化
    /// 部分失败或抛出异常，实例也是完整可用的。
    /// </summary>
    public PluginConfig()
    {
        // 显式初始化所有属性，不依赖自动属性初始化器
        // ——在部分反序列化场景下，属性可能已被覆写为无效值，
        // 这里确保最基础的字段安全。
        Version = 1;
        DefaultVersion = 7;
        WindowPosition = Vector2.Zero;
        IsWindowLocked = false;
        ShowCrystals = false;
        OnlyMissingMaterials = false;
        HqOnly = false;
        FavoritePresets = [];
        CraftProgress = null;
        _pluginInterface = null!;
    }

    /// <summary>
    /// 将当前配置保存到持久化存储。
    /// 当 _pluginInterface 不可用时（如无参构造器实例）安全跳过。
    /// </summary>
    public void Save()
    {
        if (_pluginInterface is null) return;
        try
        {
            _pluginInterface.SavePluginConfig(this);
        }
        catch (Exception ex)
        {
            // 保存失败不应导致插件崩溃，静默记录
            System.Diagnostics.Debug.WriteLine($"[CraftFlow] 配置保存失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 安全加载配置：捕获所有异常，_pluginInterface 为 null 时跳过，
    /// GetPluginConfig 返回 null 时保留默认值。
    /// </summary>
    private void LoadSafe()
    {
        if (_pluginInterface is null) return;
        try
        {
            var saved = _pluginInterface.GetPluginConfig() as PluginConfig;
            if (saved is not null)
            {
                CopyFrom(saved);
            }
        }
        catch (Exception ex)
        {
            // 反序列化失败（如模型变更导致旧 JSON 不兼容），使用默认值
            System.Diagnostics.Debug.WriteLine($"[CraftFlow] 配置加载失败，使用默认值: {ex.Message}");
        }
    }

    /// <summary>
    /// 从已保存的配置实例复制所有属性值。
    /// 每个属性独立赋值，单个赋值失败不影响其他属性。
    /// </summary>
    /// <param name="saved">已保存的配置实例。</param>
    private void CopyFrom(PluginConfig saved)
    {
        if (saved is null) return;
        try
        {
            Version = saved.Version;
            DefaultVersion = saved.DefaultVersion;
            WindowPosition = saved.WindowPosition;
            IsWindowLocked = saved.IsWindowLocked;
            ShowCrystals = saved.ShowCrystals;
            OnlyMissingMaterials = saved.OnlyMissingMaterials;
            HqOnly = saved.HqOnly;
            FavoritePresets = saved.FavoritePresets ?? [];
            CraftProgress = saved.CraftProgress;
        }
        catch (Exception ex)
        {
            // 单个属性赋值失败不应中断整体加载
            System.Diagnostics.Debug.WriteLine($"[CraftFlow] CopyFrom 失败，保留默认值: {ex.Message}");
        }
    }
}
