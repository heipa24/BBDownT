using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BBDownT.Tests;

public class ToolUpdaterTests
{
    // 记录所有 dotnet 请求，并按脚本返回结果（离线，无需真实 dotnet）。
    private sealed class RecordingDotnetRunner : IDotnetCommandRunner
    {
        public List<DotnetCommandRequest> Requests { get; } = new();
        public Func<DotnetCommandRequest, DotnetCommandResult> Script { get; set; } = _ => new DotnetCommandResult(0, "", "");
        public Task<DotnetCommandResult> RunAsync(DotnetCommandRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(Script(request));
        }
    }

    // 与 UpdateStateGuardTests 同构的内存文件系统。路径做分隔符归一化，贴近真实文件系统：
    // 真实 File.Exists 会忽略 / 与 \ 的差异，而字典精确匹配不会，否则仓库探测（含 forward-slash 的
    // ProjectRelativePath）在内存桩里永远命中不了 RegisterRepoRoot 写下的 backslash 键。
    private sealed class RecordingFileOperations : LegacyLocalFileMigration.IFileOperations
    {
        private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);
        private readonly HashSet<string> dirs = new(StringComparer.Ordinal);
        public IReadOnlyCollection<string> Directories => dirs;
        public string this[string path] { get => files[Norm(path)]; set => files[Norm(path)] = value; }
        public bool FileExists(string path) => files.ContainsKey(Norm(path));
        public bool DirectoryExists(string path) => dirs.Contains(Norm(path));
        public long FileLength(string path) => files.TryGetValue(Norm(path), out var c) ? c.Length : 0;
        public void Copy(string src, string dest) => files[Norm(dest)] = files[Norm(src)];
        public void Move(string src, string dest) { files[Norm(dest)] = files[Norm(src)]; files.Remove(Norm(src)); }
        public void Delete(string path) { var n = Norm(path); files.Remove(n); dirs.Remove(n); }
        public void CreateDirectory(string path) => dirs.Add(Norm(path));
        public void SetUnixFileMode(string path, UnixFileMode mode) { }
        // 测试用：模拟 SDK 在重装过程中删除了受保护文件
        public void SimulateDelete(string path) => files.Remove(Norm(path));
        private static string Norm(string p) => p.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static string CreateAppDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "bbupd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static ToolInstallInfo GlobalInstall(string version = "2.1.4") =>
        new(ToolInstallScope.Global, "C:\\tools", "C:\\tools\\.store", null, "bbdownt", version);
    private static ToolInstallInfo LocalInstall(string manifestDir) =>
        new(ToolInstallScope.Local, manifestDir, null, null, null, null, Path.Combine(manifestDir, ".config", "dotnet-tools.json"));

    private static void RegisterRepoRoot(RecordingFileOperations ops, string repoDir) =>
        ops[Path.Combine(repoDir, "BBDownT", "BBDownT.csproj")] = "<Project/>";

    private static BBDownTToolUpdater.NupkgCandidate Candidate(string version = "2.1.5", string path = "BBDownT.2.1.5.nupkg") =>
        new(path, Version.Parse(version));

    private static async Task<int> Run(
        RecordingDotnetRunner runner,
        RecordingFileOperations ops,
        string appDir,
        string? entryAssemblyLocation,
        bool isWindows,
        Func<ToolInstallInfo?>? detect = null,
        Func<string, BBDownTToolUpdater.NupkgCandidate?>? tryFindCandidate = null,
        Func<string, int>? spawnWorker = null,
        Func<Func<string, string?>, string>? resolveMuxer = null,
        string currentDirectory = "",
        TextWriter? stdout = null,
        TextWriter? stderr = null)
    {
        return await BBDownTToolUpdater.RunAsyncCore(
            runner, ops, _ => null,
            resolveMuxer ?? (_ => "dotnet"),
            entryAssemblyLocation, appDir, currentDirectory, isWindows,
            stdout ?? TextWriter.Null, stderr ?? TextWriter.Null,
            detect, tryFindCandidate, spawnWorker);
    }

