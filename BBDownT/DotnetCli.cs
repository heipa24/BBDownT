using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BBDownT;

/// <summary>
/// 仓库结构探测与 <c>dotnet</c> 命令调用封装，供 <c>--update</c> 重新打包并重装 dotnet tool。
/// </summary>
internal static class RepoLayout
{
    /// <summary>仓库根的判定相对路径：存在 <c>BBDownT/BBDownT.csproj</c> 即为仓库。</summary>
    internal const string ProjectRelativePath = "BBDownT/BBDownT.csproj";

    internal static bool IsRepositoryShape(string? directory, Func<string, bool>? fileExists = null)
    {
        if (string.IsNullOrEmpty(directory)) return false;
        var exists = fileExists ?? File.Exists;
        return exists(Path.Combine(directory, ProjectRelativePath));
    }

    /// <summary>从 startDirectory 逐级上溯（含自身）找第一个仓库根；到根目录仍不满足返回 null。</summary>
    internal static string? FindRepoRoot(string startDirectory, Func<string, bool>? directoryExists = null, Func<string, bool>? fileExists = null)
    {
        var existsDir = directoryExists ?? Directory.Exists;
        var dir = string.IsNullOrEmpty(startDirectory) ? null : startDirectory;
        while (dir is not null)
        {
            if (IsRepositoryShape(dir, fileExists))
                return dir;
            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }
        return null;
    }
}

/// <summary>一次 <c>dotnet</c> 命令的请求。</summary>
internal sealed record DotnetCommandRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    TimeSpan Timeout,
    bool StreamOutput);

/// <summary>一次 <c>dotnet</c> 命令的结果。</summary>
internal sealed record DotnetCommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>执行 <c>dotnet</c> 命令的可注入抽象，便于离线测试。</summary>
internal interface IDotnetCommandRunner
{
    Task<DotnetCommandResult> RunAsync(DotnetCommandRequest request, CancellationToken cancellationToken);
}

/// <summary>真实实现：拉起 <c>dotnet</c> 进程，可选逐行回显输出。</summary>
internal sealed class ProcessDotnetCommandRunner : IDotnetCommandRunner
{
    public async Task<DotnetCommandResult> RunAsync(DotnetCommandRequest request, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        if (request.WorkingDirectory is not null)
            process.StartInfo.WorkingDirectory = request.WorkingDirectory;
        foreach (var arg in request.Arguments)
            process.StartInfo.ArgumentList.Add(arg);

        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            if (request.StreamOutput) Console.WriteLine(e.Data);
            lock (output) output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            if (request.StreamOutput) Console.Error.WriteLine(e.Data);
            lock (error) error.AppendLine(e.Data);
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        try
        {
            if (!process.Start())
                throw new IOException($"无法启动 {request.FileName}");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(timeout.Token);
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new OperationCanceledException("dotnet 命令超时");
            }
        }
        catch (Exception)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            throw;
        }

        lock (output) lock (error)
            return new DotnetCommandResult(process.ExitCode, output.ToString(), error.ToString());
    }
}

/// <summary>解析 <c>dotnet</c> muxer 绝对路径；全部落空时返回字面量 "dotnet" 交给 PATH 解析。</summary>
internal static class DotnetCli
{
    internal static string ResolveDotnetMuxer(Func<string, string?> getEnvironmentVariable)
    {
        string? candidate = getEnvironmentVariable("DOTNET_HOST_PATH");
        if (candidate is { Length: > 0 } && File.Exists(candidate))
            return candidate;

        // 框架依赖安装：运行时目录 .NETCore.App/<ver> 上溯三层到 dotnet 根
        try
        {
            var runtime = RuntimeEnvironment.GetRuntimeDirectory(); // .../shared/Microsoft.NETCore.App/9.0.x/
            var appRoot = Path.GetDirectoryName(runtime);            // shared/Microsoft.NETCore.App
            var shared = Path.GetDirectoryName(appRoot);             // shared
            var dotnetRoot = Path.GetDirectoryName(shared);          // dotnet
            if (dotnetRoot is not null)
            {
                var muxer = OperatingSystem.IsWindows() ? Path.Combine(dotnetRoot, "dotnet.exe") : Path.Combine(dotnetRoot, "dotnet");
                if (File.Exists(muxer)) return muxer;
            }
        }
        catch { }

        foreach (var name in new[] { "DOTNET_ROOT", "DOTNET_ROOT(x86)" })
        {
            var root = getEnvironmentVariable(name);
            if (root is { Length: > 0 })
            {
                var muxer = OperatingSystem.IsWindows() ? Path.Combine(root, "dotnet.exe") : Path.Combine(root, "dotnet");
                if (File.Exists(muxer)) return muxer;
            }
        }

        return "dotnet";
    }
}
