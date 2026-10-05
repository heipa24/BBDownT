namespace BBDownT.Tests;

/// <summary>
/// ffmpeg 把出现在某个 -i 之前的选项归入该输入文件的选项组, 因此 -map / -map_chapters 这类
/// 输出选项必须整体排在最后一个 -i 之后。MetadataRefreshTests 里现有的 HasPair 只判断
/// "相邻两项成对存在", 完全不关心顺序, 所以顺序错误不会让测试失败; 这里补上顺序维度。
/// </summary>
internal static class FfmpegArgumentOrder
{
    /// <summary>取出某个选项后面紧跟的所有取值(选项可重复出现)</summary>
    internal static IReadOnlyList<string> ValuesOf(IReadOnlyList<string> args, string option)
    {
        var values = new List<string>();
        for (var i = 0; i + 1 < args.Count; i++)
        {
            if (args[i] == option) values.Add(args[i + 1]);
        }
        return values;
    }

    internal static IReadOnlyList<string> Inputs(IReadOnlyList<string> args) => ValuesOf(args, "-i");

    internal static IReadOnlyList<string> Mapped(IReadOnlyList<string> args) => ValuesOf(args, "-map");

    internal static IReadOnlyList<string> ChapterMappings(IReadOnlyList<string> args) => ValuesOf(args, "-map_chapters");

    /// <summary>
    /// 断言: 每个 -map / -map_chapters 的下标都大于最后一个 -i 的下标,
    /// 且它引用的输入序号确实存在(防止索引错位)。
    /// </summary>
    internal static void AssertSelectorsFollowInputs(IReadOnlyList<string> args)
    {
        var lastInput = -1;
        var inputCount = 0;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "-i")
            {
                lastInput = i;
                inputCount++;
            }
        }

        Assert.True(lastInput >= 0, $"命令里没有 -i: {string.Join(' ', args)}");

        for (var i = 0; i + 1 < args.Count; i++)
        {
            if (args[i] != "-map" && args[i] != "-map_chapters") continue;

            Assert.True(i > lastInput,
                $"{args[i]} {args[i + 1]} 出现在下标 {i}, 但最后一个 -i 在下标 {lastInput}; " +
                $"ffmpeg 会把它解析成输入选项: {string.Join(' ', args)}");

            var referenced = new string(args[i + 1].TakeWhile(char.IsDigit).ToArray());
            if (referenced.Length != 0)
            {
                Assert.True(int.Parse(referenced) < inputCount,
                    $"{args[i]} {args[i + 1]} 引用了不存在的输入(实际输入数 {inputCount}): {string.Join(' ', args)}");
            }
        }
    }
}
