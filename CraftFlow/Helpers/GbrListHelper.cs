using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CraftFlow.Data.Models;

namespace CraftFlow.Helpers;

/// <summary>
/// GBR 采集清单格式生成器。
/// 生成 GBR AutoGatherList.Config.ToBase64() 兼容的压缩 base64 字符串，
/// 可直接在 GBR 的 AutoGather 列表中粘贴导入。
/// 格式参考：GBR GatherBuddy.AutoGather.Lists.AutoGatherList.Config
/// </summary>
public static class GbrListHelper
{
    /// <summary>
    /// 生成 GBR 兼容的压缩 base64 清单字符串。
    /// 用户在 GBR AutoGather 列表中右键 → 粘贴 即可导入。
    /// </summary>
    public static string ToGbrBase64(List<MaterialEntry> materials)
    {
        var gatherable = materials
            .Where(m => m.Source == MaterialSource.Gatherable)
            .ToList();

        if (gatherable.Count == 0)
            return "";

        // 构建 JSON（匹配 GBR AutoGatherList.Config 格式）
        var itemIds = "[" + string.Join(",", gatherable.Select(i => i.ItemId.ToString())) + "]";
        var quantities = "{" + string.Join(",", gatherable.Select(i => $"\"{i.ItemId}\":{i.TotalRequired}")) + "}";
        var enabledItems = "{" + string.Join(",", gatherable.Select(i => $"\"{i.ItemId}\":true")) + "}";

        var json = $@"{{""ItemIds"":{itemIds},""Quantities"":{quantities},""PrefferedLocations"":{{}},""EnabledItems"":{enabledItems},""Name"":""CraftFlow 采集"",""Description"":""由 CraftFlow 导出 ({DateTime.Now:yyyy-MM-dd HH:mm})"",""FolderPath"":"""",""Order"":0,""Enabled"":true,""Fallback"":false}}";

        // 前置版本字节 (5 = CurrentVersion)
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var withVersion = new byte[1 + jsonBytes.Length];
        withVersion[0] = 5;
        Array.Copy(jsonBytes, 0, withVersion, 1, jsonBytes.Length);

        // GZip 压缩
        using var compressedStream = new MemoryStream();
        using (var gzip = new GZipStream(compressedStream, CompressionLevel.Fastest))
        {
            gzip.Write(withVersion, 0, withVersion.Length);
        }
        var compressed = compressedStream.ToArray();

        // Base64
        return Convert.ToBase64String(compressed);
    }

    /// <summary>
    /// 生成可读文本清单（备选）。
    /// </summary>
    public static string GenerateTextList(List<MaterialEntry> materials)
    {
        var gatherable = materials
            .Where(m => m.Source == MaterialSource.Gatherable)
            .ToList();

        if (gatherable.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine("CraftFlow 采集清单 — 共 " + gatherable.Count + " 种材料");
        sb.AppendLine();
        foreach (var mat in gatherable)
            sb.AppendLine(mat.ItemName + "  ×" + mat.TotalRequired);
        sb.AppendLine();
        sb.AppendLine("在 GBR AutoGather 列表右键 → 粘贴导入");
        return sb.ToString();
    }
}
