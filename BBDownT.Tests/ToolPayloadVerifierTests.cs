using System.IO;
using System.IO.Compression;

namespace BBDownT.Tests;

public class ToolPayloadVerifierTests
{
    [Fact]
    public void ResolveInstalledDllPath_PointsAtTheCandidateVersionStoreDirectory()
    {
        var resolved = ToolPayloadVerifier.ResolveInstalledDllPath(
            @"C:\tools\.store", "bbdownt", new Version(2, 1, 5), "tools/net9.0/any/BBDownT.dll");

        Assert.Equal(Path.Combine(@"C:\tools\.store", "bbdownt", "2.1.5", "bbdownt", "2.1.5",
            "tools", "net9.0", "any", "BBDownT.dll"), resolved);
    }

    [Fact]
    public void ResolveInstalledDllPath_ReturnsNull_WhenTheInstallHasNoStoreDirectory()
    {
        Assert.Null(ToolPayloadVerifier.ResolveInstalledDllPath(null, "bbdownt", new Version(2, 1, 5), "tools/net9.0/any/BBDownT.dll"));
        // 包内读不出主程序集相对路径时无从定位
        Assert.Null(ToolPayloadVerifier.ResolveInstalledDllPath(@"C:\tools\.store", "bbdownt", new Version(2, 1, 5), null));
    }

    [Fact]
    public void Verify_Passes_WhenTheLandedDllMatchesThePackage()
    {
        using var files = new MediaTestDirectory();
        var payload = "NEW-BUILD"u8.ToArray();
        var nupkg = MakeNupkg(files.FilePath("BBDownT.2.1.5.nupkg"), payload);
        var landed = files.FilePath("BBDownT.dll");
        File.WriteAllBytes(landed, payload);

        Assert.Null(ToolPayloadVerifier.Verify(nupkg, landed));
    }

    [Fact]
    public void Verify_ReportsMismatch_WhenTheLandedDllIsStillTheOldBuild()
    {
        using var files = new MediaTestDirectory();
        var nupkg = MakeNupkg(files.FilePath("BBDownT.2.1.5.nupkg"), "NEW-BUILD"u8.ToArray());
        var landed = files.Write("BBDownT.dll", "OLD-BUILD");

        var failure = ToolPayloadVerifier.Verify(nupkg, landed);

        Assert.NotNull(failure);
        Assert.Contains("不一致", failure);
    }

    [Fact]
    public void Verify_ReportsMismatch_WhenTheLandedDllWasNeverWritten()
    {
        using var files = new MediaTestDirectory();
        var nupkg = MakeNupkg(files.FilePath("BBDownT.2.1.5.nupkg"), "NEW-BUILD"u8.ToArray());

        var failure = ToolPayloadVerifier.Verify(nupkg, files.FilePath("BBDownT.dll"));

        Assert.NotNull(failure);
        Assert.Contains("未找到落地文件", failure);
    }

    [Fact]
    public void Verify_ReportsItCouldNotCheck_WhenThePackageCarriesNoToolDll()
    {
        using var files = new MediaTestDirectory();
        var nupkg = files.FilePath("BBDownT.2.1.5.nupkg");
        using (var zip = ZipFile.Open(nupkg, ZipArchiveMode.Create))
        {
            zip.CreateEntry("tools/net9.0/any/BBDownT.Core.dll").Open().Dispose();
        }

        var failure = ToolPayloadVerifier.Verify(nupkg, files.Write("BBDownT.dll", "OLD"));

        Assert.NotNull(failure);
        Assert.Contains("无法校验", failure);
    }

    [Fact]
    public void IsAvailableForReplace_AllowsMissingAndUnlockedFiles()
    {
        using var files = new MediaTestDirectory();
        Assert.True(ToolPayloadVerifier.IsAvailableForReplace(null));
        Assert.True(ToolPayloadVerifier.IsAvailableForReplace(files.FilePath("not-written-yet.dll")));
        Assert.True(ToolPayloadVerifier.IsAvailableForReplace(files.Write("BBDownT.dll", "OLD")));
    }

    [Fact]
    public void IsAvailableForReplace_RejectsAFileHeldByAnotherHandle()
    {
        if (!OperatingSystem.IsWindows()) return; // 文件锁是 Windows 语义，POSIX 为建议锁
        using var files = new MediaTestDirectory();
        var path = files.Write("BBDownT.dll", "OLD");
        using var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.False(ToolPayloadVerifier.IsAvailableForReplace(path));
    }

    private static string MakeNupkg(string path, byte[] toolDll)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var w = zip.CreateEntry("tools/net9.0/any/BBDownT.dll").Open();
        w.Write(toolDll);
        return path;
    }
}
