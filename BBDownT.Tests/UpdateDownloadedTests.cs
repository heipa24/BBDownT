using BBDownT.Core.Entity;
using static BBDownT.Core.Entity.Entity;

namespace BBDownT.Tests;

public class UpdateDownloadedTests : IDisposable
{
    private readonly string root;

    public UpdateDownloadedTests()
    {
        root = Path.Combine(Path.GetTempPath(), "bbdown-ud-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    private static BbdownMetadata Metadata(int pages = 2, string? selectPage = null, string? danmakuFormats = null, bool everyPageHasArtifact = false, string url = "https://www.bilibili.com/bangumi/play/ss1")
    {
        var list = Enumerable.Range(1, pages)
            .Select(page => new BbdownPage(page, $"[P{page:D2}]x.mp4", everyPageHasArtifact))
            .ToList();
        return new BbdownMetadata("/anime", url, selectPage, danmakuFormats, list);
    }

    private string CreateBangumiDir(string name, int pageCount, string extraFields = "")
    {
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        var pages = string.Join(',', Enumerable.Range(1, pageCount)
            .Select(i => $$"""{ "page": {{i}}, "file": "[P{{i:D2}}]x.mp4" }"""));
        File.WriteAllText(Path.Combine(dir, "bbdown.json"), $$"""
            {
              "sourceId": "ss1",
              "url": "https://www.bilibili.com/bangumi/play/ss1",
              "title": "{{name}}",
              {{extraFields}}
              "pages": [{{pages}}]
            }
            """);
        return dir;
    }

    [Fact]
    public void ResolveTargets_BangumiDirectory_TargetsItselfWithParentAsExecDir()
    {
        var dir = CreateBangumiDir("某番", 2);

        var targets = UpdateDownloaded.ResolveTargets(dir);

        Assert.Equal(Path.GetFullPath(root), targets.ExecDir);
        Assert.Equal(new[] { dir }, targets.MetadataDirs);
    }

    [Fact]
    public void ResolveTargets_LibraryDirectory_TargetsOnlyDirectSubdirectories()
    {
        CreateBangumiDir("番A", 2);
        var deep = Path.Combine(root, "番B", "子目录");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "bbdown.json"), """{ "url": "BV1xx411c7mD", "pages": [] }""");

        var targets = UpdateDownloaded.ResolveTargets(root);

        Assert.Equal(Path.GetFullPath(root), targets.ExecDir);
        Assert.Equal(2, targets.MetadataDirs.Count);
        Assert.DoesNotContain(deep, targets.MetadataDirs);
    }

