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

        Program.WriteBbdownMetadata(savePath, "ss1", "标题", PageOf(1), new Program.BbdownMarkers("ALL", "xml,ass"), null);

        var root = ReadBbdownJson(dir);
        Assert.Equal("ALL", (string?)root["selectPage"]);
        Assert.Equal("xml,ass", (string?)root["danmakuFormats"]);
    });

    [Fact]
    public void WriteBbdownMetadata_RunWithoutThoseOptions_KeepsPreviouslyRecordedMarkers() => InFreshDirectory(dir =>
    {
        var savePath = Path.Combine(dir, "[P01]x.mp4");
        File.WriteAllText(savePath, "x");

        Program.WriteBbdownMetadata(savePath, "ss1", "标题", PageOf(1), new Program.BbdownMarkers("ALL", "xml,ass"), null);
        Program.WriteBbdownMetadata(savePath, "ss1", "标题", PageOf(1), new Program.BbdownMarkers(null, null), null);

        var root = ReadBbdownJson(dir);
        Assert.Equal("ALL", (string?)root["selectPage"]);
        Assert.Equal("xml,ass", (string?)root["danmakuFormats"]);
    });

    [Fact]
    public void WriteBbdownMetadata_DefaultOptions_WriteNeitherMarker() => InFreshDirectory(dir =>
    {
        var savePath = Path.Combine(dir, "[P01]x.mp4");
        File.WriteAllText(savePath, "x");

        Program.WriteBbdownMetadata(savePath, "ss1", "标题", PageOf(1), new Program.BbdownMarkers(null, null), null);

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

        Program.WriteBbdownMetadata(savePath, "ss1", "标题", PageOf(3), new Program.BbdownMarkers("ALL", null), null);
        Program.WriteBbdownMetadata(savePath, "ss1", "标题", PageOf(3), new Program.BbdownMarkers("3", null), null);

        Assert.Equal("ALL", (string?)ReadBbdownJson(dir)["selectPage"]);
    });

    [Fact]
    public void WriteBbdownMetadata_V2_FreshWrite_IncludesVersionMediaDownloadAndPageDetails() => InFreshDirectory(dir =>
    {
        var savePath = Path.Combine(dir, "[P01]x.mp4");
        File.WriteAllText(savePath, "x");
        var page = PageOf(1);

        Program.WriteBbdownMetadata(savePath, "ss1", "标题", page, new Program.BbdownMarkers(null, null), DetailsFor(page));

        var root = ReadBbdownJson(dir);
        Assert.Equal(2, (int?)root["version"]);
        var media = Assert.IsType<JsonObject>(root["media"]);
        Assert.Equal("12", (string?)media["aid"]);
        Assert.Equal("ep1", (string?)media["epid"]);
        Assert.Equal("UP主", (string?)media["ownerName"]);
        Assert.Equal(1700000000, (long?)media["pubTime"]);
        var download = Assert.IsType<JsonObject>(root["download"]);
        Assert.Equal("[P<pageNumber>]<pageTitle>", (string?)download["filePattern"]);
        Assert.Equal("hevc,avc", (string?)download["encodingPriority"]);
        Assert.True((bool?)download["aria2c"]);
        var entry = Assert.IsType<JsonObject>(Assert.Single((JsonArray)root["pages"]!));
        Assert.Equal("[P01]x.mp4", (string?)entry["file"]);
        Assert.Equal("第1话", (string?)entry["title"]);
        Assert.Equal("101", (string?)entry["cid"]);
        Assert.Equal(61, (int?)entry["dur"]);
        Assert.Equal("1920x1080", (string?)entry["res"]);
        Assert.Equal("1080P 高清", (string?)entry["quality"]);
        Assert.Equal("30", (string?)entry["fps"]);
        Assert.Equal("avc", (string?)entry["codecs"]);
        Assert.Equal("mp4a", (string?)entry["audioCodecs"]);
    });

    [Fact]
    public void WriteBbdownMetadata_Merge_PreservesUnknownAndAbsentKeys() => InFreshDirectory(dir =>
    {
        var savePath = Path.Combine(dir, "[P01]x.mp4");
        File.WriteAllText(savePath, "x");
        File.WriteAllText(Path.Combine(dir, "bbdown.json"), """
            {
              "version": 2,
              "customTop": "keep-me",
              "media": { "aid": "old", "customMedia": true },
              "pages": [ { "page": 1, "file": "old.mp4", "customPage": "x", "quality": "4K" } ]
            }
            """);

        // details 为 null(等价于无 media/download 快照与无流信息): 旧值应全部保留, 只补 view 级分P数据
        Program.WriteBbdownMetadata(savePath, "ss1", "标题", PageOf(1), new Program.BbdownMarkers(null, null), null);

        var root = ReadBbdownJson(dir);
        Assert.Equal("keep-me", (string?)root["customTop"]);
        var media = Assert.IsType<JsonObject>(root["media"]);
        Assert.Equal("old", (string?)media["aid"]);
        Assert.Equal(true, (bool?)media["customMedia"]);
        var entry = Assert.IsType<JsonObject>(Assert.Single((JsonArray)root["pages"]!));
        // 本次运行带了成品文件, file 覆盖为当前名; 旧 quality(无流信息)与未知键保留
        Assert.Equal("[P01]x.mp4", (string?)entry["file"]);
        Assert.Equal("x", (string?)entry["customPage"]);
        Assert.Equal("4K", (string?)entry["quality"]);
        Assert.Equal("101", (string?)entry["cid"]);
    });

    [Fact]
    public void WriteBbdownMetadata_DownloadOptions_DefaultsOmitted_AndLatestValueWins() => InFreshDirectory(dir =>
    {
        var savePath = Path.Combine(dir, "[P01]x.mp4");
        File.WriteAllText(savePath, "x");
        var page = PageOf(1);

        Program.WriteBbdownMetadata(savePath, "ss1", "标题", page, default, DetailsFor(page));
        Program.WriteBbdownMetadata(savePath, "ss1", "标题", page, default,
            Program.BbdownPageDetails.From(page, null, null, includeStream: false,
                new MyOption { FilePattern = "changed", AudioLanguage = "en-US" }));

        var download = Assert.IsType<JsonObject>(ReadBbdownJson(dir)["download"]);
        Assert.Equal("changed", (string?)download["filePattern"]);
        Assert.Equal("en-US", (string?)download["audioLanguage"]);
        Assert.Equal("hevc,avc", (string?)download["encodingPriority"]); // 第二次没给, 保留旧值
        Assert.True((bool?)download["aria2c"]);
    });

    [Fact]
    public void WriteBbdownMetadata_LegacyV1File_IsUpgradedToVersion2() => InFreshDirectory(dir =>
    {
        var savePath = Path.Combine(dir, "[P01]x.mp4");
        File.WriteAllText(savePath, "x");
        File.WriteAllText(Path.Combine(dir, "bbdown.json"), """{ "sourceId": "ss1", "pages": [ { "page": 1, "file": "old.mp4" } ] }""");

        Program.WriteBbdownMetadata(savePath, "ss1", "标题", PageOf(1), new Program.BbdownMarkers(null, null), null);

        var root = ReadBbdownJson(dir);
        Assert.Equal(2, (int?)root["version"]);
        // 本次运行带着新成品文件, file 覆盖为当前名; 结构升级到 v2
        Assert.Equal("[P01]x.mp4", (string?)Assert.IsType<JsonObject>(Assert.Single((JsonArray)root["pages"]!))["file"]);
    });

    [Fact]
    public void WriteBbdownMetadata_KeepsExtraExtensionSlot() => InFreshDirectory(dir =>
    {
        var savePath = Path.Combine(dir, "[P01]x.mp4");
        File.WriteAllText(savePath, "x");
        File.WriteAllText(Path.Combine(dir, "bbdown.json"), """{ "sourceId": "ss1", "extra": { "note": "mine" }, "pages": [] }""");

        Program.WriteBbdownMetadata(savePath, "ss1", "标题", PageOf(1), default, null);

        var extra = Assert.IsType<JsonObject>(ReadBbdownJson(dir)["extra"]);
        Assert.Equal("mine", (string?)extra["note"]);
    });

    [Fact]
    public void BbdownPageDetails_WithoutStream_SkipsStreamInfo_ButKeepsViewLevelData()
    {
        var details = Program.BbdownPageDetails.From(PageOf(2), null, null, includeStream: false, new MyOption());

        Assert.Equal("12", details.Media!.Aid);
        Assert.Equal("ep1", details.Media.Epid);
        Assert.Null(details.Stream);
        // 默认 MyOption 的模板是空串, download 快照里按"默认值不落盘"语义不会写出
        Assert.Equal("", details.Download!.FilePattern);
    }

    [Fact]
    public void BbdownPageDetails_WithStream_RecordsSelectedTracks()
    {
        var details = DetailsFor(PageOf(1));

        Assert.Equal("1080P 高清", details.Stream!.Quality);
        Assert.Equal("30", details.Stream.Fps);
        Assert.Equal("avc", details.Stream.VideoCodecs);
        Assert.Equal("mp4a", details.Stream.AudioCodecs);
    }

    [Fact]
    public void UpdateBbdownJsonOnly_MigratesLegacyFile_KeepingArtifacts_AndAddsNewPages() => InFreshDirectory(dir =>
    {
        File.WriteAllText(Path.Combine(dir, "[P01]旧.mp4"), "x");
        File.WriteAllText(Path.Combine(dir, "[P01]旧.xml"), "<i/>");
        File.WriteAllText(Path.Combine(dir, "bbdown.json"), """
            {
              "sourceId": "ss1",
              "url": "https://www.bilibili.com/bangumi/play/ss1",
              "selectPage": "ALL",
              "danmakuFormats": "xml,ass",
              "pages": [ { "page": 1, "file": "[P01]旧.mp4", "xml": "[P01]旧.xml" } ]
            }
            """);
        var vInfo = CreateVInfo();

        Program.UpdateBbdownJsonOnly(new MyOption { BbdownJsonDir = dir, FilePattern = "F", MultiFilePattern = "M" },
            vInfo, vInfo.PagesInfo, "unused", "ss1", "WEB");

        var root = ReadBbdownJson(dir);
        Assert.Equal(2, (int?)root["version"]);
        Assert.Equal("ALL", (string?)root["selectPage"]); // 迁移不改写用法标记
        Assert.Equal("xml,ass", (string?)root["danmakuFormats"]);
        var media = Assert.IsType<JsonObject>(root["media"]);
        Assert.Equal("12", (string?)media["aid"]);
        Assert.Equal("UP主", (string?)media["ownerName"]);
        var download = Assert.IsType<JsonObject>(root["download"]);
        Assert.Equal("F", (string?)download["filePattern"]);
        var pages = (JsonArray)root["pages"]!;
        Assert.Equal(2, pages.Count);
        var p1 = Assert.IsType<JsonObject>(pages[0]);
        Assert.Equal("[P01]旧.mp4", (string?)p1["file"]); // 产物映射保留
        Assert.Equal("[P01]旧.xml", (string?)p1["xml"]);
        Assert.Equal("101", (string?)p1["cid"]); // view 级数据补全
        Assert.Equal(61, (int?)p1["dur"]);
        var p2 = Assert.IsType<JsonObject>(pages[1]);
        Assert.False(p2.ContainsKey("file")); // 新集建条目但无产物
        Assert.Equal("102", (string?)p2["cid"]);
    });

    [Fact]
    public void UpdateBbdownJsonOnly_OnlyShowInfo_IsADryRun() => InFreshDirectory(dir =>
    {
        var vInfo = CreateVInfo();

        Program.UpdateBbdownJsonOnly(new MyOption { BbdownJsonDir = dir, OnlyShowInfo = true },
            vInfo, vInfo.PagesInfo, "", "ss1", "WEB");

        Assert.False(File.Exists(Path.Combine(dir, "bbdown.json")));
    });

    [Fact]
    public void HandleConflictingOptions_JsonOnly_IgnoresOtherExclusiveModes()
    {
        var option = new MyOption { JsonOnly = true, DanmakuOnly = true, SubOnly = true };

        Program.HandleConflictingOptions(option);

        Assert.True(option.JsonOnly);
        Assert.False(option.DanmakuOnly);
        Assert.False(option.SubOnly);
    }

    private static Page PageOf(int index)
    {
        var page = new Page(index, "12", $"{100 + index}", "ep1", $"第{index}话", 60 + index, "1920x1080", 1700000000);
        page.ownerName = "UP主";
        return page;
    }

    private static VInfo CreateVInfo() => new()
    {
        Title = "标题",
        Desc = "",
        Pic = "",
        PubTime = 1700000000,
        PagesInfo =
        [
            PageOf(1),
            new Page(2, "12", "102", "ep2", "第2话", 61, "1920x1080", 1700000001)
        ]
    };

    private static Program.BbdownPageDetails DetailsFor(Page page, bool includeStream = true) =>
        Program.BbdownPageDetails.From(page,
            includeStream ? new Video { id = "80", dfn = "1080P 高清", baseUrl = "v", codecs = "avc", fps = "30", bandwith = 1, dur = 60 } : null,
            includeStream ? new Audio { id = "30280", dfn = "192K", baseUrl = "a", codecs = "mp4a", bandwith = 1, dur = 60 } : null,
            includeStream,
            new MyOption { FilePattern = "[P<pageNumber>]<pageTitle>", EncodingPriority = "hevc,avc", UseAria2c = true });

    private static void InFreshDirectory(Action<string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-markers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { body(dir); } finally { Directory.Delete(dir, true); }
    }

    private static JsonObject ReadBbdownJson(string dir) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "bbdown.json")))!.AsObject();
}
