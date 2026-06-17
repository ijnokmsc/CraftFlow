using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using CraftFlow.Data.GameData;

namespace CraftFlow.Services;

/// <summary>
/// 物品图标加载服务。通过 Lumina Item.Icon 字段获取图标 ID，
/// 使用 ITextureProvider.GetFromGameIcon 加载纹理。
/// 与 JobIconService 一致：缓存 ISharedImmediateTexture，每帧 GetWrapOrDefault 等待异步加载。
/// </summary>
public sealed class ItemIconService
{
    private readonly ITextureProvider _textureProvider;
    private readonly LuminaCache _cache;
    private readonly IPluginLog _log;
    private readonly Dictionary<uint, ISharedImmediateTexture> _iconCache = new();

    public ItemIconService(ITextureProvider textureProvider, LuminaCache cache, IPluginLog log)
    {
        _textureProvider = textureProvider;
        _cache = cache;
        _log = log;
    }

    /// <summary>
    /// 获取物品图标 ImTextureID（异步加载完成前返回 Handle==0，ImGui 静默跳过）。
    /// </summary>
    public ImTextureID GetItemIcon(uint itemId)
    {
        if (itemId == 0) return default;

        if (!_iconCache.TryGetValue(itemId, out var shared))
        {
            // 从 Lumina Item 获取图标 ID
            if (!_cache.ItemSheet.TryGetValue(itemId, out var item) || item.Icon == 0)
            {
                return default;
            }

            try
            {
                shared = _textureProvider.GetFromGameIcon(new GameIconLookup(item.Icon, false, true));
                _iconCache[itemId] = shared;
            }
            catch (Exception ex)
            {
                _log.Debug($"ItemIconService: 图标加载失败 ItemId={itemId} IconId={item.Icon}: {ex.Message}");
                return default;
            }
        }

        var wrap = shared.GetWrapOrDefault();
        return wrap is not null ? wrap.Handle : default;
    }
}