    [Fact]
    public void Decide_SelectsUpdateReinstallAndDowngrade()
    {
        Assert.Equal((BBDownTToolUpdater.UpdateAction.Update, false), BBDownTToolUpdater.Decide(new Version(2, 1, 5), new Version(2, 1, 4)));
        Assert.Equal((BBDownTToolUpdater.UpdateAction.Reinstall, false), BBDownTToolUpdater.Decide(new Version(2, 1, 4), new Version(2, 1, 4)));
        Assert.Equal((BBDownTToolUpdater.UpdateAction.Reinstall, true), BBDownTToolUpdater.Decide(new Version(2, 1, 3), new Version(2, 1, 4)));
    }

    [Fact]
    public async Task Run_ReportsStandaloneInstructions_WhenNoToolIsDetected()
    {
        var runner = new RecordingDotnetRunner();
        var code = await Run(runner, new RecordingFileOperations(), CreateAppDir(), null, true, detect: () => null);
        Assert.Equal(1, code);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Run_ReportsHowToRecordTheSource_WhenResolutionFails()
    {
        var appDir = CreateAppDir();
        var stderr = new StringWriter();
        var code = await Run(new RecordingDotnetRunner(), new RecordingFileOperations(), appDir, "x", true,
            detect: () => new ToolInstallInfo(ToolInstallScope.Global, "C:\\tools", null, null, null, null),
            stderr: stderr);
        Assert.Equal(1, code);
        Assert.Contains("--set-update-source", stderr.ToString());
    }

    [Fact]
    public async Task Run_PacksInTheRepositoryRoot_BeforeAnyToolCommand()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        var repo = Path.Combine(appDir, "repo");
        RegisterRepoRoot(ops, repo);
        WriteRecord(appDir, Path.Combine(repo, "artifacts"), ops: ops); // 记录源在仓库内，且目录确实存在

        var runner = new RecordingDotnetRunner { Script = _ => new DotnetCommandResult(0, "", "") };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => GlobalInstall(), tryFindCandidate: _ => Candidate("2.1.5"));

