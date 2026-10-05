using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace BBDownT;

/// <summary>
/// <c>--update</c> 使用的 nupkg 源记录。落于 <c>APP_DIR/BBDownT.update.json</c>。
/// 仅作便利与兜底；每次运行都重新探测安装方式并重新扫描源目录，不以记录为信任根。
/// </summary>
internal sealed class UpdateSourceRecord
{
    public int SchemaVersion { get; set; } = 1;
    public string PackageId { get; set; } = "BBDownT";
    public string? PackageVersion { get; set; }
    public string? SourceDirectory { get; set; }
    public string? NupkgPath { get; set; }
    public string Scope { get; set; } = "Unknown";
    public string? ToolPath { get; set; }
    public string? ManifestPath { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>分离式 worker 的任务描述，落于 <c>APP_DIR/BBDownT.update.task.json</c>。</summary>
internal sealed class UpdateTask
{
    public string SourceDirectory { get; set; } = "";
    public string Scope { get; set; } = "Unknown";
    public string? ToolPath { get; set; }
    public string? ManifestPath { get; set; }
    public string? TargetVersion { get; set; }
    public bool AllowDowngrade { get; set; }
    public string? BackupDir { get; set; }
    /// <summary>用于重装的 nupkg 绝对路径；与 <see cref="InstalledDllPath"/> 一起构成落地校验的两端。</summary>
    public string? NupkgPath { get; set; }
    /// <summary>重装后工具主程序集应落地的绝对路径；本地清单安装为 null（无从校验）。</summary>
    public string? InstalledDllPath { get; set; }
}

/// <summary>分离式 worker 的结果，落于 <c>APP_DIR/BBDownT.update.result.json</c>。</summary>
internal sealed class UpdateTaskResult
{
    public int ExitCode { get; set; }
    public bool Succeeded { get; set; }
    public string Message { get; set; } = "";
    public DateTimeOffset FinishedAtUtc { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(UpdateSourceRecord))]
[JsonSerializable(typeof(UpdateTask))]
[JsonSerializable(typeof(UpdateTaskResult))]
internal partial class UpdateSourceRecordJsonContext : JsonSerializerContext { }

internal static class UpdateFileNames
{
    internal const string RecordFile = "BBDownT.update.json";
    internal const string TaskFile = "BBDownT.update.task.json";
    internal const string ResultFile = "BBDownT.update.result.json";
    internal const string LockFile = "BBDownT.update.lock";
}

internal static class UpdateSourceRecordStore
{
    internal static string PathIn(string appDir) => Path.Combine(appDir, UpdateFileNames.RecordFile);

    /// <summary>永不抛异常；缺失 / 损坏 / 未来 schema 版本均返回 null。</summary>
    internal static UpdateSourceRecord? TryLoad(string appDir)
    {
        var path = PathIn(appDir);
        try
        {
            if (!File.Exists(path)) return null;
            var record = JsonSerializer.Deserialize(
                File.ReadAllText(path), UpdateSourceRecordJsonContext.Default.UpdateSourceRecord);
            if (record is null || record.SchemaVersion <= 0 || record.SchemaVersion > CurrentSchemaVersion)
                return null;
            return record;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>原子写；失败返回 false（不抛）。</summary>
    internal static bool TrySave(string appDir, UpdateSourceRecord record)
    {
        try
        {
            Directory.CreateDirectory(appDir);
            var path = PathIn(appDir);
            var tempPath = path + $".writing-{Guid.NewGuid():N}";
            var json = JsonSerializer.Serialize(record, UpdateSourceRecordJsonContext.Default.UpdateSourceRecord);
            File.WriteAllText(tempPath, json);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(tempPath, path, true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static bool Delete(string appDir)
    {
        try
        {
            var path = PathIn(appDir);
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>机会性记录：仅当记录缺失且已探测到确定作用域时，写入一份 SourceDirectory=null 的记录固化作用域。</summary>
    internal static void EnsureRecorded(string appDir, ToolInstallInfo? install)
    {
        try
        {
            if (TryLoad(appDir) is not null) return;
            if (install is null || install.Scope == ToolInstallScope.Unknown) return;
            TrySave(appDir, new UpdateSourceRecord
            {
                PackageId = "BBDownT",
                PackageVersion = install.PackageVersion,
                Scope = install.Scope.ToString().ToLowerInvariant(),
                ToolPath = install.ToolsRoot,
                ManifestPath = install.ManifestPath,
                RecordedAtUtc = DateTimeOffset.UtcNow
            });
        }
        catch (Exception)
        {
            // 静默：记录只是便利设施
        }
    }

    private const int CurrentSchemaVersion = 1;
}

internal static class UpdateTaskStore
{
    internal static string TaskPathIn(string appDir) => Path.Combine(appDir, UpdateFileNames.TaskFile);
    internal static string ResultPathIn(string appDir) => Path.Combine(appDir, UpdateFileNames.ResultFile);

    internal static bool TryWrite(string appDir, UpdateTask task)
    {
        try
        {
            Directory.CreateDirectory(appDir);
            var path = TaskPathIn(appDir);
            var tempPath = path + $".writing-{Guid.NewGuid():N}";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(task, UpdateSourceRecordJsonContext.Default.UpdateTask));
            File.Move(tempPath, path, true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static UpdateTask? TryRead(string taskPath)
    {
        try
        {
            if (!File.Exists(taskPath)) return null;
            return JsonSerializer.Deserialize(File.ReadAllText(taskPath), UpdateSourceRecordJsonContext.Default.UpdateTask);
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static void TryRemoveTask(string taskPath)
    {
        try { if (File.Exists(taskPath)) File.Delete(taskPath); } catch (Exception) { }
    }

    /// <summary>下次正常启动时打印上一次分离式更新的结果并删除结果文件。永不抛。</summary>
    internal static void TryReport(string appDir)
    {
        var path = ResultPathIn(appDir);
        try
        {
            if (!File.Exists(path)) return;
            var result = JsonSerializer.Deserialize(File.ReadAllText(path), UpdateSourceRecordJsonContext.Default.UpdateTaskResult);
            if (result is not null)
            {
                if (result.Succeeded)
                    Console.WriteLine($"[上次更新] 已完成：{result.Message}");
                else
                    Console.Error.WriteLine($"[上次更新] 失败：{result.Message}");
            }
            File.Delete(path);
        }
        catch (Exception)
        {
            // 静默：不阻塞正常启动
        }
    }

    internal static void TryWriteResult(string appDir, UpdateTaskResult result)
    {
        try
        {
            Directory.CreateDirectory(appDir);
            var path = ResultPathIn(appDir);
            var tempPath = path + $".writing-{Guid.NewGuid():N}";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(result, UpdateSourceRecordJsonContext.Default.UpdateTaskResult));
            File.Move(tempPath, path, true);
        }
        catch (Exception)
        {
            // 静默：最坏情况只是不显示结果
        }
    }
}
