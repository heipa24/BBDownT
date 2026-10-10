namespace BBDownT.Tests;

public class BbdownMetadataReaderTests : IDisposable
{
    private readonly string root;

    public BbdownMetadataReaderTests()
    {
        root = Path.Combine(Path.GetTempPath(), "bbdown-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    private string CreateDir(string name, string? json)
    {
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        if (json is not null) File.WriteAllText(Path.Combine(dir, "bbdown.json"), json);
        return dir;
    }

    [Fact]
    public void Read_NewFlags_AreHonored()
    {
        var dir = CreateDir("new", """
            {
              "sourceId": "ss38650",
              "url": "https://www.bilibili.com/bangumi/play/ss38650",
              "title": "番剧A",
              "updatedAt": "2026-01-01T00:00:00+08:00",
              "selectPage": "ALL",
              "danmakuFormats": "xml,ass",
              "pages": [
                { "page": 1, "file": "[P01]a.mp4", "xml": "[P01]a.xml", "ass": "[P01]a.ass" },
                { "page": 2, "file": "[P02]b.mp4" }
              ]
            }
            """);

        var metadata = BbdownMetadataReader.TryRead(dir);

        Assert.NotNull(metadata);
        Assert.Equal("https://www.bilibili.com/bangumi/play/ss38650", metadata.Url);
        Assert.Equal("ALL", metadata.SelectPage);
        Assert.Equal("xml,ass", metadata.DanmakuFormats);
        Assert.Equal(2, metadata.Pages.Count);
        Assert.Equal(1, metadata.Pages[0].Page);
        Assert.True(metadata.Pages[0].HasDanmakuArtifact);
        Assert.False(metadata.Pages[1].HasDanmakuArtifact);
    }

    [Fact]
    public void Read_LegacyFile_UsesSourceId_AndLeavesNewFlagsUnset()
    {
        var dir = CreateDir("legacy", """
            {
              "sourceId": "BV1xx411c7mD",
              "title": "旧版目录",
              "updatedAt": "2024-01-01T00:00:00+08:00",
              "pages": [
                { "page": 1, "file": "a.mp4", "ass": "a.ass" },
                { "page": 2, "file": "b.mp4", "ass": "b.ass" }
              ]
            }
            """);

        var metadata = BbdownMetadataReader.TryRead(dir);

        Assert.NotNull(metadata);
        Assert.Equal("BV1xx411c7mD", metadata.Url);
        Assert.Null(metadata.SelectPage);
        Assert.Null(metadata.DanmakuFormats);
        Assert.All(metadata.Pages, page => Assert.True(page.HasDanmakuArtifact));
    }

    [Fact]
    public void Read_DuplicatePages_AreKeptOnce()
    {
        var dir = CreateDir("dup", """
            {
              "url": "https://www.bilibili.com/video/BV1xx411c7mD/",
              "pages": [ { "page": 1, "file": "a.mp4" }, { "page": 1, "file": "a2.mp4" } ]
            }
            """);

        Assert.Single(BbdownMetadataReader.TryRead(dir)!.Pages);
    }

    [Fact]
    public void Read_NonArrayPages_YieldsEmptyPageList()
    {
        var dir = CreateDir("weird", """{ "url": "BV1xx411c7mD", "pages": "不是数组" }""");

        var metadata = BbdownMetadataReader.TryRead(dir);

        Assert.NotNull(metadata);
        Assert.Empty(metadata.Pages);
    }

    [Fact]
    public void Read_DirectoryWithoutMetadata_ReturnsNull()
    {
        Assert.Null(BbdownMetadataReader.TryRead(CreateDir("empty", null)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("这不是 JSON")]
    [InlineData("{}")]
    [InlineData("""{ "title": "没有来源链接" }""")]
    [InlineData("[]")]
    public void Read_UnusableContent_ReturnsNull(string content)
    {
        var dir = CreateDir("bad-" + content.Length, content);

        Assert.Null(BbdownMetadataReader.TryRead(dir));
    }
}
