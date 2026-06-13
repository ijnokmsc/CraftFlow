using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using CraftFlow.Data.Models;
using CraftFlow.Helpers;
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
    private readonly IPluginLog _log;

    /// <summary>制作开始回调，由 MainWindow 设置。用于将制作流程委托给 CraftProgressWindow。</summary>
    public Action<List<CraftStep>>? OnStartCrafting { get; set; }

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
    private int _inventoryCacheFrame;

    public MaterialListWidget(
        GbrIpcClient gbrIpc,
        ArtisanIpcClient artisanIpc,
        IpcAvailabilityChecker ipcChecker,
        CraftProgressManager progressManager,
        IPluginLog log)
    {
        _gbrIpc = gbrIpc;
        _artisanIpc = artisanIpc;
        _ipcChecker = ipcChecker;
        _progressManager = progressManager;
        _log = log;
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
    public void DrawMaterialPanel(List<MaterialEntry> materials, List<CraftStep>? craftSteps, float availableHeight = 0)
    {
        if (materials.Count == 0)
        {
            ImGui.TextColored(ColorGray, "暂无材料数据");
            return;
        }

        // 刷新库存缓存
        RefreshInventoryCache(materials);

        // 计算按钮栏高度
        const float buttonAreaHeight = 90f;

        // 材料列表（可滚动）
        float listHeight = availableHeight > 0 ? availableHeight - buttonAreaHeight : ImGui.GetContentRegionAvail().Y - buttonAreaHeight;
        if (listHeight < 50) listHeight = 50;

        ImGui.BeginChild("MaterialScrollList", new Vector2(0, listHeight), false);
        DrawSummaryTable(materials);
        ImGui.EndChild();

        // 按钮栏（固定底部）
        DrawButtonBar(materials, craftSteps);
    }

    // ================================================================
    //  库存检测
    // ================================================================

    private void RefreshInventoryCache(List<MaterialEntry> materials)
    {
        var currentFrame = ImGui.GetFrameCount();
        if (_inventoryCacheFrame != currentFrame)
        {
            _inventoryCache = InventoryHelper.CheckInventory(materials);
            _inventoryCacheFrame = currentFrame;
        }
    }

    // ================================================================
    //  汇总表格（带库存列）
    // ================================================================

    /// <summary>
    /// 绘制材料汇总表格（向后兼容，仅表格部分）。
    /// </summary>
    public void DrawSummary(List<MaterialEntry> materials)
    {
        RefreshInventoryCache(materials);
        DrawSummaryTable(materials);
    }

    private void DrawSummaryTable(List<MaterialEntry> materials)
    {
        if (materials.Count == 0)
        {
            ImGui.TextColored(ColorGray, "暂无材料数据");
            return;
        }

        if (ImGui.BeginTable("MatTable", 5, ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("需要", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("背包", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("来源", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("状态", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableHeadersRow();

            var inventoryData = _inventoryCache;
            foreach (var mat in materials)
            {
                var inv = inventoryData?.FirstOrDefault(i => i.Entry.ItemId == mat.ItemId);

                ImGui.TableNextRow();

                // 行点击选中
                ImGui.PushID($"matrow_{mat.ItemId}");
                bool isSelected = (_selectedItemId == mat.ItemId);
                if (isSelected)
                {
                    // 选中行高亮
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.4f, 0.6f, 0.3f)));
                }

                // 点击选中
                ImGui.TableNextColumn();
                ImGui.Selectable(mat.ItemName, isSelected,
                    ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick,
                    new Vector2(0, 0));
                if (ImGui.IsItemClicked())
                {
                    _selectedItemId = (_selectedItemId == mat.ItemId) ? uint.MaxValue : mat.ItemId;
                }

                // 右键菜单
                DrawItemContextMenu(mat);

                ImGui.TableNextColumn();
                ImGui.Text($"×{mat.TotalRequired}");

                ImGui.TableNextColumn();
                if (inv.HasValue)
                {
                    int owned = inv.Value.Owned;
                    if (owned >= 0)
                        ImGui.TextColored(owned >= mat.TotalRequired ? ColorGreen : ColorOrange, owned.ToString());
                    else
                        ImGui.TextColored(ColorGray, "?");
                }

                ImGui.TableNextColumn();
                DrawSourceLabel(mat.Source);

                ImGui.TableNextColumn();
                if (inv.HasValue)
                {
                    if (inv.Value.Complete)
                        ImGui.TextColored(ColorGreen, "✓");
                    else if (inv.Value.Owned >= 0)
                        ImGui.TextColored(ColorRed, $"缺{inv.Value.Deficit}");
                    else
                        ImGui.TextColored(ColorGray, "?");
                }

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

        // 推送到 GBR（仅采集来源可见）
        if (mat.Source == MaterialSource.Gatherable)
        {
            ImGui.Separator();
            if (ImGui.MenuItem("推送到 GBR"))
            {
                PushToGbr([mat]);
            }
        }

        ImGui.EndPopup();
    }

    /// <summary>
    /// 打开游戏内置物品搜索窗口（/isearch 命令），
    /// 显示该物品的所有获取途径（商店 NPC、采集点、制作配方等）。
    /// 效果与每日功能模块 AutoShowItemNPCShopInfo 一致。
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
    /// 查询 NPC 商店信息并复制到剪贴板（参考 DailyRoutines AutoShowItemNPCShopInfo）。
    /// 如果 DailyRoutines 已安装，调用其 IPC 打开商店信息窗口。
    /// </summary>
    private void ShowNpcShopInfo(MaterialEntry mat)
    {
        try
        {
            // 尝试调用 DailyRoutines 的 AutoShowItemNPCShopInfo IPC
            var drIpc = Plugin.PluginInterface.GetIpcSubscriber<uint, bool>(
                "DailyRoutines.Modules.AutoShowItemNPCShopInfo.OpenShopInfoByItemID");
            var opened = drIpc.InvokeFunc(mat.ItemId);
            if (opened)
            {
                _log.Information($"已打开 DailyRoutines 商店窗口: {mat.ItemName}");
                return;
            }
        }
        catch
        {
            // DailyRoutines 未安装或 IPC 不可用，使用内置查询
        }

        // 内置查询
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

        // 不可采集的来源显示 tooltip 提示兑换/获取方式
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
    //  按钮栏
    // ================================================================

    private void DrawButtonBar(List<MaterialEntry> materials, List<CraftStep>? craftSteps)
    {
        ImGui.Separator();

        DrawPushToGbrButton(materials);
        ImGui.SameLine();

        var steps = craftSteps ?? [];
        DrawCraftWithArtisanButton(steps, materials);

        ImGui.Separator();
        // DrawGbrNotification 只在按钮栏画一次，不在 DrawPushToGbrButton 内重复画
        DrawGbrNotification();
    }

    // ================================================================
    //  推送到 GBR
    // ================================================================

    public void DrawPushToGbrButton(List<MaterialEntry> materials)
    {
        bool gbrAvailable = _ipcChecker.IsGbrAvailable();
        if (!gbrAvailable) ImGui.BeginDisabled();

        if (ImGui.Button("推送到 GBR 采集列表###PushToGbr"))
            PushToGbr(materials);

        if (!gbrAvailable)
        {
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("重新检测 GBR###RescanGbr"))
            {
                bool nowAvailable = _gbrIpc.TryResubscribe();
                _log.Information($"GBR 重新检测结果: {(nowAvailable ? "可用" : "不可用")}");
            }
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "请先安装 GatherBuddyReborn");
        }
        else
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorGray, "(base64 粘贴导入)");
        }
    }

    private void PushToGbr(List<MaterialEntry> materials)
    {
        var b64 = GbrListHelper.ToGbrBase64(materials);
        if (string.IsNullOrEmpty(b64))
        {
            _gbrPushNotification = null;
            _log.Information("无可采集材料需要推送");
            return;
        }

        ImGui.SetClipboardText(b64);

        if (_gbrIpc.IsAvailable)
            _gbrIpc.SetAutoGatherEnabled(true);

        _gbrPushNotification = "GBR 清单 base64 已复制到剪贴板\n在 GBR AutoGather 列表中右键 → 粘贴导入";
        _log.Information("GBR 推送完成 (base64)");
    }

    public void DrawGbrNotification()
    {
        if (_gbrPushNotification is null) return;
        ImGui.Separator();
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
        // 轮询逻辑已迁移到 CraftProgressWindow

        bool artisanAvailable = _ipcChecker.IsArtisanAvailable();
        if (!artisanAvailable) ImGui.BeginDisabled();

        if (ImGui.Button("一键 Artisan 制作###CraftWithArtisan"))
            CraftWithArtisan(steps, materials);

        if (!artisanAvailable)
        {
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("重新检测 Artisan###RescanArtisan"))
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

        // 检测材料是否齐全
        if (materials is not null && materials.Count > 0)
        {
            var inv = InventoryHelper.CheckInventory(materials);
            var missing = inv.Where(i => i.Deficit > 0).ToList();
            if (missing.Count > 0)
            {
                var names = string.Join(", ", missing.Take(3).Select(m => $"{m.Entry.ItemName} 缺{m.Deficit}"));
                if (missing.Count > 3) names += $" 等{missing.Count}种";
                _log.Warning($"材料不足，无法开始制作: {names}");
                return;
            }
        }

        _progressManager.Start(steps);
        _artisanIpc.SetEnduranceStatus(true);

        // 通过回调通知外部启动制作（不再直接推送步骤）
        OnStartCrafting?.Invoke(steps);
    }

    // ================================================================
    //  BOM 树视图（不变）
    // ================================================================

    public void DrawTree(BomNode root)
    {
        if (root is null) return;
        ImGui.BeginChild("BomTreeView");
        _treeNodeIdCounter = 0;
        DrawTreeNode(root, true);
        ImGui.EndChild();
    }

    private void DrawTreeNode(BomNode node, bool startOpen)
    {
        var flags = ImGuiTreeNodeFlags.SpanAvailWidth;
        if (node.Children.Count == 0)
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        if (startOpen) flags |= ImGuiTreeNodeFlags.DefaultOpen;

        string label = node.ItemId == 0
            ? node.ItemName
            : $"{node.ItemName} ×{node.Quantity}";
        if (node.IsIncomplete) label += " [不完整]";

        bool isOpen = ImGui.TreeNodeEx($"###BomNode_{_treeNodeIdCounter++}", flags, label);
        if (isOpen && node.Children.Count > 0)
        {
            foreach (var child in node.Children)
                DrawTreeNode(child, false);
            ImGui.TreePop();
        }
    }
}
