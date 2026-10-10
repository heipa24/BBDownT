using BBDownT.Core;
using BBDownT.Core.Entity;
using System.CommandLine;
using System.Text.Json.Nodes;
using static BBDownT.Core.Entity.Entity;

namespace BBDownT.Tests;

public class OptionBehaviorTests
{
    [Fact]
    public void AudioAndVideoOnly_KeepBothStreamsAndEnableSkipMux()
    {
        var option = new MyOption { AudioOnly = true, VideoOnly = true };
        var result = CreateParsedResult();

        Program.HandleConflictingOptions(option);
        Program.ApplyDashStreamSelection(option, result);

        Assert.True(option.AudioOnly);
        Assert.True(option.VideoOnly);
        Assert.True(option.SkipMux);
        Assert.Single(result.VideoTracks);
        Assert.Single(result.AudioTracks);
        Assert.Empty(result.BackgroundAudioTracks);
        Assert.Empty(result.RoleAudioList);
        Assert.False(Program.NeedsMuxer(option));
    }

    [Theory]
    [InlineData(true, false, 0, 1)]
    [InlineData(false, true, 1, 0)]
    public void IndividualStreamOnlyOption_RemainsExclusive(
        bool audioOnly,
        bool videoOnly,
        int expectedVideoCount,
        int expectedAudioCount)
    {
        var option = new MyOption { AudioOnly = audioOnly, VideoOnly = videoOnly };
        var result = CreateParsedResult();

        Program.ApplyDashStreamSelection(option, result);

        Assert.Equal(expectedVideoCount, result.VideoTracks.Count);
        Assert.Equal(expectedAudioCount, result.AudioTracks.Count);
    }

    [Fact]
    public void PageNumberPadding_UsesOriginalTotalPageCount()
    {
        var page = new Page(7, "2", "3", "", "part", 1, "", 0);

        var path = Program.FormatSavePath(
            "[P<pageNumberWithZero>]",
            "title",
            null,
            null,
            page,
            pagesCount: 120,
            "WEB",
            0);

        Assert.Equal("[P007].mp4", path);
    }

    [Theory]
    [InlineData(new[] { "BV1xx411c7mD", "-e", "HEVC", "-q", "1080P" }, true)]
    [InlineData(new[] { "BV1xx411c7mD", "--dfn-priority=1080P", "--encoding-priority=HEVC" }, false)]
    [InlineData(new[] { "BV1xx411c7mD", "-e", "HEVC" }, true)]
    [InlineData(new[] { "BV1xx411c7mD", "-q", "1080P" }, false)]
    public async Task CommandLinePriority_UsesWrittenAliasOrder(string[] arguments, bool encodingFirst)
    {
        MyOption? captured = null;
        var command = CommandLineInvoker.GetRootCommand(option =>
        {
            captured = option;
            return Task.CompletedTask;
        });

        Assert.Equal(0, await command.InvokeAsync(arguments));
        Assert.NotNull(captured);
        Assert.Equal(encodingFirst, captured.EncodingPriorityFirst);
    }

