using System.Collections;
using BBDownT;
using QRCoder;

namespace BBDownT.Tests;

public class QrConsoleRenderTests
{
    // '#' 为深色模块, '.' 为浅色, 一行一个字符串, 便于与终端输出逐字对照。
    private static List<BitArray> Matrix(params string[] rows) =>
        rows.Select(row => new BitArray(row.Select(c => c == '#').ToArray())).ToList();

    [Fact]
    public void Render_HalfBlock_MapsAllFourCellPairs()
    {
        var rows = QrConsoleRender.Render(Matrix(
            "##..",
            ".##.",
            "#..#",
            "..##"), halfBlock: true, quietZone: 0);

        string[] expected = ["▀█▄ ", "▀ ▄█"];
        Assert.Equal(expected, rows);
    }

    [Fact]
    public void Render_OddRowCount_PairsLastRowWithLightPhantom()
    {
        var rows = QrConsoleRender.Render(Matrix(
            "###",
            "#.#",
            "###"), halfBlock: true, quietZone: 0);

        // 模块行数为奇数时末行只能出现上半格, 下半格是留白, 不能补深色。
        string[] expected = ["█▀█", "▀▀▀"];
        Assert.Equal(expected, rows);
    }

    [Fact]
    public void Render_QuietZone_PadsAllFourSides()
    {
        var rows = QrConsoleRender.Render(Matrix("#"), halfBlock: true, quietZone: 2);

        string[] expected = ["     ", "  ▀  ", "     "];
        Assert.Equal(expected, rows);
    }

    [Fact]
    public void Render_HalfBlock_HalvesBothDimensions()
    {
        var modules = Matrix(Enumerable.Repeat("#####", 5).ToArray());

        var rows = QrConsoleRender.Render(modules, halfBlock: true, quietZone: 0);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal(5, row.Length));
    }

    [Fact]
    public void Render_BlockStyle_UsesDoubleWideModules()
    {
        var rows = QrConsoleRender.Render(Matrix("#.#."), halfBlock: false, quietZone: 0);

        string[] expected = ["██  ██  "];
        Assert.Equal(expected, rows);
    }

    [Fact]
    public void Render_EmptyMatrix_ReturnsNothing()
    {
        Assert.Empty(QrConsoleRender.Render(new List<BitArray>(), halfBlock: true, quietZone: 2));
    }

    /// <summary>
    /// 屏幕小一点就显示不全, 所以紧凑渲染必须比原来的双整块小一半, 并且落在小终端的 80 列里。
    /// 登录 URL 形如 https://passport.bilibili.com/login?auth_code=&lt;32位&gt;&source=main-fe-header&qrcode_key=&lt;32位&gt;，
    /// ECC 为 Q 时正好是版本 10: 57 个码模块 + QRCoder 自带的 8 个留白模块 = 65, 终端上已压到 33 行 x 65 列的下限。
    /// </summary>
    [Fact]
    public void Render_RealisticLoginUrl_IsHalfTheLegacySize()
    {
        var url = "https://passport.bilibili.com/login?auth_code="
            + new string('a', 32) + "&source=main-fe-header&qrcode_key=" + new string('b', 32);
        using var data = new QRCodeGenerator().CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);

        var compact = QrConsoleRender.Render(data.ModuleMatrix, halfBlock: true, quietZone: 0);
        var legacy = QrConsoleRender.Render(data.ModuleMatrix, halfBlock: false, quietZone: 0);

        Assert.All(compact, row => Assert.True(row.Length <= 80, $"行宽 {row.Length} 超出 80 列"));
        Assert.Equal(legacy[0].Length / 2, compact[0].Length);
        Assert.Equal((legacy.Count + 1) / 2, compact.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("compact")]
    [InlineData("block")]
    [InlineData("BLOCK")]
    [InlineData(" block ")]
    public void ValidateStyle_AcceptsKnownStyles(string? style)
    {
        Assert.Null(QrConsoleRender.ValidateStyle(style));
    }

    [Theory]
    [InlineData("")]
    [InlineData("half")]
    [InlineData("foo")]
    public void ValidateStyle_RejectsUnknownStyles(string style)
    {
        var message = QrConsoleRender.ValidateStyle(style);

        Assert.NotNull(message);
        Assert.Contains("compact", message);
        Assert.Contains("block", message);
    }

    [Fact]
    public void UsesHalfBlocks_DefaultsToCompact()
    {
        Assert.True(QrConsoleRender.UsesHalfBlocks(null));
        Assert.True(QrConsoleRender.UsesHalfBlocks("compact"));
        Assert.False(QrConsoleRender.UsesHalfBlocks("block"));
        Assert.False(QrConsoleRender.UsesHalfBlocks(" Block "));
    }

    [Fact]
    public void Print_EmitsEveryRowAndRestoresColors()
    {
        using var data = new QRCodeGenerator().CreateQrCode("https://example.com/x", QRCodeGenerator.ECCLevel.Q);
        var rows = QrConsoleRender.Render(data.ModuleMatrix, halfBlock: true, quietZone: 0);
        var originalOut = Console.Out;
        var originalForeColor = Console.ForegroundColor;
        var originalBackColor = Console.BackgroundColor;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);

            QrConsoleRender.Print(data, style: null);

            // 每行渲染前设色、换行前复位, 颜色必须回到调用前的状态。
            Assert.Equal(originalForeColor, Console.ForegroundColor);
            Assert.Equal(originalBackColor, Console.BackgroundColor);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(string.Join(Environment.NewLine, rows) + Environment.NewLine, output.ToString());
    }
}