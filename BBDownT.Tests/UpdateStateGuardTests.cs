using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace BBDownT.Tests;

public class UpdateStateGuardTests
{
    // 内存文件系统替身：记录文件、目录与 Unix 权限调用，便于离线断言。
    private sealed class RecordingFileOperations : LegacyLocalFileMigration.IFileOperations
    {
        private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);
        private readonly HashSet<string> dirs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, UnixFileMode> unixModes = new(StringComparer.Ordinal);
        public bool ThrowOnCreateDirectory { get; set; }

        public string this[string path] { get => files[path]; set => files[path] = value; }
        public IReadOnlyDictionary<string, UnixFileMode> UnixModes => unixModes;
        public bool DirectoryExists(string path) => dirs.Contains(path);
        public void CreateDirectory(string path) { if (ThrowOnCreateDirectory) throw new IOException("synthetic"); dirs.Add(path); }
        public bool FileExists(string path) => files.ContainsKey(path);
        public long FileLength(string path) => files.TryGetValue(path, out var c) ? c.Length : 0;
        public void Copy(string src, string dest) => files[dest] = files[src];
        public void Move(string src, string dest) { files[dest] = files[src]; files.Remove(src); }
        public void Delete(string path) { files.Remove(path); dirs.Remove(path); }
        public void SetUnixFileMode(string path, UnixFileMode mode) => unixModes[path] = mode;
    }

    private static string AppDir() => "/app";
    private static void WriteAll(RecordingFileOperations ops, string dir, params (string Name, string Content)[] entries)
    {
        foreach (var (name, content) in entries)
            ops[Path.Combine(dir, name)] = content;
    }

    [Fact]
    public void DurableStateFiles_CoversAllSixKnownStateFiles()
    {
        Assert.Equal(6, UpdateStateGuard.DurableStateFiles.Count);
        foreach (var name in new[] { "BBDownT.data", "BBDownTTV.data", "BBDownTApp.data", "BBDownT.config", "BBDownT.archives", "BBDownT.web.json" })
            Assert.Contains(name, UpdateStateGuard.DurableStateFiles);
    }

    [Fact]
    public void Snapshot_CopiesProtectedFiles_AndLeavesOriginals()
    {
        var ops = new RecordingFileOperations();
        var app = AppDir();
        WriteAll(ops, app,
            ("BBDownT.data", "SESSDATA=abc"), ("BBDownTTV.data", "tv"), ("BBDownTApp.data", "app"),
            ("BBDownT.config", "--cookie=x"), ("BBDownT.archives", "1,2"), ("BBDownT.web.json", "{}"));

        var backup = UpdateStateGuard.Snapshot(app, ops);

        Assert.NotNull(backup);
        Assert.StartsWith(UpdateStateGuard.BackupDirPrefix, Path.GetFileName(backup));
        // 原文件仍在
        foreach (var name in UpdateStateGuard.DurableStateFiles)
            Assert.True(ops.FileExists(Path.Combine(app, name)), $"{name} 原文件不应被移走");
        // 备份齐全且内容一致
        foreach (var name in UpdateStateGuard.DurableStateFiles)
            Assert.Equal(ops[Path.Combine(app, name)], ops[Path.Combine(backup!, name)]);
    }

    [Fact]
    public void Snapshot_ReturnsNull_WhenNoProtectedFileExists()
    {
        var ops = new RecordingFileOperations();
        var backup = UpdateStateGuard.Snapshot(Path.Combine(AppDir(), "empty"), ops);
        Assert.Null(backup);
    }

    [Fact]
    public void Snapshot_NeverThrows_OnDirectoryCreateFailure()
    {
        var ops = new RecordingFileOperations { ThrowOnCreateDirectory = true };
        var app = AppDir();
        WriteAll(ops, app, ("BBDownT.data", "x"));
        var backup = UpdateStateGuard.Snapshot(app, ops);
        Assert.Null(backup); // 创建失败吞掉，返回 null，不抛
    }

    [Fact]
    public void RestoreIfMissing_RestoresOnlyMissingOrEmpty()
    {
        var ops = new RecordingFileOperations();
        var app = AppDir();
        var backup = Path.Combine(app, ".update-backup-1");
        ops.CreateDirectory(backup);
        // 备份里 6 个文件齐全
        WriteAll(ops, backup,
            ("BBDownT.data", "B-data"), ("BBDownTTV.data", "B-tv"), ("BBDownTApp.data", "B-app"),
            ("BBDownT.config", "B-config"), ("BBDownT.archives", "B-arch"), ("BBDownT.web.json", "B-web"));
        // app 里：data 非空（绝不被覆盖）、config 为空（应被恢复）、其余缺失
        ops[Path.Combine(app, "BBDownT.data")] = "REAL-data";
        ops[Path.Combine(app, "BBDownT.config")] = ""; // 空文件

        UpdateStateGuard.RestoreIfMissing(app, backup, ops);

        Assert.Equal("REAL-data", ops[Path.Combine(app, "BBDownT.data")]); // 非空不被覆盖
        Assert.Equal("B-config", ops[Path.Combine(app, "BBDownT.config")]); // 空文件被恢复
        Assert.Equal("B-tv", ops[Path.Combine(app, "BBDownTTV.data")]);     // 缺失被恢复
        Assert.Equal("B-app", ops[Path.Combine(app, "BBDownTApp.data")]);
        Assert.Equal("B-arch", ops[Path.Combine(app, "BBDownT.archives")]);
        Assert.Equal("B-web", ops[Path.Combine(app, "BBDownT.web.json")]);
    }

    [Fact]
    public void RestoreIfMissing_DoesNotRestoreShimOrStore()
    {
        var ops = new RecordingFileOperations();
        var app = AppDir();
        var backup = Path.Combine(app, ".update-backup-2");
        ops.CreateDirectory(backup);
        // 备份目录里混入非白名单文件（shim / store / 记录文件）
        WriteAll(ops, backup,
            ("BBDownT.data", "ok"), ("BBDownT.exe", "shim!"), (".store", "store!"), ("BBDownT.update.json", "rec!"));

        UpdateStateGuard.RestoreIfMissing(app, backup, ops);

        Assert.True(ops.FileExists(Path.Combine(app, "BBDownT.data")));
        Assert.False(ops.FileExists(Path.Combine(app, "BBDownT.exe")));
        Assert.False(ops.FileExists(Path.Combine(app, ".store")));
        Assert.False(ops.FileExists(Path.Combine(app, "BBDownT.update.json")));
    }

    [Fact]
    public void RestoreIfMissing_SetsUserOnlyPermissionsOnUnix()
    {
        var ops = new RecordingFileOperations();
        var app = AppDir();
        var backup = Path.Combine(app, ".update-backup-3");
        ops.CreateDirectory(backup);
        ops[Path.Combine(backup, "BBDownT.data")] = "cookie";

        UpdateStateGuard.RestoreIfMissing(app, backup, ops);

        var dest = Path.Combine(app, "BBDownT.data");
        Assert.True(ops.UnixModes.TryGetValue(dest, out var mode));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [Fact]
    public void Cleanup_RemovesTheBackupDir_AndIsIdempotent()
    {
        var ops = new RecordingFileOperations();
        var backup = Path.Combine(AppDir(), ".update-backup-9");
        ops.CreateDirectory(backup);

        UpdateStateGuard.Cleanup(backup, ops);
        Assert.False(ops.DirectoryExists(backup));
        // 第二次调用（目录已不存在）不应抛
        UpdateStateGuard.Cleanup(backup, ops);
        Assert.False(ops.DirectoryExists(backup));
    }
}
