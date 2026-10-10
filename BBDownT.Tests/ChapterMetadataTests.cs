using System.Diagnostics;
using System.Text;
using System.Text.Json;
using static BBDownT.Core.Entity.Entity;

namespace BBDownT.Tests;

public class ChapterMetadataTests
{
    [Fact]
    public void BothMetadataFormats_PreserveChineseChapterNamesAndTimes()
    {
        List<ViewPoint> points =
        [
            new() { title = "2021甲卷", start = 0, end = 151 },
            new() { title = "片尾", start = 151, end = 200 }
        ];

        var ffmpeg = BBDownTUtil.GetFFmpegMetaString(points);
        var mp4box = BBDownTUtil.GetMp4boxMetaString(points);

        Assert.Contains("START=151000", ffmpeg);
        Assert.Contains("END=200000", ffmpeg);
        Assert.Contains("title=2021甲卷", ffmpeg);
        Assert.Contains("title=片尾", ffmpeg);
        Assert.Equal($"00:00:00 2021甲卷{Environment.NewLine}00:02:31 片尾{Environment.NewLine}", mp4box);
    }

    [Fact]
    public void FFmpegMetadata_EscapesSpecialCharactersAndLineBreaks()
    {
        var metadata = BBDownTUtil.GetFFmpegMetaString(
            [new ViewPoint { title = "A=B;#C\\D\r\n第二行", start = 0, end = 10 }]);

        Assert.StartsWith(";FFMETADATA1" + Environment.NewLine, metadata);
        Assert.Contains("title=A\\=B\\;\\#C\\\\D\\\n第二行", metadata);
    }

    [Fact]
    public void FFmpegMetadata_ConvertsLongTimesWithoutIntegerOverflow()
    {
        var metadata = BBDownTUtil.GetFFmpegMetaString(
            [new ViewPoint { title = "long recording", start = 3_000_000, end = 3_000_001 }]);

        Assert.Contains("START=3000000000", metadata);
        Assert.Contains("END=3000001000", metadata);
    }

    [Fact]
    public void FFmpegMetadata_NullTitleKeepsAnEmptyTitle()
    {
        var metadata = BBDownTUtil.GetFFmpegMetaString(
            [new ViewPoint { title = null!, start = 0, end = 10 }]);

        Assert.Contains("title=" + Environment.NewLine, metadata);
    }

