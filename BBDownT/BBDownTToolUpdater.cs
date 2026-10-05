using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BBDownT;

/// <summary>
/// <c>--update</c> 编排：重新以安装时记录的 nupkg 源重新打包并重装 dotnet tool。
/// 所有外部副作用（dotnet 进程、文件系统、版本探测）均可经参数注入替身，便于离线测试。
/// </summary>
internal static class BBDownTToolUpdater
{
    private const int PackTimeoutMinutes = 15;
    private const int UpdateTimeoutMinutes = 5;
    private const int UninstallTimeoutMinutes = 2;
    private const int InstallTimeoutMinutes = 5;
    private const int LockPollMilliseconds = 200;

    internal enum UpdateAction { Update, Reinstall }

    internal sealed record NupkgCandidate(string Path, Version Version);

    internal enum SourceOrigin { Record, Inferred }

    internal sealed record ResolvedSource(string SourceDirectory, string? RepoRoot, SourceOrigin Origin);

    // 公共入口：全部用真实实现接线。
    // 注意：entryAssemblyLocation 必须是程序集 DLL 的位置（store 包目录内，含 DotnetToolSettings.xml），
    // 而非 Environment.ProcessPath（全局工具下是 ~/.dotnet/tools/BBDownT.exe shim，那里没有 tool 标记，
    // 会让 Detect 误判为独立二进制）。single-file 发布时 Location 为空串 → 正确落入独立二进制分支。
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Location returns empty string for single-file; Detect treats empty as standalone.")]
    internal static Task<int> RunAsync() => RunAsyncCore(
        new ProcessDotnetCommandRunner(),
        new LegacyLocalFileMigration.RealFileOperations(),
        Environment.GetEnvironmentVariable,
        DotnetCli.ResolveDotnetMuxer,
        GetEntryAssemblyLocation(),
        Program.APP_DIR,
        Environment.CurrentDirectory,
        OperatingSystem.IsWindows(),
        Console.Out,
        Console.Error);

    private static string? GetEntryAssemblyLocation()
    {
        try { return typeof(BBDownTToolUpdater).Assembly.Location; }
        catch (Exception) { return null; }
    }

    // 分离式 worker 入口：由 Program.Main 在 ["--update-worker", taskPath] 时调用。
    internal static Task<int> RunWorkerAsync(string taskPath) => RunWorkerCore(
        new ProcessDotnetCommandRunner(),
        new LegacyLocalFileMigration.RealFileOperations(),
        Environment.GetEnvironmentVariable,
        DotnetCli.ResolveDotnetMuxer,
        Program.APP_DIR,
        Console.Out,
        Console.Error,
        taskPath);

    // ---- 版本决策（纯函数，可单测）----

    /// <summary>
    /// 候选版本 vs 已装版本：更高 → <see cref="UpdateAction.Update"/>（同作用域原地升级）；
    /// 相等 → 直接卸载+重装（绕过 <c>update</c> 同版本的 no-op 与 Windows 锁风险）；
    /// 更低 → 卸载+重装并加 <c>--allow-downgrade</c>。
    /// </summary>
    internal static (UpdateAction Action, bool AllowDowngrade) Decide(Version candidate, Version installed)
    {
        var cmp = candidate.CompareTo(installed);
        if (cmp > 0) return (UpdateAction.Update, false);
        if (cmp < 0) return (UpdateAction.Reinstall, true);
        return (UpdateAction.Reinstall, false);
    }

    // ---- 编排核心（可注入）----

