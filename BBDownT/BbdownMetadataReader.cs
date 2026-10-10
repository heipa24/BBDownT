using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace BBDownT;

/// <summary>
/// bbdown.json 里单个分P的记载。
/// </summary>
internal sealed record BbdownPage(int Page, string? File, bool HasDanmakuArtifact);

/// <summary>
/// 一个输出目录的 bbdown.json 快照。
/// </summary>
internal sealed record BbdownMetadata(
    string Dir,
    string Url,
    string? SelectPage,
    string? DanmakuFormats,
    IReadOnlyList<BbdownPage> Pages);

/// <summary>
/// 读取 bbdown.json。与写出端(Program.WriteBbdownMetadata)一样走 JsonNode DOM, 不引入序列化上下文。
/// 任何读取失败都只当作"这个目录没有元数据"处理, 绝不让扫描中断。
/// </summary>
internal static class BbdownMetadataReader
{
    internal const string FileName = "bbdown.json";

    internal static BbdownMetadata? TryRead(string dir)
    {
        try
        {
            var path = Path.Combine(dir, FileName);
            if (!File.Exists(path)) return null;
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root) return null;

            // 旧版可能只有 sourceId，也可能只有 url，两者任取其一即可复原来源
            var url = ReadString(root, "url") ?? ReadString(root, "sourceId");
            if (url is null) return null;

            var pages = new List<BbdownPage>();
            var seen = new HashSet<int>();
            if (root["pages"] is JsonArray array)
            {
                foreach (var node in array)
                {
                    if (node is not JsonObject pageObj) continue;
                    if (pageObj["page"] is not JsonValue pageValue || !pageValue.TryGetValue<int>(out var page)) continue;
                    if (!seen.Add(page)) continue;
                    pages.Add(new BbdownPage(
                        page,
                        ReadString(pageObj, "file"),
                        ReadString(pageObj, "xml") is not null || ReadString(pageObj, "ass") is not null));
                }
            }

            return new BbdownMetadata(dir, url, ReadString(root, "selectPage"), ReadString(root, "danmakuFormats"), pages);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ReadString(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
}
