using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BBDownT.Core;
using static BBDownT.Core.Logger;

namespace BBDownT;

/// <summary>
/// update-downloaded（追更）：按各目录 bbdown.json 记载的来源链接重跑下载，
/// 由既有"输出文件已存在则跳过"的逻辑保证只补新集、只刷已下载条目的产物。
/// </summary>
internal static class UpdateDownloaded
{
    /// <summary>
    /// ExecDir 恒为"含 bbdown.json 的目录的父目录"：默认多P模板把成品写在 videoTitle 子目录下，
    /// 只有在这个父目录里执行，已下完的集才会命中缓存被跳过，而不是另存一份。
    /// </summary>
    internal readonly record struct Targets(string ExecDir, IReadOnlyList<string> MetadataDirs);

    internal static string? Validate(MyOption option)
    {
        if (option.SubOnly) return "--sub-only 不能与 update-downloaded 同时使用, 追更只支持 --danmaku-only、--metadata-only 与 --json-only";
        if (option.CoverOnly) return "--cover-only 不能与 update-downloaded 同时使用, 追更只支持 --danmaku-only、--metadata-only 与 --json-only";
        if (option.VideoOnly || option.AudioOnly) return "--video-only/--audio-only 不能与 update-downloaded 同时使用: 已存在的成品文件会被直接跳过, 没有可更新的内容";
        if (option.DownloadAll) return "--download-all 与 update-downloaded 不能同时使用";
        if (option.Interactive) return "update-downloaded 逐目录自动执行, 不支持 --interactive";
        if (option.DanmakuOnly && option.MetadataOnly) return "--danmaku-only 与 --metadata-only 不能同时使用";
        if (option.JsonOnly && (option.DanmakuOnly || option.MetadataOnly))
            return "--json-only 只更新 bbdown.json, 不能与 --danmaku-only/--metadata-only 同时使用";
        return null;
    }

    /// <summary>
    /// 追更的独占产物刷新模式：只对已下载过的分P动手，不去取新集。
    /// </summary>
    internal static bool IsArtifactOnlyUpdate(MyOption option) => option.DanmakuOnly || option.MetadataOnly;

    /// <summary>
    /// 传入的目录既可以是仓库根，也可以是番剧目录本身（其下有 bbdown.json），两种形态互斥。
    /// </summary>
    internal static Targets ResolveTargets(string dir)
    {
        var target = new DirectoryInfo(Path.GetFullPath(dir));
        if (!target.Exists) throw new DirectoryNotFoundException($"目录不存在: {target.FullName}");

        if (File.Exists(Path.Combine(target.FullName, BbdownMetadataReader.FileName)))
        {
            if (target.Parent is null)
                throw new ArgumentException($"无法确定 {target.FullName} 的上级目录, 请改为传入其上级目录", nameof(dir));
            return new Targets(target.Parent.FullName, new[] { target.FullName });
        }

        // 盘根/文件系统根没有上级目录, 也不该被当成仓库根去枚举整盘
        if (target.Parent is null)
            throw new ArgumentException($"请把目录指向番剧所在的目录而不是根目录: {target.FullName}", nameof(dir));

        var subDirs = Directory.EnumerateDirectories(target.FullName)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new Targets(target.FullName, subDirs);
    }

    /// <summary>
    /// 分P数大于1 是按全集下载的旧证据（旧版 bbdown.json 没有 selectPage 字段），新的用 selectPage=ALL 标记。
    /// 显式 -p ALL 时用户已经表明"所有目录都要"，该筛选整体作废。
    /// </summary>
    internal static bool IsEligibleDirectory(BbdownMetadata metadata, bool ignoreEligibility) =>
        ignoreEligibility || metadata.Pages.Count > 1 || IsAllPages(metadata.SelectPage);

