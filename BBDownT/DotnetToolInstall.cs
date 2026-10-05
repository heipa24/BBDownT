using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace BBDownT;

/// <summary>
/// 描述当前 BBDownT 进程是以哪种形式安装的 dotnet tool，以及工具目录与 store 位置。
/// 仅用于 <c>--update</c>：据此决定用 <c>dotnet tool update/uninstall/install</c> 的哪个作用域。
/// </summary>
internal enum ToolInstallScope { Unknown, Global, ToolPath, Local }

internal sealed record ToolInstallInfo(
    ToolInstallScope Scope,
    string ToolsRoot,
    string? StoreRoot,
    string? PackageDir,
    string? PackageId,
    string? PackageVersion,
    string? ManifestPath = null)
{
    /// <summary>局部工具清单安装时，重装命令的工作目录必须是清单所在目录。</summary>
    public string? ManifestDirectory => ManifestPath is null ? null : Path.GetDirectoryName(ManifestPath);
}

internal static class DotnetToolInstall
{
    private const string PackageId = "BBDownT";
    private const string SettingsMarker = "DotnetToolSettings.xml";
    private const string StoreSegment = ".store";
    private const string ManifestFileName = "dotnet-tools.json";
    private const string ManifestFolder = ".config";

    /// <summary>
    /// 从当前进程入口程序集位置推断安装方式与作用域。所有文件系统访问经委托注入以便单测。
    /// 返回 <c>null</c> 表示「不是 dotnet tool 安装」（独立二进制 / <c>dotnet run</c> / 开发态）。
    /// </summary>
    internal static ToolInstallInfo? Detect(
        string? entryAssemblyLocation,
        string defaultGlobalToolsRoot,
        string currentDirectory,
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists)
    {
        if (string.IsNullOrEmpty(entryAssemblyLocation))
            return null; // single-file AOT：Assembly.Location 为空，是独立二进制
        var entryDir = Path.GetDirectoryName(entryAssemblyLocation);
        if (string.IsNullOrEmpty(entryDir))
            return null;
        if (!fileExists(Path.Combine(entryDir, SettingsMarker)))
            return null; // dotnet run / 直接拷 DLL：缺 dotnet tool 标记

        // 1) store 形态：入口向上回溯找 .store 段
        var segments = SplitDirectorySegments(entryDir);
        for (var i = segments.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(segments[i], StoreSegment, StringComparison.OrdinalIgnoreCase))
                continue;
            var storeRoot = JoinSegments(segments, 0, i); // 含 .store 段
            if (i + 4 >= segments.Count) return null; // .store/id/ver/id/ver 不足
            var packageId = segments[i + 1];
            var packageVersion = segments[i + 2];
            var packageDir = JoinSegments(segments, 0, i + 4); // .store/id/ver/id/ver
            // 探测验证：包目录必须含与包 ID 同名的 nuspec，避免按固定深度误判
            if (!directoryExists(packageDir) || !HasNuspecFor(fileExists, packageDir, packageId))
                return null;
            var toolsRoot = i == 0 ? Path.GetPathRoot(storeRoot) ?? storeRoot : JoinSegments(segments, 0, i - 1);
            var scope = ClassifyScope(toolsRoot, defaultGlobalToolsRoot, null);
            return new ToolInstallInfo(scope, toolsRoot, storeRoot, packageDir, packageId, packageVersion);
        }

        // 2) 局部工具清单形态：有标记但不在 store 内
        var manifest = FindManifest(currentDirectory, directoryExists, fileExists);
        if (manifest is not null)
        {
            return new ToolInstallInfo(ToolInstallScope.Local, currentDirectory, null, null, null, null, manifest);
        }

