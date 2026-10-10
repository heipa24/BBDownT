namespace BBDownT.Tests;

public class ConfigParserTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidConfig_AllowsWhitespaceCommentsAndBothPathForms(bool inlinePath)
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "   \n  # indented comment\n--hide-streams\n");
        var arguments = inlinePath
            ? new List<string> { "serve", $"--config-file={path}" }
            : new List<string> { "serve", "--config-file", path };
        var rootCommand = CommandLineInvoker.GetRootCommand(_ => Task.CompletedTask);
        rootCommand.TreatUnmatchedTokensAsErrors = true;
        try
        {
            Assert.True(BBDownTConfigParser.HandleConfig(arguments, rootCommand, "serve"));
            Assert.Contains("--hide-streams", arguments);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InvalidConfig_IsReportedAndNotPartiallyApplied()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "--hide-streams\n--unknown-option\n");
        var arguments = new List<string> { "BV1xx411c7mD", "--config-file", path };
        var rootCommand = CommandLineInvoker.GetRootCommand(_ => Task.CompletedTask);
        rootCommand.TreatUnmatchedTokensAsErrors = true;
        var originalOutput = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);

            var success = BBDownTConfigParser.HandleConfig(arguments, rootCommand);

            Assert.False(success);
            Assert.DoesNotContain("--hide-streams", arguments);
            Assert.Contains("unknown-option", output.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetOut(originalOutput);
            File.Delete(path);
        }
    }

    /// <summary>
    /// 根命令的选项全部注册为 global option 后不再出现在 rootCommand.Options 里，
    /// 配置文件白名单必须改用 CommandLineInvoker.OptionAliases，否则这些老配置会集体失效。
    /// </summary>
    [Fact]
    public async Task ConfigFile_SelectPageAndDanmaku_AreRecognisedAndMerged()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "-dd\n-p ALL\n--download-danmaku-formats xml\n");
        var arguments = new List<string> { "BV1xx411c7mD", "--config-file", path };
        var rootCommand = CommandLineInvoker.GetRootCommand(_ => Task.CompletedTask);
        try
        {
            Assert.True(BBDownTConfigParser.HandleConfig(arguments, rootCommand));

            Assert.Contains("--download-danmaku", arguments);
            Assert.Contains("--select-page", arguments);
            Assert.Contains("ALL", arguments);
            Assert.Contains("--download-danmaku-formats", arguments);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
