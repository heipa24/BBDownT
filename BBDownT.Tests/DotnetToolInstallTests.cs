using System;
using System.IO;
using Xunit;

namespace BBDownT.Tests;

public class DotnetToolInstallTests
{
    private static string MakeStore(string root, string packageId = "bbdownt", string version = "2.1.4")
    {
        var packageDir = Path.Combine(root, ".store", packageId, version, packageId, version);
        var any = Path.Combine(packageDir, "tools", "net9.0", "any");
        Directory.CreateDirectory(any);
        File.WriteAllText(Path.Combine(any, "BBDownT.dll"), "x");
        File.WriteAllText(Path.Combine(any, "DotnetToolSettings.xml"), "<x/>");
        // 真实 store 的 nuspec 位于包目录层（…/bbdownt/2.1.4/bbdownt/2.1.4/），而非 tools/net9.0/any/
        File.WriteAllText(Path.Combine(packageDir, "BBDownT.nuspec"), "<x/>");
        return any;
    }

    [Fact]
    public void Detect_ReturnsNull_WhenEntryAssemblyLocationIsEmpty()
    {
        var info = DotnetToolInstall.Detect(null, "C:\\tools", "C:\\repo", Directory.Exists, File.Exists);
        Assert.Null(info);
    }

    [Fact]
    public void Detect_ReturnsNull_WithoutDotnetToolSettingsMarker()
    {
        var any = Path.Combine(Path.GetTempPath(), "bbdownt-notool", "tools", "net9.0", "any");
        Directory.CreateDirectory(any);
        File.WriteAllText(Path.Combine(any, "BBDownT.dll"), "x"); // 缺标记
        try
        {
            var info = DotnetToolInstall.Detect(Path.Combine(any, "BBDownT.dll"), "C:\\tools", "C:\\repo", Directory.Exists, File.Exists);
            Assert.Null(info);
        }
        finally { Directory.Delete(Path.GetDirectoryName(any)!, true); }
    }

