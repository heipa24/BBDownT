using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace BBDownT;

/// <summary>
/// 更新期间受保护状态文件（cookie / config / 登录态 / 历史 / 指纹）的快照与补缺恢复。
/// 由 <c>--update</c> 在第一个破坏性 dotnet tool 命令之前调用 <see cref="Snapshot"/>，
/// 在 finally / worker 完成后调用 <see cref="RestoreIfMissing"/>；仅当整体成功才 <see cref="Cleanup"/>。
/// 所有公开方法永不抛异常：更新流程的健壮性优先于备份成败。
/// </summary>
internal static class UpdateStateGuard
{
    /// <summary>固定白名单；不扫全盘，绝不误伤放在 APP_DIR 的 ffmpeg.exe 等其它文件。</summary>
    internal static readonly IReadOnlyList<string> DurableStateFiles = new[]
    {
        "BBDownT.data", "BBDownTTV.data", "BBDownTApp.data",
        "BBDownT.config", "BBDownT.archives", "BBDownT.web.json"
    };

    internal const string BackupDirPrefix = ".update-backup-";

    /// <summary>
    /// 复制每个存在的受保护文件到 <c>&lt;appDir&gt;/.update-backup-&lt;时间戳&gt;</c>（原文件保留）。
    /// 无可保护文件返回 <c>null</c>；任何异常都吞掉并返回 <c>null</c>（快照失败不应阻断更新）。
    /// </summary>
    internal static string? Snapshot(string appDir, LegacyLocalFileMigration.IFileOperations? operations = null)
    {
        operations ??= new LegacyLocalFileMigration.RealFileOperations();
        try
        {
            var present = new List<string>(DurableStateFiles.Count);
            foreach (var name in DurableStateFiles)
            {
                if (operations.FileExists(Path.Combine(appDir, name)))
                    present.Add(name);
            }
            if (present.Count == 0)
                return null;

            var baseDir = Path.Combine(appDir, BackupDirPrefix + DateTime.Now.ToString("yyyyMMddHHmmssffff"));
            var backupDir = baseDir;
            var suffix = 1;
            while (operations.DirectoryExists(backupDir) || operations.FileExists(backupDir))
                backupDir = baseDir + "-" + suffix++;
            operations.CreateDirectory(backupDir);

            foreach (var name in present)
            {
                operations.Copy(Path.Combine(appDir, name), Path.Combine(backupDir, name));
            }
            return backupDir;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 仅补缺：目标已存在且长度 &gt; 0 绝不覆盖；缺失 / 空才从备份复制回，并补 Unix 0600 权限。
    /// 备份目录不存在或 <paramref name="backupDir"/> 为 <c>null</c> 时静默返回；永不抛。
    /// </summary>
    internal static void RestoreIfMissing(string appDir, string? backupDir, LegacyLocalFileMigration.IFileOperations? operations = null)
    {
        operations ??= new LegacyLocalFileMigration.RealFileOperations();
        if (backupDir is null)
            return;
        try
        {
            if (!operations.DirectoryExists(backupDir))
                return;
            foreach (var name in DurableStateFiles)
            {
                var dest = Path.Combine(appDir, name);
                if (operations.FileExists(dest) && operations.FileLength(dest) > 0)
                    continue; // 已存在且非空：绝不覆盖重装已落下的文件
                var src = Path.Combine(backupDir, name);
                if (!operations.FileExists(src))
                    continue;
                operations.Copy(src, dest);
                operations.SetUnixFileMode(dest, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception)
        {
            // 静默：恢复失败不应让更新入口崩溃，父/worker 会在提示里给出备份目录路径
        }
    }

    /// <summary>成功后删除备份目录；幂等；永不抛。</summary>
    internal static void Cleanup(string? backupDir, LegacyLocalFileMigration.IFileOperations? operations = null)
    {
        operations ??= new LegacyLocalFileMigration.RealFileOperations();
        if (backupDir is null)
            return;
        try
        {
            if (operations.DirectoryExists(backupDir))
                operations.Delete(backupDir);
        }
        catch (Exception)
        {
            // 静默：残留一个备份目录远比删不掉严重
        }
    }

    /// <summary>
    /// 失败收尾：备份只有在"还有东西可救"时才值得留在工具目录里。
    /// 快照里的每个文件若在 <paramref name="appDir"/> 都存在且长度与快照一致，说明补缺已生效或根本没人动过，删掉备份；
    /// 长度不同（被截断/覆盖但非空，RestoreIfMissing 按约定不会覆盖）或缺失则保留，供用户手动恢复。永不抛。
    /// </summary>
    internal static void CleanupIfNothingToRestore(
        string appDir, string? backupDir, LegacyLocalFileMigration.IFileOperations? operations = null)
    {
        operations ??= new LegacyLocalFileMigration.RealFileOperations();
        if (backupDir is null)
            return;
        try
        {
            if (!operations.DirectoryExists(backupDir))
                return;
            foreach (var name in DurableStateFiles)
            {
                var src = Path.Combine(backupDir, name);
                if (!operations.FileExists(src))
                    continue;
                var dest = Path.Combine(appDir, name);
                if (!operations.FileExists(dest) || operations.FileLength(dest) != operations.FileLength(src))
                    return; // 缺失或被改成别的内容：保留备份
            }
            Cleanup(backupDir, operations);
        }
        catch (Exception)
        {
            // 静默：清理失败不影响更新结果
        }
    }
}
