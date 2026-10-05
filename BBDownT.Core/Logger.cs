using System.Text.RegularExpressions;

namespace BBDownT.Core;

public static class Logger
{
    private static readonly Regex[] SensitivePatterns =
    [
        new("(?i)(Cookie:\\s*)[^\\r\\n]+", RegexOptions.Compiled),
        new("(?i)(Authorization:\\s*)[^\\r\\n]+", RegexOptions.Compiled),
        new("(?i)(SESSDATA=)[^;\\s&]+", RegexOptions.Compiled),
        new("(?i)(bili_jct=)[^;\\s&]+", RegexOptions.Compiled),
        new("(?i)(DedeUserID=)[^;\\s&]+", RegexOptions.Compiled),
        new("(?i)(ac_time_value=)[^;\\s&]+", RegexOptions.Compiled),
        new("(?i)(refresh_token=)[^;\\s&]+", RegexOptions.Compiled),
        new("(?i)(access_token=)[^;\\s&]+", RegexOptions.Compiled),
        new("(?i)(access_key=)[^;\\s&]+", RegexOptions.Compiled),
        new("(?i)(\"(?:Cookie|AccessToken|authorization)\"\\s*:\\s*\")[^\"]+", RegexOptions.Compiled),
        new("(?i)((?:AccessToken|Authorization)\\s*=\\s*)[^;\\s&]+", RegexOptions.Compiled),
        new("(?i)(identify_v1\\s+)[^\\s,\";]+", RegexOptions.Compiled)
    ];

    public static string RedactSensitiveText(object? text)
    {
        var value = text?.ToString() ?? "";
        foreach (var pattern in SensitivePatterns)
        {
            value = pattern.Replace(value, "$1<redacted>");
        }
        return value;
    }

    public static void Log(object text, bool enter = true)
    {
        Console.Write(DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss.fff]") + " - " + RedactSensitiveText(text));
        if (enter) Console.WriteLine();
    }

    public static void LogError(object text)
    {
        Console.Write(DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss.fff]") + " - ");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write(RedactSensitiveText(text));
        Console.ResetColor();
        Console.WriteLine();
    }

    public static void LogColor(object text, bool time = true)
    {
        if (time)
            Console.Write(DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss.fff]") + " - ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        if (time)
            Console.Write(RedactSensitiveText(text));
        else
            Console.Write("                            " + RedactSensitiveText(text));
        Console.ResetColor();
        Console.WriteLine();
    }

    public static void LogWarn(object text, bool time = true)
    {
        if (time)
            Console.Write(DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss.fff]") + " - ");
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        if (time)
            Console.Write(RedactSensitiveText(text));
        else
            Console.Write("                            " + RedactSensitiveText(text));
        Console.ResetColor();
        Console.WriteLine();
    }

    public static void LogDebug(string toFormat, params object[] args)
    {
        string message;
        if (args.Length > 0)
            message = string.Format(toFormat, args).Trim();
        else
            message = toFormat;

        var line = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss.fff]") + " - " + RedactSensitiveText(message);
        RememberDebugLine(line);
        if (!Config.DEBUG_LOG) return;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(line);
        Console.ResetColor();
        Console.WriteLine();
    }

    private static readonly List<string> RecentDebugLines = [];
    private static readonly object RecentDebugLock = new();

    /// <summary>
    /// 取走并清空缓冲的近期调试信息。非 --debug 时这些行不上屏, 出错时补打出来,
    /// 免得使用者只能看到一句异常消息。取走后清空, 避免同一批日志被重复补打。
    /// </summary>
    public static string DrainRecentDebugLines()
    {
        lock (RecentDebugLock)
        {
            if (RecentDebugLines.Count == 0) return "";
            var text = string.Join(Environment.NewLine, RecentDebugLines);
            RecentDebugLines.Clear();
            return text;
        }
    }

    /// <summary>
    /// 丢弃缓冲。进入新阶段/某个步骤完成时调用, 使失败补打只覆盖出事的那个步骤。
    /// </summary>
    public static void ClearRecentDebugLines()
    {
        lock (RecentDebugLock)
        {
            RecentDebugLines.Clear();
        }
    }

    // 不设上限: 每个步骤完成都会清一次, 单步输出不会累积成长尾; 一旦真的出现超长单步,
    // 保留全部行也比截断到看不出问题更有用。--debug 下这些行本就在屏上, 缓冲只是副本。
    private static void RememberDebugLine(string line)
    {
        lock (RecentDebugLock)
        {
            RecentDebugLines.Add(line);
        }
    }
}
