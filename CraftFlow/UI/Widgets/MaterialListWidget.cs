using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using CraftFlow.Config;
using CraftFlow.Data.Models;
using CraftFlow.Helpers;
using CraftFlow.Services;
using CraftFlow.IPC;
using CraftFlow.Services;

namespace CraftFlow.UI.Widgets;

/// <summary>
/// 可复用材料清单组件，支持背包库存检测、固定按钮栏。
/// </summary>
public sealed class MaterialListWidget
{
    private readonly GbrIpcClient _gbrIpc;
    private readonly ArtisanIpcClient _artisanIpc;
    private readonly IpcAvailabilityChecker _ipcChecker;
    private readonly CraftProgressManager _progressManager;
    private readonly PluginConfig _config;
    private readonly IPluginLog _log;
        private readonly ItemIconService _itemIconService;
private readonly MainWindow? _mainWindow;

    /// <summary>制作开始回调，由 MainWindow 设置。用于将制作流程委托给 CraftProgressWindow。</summary>
    public Action<List<CraftStep>>? OnStartCrafting { get; set; }

    /// <summary>当前 BOM 树根节点，由外部 Tab 设置，用于缺失材料计算。</summary>
    private BomNode? _bomRoot;

    private int _treeNodeIdCounter;
    private string? _gbrPushNotification;

    // 列表中选中的物品 ID（-1 表示无选中）
    private uint _selectedItemId = uint.MaxValue;

    private static readonly Vector4 ColorGreen  = new(0.2f, 0.9f, 0.2f, 1f);
    private static readonly Vector4 ColorRed    = new(0.9f, 0.3f, 0.2f, 1f);
    private static readonly Vector4 ColorGray   = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 ColorOrange = new(0.9f, 0.6f, 0.2f, 1f);

    // 背包库存缓存（每帧刷新一次）
    private List<(MaterialEntry Entry, int Owned, int Deficit, bool Complete)>? _inventoryCache;
    private Dictionary<uint, int>? _effectiveNeedsCache;
    private int _cacheFrame;

    public MaterialListWidget(
        GbrIpcClient gbrIpc,
        ArtisanIpcClient artisanIpc,
        IpcAvailabilityChecker ipcChecker,
        CraftProgressManager progressManager,
        PluginConfig config,
        IPluginLog log,
        ItemIconService itemIconService,
        MainWindow? mainWindow = null)
    {
        _gbrIpc = gbrIpc;
        _artisanIpc = artisanIpc;
        _ipcChecker = ipcChecker;
        _progressManager = progressManager;
        _config = config;
        _log = log;
        _mainWindow = mainWindow;
        _itemIconService = itemIconService;
    }

    // ================================================================
    //  主入口：材料面板（可滚动列表 + 固定按钮栏）
    // ================================================================

    /// <summary>
    /// 绘制完整的材料面板。
    /// 列表区域可滚动，按钮栏固定在底部。
    /// </summary>
    /// <param name="materials">材料汇总列表。</param>
    /// <param name="craftSteps">制作步骤列表（可为空）。</param>
    /// <param name="availableHeight">可用高度（0 表示自动）。</param>
    /// <param name="bomRoot">BOM 树根节点（用于缺失材料计算，可为 null）。</param>
    public void DrawMaterialPanel(List<MaterialEntry> materials, List<CraftStep>? craftSteps, float availableHeight = 0, BomNode? bomRoot = null)
    {
        _bomRoot = bomRoot;

        if (materials.Count == 0)
        {
            ImGui.TextColored(ColorGray, "暂无材料数据");
            return;
        }

        // 刷新缓存
        RefreshCaches(materials);

        // 计算按钮栏高度（按钮行 + 提示区域，比之前少了 Checkbox 行）
        const float buttonAreaHeight = 65f;

        // 材料列表（可滚动）
        float listHeight = availableHeight > 0 ? availableHeight - buttonAreaHeight : ImGui.GetContentRegionAvail().Y - buttonAreaHeight;
        if (listHeight < 50) listHeight = 50;

        ImGui.BeginChild("MaterialScrollList", new Vector2(0, listHeight), false);
        DrawSummaryTable(materials, _effectiveNeedsCache);
        ImGui.EndChild();

        // 按钮栏（固定底部）
        DrawButtonBar(materials, craftSteps);
    }