    internal static async Task<int> RunAsyncCore(
        IDotnetCommandRunner runner,
        LegacyLocalFileMigration.IFileOperations fileOps,
        Func<string, string?> getEnvironmentVariable,
        Func<Func<string, string?>, string> resolveMuxer,
        string? entryAssemblyLocation,
        string appDir,
        string currentDirectory,
        bool isWindows,
        TextWriter stdout,
        TextWriter stderr,
        Func<ToolInstallInfo?>? detect = null,
        Func<string, NupkgCandidate?>? tryFindCandidate = null,
        Func<string, int>? spawnWorker = null)
    {
        var lockStream = TryAcquireLock(appDir);
        if (lockStream is null)
        {
            stderr.WriteLine("已有另一个更新正在进行（BBDownT.update.lock 被占用），本次跳过。");
            return 1;
        }

        using (lockStream)
        {
            // 第一个破坏性命令之前先快照受保护状态文件（cookie/config 等）。
            var backupDir = UpdateStateGuard.Snapshot(appDir, fileOps);
            var succeeded = false;
            try
            {
                var install = (detect ?? DefaultDetect(entryAssemblyLocation, getEnvironmentVariable, currentDirectory, fileOps))();
                if (install is null)
                {
                    PrintStandaloneGuidance(stderr);
                    return 1;
                }

                var source = ResolveSource(appDir, install, fileOps, stderr);
                if (source is null)
                {
                    stderr.WriteLine("无法确定 nupkg 源目录。请先执行一次：BBDownT --set-update-source <包含 BBDownT.*.nupkg 的目录>");
                    return 1;
                }

                if (source.RepoRoot is not null)
                {
                    stdout.WriteLine($"在仓库根 {source.RepoRoot} 重新打包...");
                    var pack = await RunDotnet(runner, Muxer(resolveMuxer, getEnvironmentVariable),
                        DotnetToolInstall.PackArguments(source.RepoRoot), source.RepoRoot,
                        TimeSpan.FromMinutes(PackTimeoutMinutes), stdout, stderr, CancellationToken.None);
                    if (pack.ExitCode != 0)
                    {
                        stderr.WriteLine("dotnet pack 失败，未改动现有安装。请检查生成日志后重试。");
                        return 1;
                    }
                }
                else
                {
                    stdout.WriteLine($"源目录不在仓库内（{source.SourceDirectory}），已跳过重新打包。");
                }

                var candidate = (tryFindCandidate ?? DefaultScan())(source.SourceDirectory);
                if (candidate is null)
                {
                    stderr.WriteLine($"源目录 {source.SourceDirectory} 中没有找到 BBDownT.*.nupkg，无法更新。");
                    return 1;
                }

                var installedVersion = ParseInstalledVersion(install);
                var (action, allowDowngrade) = Decide(candidate.Version, installedVersion);
                stdout.WriteLine($"当前已装 {installedVersion}，源内候选 {candidate.Version}。");

                // Windows + 从 store 内运行 → 分离式 worker（避免文件锁），父进程立即返回。
                if (DotnetToolInstall.RequiresDetachWorker(isWindows, entryAssemblyLocation, install))
                {
                    var task = new UpdateTask
                    {
                        SourceDirectory = source.SourceDirectory,
                        Scope = install.Scope.ToString().ToLowerInvariant(),
                        ToolPath = install.ToolsRoot,
                        ManifestPath = install.ManifestPath,
                        TargetVersion = candidate.Version.ToString(),
                        AllowDowngrade = allowDowngrade,
                        BackupDir = backupDir
                    };
                    UpdateTaskStore.TryWrite(appDir, task);
                    // 分离路径同样回写记录（计划：--update 成功解析源后回写），否则记录里的版本永远是旧值
                    UpdateSourceRecordStore.TrySave(appDir, BuildRecord(source, install, candidate));
                    var code = (spawnWorker ?? DefaultSpawn())(UpdateTaskStore.TaskPathIn(appDir));
                    stdout.WriteLine("已启动后台更新（分离进程），不会阻塞当前命令。");
                    stdout.WriteLine($"完成后会写入 {UpdateFileNames.LogFile}；下次运行 BBDownT 时会显示结果。");
                    return code;
                }

                succeeded = await ExecuteAsync(runner, Muxer(resolveMuxer, getEnvironmentVariable),
                    install, source, candidate, action, allowDowngrade, stdout, stderr);
                if (succeeded)
                {
                    UpdateSourceRecordStore.TrySave(appDir, BuildRecord(source, install, candidate));
                    stdout.WriteLine("更新完成，下次启动生效。");
                }
                else
                {
                    stderr.WriteLine($"更新失败：原 cookie/config 已备份在 {backupDir ?? "(无)"}，可手动恢复。");
                }
                return succeeded ? 0 : 1;
            }
            catch (Exception ex)
            {
                PrintFailure(ex, stderr);
                return 1;
            }
            finally
            {
                // 即便成功也兜底恢复（防止 SDK 误删同目录状态文件）；仅整体成功才清理备份。
                UpdateStateGuard.RestoreIfMissing(appDir, backupDir, fileOps);
                if (succeeded) UpdateStateGuard.Cleanup(backupDir, fileOps);
            }
        }
    }

