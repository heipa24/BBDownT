using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BBDownT;

internal static class DesktopImageOpener
{
    /// <summary>
    /// 用系统默认查看器打开图片。无桌面环境（SSH、容器）时返回 false，由调用方退化成打印保存路径。
    /// 绝不等待退出：xdg-open 会一直挂在查看器上，而登录轮询必须继续。
    /// </summary>
    internal static bool TryOpen(string path)
    {
        try
        {
            Process.Start(BuildStartInfo(path));
            return true;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static ProcessStartInfo BuildStartInfo(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // 文件关联只有 shell 知道，Windows 上必须走 UseShellExecute。
            return new ProcessStartInfo(path) { UseShellExecute = true, CreateNoWindow = true };
        }

        var startInfo = new ProcessStartInfo(
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(path);
        return startInfo;
    }
}
