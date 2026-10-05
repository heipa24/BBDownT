using System.IO.Compression;

namespace BBDownT.Tests;

public class DanmakuXmlValidationTests
{
    [Fact]
    public void IsUsableDanmakuXml_AcceptsRealXml()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("danmaku.xml", "<i><chatserver>chat.bilibili.com</chatserver></i>");

        Assert.True(Program.IsUsableDanmakuXml(path));
    }

    [Fact]
    public void IsUsableDanmakuXml_AcceptsLeadingWhitespaceAndBom()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("danmaku.xml", "\n  <i></i>");

        Assert.True(Program.IsUsableDanmakuXml(path));
    }

    [Fact]
    public void IsUsableDanmakuXml_RejectsEmptyFile()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("danmaku.xml", "");

        Assert.False(Program.IsUsableDanmakuXml(path));
    }

    [Fact]
    public void IsUsableDanmakuXml_RejectsGzipPayload()
    {
        using var files = new MediaTestDirectory();
        var path = files.FilePath("danmaku.xml");
        using (var stream = File.Create(path))
        using (var gzip = new GZipStream(stream, CompressionMode.Compress))
        {
            gzip.Write("<i></i>"u8);
        }

        Assert.False(Program.IsUsableDanmakuXml(path));
    }

    [Fact]
    public void IsUsableDanmakuXml_RejectsMissingFile()
    {
        using var files = new MediaTestDirectory();

        Assert.False(Program.IsUsableDanmakuXml(files.FilePath("missing.xml")));
    }

    [Fact]
    public void DescribeFailureHint_SuggestsDisablingAria2()
    {
        var error = new BBDownTDownloadUtil.Aria2cDownloadException("aria2下载失败，退出码: 1");

        Assert.Contains("--no-aria2", Program.DescribeFailureHint(error));
    }

    [Fact]
    public void DescribeFailureHint_LooksIntoInnerExceptions()
    {
        var error = new Exception("outer", new BBDownTDownloadUtil.Aria2cDownloadException("inner"));

        Assert.Contains("--no-aria2", Program.DescribeFailureHint(error));
    }

    [Fact]
    public void DescribeFailureHint_PointsAtDebugOutputForOtherErrors()
    {
        var hint = Program.DescribeFailureHint(new InvalidOperationException("boom"));

        Assert.Contains("--debug", hint);
        Assert.DoesNotContain(".json", hint);
    }
}