    // ---- 进程内重装 ----

    private static async Task<bool> ExecuteAsync(
        IDotnetCommandRunner runner,
        string muxer,
        ToolInstallInfo install,
        ResolvedSource source,
        NupkgCandidate candidate,
        UpdateAction action,
        bool allowDowngrade,
        TextWriter stdout,
        TextWriter stderr)
    {
        var workDir = install.Scope == ToolInstallScope.Local ? install.ManifestDirectory : null;

        if (action == UpdateAction.Update)
        {
            var update = await RunDotnet(runner, muxer,
                DotnetToolInstall.UpdateArguments(source.SourceDirectory, install), workDir,
                TimeSpan.FromMinutes(UpdateTimeoutMinutes), stdout, stderr, CancellationToken.None);
            if (update.ExitCode == 0) return true;
            stdout.WriteLine("dotnet tool update 失败，改为卸载后重装。");
        }

        // 卸载：退出码仅告警，继续 install 兜底（应对 Windows 锁等部分失败）。
        var uninstall = await RunDotnet(runner, muxer,
            DotnetToolInstall.UninstallArguments(install), workDir,
            TimeSpan.FromMinutes(UninstallTimeoutMinutes), stdout, stderr, CancellationToken.None);
        if (uninstall.ExitCode != 0)
            stderr.WriteLine($"dotnet tool uninstall 退出码 {uninstall.ExitCode}（告警，继续重装）。");

        var installResult = await RunDotnet(runner, muxer,
            DotnetToolInstall.InstallArguments(source.SourceDirectory, install, allowDowngrade), workDir,
            TimeSpan.FromMinutes(InstallTimeoutMinutes), stdout, stderr, CancellationToken.None);
        return installResult.ExitCode == 0;
    }

    // ---- 分离式 worker 核心 ----

