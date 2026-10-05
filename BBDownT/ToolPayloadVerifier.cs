using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace BBDownT;

/// <summary>
/// 校验 <c>--update</c> 重装后真正落地的工具程序集与源 nupkg 内的字节是否一致。
/// dotnet SDK 的 tool install 没有 --force：同 id 同版本且旧文件被运行中的进程占用时，
/// uninstall 只留下告警、install 认为该版本已就位并只改写 apphost，退出码依旧是 0。
/// 所以「重装成功」必须由落地文件的哈希证明，而不是由退出码证明。
/// </summary>
internal static class ToolPayloadVerifier
{
    /// <summary>
    /// store 形态的落地路径：<c>.store/&lt;id&gt;/&lt;ver&gt;/&lt;id&gt;/&lt;ver&gt;/tools/&lt;tfm&gt;/any/BBDownT.dll</c>。
    /// 相对路径取自 nupkg 条目而非硬编码 tfm。本地清单安装没有 store 目录，返回 null。
    /// </summary>
    internal static string? ResolveInstalledDllPath(
        string? storeRoot, string? packageId, Version candidate, string? nupkgRelativePath)
    {
        if (string.IsNullOrEmpty(storeRoot) || string.IsNullOrEmpty(packageId)
            || string.IsNullOrEmpty(nupkgRelativePath))
            return null;
        var version = candidate.ToString();
        var relative = nupkgRelativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        return Path.Combine(storeRoot, packageId, version, packageId, version, relative);
    }

    /// <summary>比对落地 dll 与包内条目。返回 null 表示一致，否则为可直接打印的失败原因。</summary>
    internal static string? Verify(string nupkgPath, string? installedDllPath)
    {
        if (string.IsNullOrEmpty(installedDllPath))
            return "未能确定重装后工具程序集的落地路径（本地清单安装或包内缺少主 dll），无法校验落地文件。";
        if (NupkgInspector.ReadToolPayloadDigest(nupkgPath) is not { } packed)
            return $"无法从 {Path.GetFileName(nupkgPath)} 读取主程序集内容，无法校验落地文件。";
        if (!File.Exists(installedDllPath))
            return $"重装后未找到落地文件：{installedDllPath}";
        byte[] actual;
        try
        {
            using var stream = File.OpenRead(installedDllPath);
            actual = SHA256.HashData(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"读取落地文件失败：{ex.Message}";
        }
        return actual.SequenceEqual(packed.Digest)
            ? null
            : $"已安装的 {Path.GetFileName(installedDllPath)} 与新包内容不一致（旧文件仍被占用，SDK 跳过了实际写入）。";
    }

    /// <summary>
    /// 落地文件当前能否被替换。Windows 下运行中的进程会锁住程序集，
    /// 此时重装只会静默留下旧文件，故在做出任何破坏性动作之前先探一次。
    /// </summary>
    internal static bool IsAvailableForReplace(string? installedDllPath)
    {
        if (string.IsNullOrEmpty(installedDllPath) || !File.Exists(installedDllPath)) return true;
        try
        {
            using var _ = new FileStream(installedDllPath, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