        return null;
    }

    /// <summary>纯函数：依据 toolsRoot 与默认全局目录精确比较判定作用域（不依赖是否处于 profile 下）。</summary>
    internal static ToolInstallScope ClassifyScope(string toolsRoot, string defaultGlobalToolsRoot, string? manifestDirectory)
    {
        if (manifestDirectory is not null)
            return ToolInstallScope.Local;
        if (PathsEqual(toolsRoot, defaultGlobalToolsRoot))
            return ToolInstallScope.Global;
        return ToolInstallScope.ToolPath;
    }

    /// <summary>纯函数：Windows + 从 store 包目录内运行时，必须先分离父进程再执行写操作，否则会被文件锁阻断。</summary>
    internal static bool RequiresDetachWorker(bool isWindows, string? entryAssemblyLocation, ToolInstallInfo? install)
    {
        return isWindows
            && install is not null
            && install.PackageDir is not null
            && entryAssemblyLocation is not null
            && IsUnder(entryAssemblyLocation, install.PackageDir);
    }

    /// <summary>作用域前缀参数。Local 返回空（重装命令的工作目录须为清单目录）。</summary>
    internal static IReadOnlyList<string> ScopeArguments(ToolInstallScope scope, string? toolPath)
    {
        return scope switch
        {
            ToolInstallScope.Global => ["--global"],
            ToolInstallScope.ToolPath => toolPath is null
                ? throw new InvalidOperationException("自定义目录安装缺少 tool-path")
                : ["--tool-path", toolPath],
            _ => Array.Empty<string>()
        };
    }

    /// <summary>计算全局工具目录：DOTNET_CLI_HOME 语义为其下建 .dotnet；否则用用户 profile。</summary>
    internal static string GlobalToolsRoot(Func<string, string?> getEnvironmentVariable)
    {
        var cliHome = getEnvironmentVariable("DOTNET_CLI_HOME") ?? "";
        var baseDir = cliHome.Length > 0
            ? Path.Combine(cliHome, ".dotnet")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
        return Path.Combine(baseDir, "tools");
    }

    // ----- 参数构造（供编排与测试断言）-----

    internal static IReadOnlyList<string> PackArguments(string repoRoot)
        => ["pack", $"{PackageId}/{PackageId}.csproj", "-c", "Release", "-o", "artifacts"];

    internal static IReadOnlyList<string> UpdateArguments(string sourceDirectory, ToolInstallInfo install)
        => Concat("tool", "update", PackageId, ScopeArguments(install.Scope, install.ToolsRoot), "--source", sourceDirectory);

    internal static IReadOnlyList<string> UninstallArguments(ToolInstallInfo install)
        => Concat("tool", "uninstall", PackageId, ScopeArguments(install.Scope, install.ToolsRoot));

    internal static IReadOnlyList<string> InstallArguments(string sourceDirectory, ToolInstallInfo install, bool allowDowngrade)
    {
        var args = Concat("tool", "install", PackageId, ScopeArguments(install.Scope, install.ToolsRoot), "--source", sourceDirectory);
        if (allowDowngrade) args = [.. args, "--allow-downgrade"];
        return args;
    }

    // ----- 内部辅助 -----

    private static List<string> Concat(string first, string second, string third, IReadOnlyList<string> middle, params string[] tail)
    {
        var list = new List<string> { first, second, third };
        list.AddRange(middle);
        list.AddRange(tail);
        return list;
    }

    private static bool HasNuspecFor(Func<string, bool> fileExists, string packageDir, string packageId)
    {
        // nuspec 文件名使用包 ID（BBDownT），store 路径段为小写（bbdownt）；按大小写不敏感匹配
        foreach (var candidate in new[] { $"{PackageId}.nuspec", $"{packageId}.nuspec" })
        {
            if (fileExists(Path.Combine(packageDir, candidate)))
                return true;
        }
        return false;
    }

    private static string? FindManifest(string startDirectory, Func<string, bool> directoryExists, Func<string, bool> fileExists)
    {
        var dir = string.IsNullOrEmpty(startDirectory) ? null : startDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, ManifestFolder, ManifestFileName);
            if (fileExists(candidate))
                return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }
        return null;
    }

    private static bool IsUnder(string child, string ancestor)
    {
        var childFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(child));
        var ancestorFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ancestor));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return childFull.Length > ancestorFull.Length
            && childFull.StartsWith(ancestorFull, comparison)
            && (childFull[ancestorFull.Length] == Path.DirectorySeparatorChar || childFull[ancestorFull.Length] == Path.AltDirectorySeparatorChar);
    }

    private static bool PathsEqual(string a, string b)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            comparison);
    }

    private static List<string> SplitDirectorySegments(string directory)
    {
        var segments = new List<string>();
        var dir = directory;
        while (!string.IsNullOrEmpty(dir))
        {
            segments.Insert(0, Path.GetFileName(dir) is { Length: > 0 } name ? name : dir);
            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }
        return segments;
    }

    private static string JoinSegments(IReadOnlyList<string> segments, int start, int endInclusive)
    {
        var path = segments[start];
        for (var i = start + 1; i <= endInclusive; i++)
            path = Path.Combine(path, segments[i]);
        return path;
    }
}
