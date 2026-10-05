using static BBDownT.Core.Entity.Entity;

namespace BBDownT.Tests;

public class MetadataRefreshTests
{
    private static List<string> Invoke(string media, string staged, string pic = "", List<Subtitle>? subs = null,
        List<ViewPoint>? points = null, bool audioOnly = false, bool simplyMux = false)
    {
        var captured = new List<string>();
        BBDownTMuxer.UpdateMetadata(media, staged,
            desc: "简介", title: "标题", author: "UP主", episodeId: "",
            pic: pic, lang: "", subs: subs, audioOnly: audioOnly, points: points, pubTime: 0, simplyMux: simplyMux,
            runFfmpeg: args =>
            {
                captured.AddRange(args);
                return 0;
            });
        return captured;
    }

    private static bool HasPair(List<string> args, string first, string second)
    {
        // 参数名可能重复出现(如多个 -metadata / -map), 需要逐个相邻对匹配
        for (var i = 0; i + 1 < args.Count; i++)
        {
            if (args[i] == first && args[i + 1] == second) return true;
        }
        return false;
    }

    [Fact]
    public void RefreshCopiesMediaStreamsAndRewritesContainerTags()
    {
        using var files = new MediaTestDirectory();
        var media = files.Write("title.mp4", "media");

        var args = Invoke(media, files.FilePath("staged.mp4"));

        Assert.True(HasPair(args, "-i", media));
        Assert.True(HasPair(args, "-c:v", "copy"));
        Assert.True(HasPair(args, "-c:a", "copy"));
        Assert.True(HasPair(args, "-map_metadata:g", "-1"));
        Assert.True(HasPair(args, "-map_metadata:s", "-1"));
        Assert.True(HasPair(args, "-metadata", "title=标题"));
        Assert.True(HasPair(args, "-metadata", "comment=简介"));
        Assert.True(HasPair(args, "-metadata", "description=简介"));
        Assert.True(HasPair(args, "-metadata", "artist=UP主"));
    }

    [Fact]
    public void SimplyMuxRefreshKeepsOnlyTheCacheFriendlyTags()
    {
        using var files = new MediaTestDirectory();
        var media = files.Write("title.mp4", "media");

        var args = Invoke(media, files.FilePath("staged.mp4"), simplyMux: true);

        Assert.Contains("-map_metadata:g", args);
        Assert.Contains("-map_metadata:s", args);
        Assert.DoesNotContain("title=标题", args);
        Assert.True(HasPair(args, "-c:v", "copy"));
    }

    [Fact]
    public void WithoutArtworkEveryExistingVideoStreamIsPreserved()
    {
        using var files = new MediaTestDirectory();
        var media = files.Write("title.mp4", "media");

        var args = Invoke(media, files.FilePath("staged.mp4"));

        Assert.True(HasPair(args, "-map", "0:v?"));
        Assert.DoesNotContain("0:v:0?", args);
        Assert.True(HasPair(args, "-map", "0:s?"));
    }

    [Fact]
    public void NewArtworkReplacesTheEmbeddedCover()
    {
        using var files = new MediaTestDirectory();
        var media = files.Write("title.mp4", "media");
        var cover = files.Write("cover.jpg", "cover");

        var args = Invoke(media, files.FilePath("staged.mp4"), pic: cover);

        // 只保留主视频流, 旧封面被丢弃后再附加新封面
        Assert.True(HasPair(args, "-map", "0:v:0?"));
        Assert.DoesNotContain("0:v?", args);
        Assert.Contains(cover, args);
        Assert.True(HasPair(args, "-disposition:v:1", "attached_pic"));
    }

    [Fact]
    public void AudioOnlyArtworkBecomesTheOnlyVideoStream()
    {
        using var files = new MediaTestDirectory();
        var media = files.Write("title.m4a", "media");
        var cover = files.Write("cover.jpg", "cover");

        var args = Invoke(media, files.FilePath("staged.m4a"), pic: cover, audioOnly: true);

        Assert.DoesNotContain("0:v:0?", args);
        Assert.DoesNotContain("0:v?", args);
        Assert.True(HasPair(args, "-disposition:v:0", "attached_pic"));
    }

    [Fact]
    public void NewSubtitlesReplaceExistingOnesAndAreConverted()
    {
        using var files = new MediaTestDirectory();
        var media = files.Write("title.mp4", "media");
        var subtitle = files.Write("zh.srt", "1\n00:00:00,000 --> 00:00:01,000\n字幕\n");

        var args = Invoke(media, files.FilePath("staged.mp4"),
            subs: [new Subtitle { lan = "zh-CN", url = "https://example.com/sub", path = subtitle }]);

        // 有新字幕时不保留旧字幕流
        Assert.DoesNotContain("0:s?", args);
        Assert.Contains(subtitle, args);
        Assert.True(HasPair(args, "-c:s", "mov_text"));
        Assert.Contains("language=chi", args);
    }

    [Fact]
    public void EmptySubtitleFilesAreIgnored()
    {
        using var files = new MediaTestDirectory();
        var media = files.Write("title.mp4", "media");
        var empty = files.Write("empty.srt", "");

        var args = Invoke(media, files.FilePath("staged.mp4"),
            subs: [new Subtitle { lan = "zh-CN", url = "https://example.com/sub", path = empty }]);

        Assert.DoesNotContain(empty, args);
        Assert.True(HasPair(args, "-map", "0:s?"));
        Assert.True(HasPair(args, "-c:s", "copy"));
    }

