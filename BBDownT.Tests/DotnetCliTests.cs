using System;
using System.IO;
using Xunit;

namespace BBDownT.Tests;

public class DotnetCliTests
{
    [Fact]
    public void ResolveDotnetMuxer_UsesDotnetHostPath_WhenPresentAndExisting()
    {
        // 指向一个真实存在的文件，否则 File.Exists 不通过会回退到字面量 "dotnet"
        var host = Path.Combine(Path.GetTempPath(), "dotnet-host-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(host, "x");
        try
        {
            var muxer = DotnetCli.ResolveDotnetMuxer(_ => host);
            Assert.Equal(host, muxer);
        }
        finally { File.Delete(host); }
    }

    [Fact]
    public void ResolveDotnetMuxer_IgnoresDotnetHostPath_WhenMissing()
    {
        var muxer = DotnetCli.ResolveDotnetMuxer(_ => "");
        Assert.Equal("dotnet", muxer); // 不保证文件存在时回退字面量
    }

    [Fact]
    public void ResolveDotnetMuxer_FallsBackToLiteralDotnet()
    {
        var muxer = DotnetCli.ResolveDotnetMuxer(_ => (string?)null);
        Assert.Equal("dotnet", muxer);
    }
}