    internal static bool IsAllPages(string? selectPage) =>
        string.Equals(selectPage?.Trim(), "ALL", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 当初下过弹幕（新标记），或每个分P都记载了 xml/ass（旧版推断），就顺带刷新弹幕。
    /// </summary>
    internal static bool ShouldRefreshDanmaku(BbdownMetadata metadata, bool explicitDanmaku) =>
        explicitDanmaku
        || !string.IsNullOrWhiteSpace(metadata.DanmakuFormats)
        || (metadata.Pages.Count > 0 && metadata.Pages.All(page => page.HasDanmakuArtifact));

    internal static IReadOnlyList<BbdownMetadata> Scan(Targets targets, bool ignoreEligibility)
    {
        var found = new List<BbdownMetadata>();
        foreach (var dir in targets.MetadataDirs)
        {
            var metadata = BbdownMetadataReader.TryRead(dir);
            if (metadata is null) continue;
            if (!IsEligibleDirectory(metadata, ignoreEligibility))
            {
                Log($"跳过 {dir}: 只有 {metadata.Pages.Count} 个分P 且未按全集下载 (加 -p ALL 可强制追更)");
                continue;
            }
            found.Add(metadata);
        }
        return found;
    }

    /// <summary>
    /// 按目录记载的用法复原当初的命令行：记录里是全集就重放 -p ALL，记载过弹幕就重放 -dd，
    /// 用户显式给出的同名参数优先。
    /// </summary>
    internal static MyOption BuildChild(MyOption parent, BbdownMetadata metadata, string execDir)
    {
        var child = parent.ForBatchVideo(metadata.Url, execDir);
        if (!parent.SelectPageSpecified && (IsAllPages(metadata.SelectPage) || metadata.Pages.Count > 1))
        {
            // md 来源且没有记录过显式 -p: 当初是"默认全集(排除PV)"下载, 交给下载侧的 md 默认逻辑
            // 以保持排除PV; 其余(旧版无标记的多P / 记录过显式 -p)维持 ALL 重放。
            if (!(string.IsNullOrWhiteSpace(metadata.SelectPage) && Program.IsBangumiMdSource(metadata.Url)))
            {
                child.SelectPage = "ALL";
                child.SelectPageSpecified = true;
            }
        }
        child.DownloadDanmaku = ShouldRefreshDanmaku(metadata, parent.DownloadDanmaku);
        if (IsArtifactOnlyUpdate(parent))
            child.UpdateRecordedPages = metadata.Pages.Select(page => page.Page).ToList();
        // json-only 是迁移语义: 更新全部分P(含新集), 写回当初发现 bbdown.json 的目录
        if (parent.JsonOnly)
            child.BbdownJsonDir = metadata.Dir;
        return child;
    }

    internal static async Task RunAsync(MyOption parent, string dir, Func<MyOption, Task> run, Func<int, Task>? delay = null)
    {
        if (Validate(parent) is { } error) throw new ArgumentException(error);

        // 废弃开关的副作用是往静态文件名模板上追加内容, 留给每个子目录各重放一遍会越叠越长, 先在父选项上归一一次
        Program.HandleDeprecatedOptions(parent);
        Program.HandleConflictingOptions(parent);

        // 必须在任何一次 chdir 之前定住扫描根: 每个子任务都会把 CurrentDirectory 切到 ExecDir
        var scanDir = !string.IsNullOrWhiteSpace(dir)
            ? dir
            : string.IsNullOrWhiteSpace(parent.WorkDir)
                ? Environment.CurrentDirectory
                : Environment.ExpandEnvironmentVariables(parent.WorkDir);
        var targets = ResolveTargets(scanDir);
        var ignoreEligibility = parent.SelectPageSpecified && IsAllPages(parent.SelectPage);

        Log($"追更扫描根目录: {targets.ExecDir}");
        var found = Scan(targets, ignoreEligibility);
        if (found.Count == 0)
        {
            Log("未找到可追更的目录: 需要存在 bbdown.json, 且当初是分P数大于1 或 -p ALL 下载; 加 -p ALL 可放宽筛选。");
            return;
        }

        var wait = delay ?? (milliseconds => Task.Delay(milliseconds));
        var failed = 0;
        for (var index = 0; index < found.Count; index++)
        {
            if (index > 0 && parent.DelayPerVideo > 0) await wait(checked(parent.DelayPerVideo * 1000));

            var metadata = found[index];
            var scope = parent.JsonOnly
                ? "仅更新 bbdown.json"
                : IsArtifactOnlyUpdate(parent)
                    ? $"已记载分P {string.Join(',', metadata.Pages.Select(p => p.Page))}"
                    : "全集";
            var danmaku = parent.JsonOnly ? "不涉及" : ShouldRefreshDanmaku(metadata, parent.DownloadDanmaku) ? "刷新" : "跳过";
            Log($"追更 {index + 1}/{found.Count}: {metadata.Dir} <- {metadata.Url} (弹幕: {danmaku}, 范围: {scope})");
            try
            {
                await run(BuildChild(parent, metadata, targets.ExecDir));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception failure)
            {
                failed++;
                LogError($"追更失败 {metadata.Dir}: {(Config.DEBUG_LOG ? failure.ToString() : failure.Message)}");
            }
        }

        Log($"追更完成：成功 {found.Count - failed}，失败 {failed}");
        if (failed > 0)
            throw new InvalidOperationException($"追更完成，其中 {failed}/{found.Count} 个目录失败；详情见前面的失败记录");
    }
}
