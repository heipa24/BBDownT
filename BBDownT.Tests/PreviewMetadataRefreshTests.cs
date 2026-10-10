using BBDownT.Core.Entity;
using static BBDownT.Core.Entity.Entity;

namespace BBDownT.Tests;

// 接口只返回试看片段时, --metadata-only 能不能刷新的判定(不落网络, ffprobe 由 readProbe 注入)
public class PreviewMetadataRefreshTests
{
    private const int FullEpisodeSeconds = 1419;
    private const string SeasonTitle = "天使降临到我身边";
    private const long EpisodePubTime = 1547560800;

    private static Page Episode(string pageTitle = "out") =>
        new(2, "40813785", "71685308", "259654", pageTitle, 0, "1920x1080", EpisodePubTime);

    private static ParsedResult Preview(bool durl = true, int totalSeconds = FullEpisodeSeconds)
    {
        var result = new ParsedResult { IsPreviewOnly = true, TotalDurationSeconds = totalSeconds };
        if (durl) result.Clips.Add("https://cdn.test/preview.mp4");
        return result;
    }

    // 模板写成绝对路径, 让计算出的输出文件落在测试目录里
    private static string RootedFormat(MediaTestDirectory files, string suffix = "") =>
        files.Root.Replace('\\', '/') + "/<pageTitle>" + suffix;

    private static Func<string[], string?> Probe(string duration) =>
        _ => $"{{\"format\":{{\"duration\":\"{duration}\"}}}}";

    private static Program.PreviewRefreshCheck Check(MyOption option, ParsedResult preview,
        MediaTestDirectory files, Func<string[], string?>? probe = null, string format = "", string? language = null) =>
        Program.CheckPreviewMetadataRefresh(option, preview, Episode(), 13, "WEB",
            format.Length > 0 ? format : RootedFormat(files), SeasonTitle, EpisodePubTime,
            language, probe ?? Probe("1419"));

    [Fact]
    public void ANonMetadataRunStillStopsWithoutAnExtraReason()
    {
        using var files = new MediaTestDirectory();
        files.Write("out.mp4", "complete episode");

        var check = Check(new MyOption(), Preview(), files);

        Assert.False(check.Verified);
        Assert.Equal("", check.Reason);
        Assert.True(Program.StopPreviewDownload(new MyOption(), Preview(), check.Verified));
    }

    [Fact]
    public void AVerifiedCompleteExistingFileAllowsThePreviewMetadataRefresh()
    {
        using var files = new MediaTestDirectory();
        files.Write("out.mp4", "complete episode");

        var check = Check(new MyOption { MetadataOnly = true }, Preview(), files);

        Assert.True(check.Verified);
        Assert.Equal("", check.Reason);
        Assert.False(Program.StopPreviewDownload(new MyOption { MetadataOnly = true }, Preview(), check.Verified));
    }

    [Fact]
    public void AMissingExistingFileIsNeverTreatedAsARefreshableEpisode()
    {
        using var files = new MediaTestDirectory();

        var check = Check(new MyOption { MetadataOnly = true }, Preview(), files);

        Assert.False(check.Verified);
        Assert.Contains("试看片段不会写入输出文件", check.Reason);
    }

    [Fact]
    public void AFileAsShortAsThePreviewClipIsNotRelabelledAsACompleteEpisode()
    {
        using var files = new MediaTestDirectory();
        files.Write("out.mp4", "preview clip");

        var check = Check(new MyOption { MetadataOnly = true }, Preview(), files, Probe("360"));

        Assert.False(check.Verified);
        Assert.Contains("更像试看片段", check.Reason);
    }

    [Theory]
    [InlineData("1419", true)]   // 与整集一致
    [InlineData("1412", true)]   // 1% 容差内
    [InlineData("1400", false)]  // 超出容差
    [InlineData("360", false)]   // 就是试看片段长度
    public void TheDurationCheckFollowsTheFullEpisodeLength(string duration, bool expected)
    {
        using var files = new MediaTestDirectory();
        files.Write("out.mp4", "media");

        var check = Check(new MyOption { MetadataOnly = true }, Preview(), files, Probe(duration));

        Assert.Equal(expected, check.Verified);
    }

    [Fact]
    public void TheRefreshIsRefusedWhenFfprobeCannotVerifyTheOriginal()
    {
        using var files = new MediaTestDirectory();
        files.Write("out.mp4", "media");

        var check = Check(new MyOption { MetadataOnly = true }, Preview(), files, _ => null);

        Assert.False(check.Verified);
        Assert.Contains("ffprobe", check.Reason);
    }

    [Fact]
    public void TheRefreshIsRefusedWhenTheResponseOmitsTheFullLength()
    {
        using var files = new MediaTestDirectory();
        files.Write("out.mp4", "media");

        var check = Check(new MyOption { MetadataOnly = true }, Preview(totalSeconds: 0), files);

        Assert.False(check.Verified);
        Assert.Contains("整集时长", check.Reason);
    }

    [Fact]
    public void AFilePatternUsingTrackPlaceholdersCannotIdentifyTheOriginalFile()
    {
        using var files = new MediaTestDirectory();
        files.Write("out.mp4", "media");

        var check = Check(new MyOption { MetadataOnly = true }, Preview(), files,
            format: RootedFormat(files, "-<dfn>"));

        Assert.False(check.Verified);
        Assert.Contains("轨道信息", check.Reason);
        Assert.True(Program.UsesTrackPlaceholder("<videoTitle>/<dfn>"));
        Assert.True(Program.UsesTrackPlaceholder("<videoTitle>/<videoBandwidth>"));
        Assert.False(Program.UsesTrackPlaceholder("<videoTitle>/[P<pageNumberWithZero>]<pageTitle>"));
    }

    [Fact]
    public void SkipMuxStillRefusesThePreviewRefresh()
    {
        using var files = new MediaTestDirectory();
        files.Write("out.mp4", "media");

        var check = Check(new MyOption { MetadataOnly = true, SkipMux = true }, Preview(), files);

        Assert.False(check.Verified);
    }

    [Fact]
    public void TheRefreshPathMatchesTheBranchThatWouldWriteTheFile()
    {
        using var files = new MediaTestDirectory();

        // DASH 分支会给带配音的输出加语言后缀, DURL 分支不会
        var dash = Program.BuildMetadataRefreshPath(new MyOption { AudioOnly = true },
            Preview(durl: false), RootedFormat(files), SeasonTitle, Episode(), 13, "WEB", EpisodePubTime, "jpn");
        var durl = Program.BuildMetadataRefreshPath(new MyOption { AudioOnly = true },
            Preview(), RootedFormat(files), SeasonTitle, Episode(), 13, "WEB", EpisodePubTime, "jpn");

        Assert.EndsWith("out.audio-jpn.m4a", dash.Replace('\\', '/'));
        Assert.EndsWith("out.m4a", durl.Replace('\\', '/'));
        Assert.DoesNotContain("audio-jpn", durl);
    }

    [Fact]
    public void TheMediaFallbackGateRefusesToWriteAPreviewStream()
    {
        Assert.True(Program.StopPreviewMediaDownload(Preview()));
        Assert.False(Program.StopPreviewMediaDownload(new ParsedResult()));
    }
}
