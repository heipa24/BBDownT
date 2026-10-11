using BBDownT.Core.Entity;

namespace BBDownT.Tests;

public class OutputCacheTests
{
    [Fact]
    public void RequiresAnExistingNonemptyArtifact()
    {
        using var files = new MediaTestDirectory();
        var path = files.FilePath("title.mp4");
        Assert.False(Program.IsUsableArtifact(path));
        File.WriteAllText(path, "");
        Assert.False(Program.IsUsableArtifact(path));
        File.WriteAllText(path, "completed media");
        Assert.True(Program.IsUsableArtifact(path));
    }

    [Fact]
    public void RawStreamModeDoesNotSkipBecauseAMuxedFileExists()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("title.mp4", "completed media");
        var option = new MyOption { AudioOnly = true, VideoOnly = true };
        Program.HandleConflictingOptions(option);

        Assert.False(Program.ShouldUseMuxedOutputCache(option, path));
        Assert.True(Program.ShouldUseMuxedOutputCache(new(), path));
    }

    [Fact]
    public void MetadataRefreshOnlyAppliesToAnExistingMuxedArtifact()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("title.mp4", "completed media");
        var missing = files.FilePath("missing.mp4");

        Assert.True(Program.ShouldRefreshExistingMetadata(new MyOption { MetadataOnly = true }, path));
        Assert.False(Program.ShouldRefreshExistingMetadata(new(), path));
        Assert.False(Program.ShouldRefreshExistingMetadata(new MyOption { MetadataOnly = true }, missing));
    }

    [Fact]
    public void MetadataRefreshDoesNotApplyWhenMuxingIsSkipped()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("title.mp4", "completed media");

        Assert.False(Program.ShouldRefreshExistingMetadata(new MyOption { MetadataOnly = true, SkipMux = true }, path));
    }
}

// 已存在输出文件若是旧版留下的试看片段(时长不足整集), 不能当作缓存跳过, 要重新下载完整版本
public class PreviewExistingOutputTests
{
    private const int FullEpisodeSeconds = 1419;

    private static ParsedResult Result(int totalSeconds) => new() { TotalDurationSeconds = totalSeconds };

    private static Func<string[], string?> Probe(string duration) =>
        _ => $"{{\"format\":{{\"duration\":\"{duration}\"}}}}";

    [Fact]
    public void ACompleteExistingFileIsKeptAsTheOutputCache()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("title.mp4", "completed media");

        Assert.Null(Program.HandlePreviewExistingOutput(Result(FullEpisodeSeconds), new MyOption(), path, Probe("1419")));
    }

    [Fact]
    public void AShortExistingFileFallsThroughToTheDownloadPath()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("title.mp4", "preview clip");

        Assert.Null(Program.HandlePreviewExistingOutput(Result(FullEpisodeSeconds), new MyOption(), path, Probe("360")));
    }

    [Fact]
    public void MetadataOnlyRefusesToLaunderAPreviewClip()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("title.mp4", "preview clip");

        var outcome = Program.HandlePreviewExistingOutput(Result(FullEpisodeSeconds),
            new MyOption { MetadataOnly = true }, path, Probe("360"));

        Assert.Equal(DownloadPageOutcome.Failed, outcome);
    }

    [Theory]
    [InlineData("1419", false)]  // 与整集一致
    [InlineData("1412", false)]  // 1% 容差内
    [InlineData("1400", true)]   // 超出容差
    [InlineData("360", true)]    // 就是试看片段长度
    public void ThePreviewDecisionFollowsTheFullEpisodeLength(string duration, bool expected)
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("title.mp4", "media");

        // 普通模式即使判为试看也返回 null(继续下载), 用 MetadataOnly 的 Failed 与否观察判定结果
        var outcome = Program.HandlePreviewExistingOutput(Result(FullEpisodeSeconds),
            new MyOption { MetadataOnly = true }, path, Probe(duration));

        Assert.Equal(expected, outcome == DownloadPageOutcome.Failed);
    }

    [Fact]
    public void AnUnknownEpisodeLengthNeverTriggersARedownload()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("title.mp4", "media");

        Assert.Null(Program.HandlePreviewExistingOutput(Result(0), new MyOption(), path, Probe("360")));
    }

    [Fact]
    public void AnUnprobeableFileNeverTriggersARedownload()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("title.mp4", "media");

        Assert.Null(Program.HandlePreviewExistingOutput(Result(FullEpisodeSeconds), new MyOption(), path, _ => null));
    }

    [Theory]
    [InlineData(1419.0, 1419, false)]
    [InlineData(1412.0, 1419, false)]
    [InlineData(1400.0, 1419, true)]
    [InlineData(null, 1419, false)]
    [InlineData(360.0, 0, false)]
    public void TheSharedDurationCheckDegradesConservatively(double? actual, int total, bool expected)
    {
        Assert.Equal(expected, Program.IsDurationShorterThanEpisode(actual, total));
    }
}