    // ================================================================
    //  库存检测 + 有效需求计算（合并缓存，每帧一次）
    // ================================================================

    private void RefreshCaches(List<MaterialEntry> materials)
    {
        var currentFrame = ImGui.GetFrameCount();
        if (_cacheFrame == currentFrame) return;

        // HQ 联动"仅缺失材料"：仅影响半成品扣减，不影响背包/需要列的显示
        bool useHq = _config.OnlyMissingMaterials && _config.HqOnly;
        // 背包列始终显示 HQ+NQ，不受"仅计HQ"影响
        _inventoryCache = InventoryHelper.CheckInventory(materials, false);
        _cacheFrame = currentFrame;

        // "仅缺失材料"开启即计算有效需求（含半成品扣减，useHq 联动"仅计HQ"）
        bool showDeficit = _config.OnlyMissingMaterials;
        if (showDeficit && _bomRoot is not null)
            _effectiveNeedsCache = InventoryHelper.CalculateEffectiveNeeds(_bomRoot, useHq);
        else
            _effectiveNeedsCache = null;
    }

    // ================================================================
    //  汇总表格（带库存列）
    // ================================================================

    /// <summary>
    /// 绘制材料汇总表格（向后兼容，仅表格部分）。
    /// </summary>
    public void DrawSummary(List<MaterialEntry> materials)
    {
        RefreshCaches(materials);
        DrawSummaryTable(materials, _effectiveNeedsCache);
    }

