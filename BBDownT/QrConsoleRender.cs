using QRCoder;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace BBDownT;

internal static class QrConsoleRender
{
    internal static readonly string[] AllStyleNames = ["compact", "block"];

    private const string DarkBlock = "██";
    private const string LightBlock = "  ";

    internal static string? ValidateStyle(string? value) => value is null
        || value.Trim().ToLowerInvariant() is "compact" or "block"
        ? null : $"二维码渲染方式无效，可选 {string.Join('/', AllStyleNames)}。";

    /// <summary>
    /// block 之外的任何取值都走半角块：把两行模块压成一行文本，宽高各减半。
    /// </summary>
    internal static bool UsesHalfBlocks(string? style) =>
        style?.Trim().ToLowerInvariant() != "block";

    /// <summary>
    /// 半角块只依赖前景/背景两层颜色：前景涂上的部分与背景露出的部分刚好凑成明暗四象限，
    /// 因此整幅图不必逐字符切色，每行设一次即可。
    /// quietZone 默认 0：QRCoder 的 ModuleMatrix 自带 4 模块留白，再加只会白白多出好几行。
    /// </summary>
    internal static void Print(QRCodeData data, string? style, int quietZone = 0)
    {
        var rows = Render(data.ModuleMatrix, UsesHalfBlocks(style), quietZone);
        var previousForeColor = Console.ForegroundColor;
        var previousBackColor = Console.BackgroundColor;
        try
        {
            foreach (var row in rows)
            {
                Console.ForegroundColor = ConsoleColor.Black;
                Console.BackgroundColor = ConsoleColor.White;
                Console.Write(row);
                // 必须在换行前复位: 否则背景色会一路涂到行尾, 二维码看着比实际更大。
                Console.ResetColor();
                Console.Write(Environment.NewLine);
            }
        }
        finally
        {
            Console.ForegroundColor = previousForeColor;
            Console.BackgroundColor = previousBackColor;
        }
    }

    internal static List<string> Render(IReadOnlyList<BitArray> modules, bool halfBlock, int quietZone)
    {
        var rows = new List<string>();
        if (modules.Count == 0) return rows;

        var height = modules.Count + quietZone * 2;
        var width = modules[0].Length + quietZone * 2;

        if (halfBlock)
        {
            for (var y = 0; y < height; y += 2)
            {
                var builder = new StringBuilder(width);
                for (var x = 0; x < width; x++)
                {
                    // 边长为奇数, 末行与一条虚拟的浅色行配对, 下半格天然成为留白。
                    var top = IsDark(modules, y, x, quietZone);
                    var bottom = y + 1 < height && IsDark(modules, y + 1, x, quietZone);
                    builder.Append((top, bottom) switch
                    {
                        (true, true) => '█',
                        (true, false) => '▀',
                        (false, true) => '▄',
                        (false, false) => ' '
                    });
                }
                rows.Add(builder.ToString());
            }
            return rows;
        }

        for (var y = 0; y < height; y++)
        {
            var builder = new StringBuilder(width * 2);
            for (var x = 0; x < width; x++)
            {
                builder.Append(IsDark(modules, y, x, quietZone) ? DarkBlock : LightBlock);
            }
            rows.Add(builder.ToString());
        }
        return rows;
    }

    private static bool IsDark(IReadOnlyList<BitArray> modules, int y, int x, int quietZone)
    {
        var row = y - quietZone;
        var column = x - quietZone;
        return row >= 0 && row < modules.Count
            && column >= 0 && column < modules[row].Length
            && modules[row][column];
    }
}