    internal static async Task<int> RunWorkerCore(
        IDotnetCommandRunner runner,
        LegacyLocalFileMigration.IFileOperations fileOps,
        Func<string, string?> getEnvironmentVariable,
        Func<Func<string, string?>, string> resolveMuxer,
        string appDir,
        TextWriter stdout,
        TextWriter stderr,
        string taskPath)
    {
        var task = UpdateTaskStore.TryRead(taskPath);
        if (task is null)
        {
            stderr.WriteLine("更新任务文件缺失或无法读取，后台更新中止。");
            return 1;
        }

        // 轮询独占锁：父进程退出并释放后才继续（Windows 下 FileShare.None 会直接抛，需重试）。
        var lockPath = Path.Combine(appDir, UpdateFileNames.LockFile);
        using var _ = await AcquireLockOrWaitAsync(lockPath);
        if (_ is null)
        {
            stderr.WriteLine("等待更新锁超时，后台更新中止。");
            return 1;
        }

        try
        {
            var scope = Enum.TryParse<ToolInstallScope>(task.Scope, true, out var s) ? s : ToolInstallScope.Unknown;
            var install = new ToolInstallInfo(scope, task.ToolPath ?? "", null, null, "BBDownT", task.TargetVersion, task.ManifestPath);
            var workDir = scope == ToolInstallScope.Local ? install.ManifestDirectory : null;

            await RunDotnet(runner, Muxer(resolveMuxer, getEnvironmentVariable),
                DotnetToolInstall.UninstallArguments(install), workDir,
                TimeSpan.FromMinutes(UninstallTimeoutMinutes), stdout, stderr, CancellationToken.None);
            var installResult = await RunDotnet(runner, Muxer(resolveMuxer, getEnvironmentVariable),
                DotnetToolInstall.InstallArguments(task.SourceDirectory, install, task.AllowDowngrade), workDir,
                TimeSpan.FromMinutes(InstallTimeoutMinutes), stdout, stderr, CancellationToken.None);

            var ok = installResult.ExitCode == 0;
            // 重装后补缺恢复受保护状态文件（绝不覆盖），仅成功清理备份。
            UpdateStateGuard.RestoreIfMissing(appDir, task.BackupDir, fileOps);
            if (ok) UpdateStateGuard.Cleanup(task.BackupDir, fileOps);
            UpdateTaskStore.TryWriteResult(appDir, new UpdateTaskResult
            {
                ExitCode = installResult.ExitCode,
                Succeeded = ok,
                Message = ok ? "dotnet tool 已重装" : "dotnet tool 重装失败，请查看日志",
                FinishedAtUtc = DateTimeOffset.UtcNow
            });
            UpdateTaskStore.TryRemoveTask(taskPath); // 任务已消费，清理残留
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            UpdateTaskStore.TryWriteResult(appDir, new UpdateTaskResult
            {
                ExitCode = -1,
                Succeeded = false,
                Message = $"后台更新异常：{ex.Message}",
                FinishedAtUtc = DateTimeOffset.UtcNow
            });
            UpdateTaskStore.TryRemoveTask(taskPath);
            return 1;
        }
    }

    // ---- 源解析 ----

    private static ResolvedSource? ResolveSource(string appDir, ToolInstallInfo install, LegacyLocalFileMigration.IFileOperations fileOps, TextWriter stderr)
    {
        var record = UpdateSourceRecordStore.TryLoad(appDir);
        string? sourceDir = null;
        var origin = SourceOrigin.Inferred;

        if (record?.SourceDirectory is { Length: > 0 } recorded && fileOps.DirectoryExists(recorded))
        {
            sourceDir = recorded;
            origin = SourceOrigin.Record;
        }
        else if (install.StoreRoot is not null)
        {
            var repo = RepoLayout.FindRepoRoot(install.StoreRoot, fileOps.DirectoryExists, fileOps.FileExists);
            if (repo is not null)
                sourceDir = Path.Combine(repo, "artifacts");
        }

        if (sourceDir is null)
        {
            if (record is not null)
                stderr.WriteLine("记录中的源目录已不存在，且无法从安装位置推断仓库。");
            return null;
        }

        // 无论来源，都尝试从源目录上溯仓库根以决定是否重新打包。
        var repoRoot = RepoLayout.FindRepoRoot(sourceDir, fileOps.DirectoryExists, fileOps.FileExists);
        return new ResolvedSource(sourceDir, repoRoot, origin);
    }

    // ---- 默认实现（真实）----

    private static Func<ToolInstallInfo?> DefaultDetect(
        string? entryAssemblyLocation, Func<string, string?> getEnvironmentVariable, string currentDirectory,
        LegacyLocalFileMigration.IFileOperations fileOps) => () =>
        DotnetToolInstall.Detect(entryAssemblyLocation, DotnetToolInstall.GlobalToolsRoot(getEnvironmentVariable),
            currentDirectory, fileOps.DirectoryExists, fileOps.FileExists);

    private static Func<string, NupkgCandidate?> DefaultScan() => dir =>
    {
        var path = NupkgInspector.FindCandidateNupkg(dir);
        if (path is null) return null;
        try { return new NupkgCandidate(path, NupkgInspector.ReadPackageVersion(path)); }
        catch (Exception) { return null; }
    };