    private void DrawSummaryTable(List<MaterialEntry> materials, Dictionary<uint, int>? effectiveNeeds)
    {
        if (materials.Count == 0)
        {
            ImGui.TextColored(ColorGray, "暂无材料数据");
            return;
        }

        bool showingDeficit = effectiveNeeds is not null;

        // 6 列：物品 | 需要 | 背包 | 实际所需 | 来源 | 状态
        if (ImGui.BeginTable("MatTable", 6, ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("需要", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("背包", ImGuiTableColumnFlags.WidthFixed, 35);
            ImGui.TableSetupColumn("实际所需", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("来源", ImGuiTableColumnFlags.WidthFixed, 35);
            ImGui.TableSetupColumn("状态", ImGuiTableColumnFlags.WidthFixed, 30);
            ImGui.TableHeadersRow();

            var inventoryData = _inventoryCache;
            foreach (var mat in materials)
            {
                int totalNeeded = mat.TotalRequired; // "需要"始终显示总需求
                int backpackOwned = 0;
                int actualNeeded; // "实际所需" = 需要 - 已有

                // 查背包库存
                var inv = inventoryData?.FirstOrDefault(i => i.Entry.ItemId == mat.ItemId);
                if (inv.HasValue && inv.Value.Owned >= 0)
                    backpackOwned = inv.Value.Owned;

                // 计算实际所需
                if (showingDeficit && effectiveNeeds!.TryGetValue(mat.ItemId, out int effectiveNeed))
                {
                    // 有效需求已含半成品扣减，再减直接背包库存
                    actualNeeded = Math.Max(0, effectiveNeed - backpackOwned);
                }
                else if (showingDeficit)
                {
                    // 有效需求中不存在 = 被半成品完全覆盖
                    actualNeeded = 0;
                }
                else
                {
                    // 非缺失模式：直接减法
                    actualNeeded = backpackOwned >= 0 ? Math.Max(0, mat.TotalRequired - backpackOwned) : mat.TotalRequired;
                }

                bool fullyCovered = showingDeficit && actualNeeded == 0 && !effectiveNeeds!.ContainsKey(mat.ItemId);

                ImGui.TableNextRow();
                ImGui.PushID($"matrow_{mat.ItemId}");
                bool isSelected = (_selectedItemId == mat.ItemId);

                if (isSelected)
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.4f, 0.6f, 0.3f)));

                                // 物品名
                ImGui.TableNextColumn();
                var nameColor = fullyCovered ? ColorGray : new Vector4(1f, 1f, 1f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Text, nameColor);
                var matIcon = _itemIconService.GetItemIcon(mat.ItemId);
                if (matIcon.Handle != 0) { ImGui.Image(matIcon, new Vector2(20, 20)); ImGui.SameLine(); }
                ImGui.Selectable(mat.ItemName, isSelected,
                    ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick,
                    new Vector2(0, 0));
                ImGui.PopStyleColor();
                if (ImGui.IsItemClicked())
                    _selectedItemId = (_selectedItemId == mat.ItemId) ? uint.MaxValue : mat.ItemId;

                DrawItemContextMenu(mat);

                // 需要（始终显示总需求，不受缺失/覆盖影响）
                ImGui.TableNextColumn();
                ImGui.Text($"×{totalNeeded}");

                // 背包（始终显示 HQ+NQ）
                ImGui.TableNextColumn();
                if (backpackOwned > 0)
                    ImGui.TextColored(backpackOwned >= totalNeeded ? ColorGreen : ColorOrange, backpackOwned.ToString());
                else if (inv.HasValue && inv.Value.Owned < 0)
                    ImGui.TextColored(ColorGray, "?");
                else
                    ImGui.TextColored(ColorGray, "0");

                // 实际所需
                ImGui.TableNextColumn();
                if (fullyCovered)
                    ImGui.TextColored(ColorGreen, "✓");
                else if (actualNeeded == 0)
                    ImGui.TextColored(ColorGreen, "✓");
                else
                    ImGui.TextColored(ColorOrange, $"{actualNeeded}");

                // 来源
                ImGui.TableNextColumn();
                DrawSourceLabel(mat.Source);

                // 状态
                ImGui.TableNextColumn();
                if (fullyCovered || actualNeeded == 0)
                    ImGui.TextColored(ColorGreen, "✓");
                else if (inv.HasValue && inv.Value.Owned >= 0)
                    ImGui.TextColored(ColorRed, $"缺{actualNeeded}");
                else
                    ImGui.TextColored(ColorGray, "?");

                ImGui.PopID();
            }

            ImGui.EndTable();
        }
    }

    /// <summary>
    /// 绘制材料行的右键菜单：复制名称、游戏内物品搜索、推送到 GBR。
    /// </summary>
    private void DrawItemContextMenu(MaterialEntry mat)
    {
        if (!ImGui.BeginPopupContextItem($"###ctx_{mat.ItemId}"))
            return;

        // 复制物品名称
        if (ImGui.MenuItem("复制物品名称"))
        {
            ImGui.SetClipboardText(mat.ItemName);
        }

        // 搜索物品获取途径
        if (ImGui.MenuItem("搜索物品获取途径"))
        {
            OpenItemSearch(mat.ItemName);
        }

        // NPC 商店信息（仅商店来源可见）
        if (mat.Source == MaterialSource.Purchasable)
        {
            if (ImGui.MenuItem("NPC 商店信息"))
            {
                ShowNpcShopInfo(mat);
            }
        }

        // 复制采集物品到剪贴板（仅采集来源可见）
        if (mat.Source == MaterialSource.Gatherable)
        {
            ImGui.Separator();
            if (ImGui.MenuItem("复制到剪贴板"))
            {
                PushToGbr([mat]);
            }
        }

        ImGui.EndPopup();
    }

    /// <summary>
    /// 打开游戏内置物品搜索窗口（/isearch 命令）。
    /// </summary>
    private static void OpenItemSearch(string itemName)
    {
        try
        {
            var cmd = $"/isearch {itemName}";
            Dalamud.Bindings.ImGui.ImGui.SetClipboardText(cmd);
        }
        catch { }
    }

