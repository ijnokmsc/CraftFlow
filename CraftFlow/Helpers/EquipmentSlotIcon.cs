using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using CraftFlow.Data.Models;

namespace CraftFlow.Helpers;

/// <summary>
/// 装备槽位图标服务。
/// 图标设计参考 HQHelper 项目 (https://hqhelper.nbb.fan/) 的 game-gear SVG 图标集。
/// 通过 TextColored 直接渲染符号文本，ImGui 原生对齐，稳定兼容。
/// </summary>
public sealed class EquipmentSlotIcon
{
    /// <summary>
    /// 槽位对应的颜色和符号。
    /// </summary>
    private static readonly Dictionary<EquipmentSlotType, (char Symbol, Vector4 Color)> SlotVisuals = new()
    {
        [EquipmentSlotType.MainHand] = ('⚔', new Vector4(0.95f, 0.85f, 0.55f, 1f)),
        [EquipmentSlotType.OffHand]   = ('⚔', new Vector4(0.85f, 0.75f, 0.50f, 1f)),
        [EquipmentSlotType.Head]      = ('⛑', new Vector4(0.40f, 0.75f, 0.95f, 1f)),
        [EquipmentSlotType.Body]      = ('⚜', new Vector4(0.30f, 0.80f, 0.60f, 1f)),
        [EquipmentSlotType.Hands]     = ('⛓', new Vector4(0.90f, 0.60f, 0.40f, 1f)),
        [EquipmentSlotType.Legs]      = ('⛰', new Vector4(0.60f, 0.50f, 0.90f, 1f)),
        [EquipmentSlotType.Feet]      = ('⬆', new Vector4(0.70f, 0.70f, 0.70f, 1f)),
        [EquipmentSlotType.Ears]      = ('◆', new Vector4(0.95f, 0.65f, 0.85f, 1f)),
        [EquipmentSlotType.Neck]      = ('⬡', new Vector4(0.60f, 0.85f, 0.95f, 1f)),
        [EquipmentSlotType.Wrists]    = ('◎', new Vector4(0.85f, 0.75f, 0.55f, 1f)),
        [EquipmentSlotType.Fingers]   = ('◈', new Vector4(0.90f, 0.70f, 0.30f, 1f)),
    };

    /// <summary>
    /// 绘制彩色符号图标。通过 TextColored 输出，与周围文字自然对齐。
    /// 符号前后加空格，背景色块用 DrawList 绘于文字后方。
    /// </summary>
    public void DrawIconOnly(EquipmentSlotType slot)
    {
        if (!SlotVisuals.TryGetValue(slot, out var visual))
        {
            ImGui.TextColored(new Vector4(0.3f, 0.7f, 1.0f, 1f), "●");
            return;
        }

        var cursor = ImGui.GetCursorScreenPos();
        var symbol = $" {visual.Symbol} ";
        var textSize = ImGui.CalcTextSize(symbol);
        var padding = new Vector2(2, 2);
        var bgSize = textSize + padding * 2;

        // 背景色块（圆角矩形）
        var drawList = ImGui.GetWindowDrawList();
        var bgColor = visual.Color * 0.25f;
        bgColor.W = 0.6f;
        drawList.AddRectFilled(cursor, cursor + bgSize,
            ImGui.ColorConvertFloat4ToU32(bgColor), 3f);

        // 符号文字 — ImGui 原生 TextColored，自动处理行内对齐
        ImGui.TextColored(visual.Color, symbol);
    }

    /// <summary>
    /// 绘制图标 + 同行文字标签。
    /// </summary>
    public void DrawIconWithLabel(EquipmentSlotType slot, string label)
    {
        DrawIconOnly(slot);
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.7f, 0.85f, 1.0f, 1.0f), label);
    }
}