    [Fact]
    public void Detect_ReadsPackageIdentityFromTheStoreLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "bbdownt-store-" + Guid.NewGuid().ToString("N"));
        var any = MakeStore(root);
        try
        {
            var info = DotnetToolInstall.Detect(Path.Combine(any, "BBDownT.dll"), "C:\\tools", "C:\\repo", Directory.Exists, File.Exists);
            Assert.NotNull(info);
            Assert.Equal("bbdownt", info!.PackageId);
            Assert.Equal("2.1.4", info.PackageVersion);
            Assert.Equal(ToolInstallScope.ToolPath, info.Scope); // default global root 不匹配，落 ToolPath
            Assert.EndsWith(Path.Combine(".store", "bbdownt", "2.1.4", "bbdownt", "2.1.4"), info.PackageDir);
            Assert.NotNull(info.StoreRoot);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Detect_ReturnsNull_WhenTheStoreShapeIsUnexpected()
    {
        var root = Path.Combine(Path.GetTempPath(), "bbdownt-bad-" + Guid.NewGuid().ToString("N"));
        // 缺 nuspec 的 store 形状
        var any = Path.Combine(root, ".store", "bbdownt", "2.1.4", "bbdownt", "2.1.4", "tools", "net9.0", "any");
        Directory.CreateDirectory(any);
        File.WriteAllText(Path.Combine(any, "BBDownT.dll"), "x");
        File.WriteAllText(Path.Combine(any, "DotnetToolSettings.xml"), "<x/>");
        try
        {
            var info = DotnetToolInstall.Detect(Path.Combine(any, "BBDownT.dll"), "C:\\tools", "C:\\repo", Directory.Exists, File.Exists);
            Assert.Null(info);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Detect_ClassifiesGlobalStoreByMatchingTheDefaultToolsRoot()
    {
        var globalRoot = Path.Combine(Path.GetTempPath(), "bbdotnet-" + Guid.NewGuid().ToString("N"));
        var any = MakeStore(globalRoot);
        try
        {
            var info = DotnetToolInstall.Detect(Path.Combine(any, "BBDownT.dll"), globalRoot, "C:\\repo", Directory.Exists, File.Exists);
            Assert.NotNull(info);
            Assert.Equal(ToolInstallScope.Global, info!.Scope);
        }
        finally { Directory.Delete(globalRoot, true); }
    }

    [Fact]
    public void Detect_ClassifiesLocalToolFromTheManifestOutsideAnyStore()
    {
        var repo = Path.Combine(Path.GetTempPath(), "bbdownt-local-" + Guid.NewGuid().ToString("N"));
        var any = Path.Combine(repo, ".nuget", "packages", "bbdownt", "2.1.4", "tools", "net9.0", "any");
        Directory.CreateDirectory(any);
        File.WriteAllText(Path.Combine(any, "BBDownT.dll"), "x");
        File.WriteAllText(Path.Combine(any, "DotnetToolSettings.xml"), "<x/>");
        Directory.CreateDirectory(Path.Combine(repo, ".config"));
        File.WriteAllText(Path.Combine(repo, ".config", "dotnet-tools.json"), "{}");
        try
        {
            var info = DotnetToolInstall.Detect(Path.Combine(any, "BBDownT.dll"), "C:\\tools", repo, Directory.Exists, File.Exists);
            Assert.NotNull(info);
            Assert.Equal(ToolInstallScope.Local, info!.Scope);
            Assert.NotNull(info.ManifestPath);
            Assert.EndsWith(Path.Combine(".config", "dotnet-tools.json"), info.ManifestPath);
        }
        finally { Directory.Delete(repo, true); }
    }

    [Fact]
    public void Detect_ReturnsNull_ForManifestInstallWithoutAManifest()
    {
        var repo = Path.Combine(Path.GetTempPath(), "bbdownt-nomanifest-" + Guid.NewGuid().ToString("N"));
        var any = Path.Combine(repo, ".nuget", "packages", "bbdownt", "2.1.4", "tools", "net9.0", "any");
        Directory.CreateDirectory(any);
        File.WriteAllText(Path.Combine(any, "BBDownT.dll"), "x");
        File.WriteAllText(Path.Combine(any, "DotnetToolSettings.xml"), "<x/>");
        // 无 .config/dotnet-tools.json
        try
        {
            var info = DotnetToolInstall.Detect(Path.Combine(any, "BBDownT.dll"), "C:\\tools", repo, Directory.Exists, File.Exists);
            Assert.Null(info);
        }
        finally { Directory.Delete(repo, true); }
    }

    [Theory]
    [InlineData("local", "manifest", "Local")]
    [InlineData("global", null, "Global")]
    [InlineData("toolpath", null, "ToolPath")]
    public void ClassifyScope_PrefersLocal_ThenGlobal_ThenToolPath(string which, string? manifest, string expected)
    {
        // toolsRoot 与 defaultRoot 用相对段构造，Path.GetFullPath 后跨平台一致，可精确比较
        var toolsRoot = Path.Combine("x", "tools");
        var defaultRoot = which == "global" ? toolsRoot : Path.Combine("other", "tools");
        Assert.Equal(Enum.Parse<ToolInstallScope>(expected), DotnetToolInstall.ClassifyScope(toolsRoot, defaultRoot, manifest));
    }

    [Fact]
    public void ClassifyScope_DoesNotTreatAHomeDirectoryToolPathAsGlobal()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var tools = Path.Combine(home, "tools"); // 在 profile 下但不是 .dotnet/tools
        var defaultGlobal = Path.Combine(home, ".dotnet", "tools");
        Assert.Equal(ToolInstallScope.ToolPath, DotnetToolInstall.ClassifyScope(tools, defaultGlobal, null));
    }

    [Theory]
    [InlineData("/home/u/.dotnet/tools", "/home/u/.dotnet/tools", "Global")]
    [InlineData("C:\\Users\\u\\.dotnet\\tools", "c:\\users\\u\\.dotnet\\tools", "Global")]
    [InlineData("C:\\repo\\tools", "C:\\Users\\u\\.dotnet\\tools", "ToolPath")]
    public void ClassifyScope_HandlesUnixAndWindowsPathShapes(string toolsRoot, string defaultRoot, string expected)
    {
        Assert.Equal(Enum.Parse<ToolInstallScope>(expected), DotnetToolInstall.ClassifyScope(toolsRoot, defaultRoot, null));
    }

    [Fact]
    public void GlobalToolsRoot_HonorsDotnetCliHome()
    {
        var result = DotnetToolInstall.GlobalToolsRoot(_ => "/tmp/cli");
        Assert.Equal(Path.Combine("/tmp/cli", ".dotnet", "tools"), result);
    }

    [Fact]
    public void GlobalToolsRoot_FallsBackToTheUserProfile()
    {
        var result = DotnetToolInstall.GlobalToolsRoot(_ => "");
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools"), result);
    }

    [Theory]
    [InlineData(true, "C:\\repo\\tools\\.store\\bbdownt\\2.1.4\\bbdownt\\2.1.4\\tools\\net9.0\\any\\BBDownT.dll", "C:\\repo\\tools\\.store\\bbdownt\\2.1.4\\bbdownt\\2.1.4", true)]
    [InlineData(true, "C:\\outside\\BBDownT.dll", "C:\\repo\\tools\\.store\\bbdownt\\2.1.4\\bbdownt\\2.1.4", false)]
    [InlineData(false, "C:\\repo\\tools\\.store\\bbdownt\\2.1.4\\bbdownt\\2.1.4\\tools\\net9.0\\any\\BBDownT.dll", "C:\\repo\\tools\\.store\\bbdownt\\2.1.4\\bbdownt\\2.1.4", false)]
    public void RequiresDetachWorker_OnlyOnWindowsInsideTheStorePackage(bool isWindows, string entry, string packageDir, bool expected)
    {
        var install = new ToolInstallInfo(ToolInstallScope.ToolPath, "C:\\repo\\tools", "C:\\repo\\tools\\.store", packageDir, "bbdownt", "2.1.4");
        Assert.Equal(expected, DotnetToolInstall.RequiresDetachWorker(isWindows, entry, install));
    }

    [Fact]
    public void BuildToolArguments_MatchesTheDocumentedArgvForEveryScope()
    {
        const string src = "C:\\repo\\artifacts";
        var global = new ToolInstallInfo(ToolInstallScope.Global, "C:\\u\\.dotnet\\tools", null, null, null, null);
        var toolPath = new ToolInstallInfo(ToolInstallScope.ToolPath, "C:\\repo\\tools", null, null, null, null);
        var local = new ToolInstallInfo(ToolInstallScope.Local, "C:\\repo", null, null, null, null, "C:\\repo\\.config\\dotnet-tools.json");

        Assert.Equal(new[] { "tool", "update", "BBDownT", "--global", "--source", src }, DotnetToolInstall.UpdateArguments(src, global).ToArray());
        Assert.Equal(new[] { "tool", "update", "BBDownT", "--source", src }, DotnetToolInstall.UpdateArguments(src, local).ToArray());
        Assert.Equal(new[] { "tool", "uninstall", "BBDownT", "--tool-path", "C:\\repo\\tools" }, DotnetToolInstall.UninstallArguments(toolPath).ToArray());
        Assert.Equal(new[] { "tool", "install", "BBDownT", "--global", "--source", src }, DotnetToolInstall.InstallArguments(src, global, false).ToArray());
        Assert.Equal(new[] { "tool", "install", "BBDownT", "--global", "--source", src, "--allow-downgrade" }, DotnetToolInstall.InstallArguments(src, global, true).ToArray());
        Assert.Equal(new[] { "pack", "BBDownT/BBDownT.csproj", "-c", "Release", "-o", "artifacts" }, DotnetToolInstall.PackArguments("C:\\repo").ToArray());
    }
}
