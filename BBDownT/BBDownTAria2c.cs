using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BBDownT.Core.Util;

namespace BBDownT;

static class BBDownTAria2c
{
    public static string ARIA2C = "aria2c";

    /// <summary>
    /// aria2c 调用入口。默认启动真实 aria2c 进程; 单元测试可整体替换为不启动进程的假实现。
    /// 委托签名: (url, path, extraArgs) => 退出码
    /// </summary>
    internal static Func<string, string, string, Task<int>> DownloadRunner { get; set; } = RunAria2cProcessAsync;

    public static async Task<int> RunCommandCodeAsync(string command, string args)
    {
        using Process p = new();
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardOutput = false;
        p.StartInfo.FileName = command;
        p.StartInfo.Arguments = args;
        p.Start();
        await p.WaitForExitAsync();
        return p.ExitCode;
    }

    public static Task<int> DownloadFileByAria2cAsync(string url, string path, string extraArgs)
    {
        return DownloadRunner(url, path, extraArgs);
    }

    /// <summary>
    /// 构造 aria2c 命令行参数。
    /// 关键改动: 显式发送 <c>Accept-Encoding: identity</c> 并显式关闭 <c>--http-accept-gzip</c>。
    /// aria2 在响应带 Content-Encoding: gzip/deflate 时会无条件 inflate, 一旦正文不是 zlib 标准流就
    /// 会报 libz "incorrect header check" 并以退出码 1 结束。
    /// 注意: 该请求头只能排除"服务端按需压缩"的情形。comment.bilibili.com 无视 Accept-Encoding,
    /// 恒定返回 Content-Encoding: deflate 且正文是不带 zlib 头的 raw deflate, 加 identity 也无效,
    /// 因此弹幕必须走内置下载器(见 DownloadConfig.WithoutAria2c)。
    /// </summary>
    internal static string BuildArguments(string url, string path, string extraArgs)
    {
        var headerArgs = "";
        if (!url.Contains("platform=android_tv_yst") && !url.Contains("platform=android"))
            headerArgs += " --header=\"Referer: https://www.bilibili.com\"";
        headerArgs += " --header=\"User-Agent: Mozilla/5.0\"";
        headerArgs += " --header=\"Accept-Encoding: identity\"";
        if (HTTPUtil.ShouldSendCookie(url))
            headerArgs += $" --header=\"Cookie: {HTTPUtil.GetCookieHeaderValue(url)}\"";
        return $" --auto-file-renaming=false --download-result=hide --allow-overwrite=true --console-log-level=warn --summary-interval=0 --file-allocation=none --continue=true --max-tries=5 --retry-wait=3 --min-split-size=1M --disk-cache=64M --optimize-concurrent-downloads=true -x16 -s16 -j16 --http-accept-gzip=false{headerArgs} {extraArgs} \"{url}\" -d \"{Path.GetDirectoryName(path)}\" -o \"{Path.GetFileName(path)}\"";
    }

    private static async Task<int> RunAria2cProcessAsync(string url, string path, string extraArgs)
    {
        return await RunCommandCodeAsync(ARIA2C, BuildArguments(url, path, extraArgs));
    }
}
