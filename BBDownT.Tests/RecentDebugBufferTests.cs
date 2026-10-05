using BBDownT.Core;

namespace BBDownT.Tests;

public class RecentDebugBufferTests
{
    [Fact]
    public void LogDebug_BuffersLineForFailureOutput_AndDrainsOnce()
    {
        Logger.ClearRecentDebugLines();

        Logger.LogDebug("接口响应: {0}", "{\"code\":-404}");

        var drained = Logger.DrainRecentDebugLines();

        Assert.Contains("接口响应: {\"code\":-404}", drained);
        Assert.Equal("", Logger.DrainRecentDebugLines());
    }

    [Fact]
    public void DrainRecentDebugLines_KeepsEveryLineUntilTheNextStepBoundary()
    {
        Logger.ClearRecentDebugLines();

        for (var i = 0; i < 260; i++)
        {
            Logger.LogDebug("第 {0} 行", i);
        }

        var drained = Logger.DrainRecentDebugLines();

        Assert.Equal(260, drained.Split(Environment.NewLine).Length);
        Assert.Contains("第 0 行", drained);
        Assert.EndsWith("第 259 行", drained);
    }

    [Fact]
    public void ClearRecentDebugLines_DropsWhatTheFinishedStepProduced()
    {
        Logger.ClearRecentDebugLines();
        Logger.LogDebug("上一个步骤的行");
        Logger.ClearRecentDebugLines();
        Logger.LogDebug("当前步骤的行");

        var drained = Logger.DrainRecentDebugLines();

        Assert.DoesNotContain("上一个步骤的行", drained);
        Assert.Contains("当前步骤的行", drained);
    }

    [Fact]
    public void DescribeDebugContext_AdvisesDebugRerunWhenNothingBuffered()
    {
        Logger.ClearRecentDebugLines();

        var context = Program.DescribeDebugContext(new InvalidOperationException("boom"));

        Assert.Contains("--debug", context);
        Assert.Contains("重跑", context);
    }

    [Fact]
    public void DescribeDebugContext_PrintsBufferedLinesInsteadOfAdvice()
    {
        Logger.ClearRecentDebugLines();
        Logger.LogDebug("Response: {0}", "{\"code\":62002,\"message\":\"稿件不可见\"}");

        var context = Program.DescribeDebugContext(new InvalidOperationException("boom"));

        Assert.Contains("{\"code\":62002,\"message\":\"稿件不可见\"}", context);
        Assert.DoesNotContain("重跑", context);
        Assert.Equal("", Logger.DrainRecentDebugLines());
    }

    [Fact]
    public void DescribeDebugContext_PrefersFailedStageLinesCarriedByTheException()
    {
        Logger.ClearRecentDebugLines();
        Logger.LogDebug("上一个分P的行, 不该被补打");

        var failure = new PageFailuresException("共1个分P下载失败: P1", ["P1 的调试信息:\nResponse: {\"code\":-404}"]);

        var context = Program.DescribeDebugContext(failure);

        Assert.Contains("P1 的调试信息", context);
        Assert.Contains("{\"code\":-404}", context);
        Assert.DoesNotContain("上一个分P的行", context);
    }

    [Fact]
    public void DescribeFailureHint_IsSilentForUnknownErrors()
    {
        Assert.Equal("", Program.DescribeFailureHint(new InvalidOperationException("boom")));
    }
}