    [Fact]
    public void ResolveTargets_MissingDirectory_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => UpdateDownloaded.ResolveTargets(Path.Combine(root, "不存在")));
    }

    [Fact]
    public void ResolveTargets_DriveRoot_Throws()
    {
        Assert.Throws<ArgumentException>(() => UpdateDownloaded.ResolveTargets(Directory.GetDirectoryRoot(root)));
    }

    [Theory]
    [InlineData(2, null, null, true)]
    [InlineData(1, "ALL", null, true)]
    [InlineData(1, "all", null, true)]
    [InlineData(1, " ALL ", null, true)]
    [InlineData(1, null, "xml,ass", false)]
    [InlineData(1, null, null, false)]
    [InlineData(0, null, null, false)]
    [InlineData(1, "3-5", null, false)]
    public void IsEligibleDirectory_MatchesRecordedUsage_AndCanBeOverridden(int pages, string? selectPage, string? danmakuFormats, bool expected)
    {
        Assert.Equal(expected, UpdateDownloaded.IsEligibleDirectory(Metadata(pages, selectPage, danmakuFormats), ignoreEligibility: false));
        Assert.True(UpdateDownloaded.IsEligibleDirectory(Metadata(pages, selectPage, danmakuFormats), ignoreEligibility: true));
    }

    [Theory]
    [InlineData(false, null, null, false, false)]
    [InlineData(false, null, null, true, true)]
    [InlineData(false, null, "xml", false, true)]
    [InlineData(false, "3-5", null, false, false)]
    [InlineData(true, "3-5", null, false, true)]
    public void ShouldRefreshDanmaku_PrefersMarkerThenLegacyArtifacts(bool explicitDd, string? selectPage, string? danmakuFormats, bool everyPageHasArtifact, bool expected)
    {
        var metadata = Metadata(2, selectPage, danmakuFormats, everyPageHasArtifact);

        Assert.Equal(expected, UpdateDownloaded.ShouldRefreshDanmaku(metadata, explicitDd));
    }

    [Fact]
    public void Scan_SkipsDirectoryWithoutMetadata()
    {
        CreateBangumiDir("番A", 2);
        Directory.CreateDirectory(Path.Combine(root, "无元数据"));

        var found = UpdateDownloaded.Scan(UpdateDownloaded.ResolveTargets(root), ignoreEligibility: false);

        Assert.Single(found);
        Assert.Equal("番A", Path.GetFileName(found[0].Dir));
    }

    [Fact]
    public void Scan_SinglePageDirectoryIsIgnoredUnlessEligibilityIgnored()
    {
        CreateBangumiDir("单P", 1);

        Assert.Empty(UpdateDownloaded.Scan(UpdateDownloaded.ResolveTargets(root), ignoreEligibility: false));
        Assert.Single(UpdateDownloaded.Scan(UpdateDownloaded.ResolveTargets(root), ignoreEligibility: true));
    }

    [Fact]
    public void BuildChild_UsesExecDirAndRecordedUrl()
    {
        var metadata = Metadata(2, "ALL");

        var child = UpdateDownloaded.BuildChild(new MyOption(), metadata, root);

        Assert.Equal(root, child.WorkDir);
        Assert.Equal("https://www.bilibili.com/bangumi/play/ss1", child.Url);
        Assert.False(child.DownloadAll);
    }

    [Fact]
    public void BuildChild_ReplaysSelectPageAll_ForMarkedAndLegacyMultiPageDirs()
    {
        var marked = UpdateDownloaded.BuildChild(new MyOption(), Metadata(1, "ALL"), root);
        var legacy = UpdateDownloaded.BuildChild(new MyOption(), Metadata(3), root);

        Assert.Equal("ALL", marked.SelectPage);
        Assert.True(marked.SelectPageSpecified);
        Assert.Equal("ALL", legacy.SelectPage);
    }

    [Fact]
    public void BuildChild_MdSourceWithoutExplicitRecord_DelegatesToMdDefault()
    {
        var child = UpdateDownloaded.BuildChild(
            new MyOption(), Metadata(5, url: "https://www.bilibili.com/bangumi/media/md28230042"), root);

        Assert.Equal("", child.SelectPage);
        Assert.False(child.SelectPageSpecified);
    }

    [Fact]
    public void BuildChild_MdSourceWithExplicitAllRecord_ReplaysAll()
    {
        var child = UpdateDownloaded.BuildChild(
            new MyOption(), Metadata(6, "ALL", url: "https://www.bilibili.com/bangumi/media/md28230042"), root);

        Assert.Equal("ALL", child.SelectPage);
        Assert.True(child.SelectPageSpecified);
    }

    [Fact]
    public void BuildChild_KeepsExplicitSelectPage()
    {
        var parent = new MyOption { SelectPage = "LAST", SelectPageSpecified = true };

        var child = UpdateDownloaded.BuildChild(parent, Metadata(4, "ALL"), root);

        Assert.Equal("LAST", child.SelectPage);
    }

    [Fact]
    public void BuildChild_TurnsDanmakuOnForMarkedDir_AndOffForPlainDir()
    {
        var marked = UpdateDownloaded.BuildChild(new MyOption(), Metadata(2, "ALL", "xml,ass"), root);
        var plain = UpdateDownloaded.BuildChild(new MyOption(), Metadata(2, "ALL"), root);
        var forced = UpdateDownloaded.BuildChild(new MyOption { DownloadDanmaku = true }, Metadata(2, "ALL"), root);

        Assert.True(marked.DownloadDanmaku);
        Assert.False(plain.DownloadDanmaku);
        Assert.True(forced.DownloadDanmaku);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void BuildChild_RestrictsToRecordedPagesOnlyInExclusiveUpdateModes(bool danmakuOnly, bool metadataOnly)
    {
        var parent = new MyOption { DanmakuOnly = danmakuOnly, MetadataOnly = metadataOnly };

        var child = UpdateDownloaded.BuildChild(parent, Metadata(3, "ALL"), root);

        Assert.Equal(new[] { 1, 2, 3 }, child.UpdateRecordedPages);
    }

    [Fact]
    public void BuildChild_WithoutExclusiveMode_LeavesRecordedPagesUnset()
    {
        var child = UpdateDownloaded.BuildChild(new MyOption(), Metadata(3, "ALL"), root);

        Assert.Null(child.UpdateRecordedPages);
    }

    [Theory]
    [InlineData(nameof(MyOption.SubOnly))]
    [InlineData(nameof(MyOption.CoverOnly))]
    [InlineData(nameof(MyOption.VideoOnly))]
    [InlineData(nameof(MyOption.AudioOnly))]
    [InlineData(nameof(MyOption.DownloadAll))]
    [InlineData(nameof(MyOption.Interactive))]
    public void Validate_RejectsConflictingOptions(string property)
    {
        var option = new MyOption();
        typeof(MyOption).GetProperty(property)!.SetValue(option, true);

        Assert.NotNull(UpdateDownloaded.Validate(option));
    }

    [Fact]
    public void Validate_RejectsBothExclusiveUpdateModesAtOnce()
    {
        Assert.NotNull(UpdateDownloaded.Validate(new MyOption { DanmakuOnly = true, MetadataOnly = true }));
    }

    [Fact]
    public void Validate_AllowsDryRunAndPlainUpdate()
    {
        Assert.Null(UpdateDownloaded.Validate(new MyOption()));
        Assert.Null(UpdateDownloaded.Validate(new MyOption { OnlyShowInfo = true }));
        Assert.Null(UpdateDownloaded.Validate(new MyOption { DanmakuOnly = true }));
        Assert.Null(UpdateDownloaded.Validate(new MyOption { MetadataOnly = true }));
        Assert.Null(UpdateDownloaded.Validate(new MyOption { JsonOnly = true }));
    }

    [Fact]
    public void Validate_RejectsJsonOnlyWithArtifactRefreshModes()
    {
        Assert.NotNull(UpdateDownloaded.Validate(new MyOption { JsonOnly = true, DanmakuOnly = true }));
        Assert.NotNull(UpdateDownloaded.Validate(new MyOption { JsonOnly = true, MetadataOnly = true }));
    }

    [Fact]
    public void BuildChild_JsonOnly_TargetsTheMetadataDirectory_AndKeepsAllPages()
    {
        var child = UpdateDownloaded.BuildChild(new MyOption { JsonOnly = true }, Metadata(3, "ALL"), root);

        Assert.True(child.JsonOnly);
        Assert.Equal("/anime", child.BbdownJsonDir);
        Assert.Null(child.UpdateRecordedPages);
    }

    [Fact]
    public async Task RunAsync_RunsEveryEligibleDirectory_AndAggregatesFailures()
    {
        CreateBangumiDir("番A", 2);
        CreateBangumiDir("番B", 3);
        var runs = new List<MyOption>();
        var failures = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateDownloaded.RunAsync(
            new MyOption(),
            root,
            option =>
            {
                runs.Add(option);
                // 抛异常走的是"下载失败"分支，先记录再抛，保证两个目录都被尝试过
                return ++failures == 1 ? Task.FromException(new Exception("接口失败")) : Task.CompletedTask;
            },
            milliseconds => Task.CompletedTask));

        Assert.Equal(2, runs.Count);
        Assert.All(runs, option => Assert.Equal("ALL", option.SelectPage));
    }

    [Fact]
    public async Task RunAsync_UsesDelayPerVideoBetweenDirectories()
    {
        CreateBangumiDir("番A", 2);
        CreateBangumiDir("番B", 2);
        var waits = new List<int>();

        await UpdateDownloaded.RunAsync(
            new MyOption { DelayPerVideo = 7 },
            root,
            _ => Task.CompletedTask,
            milliseconds => { waits.Add(milliseconds); return Task.CompletedTask; });

        Assert.Equal(new[] { 7000 }, waits);
    }

    [Fact]
    public async Task RunAsync_RejectsConflictingOptionBeforeTouchingTheFileSystem()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => UpdateDownloaded.RunAsync(
            new MyOption { SubOnly = true }, "/不存在的目录", _ => Task.CompletedTask));
    }

    [Fact]
    public async Task RunAsync_NoEligibleDirectory_RunsNothing()
    {
        CreateBangumiDir("单P", 1);
        var runs = 0;

        await UpdateDownloaded.RunAsync(new MyOption(), root, _ => { runs++; return Task.CompletedTask; });

        Assert.Equal(0, runs);
    }

    [Fact]
    public void RetainRecordedPages_DropsPagesTheApiNoLongerReports()
    {
        var info = new VInfo
        {
            Title = "t",
            Desc = "",
            Pic = "",
            PubTime = 0,
            PagesInfo = new List<Page> { new(1, "1", "1", "", "p1", 0, "", 0), new(2, "1", "2", "", "p2", 0, "", 0) }
        };

        var selected = Program.RetainRecordedPages(info, new[] { 1, 2, 7 });

        Assert.Equal(new[] { "1", "2" }, selected);
    }
}
