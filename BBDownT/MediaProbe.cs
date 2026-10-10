using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using static BBDownT.Core.Entity.Entity;

namespace BBDownT;

/// <summary>
/// 读取已存在媒体文件的时长/标签/章节, 供 --metadata-only 刷新前校验与刷新时透传使用。
/// 与 MediaOutput(写侧) 相对, 只读; 任何失败都返回 null, 由调用方决定降级还是拒绝。
/// </summary>
internal static class MediaProbe
{
    internal static string FFPROBE = "ffprobe";

    // 容器/流级的技术性标签不该被当成用户数据带回输出文件
    private static readonly HashSet<string> ignoredTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "encoder", "major_brand", "minor_version", "compatible_brands", "handler_name"
    };

    internal sealed record Tag(string Key, string Value);

    internal sealed record StreamTag(string CodecType, int IndexInType, List<Tag> Tags);

    internal sealed record FileTags(double DurationSeconds, List<Tag> FormatTags, List<StreamTag> Streams, List<ViewPoint> Chapters);

    /// <summary>
    /// 取容器总时长(秒); ffprobe 不可用或读不出时返回 null
    /// </summary>
    internal static double? DurationSeconds(string path, Func<string[], string?>? read = null)
    {
        var json = Probe(path, ["format=duration"], read);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ReadDuration(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 一次取回时长、容器标签、各流标签与章节, 免得刷新流程重复拉起 ffprobe
    /// </summary>
    internal static FileTags? ReadTags(string path, Func<string[], string?>? read = null)
    {
        var json = Probe(path,
            ["format=duration", "format_tags", "stream=index,codec_type", "stream_tags", "chapter=start_time,end_time", "chapter_tags"],
            read);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var streams = new List<StreamTag>();
            var perType = new Dictionary<string, int>();
            foreach (var stream in Children(root, "streams"))
            {
                var type = stream.TryGetProperty("codec_type", out var found) ? found.GetString() ?? "" : "";
                perType.TryGetValue(type, out var ordinal);
                perType[type] = ordinal + 1;
                streams.Add(new StreamTag(type, ordinal, TagsOf(stream)));
            }
            var chapters = new List<ViewPoint>();
            foreach (var chapter in Children(root, "chapters"))
            {
                chapters.Add(new ViewPoint
                {
                    title = FirstTagValue(chapter, "title") ?? "",
                    start = (int)Math.Round(ParseSeconds(chapter, "start_time")),
                    end = (int)Math.Round(ParseSeconds(chapter, "end_time"))
                });
            }
            return new FileTags(ReadDuration(root) ?? 0, TagsOf(Child(root, "format")), streams, chapters);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Probe(string path, string[] entries, Func<string[], string?>? read)
    {
        read ??= RunFfprobe;
        var args = new List<string>
        {
            "-v", "error", "-hide_banner",
            "-show_entries", string.Join(":", entries),
            "-of", "json", "--", path
        };
        var output = read(args.ToArray());
        return string.IsNullOrWhiteSpace(output) ? null : output;
    }

    private static double? ReadDuration(JsonElement root)
    {
        var format = Child(root, "format");
        return format.TryGetProperty("duration", out var duration)
            && double.TryParse(duration.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0 ? seconds : null;
    }

    private static List<Tag> TagsOf(JsonElement node)
    {
        var tags = new List<Tag>();
        if (Child(node, "tags") is not { ValueKind: JsonValueKind.Object } found) return tags;
        foreach (var tag in found.EnumerateObject())
        {
            if (ignoredTags.Contains(tag.Name) || tag.Value.ValueKind != JsonValueKind.String) continue;
            tags.Add(new Tag(tag.Name, tag.Value.GetString()!));
        }
        return tags;
    }

    private static string? FirstTagValue(JsonElement node, string key) =>
        TagsOf(node).FirstOrDefault(tag => string.Equals(tag.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    private static double ParseSeconds(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value)
        && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;

    private static JsonElement Child(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value)
        && value.ValueKind != JsonValueKind.Null ? value : default;

    private static IEnumerable<JsonElement> Children(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    private static string? RunFfprobe(string[] args)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = FFPROBE;
            foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            // ffprobe 输出 UTF-8; 不显式指定会按系统代码页解码, 中文标题会乱码
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            if (!process.Start()) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return null;
            }
            return process.ExitCode == 0 ? output.Result : null;
        }
        catch (Exception)
        {
            // ffprobe 缺失/无权限都不该让刷新流程崩成未捕获异常
            return null;
        }
    }
}