    [Theory]
    [InlineData(true, "-e HEVC\n-q 1080P\n")]
    [InlineData(true, "--encoding-priority=HEVC\n--dfn-priority=1080P\n")]
    [InlineData(false, "-q 1080P\n-e HEVC\n")]
    public async Task ConfigPriority_UsesWrittenOrder(bool encodingFirst, string config)
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, config);
        MyOption? captured = null;
        var command = CommandLineInvoker.GetRootCommand(option =>
        {
            captured = option;
            return Task.CompletedTask;
        });
        var arguments = new List<string> { "BV1xx411c7mD", "--config-file", path };
        try
        {
            Assert.True(BBDownTConfigParser.HandleConfig(arguments, command));
            Assert.Equal(0, await command.InvokeAsync(arguments.ToArray()));
            Assert.NotNull(captured);
            Assert.Equal(encodingFirst, captured.EncodingPriorityFirst);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true, "-e", "HEVC", "-q 1080P\n")]
    [InlineData(false, "-q", "1080P", "-e HEVC\n")]
    public async Task CommandLinePriority_PrecedesLaterConfigPriority(
        bool encodingFirst,
        string commandOption,
        string commandValue,
        string config)
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, config);
        MyOption? captured = null;
        var command = CommandLineInvoker.GetRootCommand(option =>
        {
            captured = option;
            return Task.CompletedTask;
        });
        var arguments = new List<string>
        {
            "BV1xx411c7mD", commandOption, commandValue, "--config-file", path
        };
        try
        {
            Assert.True(BBDownTConfigParser.HandleConfig(arguments, command));
            Assert.Equal(0, await command.InvokeAsync(arguments.ToArray()));
            Assert.NotNull(captured);
            Assert.Equal(encodingFirst, captured.EncodingPriorityFirst);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VideoSorting_UsesSelectedPrimaryPriority()
    {
        var videos = new List<Video>
        {
            CreateVideo("80", "1080P", "AVC"),
            CreateVideo("64", "720P", "HEVC")
        };
        var qualityPriority = new Dictionary<string, int> { ["1080P"] = 0, ["720P"] = 1 };
        var encodingPriority = new Dictionary<string, byte> { ["HEVC"] = 0, ["AVC"] = 1 };

        var encodingFirst = Program.SortTracks(videos, qualityPriority, encodingPriority, false, true);
        var qualityFirst = Program.SortTracks(videos, qualityPriority, encodingPriority, false, false);

        Assert.Equal("HEVC", encodingFirst[0].codecs);
        Assert.Equal("1080P", qualityFirst[0].dfn);
    }

    [Theory]
    [InlineData(null, "129")]
    [InlineData("HDR Vivid", "129")]
    [InlineData("8K 超高清", "127")]
    public void VideoSorting_HdrVividSupportsDefaultAndExplicitQualityPriority(string? preferredQuality, string expectedId)
    {
        var videos = new List<Video>
        {
            CreateVideo("127", Config.qualitys["127"], "HEVC"),
            CreateVideo("129", Config.qualitys["129"], "HEVC"),
            CreateVideo("125", Config.qualitys["125"], "HEVC")
        };
        var qualityPriority = new Dictionary<string, int>();
        if (preferredQuality != null) qualityPriority[preferredQuality] = 0;

        var sorted = Program.SortTracks(videos, qualityPriority, new(), false, false);

        Assert.Equal(expectedId, sorted[0].id);
    }

    [Fact]
    public async Task CommandLine_HdrVividPriorityIsAppliedBeforeFallbackQuality()
    {
        MyOption? captured = null;
        var command = CommandLineInvoker.GetRootCommand(option =>
        {
            captured = option;
            return Task.CompletedTask;
        });

        Assert.Equal(0, await command.InvokeAsync([
            "BV1xx411c7mD", "-q", "HDR Vivid,8K 超高清", "-info"]));
        Assert.NotNull(captured);

        var setup = Program.SetUpWork(captured);
        var videos = new List<Video>
        {
            CreateVideo("127", Config.qualitys["127"], "HEVC"),
            CreateVideo("129", Config.qualitys["129"], "HEVC")
        };
        var sorted = Program.SortTracks(videos, setup.dfnPriority, setup.encodingPriority,
            captured.VideoAscending, captured.EncodingPriorityFirst);

        Assert.Equal("129", sorted[0].id);
        Assert.Equal(0, setup.dfnPriority["HDR Vivid"]);
        Assert.Equal(1, setup.dfnPriority["8K 超高清"]);
    }

    [Fact]
    public async Task SkipMuxTask_RecordsOnlyExistingNonEmptyStreamFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"bbdownt-streams-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var videoPath = Path.Combine(directory, "video.mp4");
        var audioPath = Path.Combine(directory, "audio.m4a");
        var emptyPath = Path.Combine(directory, "empty.m4a");
        var missingPath = Path.Combine(directory, "missing.m4a");
        var task = new DownloadTask("2", "BV1xx411c7mD", 1);
        try
        {
            await File.WriteAllTextAsync(videoPath, "video");
            await File.WriteAllTextAsync(audioPath, "audio");
            await File.WriteAllTextAsync(emptyPath, "");

            Program.RecordDownloadedStreams(task, videoPath, audioPath, emptyPath, missingPath, "");

            Assert.Equal(new[] { videoPath, audioPath }, task.CreateSnapshot().SavePaths);
        }
        finally
        {
            if (File.Exists(videoPath)) File.Delete(videoPath);
            if (File.Exists(audioPath)) File.Delete(audioPath);
            if (File.Exists(emptyPath)) File.Delete(emptyPath);
            Directory.Delete(directory);
        }
    }

    private static ParsedResult CreateParsedResult()
    {
        var audio = new Audio
        {
            id = "30280",
            dfn = "192K",
            baseUrl = "audio",
            codecs = "M4A",
            bandwith = 192000,
            dur = 1
        };
        return new ParsedResult
        {
            VideoTracks = [CreateVideo("80", "1080P", "AVC")],
            AudioTracks = [audio],
            BackgroundAudioTracks = [audio],
            RoleAudioList =
            [
                new AudioMaterialInfo
                {
                    title = "role",
                    personName = "person",
                    path = "role.m4a",
                    audio = [audio]
                }
            ]
        };
    }

    private static Video CreateVideo(string id, string dfn, string codecs)
    {
        return new Video
        {
            id = id,
            dfn = dfn,
            baseUrl = "video",
            codecs = codecs,
            bandwith = 1,
            dur = 1
        };
    }

    [Fact]
    public void WriteBbdownMetadata_RecordsSelectPageAndDanmakuFormats() => InFreshDirectory(dir =>
    {
        var savePath = Path.Combine(dir, "[P01]x.mp4");
        File.WriteAllText(savePath, "x");
        File.WriteAllText(Path.ChangeExtension(savePath, ".xml"), "<i/>");

        Program.WriteBbdownMetadata(savePath, "ss1", "标题", 1, new Program.BbdownMarkers("ALL", "xml,ass"));

        var root = ReadBbdownJson(dir);
        Assert.Equal("ALL", (string?)root["selectPage"]);
        Assert.Equal("xml,ass", (string?)root["danmakuFormats"]);
    });

    [Fact]
    public void WriteBbdownMetadata_RunWithoutThoseOptions_KeepsPreviouslyRecordedMarkers() => InFreshDirectory(dir =>
    {
        var savePath = Path.Combine(dir, "[P01]x.mp4");
        File.WriteAllText(savePath, "x");

        Program.WriteBbdownMetadata(savePath, "ss1", "标题", 1, new Program.BbdownMarkers("ALL", "xml,ass"));
        Program.WriteBbdownMetadata(savePath, "ss1", "标题", 1, new Program.BbdownMarkers(null, null));

        var root = ReadBbdownJson(dir);
        Assert.Equal("ALL", (string?)root["selectPage"]);
        Assert.Equal("xml,ass", (string?)root["danmakuFormats"]);
    });

    [Fact]
    public void WriteBbdownMetadata_DefaultOptions_WriteNeitherMarker() => InFreshDirectory(dir =>
    {
        var savePath = Path.Combine(dir, "[P01]x.mp4");
        File.WriteAllText(savePath, "x");

        Program.WriteBbdownMetadata(savePath, "ss1", "标题", 1, new Program.BbdownMarkers(null, null));

        var root = ReadBbdownJson(dir);
        Assert.False(root.ContainsKey("selectPage"));
        Assert.False(root.ContainsKey("danmakuFormats"));
    });

    [Fact]
    public void BuildBbdownMarkers_WithoutExplicitOptions_RecordsNothing()
    {
        var markers = Program.BuildBbdownMarkers(new MyOption(), false, BBDownTDanmakuFormatInfo.DefaultFormats);

        Assert.Null(markers.SelectPage);
        Assert.Null(markers.DanmakuFormats);
    }

    [Fact]
    public void BuildBbdownMarkers_RecordsRawSelectPageAndEffectiveFormats()
    {
        var markers = Program.BuildBbdownMarkers(
            new MyOption { SelectPage = "3-5,LATEST", SelectPageSpecified = true },
            downloadDanmaku: true,
            formats: [BBDownTDanmakuFormat.Ass]);

        Assert.Equal("3-5,LATEST", markers.SelectPage);
        Assert.Equal("ass", markers.DanmakuFormats);
    }

    [Fact]
    public void BuildBbdownMarkers_DanmakuRun_RecordsDefaultFormats()
    {
        // --danmaku-only 也会让 SetUpWork 把 downloadDanmaku 置真, 同样算"下过弹幕"
        var markers = Program.BuildBbdownMarkers(new MyOption { DanmakuOnly = true }, true, BBDownTDanmakuFormatInfo.DefaultFormats);

        Assert.Equal("xml,ass", markers.DanmakuFormats);
    }

    [Theory]
    [InlineData(null, "ALL", true)]
    [InlineData("3", "ALL", true)]
    [InlineData("ALL", "all", true)]
    [InlineData("3", "5", true)]
    [InlineData("ALL", "3", false)]
    [InlineData("ALL", null, false)]
    [InlineData("ALL", "", false)]
    [InlineData(null, null, false)]
    public void ShouldRecordSelectPage_KeepsTheAllMarker(string? recorded, string? incoming, bool expected) =>
        Assert.Equal(expected, Program.ShouldRecordSelectPage(recorded, incoming));

    [Fact]
    public void WriteBbdownMetadata_NarrowerSelectPage_DoesNotDowngradeTheAllMarker() => InFreshDirectory(dir =>
    {
        var savePath = Path.Combine(dir, "[P03]x.mp4");
        File.WriteAllText(savePath, "x");

        Program.WriteBbdownMetadata(savePath, "ss1", "标题", 3, new Program.BbdownMarkers("ALL", null));
        Program.WriteBbdownMetadata(savePath, "ss1", "标题", 3, new Program.BbdownMarkers("3", null));

        Assert.Equal("ALL", (string?)ReadBbdownJson(dir)["selectPage"]);
    });

    private static void InFreshDirectory(Action<string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-markers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { body(dir); } finally { Directory.Delete(dir, true); }
    }

    private static JsonObject ReadBbdownJson(string dir) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "bbdown.json")))!.AsObject();
}
