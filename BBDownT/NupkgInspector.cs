using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace BBDownT;

/// <summary>
/// 从本地 nupkg 目录中挑选候选包并读取其版本号，用于 <c>--update</c> 决定重装/降级。
/// </summary>
internal static class NupkgInspector
{
    private const string PackageId = "BBDownT";

    /// <summary>
    /// 在目录中找到版本最高的 <c>BBDownT.*.nupkg</c>（忽略大小写与文件名版本号，真实版本以 nuspec 为准）。
    /// 返回 null 表示无候选。
    /// </summary>
    internal static string? FindCandidateNupkg(string directory, Func<string, bool>? directoryExists = null)
    {
        var exists = directoryExists ?? Directory.Exists;
        if (string.IsNullOrEmpty(directory) || !exists(directory))
            return null;
        var matches = Directory.EnumerateFiles(directory, "*.nupkg")
            .Where(p => Path.GetFileName(p).StartsWith(PackageId, StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileName(p).EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0) return null;
        if (matches.Count == 1) return matches[0];

        string? best = null;
        Version? bestVersion = null;
        foreach (var path in matches)
        {
            Version? v = TryReadVersion(path);
            if (v is null) continue;
            if (bestVersion is null || v > bestVersion)
            {
                bestVersion = v;
                best = path;
            }
        }
        // 若一个版本都读不出，退化到文件名最高（按 Version 解析文件名中的数字）
        if (best is null)
        {
            best = matches.OrderByDescending(p => ExtractVersionFromName(p) ?? new Version(0, 0, 0)).First();
        }
        return best;
    }

    /// <summary>读取 nupkg 内根目录 nuspec 的版本。失败抛 <see cref="InvalidDataException"/>。</summary>
    internal static Version ReadPackageVersion(string nupkgPath)
    {
        var v = TryReadVersion(nupkgPath);
        if (v is null)
            throw new InvalidDataException($"nupkg 无效或缺少版本信息：{nupkgPath}");
        return v;
    }

    private static Version? TryReadVersion(string nupkgPath)
    {
        if (!File.Exists(nupkgPath)) return null;
        try
        {
            using var zip = ZipFile.OpenRead(nupkgPath);
            var nuspecEntry = zip.Entries
                .FirstOrDefault(e => !e.FullName.Contains('/') && !e.FullName.Contains('\\')
                                      && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspecEntry is null) return null;
            using var stream = nuspecEntry.Open();
            var doc = XDocument.Load(stream);
            var versionElement = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "version");
            if (versionElement is null || string.IsNullOrWhiteSpace(versionElement.Value))
                return null;
            if (!Version.TryParse(versionElement.Value.Trim(), out var version) || version.Build < 0)
                return null;
            return version;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Version? ExtractVersionFromName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path); // BBDownT.2.1.10
        var idx = name.LastIndexOf('.');
        if (idx < 0) return null;
        var tail = name[(idx + 1)..];
        return Version.TryParse(tail, out var v) ? v : null;
    }

    /// <summary>
    /// 读取包内工具主程序集（<c>tools/&lt;tfm&gt;/any/BBDownT.dll</c>）的相对路径与 SHA-256，
    /// 供重装后比对落地文件。读不出（包缺失、损坏、无该条目）返回 null —— 调用方据此判"无法校验"，不得当作通过。
    /// </summary>
    internal static (string RelativePath, byte[] Digest)? ReadToolPayloadDigest(string nupkgPath)
    {
        if (string.IsNullOrEmpty(nupkgPath) || !File.Exists(nupkgPath)) return null;
        try
        {
            using var zip = ZipFile.OpenRead(nupkgPath);
            var entry = zip.Entries.FirstOrDefault(e => IsToolDllEntry(e.FullName));
            if (entry is null) return null;
            using var stream = entry.Open();
            return (NormalizeEntryPath(entry.FullName), SHA256.HashData(stream));
        }
        catch (Exception)
        {
            return null;
        }
    }

    // 只认 tools/ 下的主程序集：BBDownT.Core.dll 等依赖的结尾不是 "/BBDownT.dll"
    private static bool IsToolDllEntry(string entryName)
    {
        var normalized = NormalizeEntryPath(entryName);
        return normalized.StartsWith("tools/", StringComparison.OrdinalIgnoreCase)
               && normalized.EndsWith("/" + PackageId + ".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEntryPath(string entryName) => entryName.Replace('\\', '/');
}