        Assert.Equal(0, code);
        Assert.NotEmpty(runner.Requests);
        var pack = runner.Requests[0];
        Assert.Equal("pack", pack.Arguments[0]);
        Assert.Equal(repo, pack.WorkingDirectory); // 打包工作目录必须是仓库根
    }

    [Fact]
    public async Task Run_SkipsPack_WhenTheSourceDirectoryIsOutsideARepository()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        WriteRecord(appDir, @"C:\outside\artifacts", ops: ops); // 不在任何仓库内，但目录确实存在

        var runner = new RecordingDotnetRunner { Script = _ => new DotnetCommandResult(0, "", "") };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => GlobalInstall(), tryFindCandidate: _ => Candidate("2.1.5"));

        Assert.Equal(0, code);
        Assert.DoesNotContain(runner.Requests, r => r.Arguments.Count > 0 && r.Arguments[0] == "pack");
    }

    [Fact]
    public async Task Run_UsesToolUpdate_WhenTheCandidateIsNewer()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        RecordSource(appDir, ops);

        var runner = new RecordingDotnetRunner { Script = _ => new DotnetCommandResult(0, "", "") };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => GlobalInstall("2.1.4"), tryFindCandidate: _ => Candidate("2.1.5"));

        Assert.Equal(0, code);
        var update = Assert.Single(runner.Requests.Where(r => r.Arguments.Count >= 2 && r.Arguments[1] == "update"));
        Assert.Equal("BBDownT", update.Arguments[2]);
        Assert.Contains("--global", update.Arguments);
    }

    [Fact]
    public async Task Run_Reinstalls_WhenTheCandidateVersionEqualsTheInstalledVersion()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        RecordSource(appDir, ops);

        var runner = new RecordingDotnetRunner { Script = _ => new DotnetCommandResult(0, "", "") };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => GlobalInstall("2.1.4"), tryFindCandidate: _ => Candidate("2.1.4"));

        Assert.Equal(0, code);
        // 同版本：直接卸载 + 重装，绝不调用 update
        Assert.DoesNotContain(runner.Requests, r => r.Arguments.Count >= 2 && r.Arguments[1] == "update");
        Assert.Contains(runner.Requests, r => r.Arguments.Count >= 2 && r.Arguments[1] == "uninstall");
        Assert.Contains(runner.Requests, r => r.Arguments.Count >= 2 && r.Arguments[1] == "install");
    }

    [Fact]
    public async Task Run_FallsBackToReinstall_WhenToolUpdateFails()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        RecordSource(appDir, ops);

        var runner = new RecordingDotnetRunner
        {
            Script = r => r.Arguments.Count >= 2 && r.Arguments[1] == "update"
                ? new DotnetCommandResult(1, "", "update failed")
                : new DotnetCommandResult(0, "", "")
        };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => GlobalInstall("2.1.4"), tryFindCandidate: _ => Candidate("2.1.5"));

        Assert.Equal(0, code);
        Assert.Equal(3, runner.Requests.Count);
        Assert.Equal("update", runner.Requests[0].Arguments[1]);
        Assert.Equal("uninstall", runner.Requests[1].Arguments[1]);
        Assert.Equal("install", runner.Requests[2].Arguments[1]);
    }

    [Fact]
    public async Task Run_AddsAllowDowngrade_WhenTheCandidateIsOlder()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        RecordSource(appDir, ops);

        var runner = new RecordingDotnetRunner { Script = _ => new DotnetCommandResult(0, "", "") };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => GlobalInstall("2.1.4"), tryFindCandidate: _ => Candidate("2.1.3"));

        Assert.Equal(0, code);
        var install = Assert.Single(runner.Requests.Where(r => r.Arguments.Count >= 2 && r.Arguments[1] == "install"));
        Assert.Contains("--allow-downgrade", install.Arguments);
    }

    [Fact]
    public async Task Run_ContinuesToInstall_WhenUninstallFails()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        RecordSource(appDir, ops);

        var runner = new RecordingDotnetRunner
        {
            Script = r => r.Arguments.Count >= 2 && r.Arguments[1] == "uninstall"
                ? new DotnetCommandResult(1, "", "locked")
                : new DotnetCommandResult(0, "", "")
        };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => GlobalInstall("2.1.4"), tryFindCandidate: _ => Candidate("2.1.4"));

        Assert.Equal(0, code); // uninstall 仅告警，install 成功
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task Run_ReturnsFailure_WhenTheReinstallFails()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        RecordSource(appDir, ops);

        var runner = new RecordingDotnetRunner { Script = _ => new DotnetCommandResult(1, "", "boom") };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => GlobalInstall("2.1.4"), tryFindCandidate: _ => Candidate("2.1.4"));

        Assert.Equal(1, code);
    }

    [Fact]
    public async Task Run_StopsBeforeAnyToolCommand_WhenPackFails()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        var repo = Path.Combine(appDir, "repo");
        RegisterRepoRoot(ops, repo);
        WriteRecord(appDir, Path.Combine(repo, "artifacts"), ops: ops);

        var runner = new RecordingDotnetRunner { Script = _ => new DotnetCommandResult(1, "", "pack failed") };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => GlobalInstall(), tryFindCandidate: _ => Candidate("2.1.5"));

        Assert.Equal(1, code);
        Assert.Single(runner.Requests); // 只有 pack，未进入 uninstall/install
        Assert.Equal("pack", runner.Requests[0].Arguments[0]);
    }

    [Fact]
    public async Task Run_ReturnsFailure_WhenTheSourceDirectoryHasNoNupkg()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        RecordSource(appDir, ops);

        var runner = new RecordingDotnetRunner { Script = _ => new DotnetCommandResult(0, "", "") };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => GlobalInstall(), tryFindCandidate: _ => null);

        Assert.Equal(1, code);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Run_RefreshesTheRecordFile_AfterASuccessfulRun()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        WriteRecord(appDir, @"C:\src\artifacts", ops: ops);

        var runner = new RecordingDotnetRunner { Script = _ => new DotnetCommandResult(0, "", "") };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => GlobalInstall("2.1.4"), tryFindCandidate: _ => Candidate("2.1.5"));

        Assert.Equal(0, code);
        Assert.True(File.Exists(UpdateSourceRecordStore.PathIn(appDir)));
    }

    [Fact]
    public async Task Run_PrefersTheRecordedSource_OverInference()
    {
        var appDir = CreateAppDir();
        var recorded = @"C:\recorded\artifacts";
        var ops = new RecordingFileOperations();
        WriteRecord(appDir, recorded, ops: ops);
        // 即便能从 store 推断出一个不同目录，也应使用记录中的源
        var storeRepo = Path.Combine(appDir, "storerepo");
        RegisterRepoRoot(ops, storeRepo);

        var runner = new RecordingDotnetRunner { Script = _ => new DotnetCommandResult(0, "", "") };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => new ToolInstallInfo(ToolInstallScope.Global, "C:\\tools", Path.Combine(storeRepo, ".store"), null, "bbdownt", "2.1.4"),
            tryFindCandidate: _ => Candidate("2.1.5"));

        Assert.Equal(0, code);
        var install = Assert.Single(runner.Requests.Where(r => r.Arguments.Count >= 2 && r.Arguments[1] == "update"));
        var sourceArg = install.Arguments[install.Arguments.ToList().IndexOf("--source") + 1];
        Assert.Equal(recorded, sourceArg);
    }

    [Fact]
    public async Task Run_PrefersInference_WhenTheRecordIsAbsent()
    {
        var appDir = CreateAppDir(); // 无记录文件
        var ops = new RecordingFileOperations();
        var repo = Path.Combine(appDir, "repo");
        RegisterRepoRoot(ops, repo);
        var inferred = Path.Combine(repo, "artifacts");

        var runner = new RecordingDotnetRunner { Script = _ => new DotnetCommandResult(0, "", "") };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => new ToolInstallInfo(ToolInstallScope.Global, "C:\\tools", Path.Combine(repo, ".store"), null, "bbdownt", "2.1.4"),
            tryFindCandidate: _ => Candidate("2.1.5"));

        Assert.Equal(0, code);
        var update = Assert.Single(runner.Requests.Where(r => r.Arguments.Count >= 2 && r.Arguments[1] == "update"));
        var sourceArg = update.Arguments[update.Arguments.ToList().IndexOf("--source") + 1];
        Assert.Equal(inferred, sourceArg);
    }

    [Fact]
    public async Task Run_UsesTheDetectedScope_EvenWhenTheRecordDisagrees()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        WriteRecord(appDir, @"C:\src\artifacts", scope: "toolpath", ops: ops); // 记录说 toolpath

        var runner = new RecordingDotnetRunner { Script = _ => new DotnetCommandResult(0, "", "") };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => GlobalInstall("2.1.4"), tryFindCandidate: _ => Candidate("2.1.4"));

        Assert.Equal(0, code);
        var install = Assert.Single(runner.Requests.Where(r => r.Arguments.Count >= 2 && r.Arguments[1] == "install"));
        Assert.Contains("--global", install.Arguments);
        Assert.DoesNotContain(install.Arguments, a => a == "--tool-path");
    }

    [Fact]
    public async Task Run_WritesATaskFile_AndReturnsSuccess_WhenDetachIsRequired()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        ops[Path.Combine(appDir, "BBDownT.data")] = "cookie";
        // 记录一个“不在仓库内”的源目录，避免触发 pack，从而父进程直接走 detach 分支。
        WriteRecord(appDir, @"C:\repo\artifacts", ops: ops);

        var packageDir = @"C:\repo\.store\bbdownt\2.1.4\bbdownt\2.1.4";
        var entry = Path.Combine(packageDir, "tools", "net9.0", "any", "BBDownT.dll");
        var spawnCalls = new List<string>();

        var runner = new RecordingDotnetRunner { Script = _ => new DotnetCommandResult(0, "", "") };
        var code = await Run(runner, ops, appDir, entry, true,
            detect: () => new ToolInstallInfo(ToolInstallScope.Global, "C:\\tools", @"C:\repo\.store", packageDir, "bbdownt", "2.1.4"),
            tryFindCandidate: _ => Candidate("2.1.5"),
            spawnWorker: taskPath => { spawnCalls.Add(taskPath); return 0; });

        Assert.Equal(0, code);
        Assert.Empty(runner.Requests); // 父进程不执行任何 dotnet 命令（包括 pack）
        Assert.True(File.Exists(UpdateTaskStore.TaskPathIn(appDir)));
        Assert.Single(spawnCalls);
    }

    [Fact]
    public async Task Run_ReportsAnAlreadyRunningUpdate_WhenTheLockIsHeld()
    {
        var appDir = CreateAppDir();
        var lockPath = Path.Combine(appDir, UpdateFileNames.LockFile);
        using var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        var runner = new RecordingDotnetRunner();
        var code = await Run(runner, new RecordingFileOperations(), appDir, "x", true,
            detect: () => GlobalInstall(), tryFindCandidate: _ => Candidate("2.1.5"));
        Assert.Equal(1, code);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Run_TranslatesAMissingDotnetMuxer_IntoGuidance()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        WriteRecord(appDir, @"C:\src\artifacts", ops: ops); // 先解析到源，才能走到 dotnet 调用
        var stderr = new StringWriter();
        var runner = new RecordingDotnetRunner { Script = _ => new DotnetCommandResult(0, "", "") };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => GlobalInstall("2.1.4"), tryFindCandidate: _ => Candidate("2.1.4"),
            resolveMuxer: _ => @"C:\nope\dotnet.exe", stderr: stderr);

        Assert.Equal(1, code);
        Assert.Contains("未找到 dotnet", stderr.ToString());
    }

    [Fact]
    public async Task Run_BacksUpStateFilesBeforeAnyToolCommand_AndRestoresAfterReinstall()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        ops[Path.Combine(appDir, "BBDownT.data")] = "SESSDATA=tok";
        RecordSource(appDir, ops);

        // 模拟 uninstall/install 过程中 SDK 删除了受保护文件
        var runner = new RecordingDotnetRunner
        {
            Script = r =>
            {
                if (r.Arguments.Count >= 2 && r.Arguments[1] == "install")
                    ops.SimulateDelete(Path.Combine(appDir, "BBDownT.data"));
                return new DotnetCommandResult(0, "", "");
            }
        };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => GlobalInstall("2.1.4"), tryFindCandidate: _ => Candidate("2.1.4"));

        Assert.Equal(0, code);
        // 重装后从备份补缺：cookie 仍在且内容一致（证明快照在最前、恢复在最后兜底）。
        Assert.True(ops.FileExists(Path.Combine(appDir, "BBDownT.data")));
        Assert.Equal("SESSDATA=tok", ops[Path.Combine(appDir, "BBDownT.data")]);
    }

    [Fact]
    public async Task Run_KeepsTheBackupDirectoryOnFailure_AndMentionsIt()
    {
        var appDir = CreateAppDir();
        var ops = new RecordingFileOperations();
        ops[Path.Combine(appDir, "BBDownT.data")] = "tok";
        RecordSource(appDir, ops);
        var stderr = new StringWriter();

        var runner = new RecordingDotnetRunner { Script = _ => new DotnetCommandResult(1, "", "boom") };
        var code = await Run(runner, ops, appDir, "x", true,
            detect: () => GlobalInstall("2.1.4"), tryFindCandidate: _ => Candidate("2.1.5"),
            stderr: stderr);

        Assert.Equal(1, code);
        // 失败：备份目录不被清理，且提示里出现备份路径
        Assert.Contains(ops.Directories, d => d.Contains(UpdateStateGuard.BackupDirPrefix));
        Assert.Contains("备份", stderr.ToString());
    }

    // 写一份真实记录文件（落盘，供 UpdateSourceRecordStore 读取），并（当提供 ops 时）在内存桩里登记源目录为“已存在”。
    // 用 TrySave 走真正的 JSON 序列化，避免手写 JSON 把 Windows 反斜杠写成非法转义。
    private static void WriteRecord(string appDir, string sourceDirectory, string scope = "global", RecordingFileOperations? ops = null)
    {
        Directory.CreateDirectory(appDir);
        UpdateSourceRecordStore.TrySave(appDir, new UpdateSourceRecord
        {
            SchemaVersion = 1,
            PackageId = "BBDownT",
            PackageVersion = "2.1.4",
            SourceDirectory = sourceDirectory,
            Scope = scope,
            RecordedAtUtc = DateTimeOffset.UtcNow
        });
        ops?.CreateDirectory(sourceDirectory);
    }

    // 便捷：登记一个不在仓库内的平面源目录（跳过 pack），用于普通 update/reinstall 用例。
    private static void RecordSource(string appDir, RecordingFileOperations ops, string sourceDir = @"C:\src\artifacts", string scope = "global")
        => WriteRecord(appDir, sourceDir, scope, ops);
}
