using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BBDownT.Core;
using static BBDownT.Core.Entity.Entity;

namespace BBDownT;

// Owns serial scheduling, archive outcomes and cross-page fault tolerance only.
// Network, files, credentials, selection and per-page retries belong to the
// injected page operation. A page that ultimately fails is recorded and the
// remaining pages still run; failures are reported once at the end.
internal sealed class PageDownloadRunner(
    Func<string, bool> isArchived,
    Action<string> archive,
    Func<int, Task> delayMilliseconds,
    Action<string> log)
{
    internal async Task RunAsync(
        List<Page> pages, bool saveArchives, int delaySeconds,
        Func<Page, Task<DownloadPageOutcome>> downloadPage, IReadOnlyCollection<Page>? allPages = null)
    {
        // A legacy AID cannot prove completion of every page. Only the original
        // unfiltered list can confirm that an AID still represents one page.
        var legacySinglePageAids = allPages?.GroupBy(page => page.aid)
            .Where(group => group.Count() == 1).Select(group => group.Key).ToHashSet()
            ?? new HashSet<string>();
        var failedPages = new List<string>();
        var failedDebugSections = new List<string>();
        foreach (var page in pages)
        {
            // Preserve the existing wait before every selected page, including
            // the first page and pages subsequently skipped by the archive check.
            if (pages.Count > 1 && delaySeconds > 0)
            {
                log($"停顿{delaySeconds}秒...");
                await delayMilliseconds(delaySeconds * 1000);
            }
            log($"开始解析P{page.index}: {page.aid}... ({pages.IndexOf(page) + 1} of {pages.Count})");

            var archiveKey = $"{page.aid}:{page.cid}";
            if (saveArchives && (isArchived(archiveKey)
                || (legacySinglePageAids.Contains(page.aid) && isArchived(page.aid))))
            {
                log($"P{page.index}已下载过, 跳过下载...");
                continue;
            }

            DownloadPageOutcome outcome;
            try
            {
                outcome = await downloadPage(page);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 单个分P失败(如需会员、CDN 持续不可用)不该牵连后面的分P: 记录后继续, 失败分P不写归档。
                log($"P{page.index} 下载失败: {ex.Message}");
                failedPages.Add($"P{page.index}");
                failedDebugSections.Add(FormatStageDebugLines(page.index, Logger.DrainRecentDebugLines()));
                continue;
            }
            if (!outcome.IsSuccessful())
            {
                log($"P{page.index} 下载失败");
                failedPages.Add($"P{page.index}");
                failedDebugSections.Add(FormatStageDebugLines(page.index, Logger.DrainRecentDebugLines()));
                continue;
            }

            if (saveArchives && outcome.ShouldArchive())
                archive(archiveKey);
        }

        // 仍要让调用方(与退出码)知道有分P没跑完, 批量任务不会把部分失败当成功。
        if (failedPages.Count > 0)
            throw new PageFailuresException(
                $"共{failedPages.Count}个分P下载失败: {string.Join(", ", failedPages)}",
                failedDebugSections.Where(section => section.Length > 0).ToList());

        log("任务完成");
    }

    private static string FormatStageDebugLines(int pageIndex, string debugLines) =>
        debugLines.Length == 0 ? "" : $"P{pageIndex} 的调试信息:{Environment.NewLine}{debugLines}";
}

/// <summary>
/// 分P汇总失败。顺带带上各失败分P出事前的调试信息, 顶层补打时才不会错打成缓冲区里最后活动的行
/// (多P场景下最后跑完的往往是成功的分P)。
/// </summary>
internal sealed class PageFailuresException(string message, IReadOnlyList<string> stageDebugSections)
    : InvalidOperationException(message)
{
    internal IReadOnlyList<string> StageDebugSections { get; } = stageDebugSections;
}