    [Fact]
    public void ChaptersAreMappedFromATemporaryFileThatIsRemoved()
    {
        using var files = new MediaTestDirectory();
        var media = files.Write("title.mp4", "media");
        var before = Directory.GetFiles(Path.GetTempPath(), "bbdownt-chapters-*").Length;

        var args = Invoke(media, files.FilePath("staged.mp4"),
            points: [new ViewPoint { title = "章节", start = 0, end = 10 }]);

        Assert.Contains("-map_chapters", args);
        Assert.True(HasPair(args, "-map_chapters", "1"));   // 章节是 1 号输入(0 号是媒体文件)
        FfmpegArgumentOrder.AssertSelectorsFollowInputs(args);
        Assert.Equal(before, Directory.GetFiles(Path.GetTempPath(), "bbdownt-chapters-*").Length);
    }

    [Theory]
    [InlineData(false, false, false, false)] // 只有媒体文件
    [InlineData(true, false, false, false)]  // 封面
    [InlineData(false, true, false, false)]  // 字幕
    [InlineData(false, false, true, false)]  // 章节
    [InlineData(true, true, true, false)]    // 封面 + 字幕 + 章节
    [InlineData(true, false, true, true)]    // audioOnly + 封面 + 章节
    public void StreamSelectorsAreAlwaysPlacedAfterTheLastInput(bool withPic, bool withSub, bool withChapters, bool audioOnly)
    {
        using var files = new MediaTestDirectory();
        var media = files.Write(audioOnly ? "title.m4a" : "title.mp4", "media");
        var pic = withPic ? files.Write("cover.jpg", "cover") : "";
        List<Subtitle>? subs = withSub
            ? [new Subtitle { lan = "zh-CN", url = "https://example.com/sub", path = files.Write("zh.srt", "1\n00:00:00,000 --> 00:00:01,000\n字幕\n") }]
            : null;
        List<ViewPoint>? points = withChapters ? [new ViewPoint { title = "章节", start = 0, end = 10 }] : null;

        var args = Invoke(media, files.FilePath(audioOnly ? "staged.m4a" : "staged.mp4"),
            pic: pic, subs: subs, points: points, audioOnly: audioOnly);

        FfmpegArgumentOrder.AssertSelectorsFollowInputs(args);
    }

    [Fact]
    public void CombinedRefreshUsesTheExpectedInputThenMappingOrder()
    {
        using var files = new MediaTestDirectory();
        var media = files.Write("title.mp4", "media");
        var cover = files.Write("cover.jpg", "cover");
        var subtitle = files.Write("zh.srt", "1\n00:00:00,000 --> 00:00:01,000\n字幕\n");

        var args = Invoke(media, files.FilePath("staged.mp4"), pic: cover,
            subs: [new Subtitle { lan = "zh-CN", url = "https://example.com/sub", path = subtitle }],
            points: [new ViewPoint { title = "章节", start = 0, end = 10 }]);

        // -i 必须连续且顺序为 媒体 -> 封面 -> 字幕 -> 章节
        var inputs = FfmpegArgumentOrder.Inputs(args);
        Assert.Equal(4, inputs.Count);
        Assert.Equal(new[] { media, cover, subtitle }, inputs.Take(3));
        Assert.StartsWith("bbdownt-chapters-", Path.GetFileName(inputs[3]));

        // 映射整体在 -i 之后, 且保持 视频/音频/封面/字幕 的相对顺序
        Assert.Equal(new[] { "0:v:0?", "0:a?", "1", "2" }, FfmpegArgumentOrder.Mapped(args));
        Assert.Equal(new[] { "3" }, FfmpegArgumentOrder.ChapterMappings(args));
        FfmpegArgumentOrder.AssertSelectorsFollowInputs(args);
    }

    [Fact]
    public void MissingArtworkIsNeverAddedAsAnInput()
    {
        using var files = new MediaTestDirectory();
        var media = files.Write("title.mp4", "media");
        var missing = files.FilePath("missing.jpg");

        var args = Invoke(media, files.FilePath("staged.mp4"), pic: missing);

        Assert.DoesNotContain(missing, args);
        Assert.True(HasPair(args, "-map", "0:v?"));
    }

    [Fact]
    public void TheOriginalFileIsLeftUntouchedWhenFfmpegFails()
    {
        using var files = new MediaTestDirectory();
        var media = files.Write("source.mp4", "media");
        var destination = files.Write("title.mp4", "media");

        var refreshed = MediaOutput.Write(destination,
            staged => BBDownTMuxer.UpdateMetadata(media, staged, runFfmpeg: _ => 1));

        Assert.False(refreshed);
        Assert.Equal("media", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(files.Root, "*.partial*"));
    }

    [Fact]
    public void SuccessfulRefreshReplacesTheDestinationAtomically()
    {
        using var files = new MediaTestDirectory();
        var media = files.Write("source.mp4", "media");
        var destination = files.Write("title.mp4", "old media");

        var refreshed = MediaOutput.Write(destination, staged =>
        {
            File.WriteAllText(staged, "new media");
            return BBDownTMuxer.UpdateMetadata(media, staged, runFfmpeg: _ => 0);
        });

        Assert.True(refreshed);
        Assert.Equal("new media", File.ReadAllText(destination));
        Assert.Equal("media", File.ReadAllText(media));
    }
}