    /// <summary>
    /// 查询 NPC 商店信息并复制到剪贴板。
    /// </summary>
    private void ShowNpcShopInfo(MaterialEntry mat)
    {
        try
        {
            var drIpc = Plugin.PluginInterface.GetIpcSubscriber<uint, bool>(
                "DailyRoutines.Modules.AutoShowItemNPCShopInfo.OpenShopInfoByItemID");
            var opened = drIpc.InvokeFunc(mat.ItemId);
            if (opened)
            {
                _log.Information($"已打开 DailyRoutines 商店窗口: {mat.ItemName}");
                return;
            }
        }
        catch { }

        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            if (sheet != null && sheet.TryGetRow(mat.ItemId, out var item))
            {
                var priceGil = item.PriceMid > 0 ? item.PriceMid : item.PriceLow;
                var msg = $"🛒 {mat.ItemName}\n{(priceGil > 0 ? $"售价: {priceGil} Gil" : "工票/军票等兑换")}\n\n搜索物品获取途径 → 查看具体方式";
                Dalamud.Bindings.ImGui.ImGui.SetClipboardText(msg);
                _gbrPushNotification = msg + "\n\n(安装 DailyRoutines 可获完整商店窗口)";
            }
            else
            {
                var msg = $"🛒 {mat.ItemName}\n搜索物品获取途径 → 查看商店信息";
                Dalamud.Bindings.ImGui.ImGui.SetClipboardText(msg);
                _gbrPushNotification = msg;
            }
        }
        catch (Exception ex)
        {
            _gbrPushNotification = $"🛒 {mat.ItemName}\n查询失败: {ex.Message}";
        }
    }

    // ================================================================
    //  来源标签
    // ================================================================

    private static void DrawSourceLabel(MaterialSource source)
    {
        var (label, color) = source switch
        {
            MaterialSource.Gatherable   => ("采集", ColorGreen),
            MaterialSource.Purchasable  => ("商店", new Vector4(0.8f, 0.8f, 0.2f, 1f)),
            MaterialSource.Craftable    => ("制作", new Vector4(0.2f, 0.6f, 0.8f, 1f)),
            MaterialSource.Drop         => ("掉落", new Vector4(0.8f, 0.4f, 0.2f, 1f)),
            _                           => ("未知", ColorGray),
        };
        ImGui.TextColored(color, label);

        if (source != MaterialSource.Gatherable && ImGui.IsItemHovered())
        {
            var tip = source switch
            {
                MaterialSource.Purchasable => "可在 NPC 商店购买\n右键物品 → 游戏内物品搜索 查看具体商店",
                MaterialSource.Craftable   => "需由其他配方制作\n展开树视图查看下级材料",
                MaterialSource.Drop        => "怪物/副本掉落\n右键物品 → 游戏内物品搜索 查看掉落来源",
                _                          => "来源未知\n右键物品 → 游戏内物品搜索 查证",
            };
            ImGui.SetTooltip(tip);
        }
    }

    // ================================================================
    //  按钮栏（Checkbox 已移到 Tab 层级）
    // ================================================================

    private void DrawButtonBar(List<MaterialEntry> materials, List<CraftStep>? craftSteps)
    {
        ImGui.Separator();

        float leftW = ImGui.GetContentRegionAvail().X * 0.48f;
        float h = ImGui.GetContentRegionAvail().Y;

        // 左栏：按钮
        ImGui.BeginChild("BtnLeft", new Vector2(leftW, h), false);
        DrawPushToGbrButton(materials);
        DrawCraftWithArtisanButton(craftSteps ?? [], materials);
        ImGui.EndChild();

        ImGui.SameLine();

        // 右栏：通知
        ImGui.BeginChild("BtnRight", new Vector2(0, h), false);
        DrawGbrNotification();
        ImGui.EndChild();
    }

    // ================================================================
    //  推送到 GBR（纯剪贴板，不依赖 GBR IPC）
    // ================================================================

    public void DrawPushToGbrButton(List<MaterialEntry> materials)
    {
        float btnWidth = ImGui.GetContentRegionAvail().X;
        if (ImGui.Button("复制采集清单到剪贴板###PushToGbr", new Vector2(btnWidth, 0)))
            PushToGbr(materials);
    }

    private void PushToGbr(List<MaterialEntry> materials)
    {
        _mainWindow?.AddLog($"推送 {materials.Count} 项到 GBR...", LogLevel.Info);

        // 优先复用已缓存的有效需求/差额数据
        Dictionary<uint, int>? deficitMap = null;

        if (_config.OnlyMissingMaterials && _bomRoot is not null)
        {
            bool useHq = _config.OnlyMissingMaterials && _config.HqOnly;
            deficitMap = InventoryHelper.CalculateGatherableDeficit(_bomRoot, materials, useHq);
            if (deficitMap.Count == 0)
            {
                _gbrPushNotification = "所有采集材料已齐全，无需采集";
                _log.Information("GBR 推送: 所有采集材料已齐全");
                _mainWindow?.AddLog("所有采集材料已齐全，无需推送", LogLevel.Success);
                return;
            }
        }

        var b64 = GbrListHelper.ToGbrBase64(materials, deficitMap);
        if (string.IsNullOrEmpty(b64))
        {
            _gbrPushNotification = null;
            _log.Information("无可采集材料需要推送");
            _mainWindow?.AddLog("无可采集材料需要推送", LogLevel.Warning);
            return;
        }

        ImGui.SetClipboardText(b64);

        var count = deficitMap?.Count ?? materials.Count(m => m.Source == MaterialSource.Gatherable);
        _gbrPushNotification = $"GBR 采集清单已复制到剪贴板\n（{count} 种，总需求量）";
        _log.Information($"GBR 推送完成 (base64, onlyMissing={_config.OnlyMissingMaterials}, hqOnly={_config.HqOnly})");
        _mainWindow?.AddLog($"GBR 采集清单（{count} 种）已复制到剪贴板", LogLevel.Success);
    }

    public void DrawGbrNotification()
    {
        if (_gbrPushNotification is null)
        {
            ImGui.TextColored(ColorGray, "点击左侧按钮\n复制 GBR 采集清单");
            return;
        }
        ImGui.TextColored(ColorGreen, "✅ GBR 推送结果:");
        ImGui.TextWrapped(_gbrPushNotification);
        if (ImGui.SmallButton("关闭提示###DismissGbrNote"))
            _gbrPushNotification = null;
    }

    // ================================================================
    //  一键 Artisan 制作
    // ================================================================

    public void DrawCraftWithArtisanButton(List<CraftStep> steps, List<MaterialEntry>? materials = null)
    {
        float btnWidth = ImGui.GetContentRegionAvail().X;

        bool artisanAvailable = _ipcChecker.IsArtisanAvailable();
        if (!artisanAvailable) ImGui.BeginDisabled();

        if (ImGui.Button("一键 Artisan 制作###CraftWithArtisan", new Vector2(btnWidth, 0)))
            CraftWithArtisan(steps, materials);

        if (!artisanAvailable)
        {
            ImGui.EndDisabled();
            if (ImGui.Button("重新检测###RescanArtisan", new Vector2(btnWidth, 0)))
            {
                bool nowAvailable = _artisanIpc.TryResubscribe();
                _log.Information($"Artisan 重新检测结果: {(nowAvailable ? "可用" : "不可用")}");
            }
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "请先安装 Artisan");
        }
        else if (steps.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorGray, $"({steps.Count} 个制作步骤)");
        }
    }

    private void CraftWithArtisan(List<CraftStep> steps, List<MaterialEntry>? materials)
    {
        if (steps.Count == 0) { _log.Information("无制作步骤"); return; }
        if (!_artisanIpc.IsAvailable) { _log.Warning("Artisan 不可用"); return; }

        if (materials is not null && materials.Count > 0)
        {
            // 检查材料是否充足：考虑半成品（effective needs）扣减
            bool useHq = _config.OnlyMissingMaterials && _config.HqOnly;
            List<(MaterialEntry Entry, int Deficit)> missing;

            if (_config.OnlyMissingMaterials && _bomRoot is not null)
            {
                // 使用有效需求（含半成品扣减），再扣除原材料背包库存
                var effectiveNeeds = InventoryHelper.CalculateEffectiveNeeds(_bomRoot, useHq);
                missing = materials
                    .Where(m => effectiveNeeds.TryGetValue(m.ItemId, out int need) && need > 0)
                    .Select(m =>
                    {
                        int owned = InventoryHelper.GetItemCount(m.ItemId, false);
                        int deficit = Math.Max(0, effectiveNeeds[m.ItemId] - owned);
                        return (m, deficit);
                    })
                    .Where(x => x.deficit > 0)
                    .ToList();
            }
            else
            {
                var inv = InventoryHelper.CheckInventory(materials, useHq);
                missing = inv.Where(i => i.Deficit > 0)
                    .Select(i => (i.Entry, i.Deficit))
                    .ToList();
            }

            if (missing.Count > 0)
            {
                var names = string.Join(", ", missing.Take(3).Select(m => $"{m.Entry.ItemName} 缺{m.Deficit}"));
                if (missing.Count > 3) names += $" 等{missing.Count}种";
                _log.Warning($"材料不足，无法开始制作: {names}");
                return;
            }
        }

        // 过滤：已有半成品跳过制作或减少制作次数
        var filteredSteps = steps
            .Select(s =>
            {
                int owned = InventoryHelper.GetItemCount(s.ItemId, false);
                if (owned <= 0) return (Step: s, Skip: false);

                int yield = s.AmountResult > 0 ? s.AmountResult : 1;
                int totalProduced = s.Quantity * yield;
                if (owned >= totalProduced)
                {
                    _log.Information($"跳过制作 {s.ItemName}（已有 {owned}，需要 {totalProduced}）");
                    return (Step: s, Skip: true);
                }

                int stillNeeded = totalProduced - owned;
                s.Quantity = Math.Max(1, (int)Math.Ceiling((double)stillNeeded / yield));
                _log.Information($"调整制作 {s.ItemName}：需要 {stillNeeded} 件，制作 {s.Quantity} 次 (yield={yield})");
                return (Step: s, Skip: false);
            })
            .Where(x => !x.Skip)
            .Select(x => x.Step)
            .ToList();

        _progressManager.Start(filteredSteps);
        _artisanIpc.SetEnduranceStatus(true);

        OnStartCrafting?.Invoke(filteredSteps);
    }

    // ================================================================
    //  BOM 树视图
    // ================================================================

    public void DrawTree(BomNode root)
    {
        if (root is null) return;
        _bomRoot = root;

        // 树视图同步"仅缺失材料"：动态计算有效需求，useHq 联动"仅计HQ"
        Dictionary<uint, int>? effectiveNeeds = null;
        if (_config.OnlyMissingMaterials)
        {
            bool useHq = _config.OnlyMissingMaterials && _config.HqOnly;
            effectiveNeeds = InventoryHelper.CalculateEffectiveNeeds(root, useHq);
        }

        ImGui.BeginChild("BomTreeView");
        _treeNodeIdCounter = 0;
        // 跨分支库存消费追踪，防止组合 BOM 树中共享中间产物被重复扣减
        var inventoryConsumed = new Dictionary<uint, int>();
        DrawTreeNode(root, effectiveNeeds, 1.0, true, inventoryConsumed);
        ImGui.EndChild();
    }

    private void DrawTreeNode(BomNode node, Dictionary<uint, int>? effectiveNeeds, double scale,
        bool startOpen, Dictionary<uint, int> inventoryConsumed)
    {
        var flags = ImGuiTreeNodeFlags.SpanAvailWidth;
        if (node.Children.Count == 0)
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        if (startOpen) flags |= ImGuiTreeNodeFlags.DefaultOpen;

        // 计算实际需求量
        int rawQty = node.Quantity;
        bool showingDeficit = effectiveNeeds is not null;

        if (showingDeficit && node.IsLeaf && node.ItemId != 0)
        {
            // 叶节点按本分支 scale × Quantity 计算需求量（不使用全局聚合的 effectiveNeeds，
            // 否则所有同 ItemId 的叶节点会显示相同的全局总量，而非各自分支的实际量）。
            rawQty = (int)Math.Ceiling(node.Quantity * scale);
            if (rawQty < 0) rawQty = 0;
        }
        else if (showingDeficit && !node.IsLeaf && node.ItemId != 0)
        {
            // 成品/顶层节点（Depth == 0）：不扣成品背包库存
            if (node.Depth == 0)
            {
                rawQty = (int)Math.Ceiling(node.Quantity * scale);
                string label = $"{node.ItemName} ×{rawQty}";
                if (node.IsIncomplete) label += " [不完整]";
                bool isOpen = ImGui.TreeNodeEx($"###BomNode_{_treeNodeIdCounter++}", flags, label);
                if (isOpen && node.Children.Count > 0)
                {
                    foreach (var child in node.Children)
                        DrawTreeNode(child, effectiveNeeds, 1.0, false, inventoryConsumed);
                    ImGui.TreePop();
                }
                return;
            }

            // 半成品（Depth > 0）：查背包库存显示扣减后信息（始终用 HQ+NQ）
            int owned = InventoryHelper.GetItemCount(node.ItemId, false);

            // 扣除其他分支已消费的库存，防止共享中间产物重复扣减
            if (owned > 0 && inventoryConsumed.TryGetValue(node.ItemId, out int alreadyConsumed))
            {
                owned = Math.Max(0, owned - alreadyConsumed);
            }

            rawQty = (int)Math.Ceiling(node.Quantity * scale);
            if (owned > 0)
            {
                int remaining = Math.Max(0, rawQty - owned);
                int consumedThisBranch = rawQty - remaining;
                if (consumedThisBranch > 0)
                {
                    if (inventoryConsumed.TryGetValue(node.ItemId, out int prev))
                        inventoryConsumed[node.ItemId] = prev + consumedThisBranch;
                    else
                        inventoryConsumed[node.ItemId] = consumedThisBranch;
                }

                // owned 已扣除前序分支消费量，直接显示为本分支可用库存
                string ownedStr = $"已有 {owned}";
                string label = node.ItemId == 0
                    ? node.ItemName
                    : $"{node.ItemName} ×{remaining} (需{rawQty} {ownedStr})";
                if (node.IsIncomplete) label += " [不完整]";
                bool isOpen = ImGui.TreeNodeEx($"###BomNode_{_treeNodeIdCounter++}", flags, label);
                if (isOpen && node.Children.Count > 0)
                {
                    double childScale = rawQty > 0 ? scale * ((double)remaining / rawQty) : 0;
                    foreach (var child in node.Children)
                        DrawTreeNode(child, effectiveNeeds, childScale, false, inventoryConsumed);
                    ImGui.TreePop();
                }
                return;
            }
        }

        // 默认标签
        // 物品图标
        if (node.ItemId != 0) {
            var nodeIcon = _itemIconService.GetItemIcon(node.ItemId);
            if (nodeIcon.Handle != 0) { ImGui.Image(nodeIcon, new Vector2(20, 20)); ImGui.SameLine(); }
        }
                string defaultLabel = node.ItemId == 0
            ? node.ItemName
            : $"{node.ItemName} ×{rawQty}";
        if (node.IsIncomplete) defaultLabel += " [不完整]";

        if (showingDeficit && rawQty == 0 && node.IsLeaf)
            defaultLabel += " ✓(已覆盖)";

        bool isNodeOpen = ImGui.TreeNodeEx($"###BomNode_{_treeNodeIdCounter++}", flags, defaultLabel);
        if (isNodeOpen && node.Children.Count > 0)
        {
            foreach (var child in node.Children)
                DrawTreeNode(child, effectiveNeeds, scale, false, inventoryConsumed);
            ImGui.TreePop();
        }
    }
}
