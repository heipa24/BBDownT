namespace BBDownT.Tests;

public class Aria2cDownloadTests
{
    [Fact]
    public void EnsureAria2cDownloadSucceeded_AcceptsSuccessfulCompletedFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            // 原用例使用 Path.GetTempFileName() 的 0 字节文件, 现在 0 字节产物已被视为失败
            File.WriteAllText(path, "<i></i>");
            BBDownTDownloadUtil.EnsureAria2cDownloadSucceeded(0, path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnsureAria2cDownloadSucceeded_RejectsNonZeroExitEvenWhenOldFileExists()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "<i></i>");
            var error = Assert.ThrowsAny<InvalidOperationException>(
                () => BBDownTDownloadUtil.EnsureAria2cDownloadSucceeded(7, path));

            Assert.Contains("7", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnsureAria2cDownloadSucceeded_RejectsMissingOutput()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bbdownt-{Guid.NewGuid():N}");

        Assert.ThrowsAny<InvalidOperationException>(
            () => BBDownTDownloadUtil.EnsureAria2cDownloadSucceeded(0, path));
    }

    [Fact]
    public void EnsureAria2cDownloadSucceeded_RejectsEmptyOutput()
    {
        var path = Path.GetTempFileName();
        try
        {
            var error = Assert.ThrowsAny<InvalidOperationException>(
                () => BBDownTDownloadUtil.EnsureAria2cDownloadSucceeded(0, path));

            Assert.Contains("空文件", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnsureAria2cDownloadSucceeded_RejectsLeftoverControlFile()
    {
        var path = Path.GetTempFileName();
        var control = path + ".aria2";
        try
        {
            File.WriteAllText(path, "<i></i>");
            File.WriteAllText(control, "partial");
            var error = Assert.ThrowsAny<InvalidOperationException>(
                () => BBDownTDownloadUtil.EnsureAria2cDownloadSucceeded(0, path));

            Assert.Contains(".aria2", error.Message);
        }
        finally
        {
            File.Delete(path);
            File.Delete(control);
        }
    }

    [Fact]
    public void DeleteAria2cLeftovers_RemovesControlFileAndPartialOutput()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("danmaku.xml", "partial");
        File.WriteAllText(path + ".aria2", "control");

        BBDownTDownloadUtil.DeleteAria2cLeftovers(path);

        Assert.False(File.Exists(path + ".aria2"));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DeleteAria2cLeftovers_IgnoresMissingFiles()
    {
        using var files = new MediaTestDirectory();

        BBDownTDownloadUtil.DeleteAria2cLeftovers(files.FilePath("missing.xml"));
    }

    [Fact]
    public void BuildArguments_RequestsIdentityEncoding()
    {
        var args = BBDownTAria2c.BuildArguments("https://comment.bilibili.com/1.xml", "/tmp/1.xml", "");

        Assert.Contains("--header=\"Accept-Encoding: identity\"", args);
        Assert.Contains("--http-accept-gzip=false", args);
        Assert.DoesNotContain("--http-accept-gzip=true", args);
    }

    [Fact]
    public void BuildArguments_KeepsUserExtraArgs()
    {
        var args = BBDownTAria2c.BuildArguments(
            "https://comment.bilibili.com/1.xml", "/tmp/1.xml", "--all-proxy=\"http://127.0.0.1:7890\"");

        Assert.Contains("--all-proxy=\"http://127.0.0.1:7890\"", args);
        Assert.Contains("--header=\"Accept-Encoding: identity\"", args);
    }

    [Fact]
    public void BuildArguments_StillSendsRefererAndOutputPath()
    {
        var args = BBDownTAria2c.BuildArguments("https://comment.bilibili.com/1.xml", "/tmp/1.xml", "");

        Assert.Contains("--header=\"Referer: https://www.bilibili.com\"", args);
        Assert.Contains("-o \"1.xml\"", args);
    }
}