    // Supply an empty directory in BBDOWN_CHAPTER_TEST_DIR. The caller owns
    // cleanup of audio.m4a, output.m4a and chapters after inspecting the result.
    [ChapterMuxFact]
    public void FFmpegMux_PreservesChapterTitlesWithoutInheritingInputMetadata()
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("BBDOWN_CHAPTER_TEST_DIR")!);
        Assert.True(Directory.Exists(root));
        var audio = Path.Combine(root, "audio.m4a");
        var output = Path.Combine(root, "output.m4a");
        Assert.False(File.Exists(audio));
        Assert.False(File.Exists(output));
        Assert.False(File.Exists(Path.Combine(root, "chapters")));

        var ffmpeg = BBDownTUtil.FindExecutable("ffmpeg")!;
        var ffprobe = BBDownTUtil.FindExecutable("ffprobe")!;
        RunTool(ffmpeg, "-v", "error", "-f", "lavfi", "-i", "anullsrc=r=8000:cl=mono",
            "-t", "2", "-c:a", "aac", "-threads", "1",
            "-metadata", "title=source title",
            "-metadata:s:a:0", "language=fra",
            "-metadata:s:a:0", "handler_name=source handler", audio);
        List<ViewPoint> points =
        [
            new() { title = "A=B;#C\\D\n第二行", start = 0, end = 1 },
            new() { title = "片尾", start = 1, end = 2 }
        ];

        var previousFfmpeg = BBDownTMuxer.FFMPEG;
        try
        {
            BBDownTMuxer.FFMPEG = ffmpeg;
            Assert.Equal(0, BBDownTMuxer.MuxAV(false, "chapter-test", "", audio, [], output,
                title: "selected title", lang: "eng", audioOnly: true, points: points));
        }
        finally
        {
            BBDownTMuxer.FFMPEG = previousFfmpeg;
        }

        var rawProbe = RunTool(ffprobe, "-v", "error", "-show_chapters",
            "-show_streams", "-show_format", "-of", "json", output);
        using var probe = JsonDocument.Parse(rawProbe);
        var chapters = probe.RootElement.GetProperty("chapters").EnumerateArray().ToArray();
        Assert.Equal(points.Select(point => point.title),
            chapters.Select(chapter => chapter.GetProperty("tags").GetProperty("title").GetString()));
        Assert.Equal(new[] { "0.000000", "1.000000" },
            chapters.Select(chapter => chapter.GetProperty("start_time").GetString()));
        Assert.Equal(new[] { "1.000000", "2.000000" },
            chapters.Select(chapter => chapter.GetProperty("end_time").GetString()));
        Assert.Equal("selected title", probe.RootElement.GetProperty("format")
            .GetProperty("tags").GetProperty("title").GetString());
        var stream = Assert.Single(probe.RootElement.GetProperty("streams").EnumerateArray(),
            stream => stream.GetProperty("codec_type").GetString() == "audio");
        Assert.Equal("eng", stream.GetProperty("tags").GetProperty("language").GetString());
        Assert.NotEqual("source handler", stream.GetProperty("tags").GetProperty("handler_name").GetString());
    }

    // 复用 ChapterMuxFact: 需要 BBDOWN_CHAPTER_TEST_DIR + ffmpeg/ffprobe, 否则 Skip。
    // BBDOWN_CHAPTER_TEST_DIR 指向一个独立空目录, refresh-source.mp4 / refresh-target.mp4 由本用例生成。
    [ChapterMuxFact]
    public void FFmpegRefresh_RewritesMetadataOfAnExistingMp4WithoutRecoding()
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("BBDOWN_CHAPTER_TEST_DIR")!);
        Assert.True(Directory.Exists(root));
        var source = Path.Combine(root, "refresh-source.mp4");
        var target = Path.Combine(root, "refresh-target.mp4");
        Assert.False(File.Exists(source));
        Assert.False(File.Exists(target));

        var ffmpeg = BBDownTUtil.FindExecutable("ffmpeg")!;
        var ffprobe = BBDownTUtil.FindExecutable("ffprobe")!;
        // 造一个真实的"已存在输出文件": 视频 + 音频, 2 秒
        RunTool(ffmpeg, "-v", "error", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=10",
            "-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo",
            "-t", "2", "-c:v", "mpeg4", "-c:a", "aac", "-shortest", "-threads", "1",
            "-metadata", "title=source title", source);
        File.Copy(source, target, overwrite: true);

        List<ViewPoint> points =
        [
            new() { title = "A=B;#C\\D\n第二行", start = 0, end = 1 },
            new() { title = "片尾", start = 1, end = 2 }
        ];

        var before = Directory.GetFiles(Path.GetTempPath(), "bbdownt-chapters-*").Length;
        var previousFfmpeg = BBDownTMuxer.FFMPEG;
        try
        {
            BBDownTMuxer.FFMPEG = ffmpeg;
            // 走生产同款路径: 目标文件自己作为输入, MediaOutput.Write 负责 staged + 原子替换
            Assert.True(MediaOutput.Write(target, staged =>
                BBDownTMuxer.UpdateMetadata(target, staged, title: "refreshed title", points: points)));
        }
        finally
        {
            BBDownTMuxer.FFMPEG = previousFfmpeg;
        }

        // 临时章节文件必须被清掉
        Assert.Equal(before, Directory.GetFiles(Path.GetTempPath(), "bbdownt-chapters-*").Length);

        var rawProbe = RunTool(ffprobe, "-v", "error", "-show_chapters",
            "-show_streams", "-show_format", "-of", "json", target);
        using var probe = JsonDocument.Parse(rawProbe);
        var chapters = probe.RootElement.GetProperty("chapters").EnumerateArray().ToArray();
        Assert.Equal(points.Select(point => point.title),
            chapters.Select(chapter => chapter.GetProperty("tags").GetProperty("title").GetString()));
        Assert.Equal(new[] { "0.000000", "1.000000" },
            chapters.Select(chapter => chapter.GetProperty("start_time").GetString()));
        Assert.Equal(new[] { "1.000000", "2.000000" },
            chapters.Select(chapter => chapter.GetProperty("end_time").GetString()));
        Assert.Equal("refreshed title", probe.RootElement.GetProperty("format")
            .GetProperty("tags").GetProperty("title").GetString());
        var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        Assert.Contains(streams, stream => stream.GetProperty("codec_type").GetString() == "video");
        Assert.Contains(streams, stream => stream.GetProperty("codec_type").GetString() == "audio");
    }

    // 复用 ChapterMuxFact: 需要 BBDOWN_CHAPTER_TEST_DIR + ffmpeg/ffprobe, 否则 Skip。
    // 复刻"接口只返回试看时"的退化场景: 拿不回配音语言与字幕, 原文件的这两类标签必须保住。
    [ChapterMuxFact]
    public void FFmpegRefreshKeepsAudioLanguageAndSubtitleNameOfTheExistingFile()
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("BBDOWN_CHAPTER_TEST_DIR")!);
        var bare = Path.Combine(root, "tags-bare.mp4");
        var source = Path.Combine(root, "tags-source.mp4");
        var target = Path.Combine(root, "tags-target.mp4");
        var srt = Path.Combine(root, "tags.srt");
        File.WriteAllText(srt, "1\n00:00:00,000 --> 00:00:01,000\n字幕\n");

        var ffmpeg = BBDownTUtil.FindExecutable("ffmpeg")!;
        var ffprobe = BBDownTUtil.FindExecutable("ffprobe")!;
        RunTool(ffmpeg, "-v", "error", "-f", "lavfi", "-i", "testsrc=size=64x64:rate=5",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-t", "2", "-shortest",
            "-c:v", "mpeg4", "-c:a", "aac", "-threads", "1",
            "-metadata", "title=旧标题", "-metadata", "artist=旧UP",
            "-metadata:s:a:0", "language=jpn", bare);
        RunTool(ffmpeg, "-v", "error", "-i", bare, "-i", srt, "-map", "0", "-map", "1",
            "-c", "copy", "-c:s", "mov_text", "-threads", "1",
            "-metadata:s:s:0", "language=chi", "-metadata:s:s:0", "title=中文", source);
        File.Copy(source, target, overwrite: true);

        var previousFfmpeg = BBDownTMuxer.FFMPEG;
        var previousFfprobe = MediaProbe.FFPROBE;
        try
        {
            BBDownTMuxer.FFMPEG = ffmpeg;
            MediaProbe.FFPROBE = ffprobe;
            var preserved = MediaProbe.ReadTags(target);
            Assert.NotNull(preserved);
            // 刷新时既没有 --lang 也没有可重下的字幕, 全靠读回来的原标签
            Assert.True(MediaOutput.Write(target, staged => BBDownTMuxer.UpdateMetadata(target, staged,
                desc: "新简介", title: "新标题", author: "", episodeId: "", pic: "", lang: "",
                subs: [], points: null, pubTime: 0, preserved: preserved)));
        }
        finally
        {
            BBDownTMuxer.FFMPEG = previousFfmpeg;
            MediaProbe.FFPROBE = previousFfprobe;
        }

        using var probe = JsonDocument.Parse(RunTool(ffprobe, "-v", "error",
            "-show_entries", "format=duration:format_tags:stream=index,codec_type:stream_tags",
            "-of", "json", target));
        var formatTags = probe.RootElement.GetProperty("format").GetProperty("tags");
        Assert.Equal("新标题", formatTags.GetProperty("title").GetString());   // 新值覆盖
        Assert.Equal("旧UP", formatTags.GetProperty("artist").GetString());    // 接口给不出作者, 保留原值
        var streams = probe.RootElement.GetProperty("streams").EnumerateArray()
            .ToDictionary(stream => stream.GetProperty("codec_type").GetString(),
                stream => stream.GetProperty("tags"));
        Assert.Equal("jpn", streams["audio"].GetProperty("language").GetString());
        Assert.Equal("chi", streams["subtitle"].GetProperty("language").GetString());
        Assert.Equal("中文", streams["subtitle"].GetProperty("name").GetString());
        Assert.Equal("2.000000", probe.RootElement.GetProperty("format").GetProperty("duration").GetString());
    }

    private static string RunTool(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // ffprobe 输出 UTF-8; 不显式指定会按系统默认代码页解码, 导致中文标题变成乱码
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Task.WhenAll(output, error).GetAwaiter().GetResult();
            Assert.Fail(executable + " timed out");
        }
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, error.Result);
        return output.Result;
    }
}

public sealed class ChapterMuxFactAttribute : FactAttribute
{
    public ChapterMuxFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BBDOWN_CHAPTER_TEST_DIR")))
            Skip = "设置 BBDOWN_CHAPTER_TEST_DIR 为独立空目录后执行 FFmpeg 混流回归";
        else if (BBDownTUtil.FindExecutable("ffmpeg") is null || BBDownTUtil.FindExecutable("ffprobe") is null)
            Skip = "FFmpeg 混流回归需要 ffmpeg 和 ffprobe";
    }
}
