namespace BBDownT.Tests;

public class MediaProbeTests
{
    private const string Payload = """
        {
          "format": { "duration": "1419.136000", "tags": {
            "title": "旧标题", "artist": "旧UP", "encoder": "Lavf63.1.101", "major_brand": "isom" } },
          "streams": [
            { "index": 0, "codec_type": "video", "tags": { "language": "und", "handler_name": "VideoHandler" } },
            { "index": 1, "codec_type": "audio", "tags": { "language": "jpn" } },
            { "index": 2, "codec_type": "subtitle", "tags": { "language": "chi", "title": "中文" } }
          ],
          "chapters": [
            { "start_time": "69.000000", "end_time": "160.000000", "tags": { "title": "Intro" } },
            { "start_time": "1291.000000", "end_time": "1381.000000", "tags": { "title": "Outro" } }
          ]
        }
        """;

    [Fact]
    public void ReadTagsParsesDurationContainerTagsStreamOrdinalsAndChapters()
    {
        string? captured = null;

        var tags = MediaProbe.ReadTags("x.mp4", args =>
        {
            captured = string.Join(" ", args);
            return Payload;
        });

        Assert.NotNull(tags);
        Assert.Equal(1419.136, tags!.DurationSeconds, 3);
        // encoder/major_brand 属于封装器写的技术标签, 不该被当成用户数据带回
        Assert.Equal(new[] { "title", "artist" }, tags.FormatTags.Select(tag => tag.Key));
        Assert.Equal(new[] { "video", "audio", "subtitle" }, tags.Streams.Select(stream => stream.CodecType));
        Assert.Equal(new[] { 0, 0, 0 }, tags.Streams.Select(stream => stream.IndexInType));
        Assert.Equal("jpn", tags.Streams[1].Tags.Single().Value);
        Assert.Equal(new[] { "Intro", "Outro" }, tags.Chapters.Select(chapter => chapter.title));
        Assert.Equal(new[] { 69, 1291 }, tags.Chapters.Select(chapter => chapter.start));
        Assert.Equal(new[] { 160, 1381 }, tags.Chapters.Select(chapter => chapter.end));
        Assert.Contains("-of json", captured);
        Assert.Contains("format_tags", captured);
        Assert.Equal("x.mp4", captured!.Split(' ').Last());
    }

    [Fact]
    public void DurationSecondsReadsOnlyTheContainerDuration()
    {
        var seconds = MediaProbe.DurationSeconds("x.mp4", args =>
        {
            Assert.Contains("format=duration", args);
            Assert.DoesNotContain("format_tags", args);
            return "{\"format\":{\"duration\":\"360.151000\"}}";
        });

        Assert.Equal(360.151, seconds!.Value, 3);
    }

    [Fact]
    public void MissingTagsAndChaptersYieldEmptyCollections()
    {
        var tags = MediaProbe.ReadTags("x.mp4", _ => "{\"format\":{\"duration\":\"10\"}}");

        Assert.NotNull(tags);
        Assert.Empty(tags!.FormatTags);
        Assert.Empty(tags.Streams);
        Assert.Empty(tags.Chapters);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("{ not json")]
    public void AFailedFfprobeRunIsReportedAsNoData(string? output)
    {
        Assert.Null(MediaProbe.DurationSeconds("x.mp4", _ => output));
        Assert.Null(MediaProbe.ReadTags("x.mp4", _ => output));
    }
}
