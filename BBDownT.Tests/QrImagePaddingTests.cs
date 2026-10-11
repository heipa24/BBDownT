using System.Collections;
using BBDownT;
using QRCoder;

namespace BBDownT.Tests;

public class QrImagePaddingTests
{
    private static List<BitArray> Matrix(params string[] rows) =>
        rows.Select(row => new BitArray(row.Select(c => c == '#').ToArray())).ToList();

    private static bool[] Bits(BitArray row) => row.Cast<bool>().ToArray();

    [Fact]
    public void PadQrMatrixForViewer_GrowsSquareAndKeepsCodeCentered()
    {
        var padded = BBDownTLoginUtil.PadQrMatrixForViewer(Matrix("##", ".#"), extraModules: 4);

        Assert.Equal(6, padded.Count);
        Assert.All(padded, row => Assert.Equal(6, row.Length));
        // 多出来的列左右均分(各 2 列), 新增的行全在底部, 因此底部最宽。
        Assert.Equal([false, false, true, true, false, false], Bits(padded[0]));
        Assert.Equal([false, false, false, true, false, false], Bits(padded[1]));
        Assert.Equal([false, false, false, false, false, false], Bits(padded[2]));
        Assert.Equal([false, false, false, false, false, false], Bits(padded[5]));
    }

    [Fact]
    public void PadQrMatrixForViewer_KeepsEveryModule()
    {
        using var data = new QRCodeGenerator().CreateQrCode("https://example.com/x", QRCodeGenerator.ECCLevel.Q);
        var original = data.ModuleMatrix;
        var darkBefore = original.Sum(row => row.Cast<bool>().Count(v => v));

        var padded = BBDownTLoginUtil.PadQrMatrixForViewer(original, 8);

        Assert.Equal(original.Count + 8, padded.Count);
        Assert.All(padded, row => Assert.Equal(original[0].Length + 8, row.Length));
        Assert.Equal(darkBefore, padded.Sum(row => row.Cast<bool>().Count(v => v)));
    }

    [Fact]
    public void PaddedMatrix_EncodesAsSquarePng()
    {
        var url = "https://passport.bilibili.com/login?auth_code="
            + new string('a', 32) + "&source=main-fe-header&qrcode_key=" + new string('b', 32);
        using var data = new QRCodeGenerator().CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        data.ModuleMatrix = BBDownTLoginUtil.PadQrMatrixForViewer(data.ModuleMatrix, 8);

        using var pngByteCode = new PngByteQRCode(data);
        var png = pngByteCode.GetGraphic(8, drawQuietZones: true);

        // 非正方形矩阵会让 PngByteQRCode 抛 IndexOutOfRange, 这里把“加高必须同时加宽”钉死。
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
        var expectedSide = data.ModuleMatrix.Count * 8;
        Assert.Equal(expectedSide, ReadBigEndianInt32(png, 16));
        Assert.Equal(expectedSide, ReadBigEndianInt32(png, 20));
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}
