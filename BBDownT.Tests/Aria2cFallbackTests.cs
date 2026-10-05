using System.Net;

namespace BBDownT.Tests;

public class Aria2cFallbackTests
{
    [Fact]
    public async Task DownloadFileAsync_FallsBackToBuiltInDownloader_WhenAria2cFails()
    {
        using var files = new MediaTestDirectory();
        var destination = files.FilePath("danmaku.xml");
        var original = BBDownTAria2c.DownloadRunner;
        var aria2Calls = 0;
        BBDownTAria2c.DownloadRunner = (_, _, _) =>
        {
            aria2Calls++;
            return Task.FromResult(1);
        };
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("<i></i>"u8.ToArray())
        }));

        try
        {
            await BBDownTDownloadUtil.DownloadFileAsync(
                "https://comment.bilibili.com/1.xml", destination,
                new BBDownTDownloadUtil.DownloadConfig { UseAria2c = true }, client);
        }
        finally
        {
            BBDownTAria2c.DownloadRunner = original;
        }

        Assert.Equal(1, aria2Calls);
        Assert.Equal("<i></i>", File.ReadAllText(destination));
        Assert.False(File.Exists(destination + ".aria2"));
    }

    [Fact]
    public async Task DownloadFileAsync_CleansAria2LeftoversBeforeFallingBack()
    {
        using var files = new MediaTestDirectory();
        var destination = files.FilePath("danmaku.xml");
        var original = BBDownTAria2c.DownloadRunner;
        BBDownTAria2c.DownloadRunner = (_, path, _) =>
        {
            // 模拟 aria2 留下的 gzip 半成品与 .aria2 控制文件
            File.WriteAllBytes(path, [0x1f, 0x8b, 0x08, 0x00]);
            File.WriteAllText(path + ".aria2", "control");
            return Task.FromResult(1);
        };
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("<i><d p=\"1,1,25,16777215,0,0,0,0\">hi</d></i>"u8.ToArray())
        }));

        try
        {
            await BBDownTDownloadUtil.DownloadFileAsync(
                "https://comment.bilibili.com/1.xml", destination,
                new BBDownTDownloadUtil.DownloadConfig { UseAria2c = true }, client);
        }
        finally
        {
            BBDownTAria2c.DownloadRunner = original;
        }

        Assert.False(File.Exists(destination + ".aria2"));
        Assert.Equal("<i><d p=\"1,1,25,16777215,0,0,0,0\">hi</d></i>", File.ReadAllText(destination));
    }

    [Fact]
    public async Task DownloadFileAsync_ThrowsAria2cFailure_WhenFallbackAlsoFails()
    {
        using var files = new MediaTestDirectory();
        var destination = files.FilePath("danmaku.xml");
        // 内置下载器下载到 .tmp 再移动, 失败时会把 .tmp 留在目录里, 注册以便夹具清理
        files.FilePath("danmaku.tmp");
        var original = BBDownTAria2c.DownloadRunner;
        BBDownTAria2c.DownloadRunner = (_, _, _) => Task.FromResult(1);
        using var client = new HttpClient(new Handler(_ => throw new HttpRequestException("boom")));

        try
        {
            var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(
                () => BBDownTDownloadUtil.DownloadFileAsync(
                    "https://comment.bilibili.com/1.xml", destination,
                    new BBDownTDownloadUtil.DownloadConfig { UseAria2c = true }, client,
                    // 零延迟注入: 内置下载器已按 aria2 预算重试, 用例不该真的等退避
                    _ => Task.CompletedTask));

            Assert.Contains("aria2", error.Message);
            Assert.Contains("boom", error.Message);
        }
        finally
        {
            BBDownTAria2c.DownloadRunner = original;
        }
    }

    [Fact]
    public async Task DownloadFileAsync_SkipsAria2WhenDisabled()
    {
        using var files = new MediaTestDirectory();
        var destination = files.FilePath("danmaku.xml");
        var original = BBDownTAria2c.DownloadRunner;
        var aria2Calls = 0;
        BBDownTAria2c.DownloadRunner = (_, _, _) =>
        {
            aria2Calls++;
            return Task.FromResult(0);
        };
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("<i></i>"u8.ToArray())
        }));

        try
        {
            await BBDownTDownloadUtil.DownloadFileAsync(
                "https://comment.bilibili.com/1.xml", destination,
                new BBDownTDownloadUtil.DownloadConfig(), client);
        }
        finally
        {
            BBDownTAria2c.DownloadRunner = original;
        }

        Assert.Equal(0, aria2Calls);
        Assert.Equal("<i></i>", File.ReadAllText(destination));
    }

    [Fact]
    public void WithoutAria2c_KeepsOtherOptionsAndLeavesSourceUntouched()
    {
        var config = new BBDownTDownloadUtil.DownloadConfig
        {
            UseAria2c = true,
            Aria2cArgs = "--all-proxy=x",
            ForceHttp = true,
            MultiThread = true
        };

        var copy = config.WithoutAria2c();

        Assert.False(copy.UseAria2c);
        Assert.Equal("--all-proxy=x", copy.Aria2cArgs);
        Assert.True(copy.ForceHttp);
        Assert.True(copy.MultiThread);
        Assert.True(config.UseAria2c);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