    private static Func<string, int> DefaultSpawn() => taskPath =>
    {
        var psi = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? "dotnet",
            ArgumentList = { "--update-worker", taskPath },
            UseShellExecute = false,
            CreateNoWindow = true
        };
        try
        {
            using var process = Process.Start(psi);
            return 0; // 不等待：worker 自行完成并写结果
        }
        catch (Exception)
        {
            return 1;
        }
    };

    private static string Muxer(Func<Func<string, string?>, string> resolveMuxer, Func<string, string?> getEnv)
        => resolveMuxer(getEnv);

    private static Version ParseInstalledVersion(ToolInstallInfo install)
    {
        if (install.PackageVersion is not null && Version.TryParse(install.PackageVersion, out var fromStore))
            return fromStore;
        var asm = Assembly.GetExecutingAssembly().GetName().Version;
        return asm is not null ? new Version(asm.Major, asm.Minor, asm.Build) : new Version(0, 0, 0);
    }

    private static UpdateSourceRecord BuildRecord(ResolvedSource source, ToolInstallInfo install, NupkgCandidate candidate) => new UpdateSourceRecord
    {
        SchemaVersion = 1,
        PackageId = "BBDownT",
        PackageVersion = candidate.Version.ToString(),
        SourceDirectory = source.SourceDirectory,
        NupkgPath = candidate.Path,
        Scope = install.Scope.ToString().ToLowerInvariant(),
        ToolPath = install.ToolsRoot,
        ManifestPath = install.ManifestPath,
        RecordedAtUtc = DateTimeOffset.UtcNow
    };

    // ---- 进程执行与锁 ----

    private static async Task<DotnetCommandResult> RunDotnet(
        IDotnetCommandRunner runner, string muxer, IReadOnlyList<string> arguments, string? workingDirectory,
        TimeSpan timeout, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        if (!File.Exists(muxer) && muxer != "dotnet")
        {
            stderr.WriteLine("未找到 dotnet 可执行文件，请确认已安装 .NET 9 SDK：dotnet --list-sdks");
            return new DotnetCommandResult(-1, "", "dotnet muxer 未找到");
        }

        var request = new DotnetCommandRequest(muxer, arguments, workingDirectory, timeout, StreamOutput: true);
        try
        {
            return await runner.RunAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or IOException)
        {
            stderr.WriteLine("未找到 dotnet 可执行文件，请确认已安装 .NET 9 SDK：dotnet --list-sdks");
            return new DotnetCommandResult(-1, "", ex.Message);
        }
    }

    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Update lock is file based and works under single-file.")]
    private static FileStream? TryAcquireLock(string appDir)
    {
        try
        {
            var lockPath = Path.Combine(appDir, UpdateFileNames.LockFile);
            return new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<FileStream?> AcquireLockOrWaitAsync(string lockPath)
    {
        var deadline = DateTime.UtcNow.AddMinutes(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                return new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (Exception)
            {
                await Task.Delay(LockPollMilliseconds);
            }
        }
        return null;
    }

    // ---- 文案 ----

    private static void PrintStandaloneGuidance(TextWriter stderr)
    {
        stderr.WriteLine("当前不是以 dotnet tool 形式安装（独立二进制或 dotnet run），不会改写您的文件。");
        stderr.WriteLine("如需以本机 nupkg 重装，请在仓库内执行：dotnet pack BBDownT/BBDownT.csproj -c Release -o artifacts");
        stderr.WriteLine("随后：dotnet tool install --global --source ./artifacts BBDownT");
    }

    private static void PrintFailure(Exception ex, TextWriter stderr)
    {
        if (ex is Win32Exception or FileNotFoundException or IOException)
            stderr.WriteLine("未找到 dotnet 可执行文件，请确认已安装 .NET 9 SDK：dotnet --list-sdks");
        else
            stderr.WriteLine($"更新过程中发生异常：{ex.Message}");
    }
}
