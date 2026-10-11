using QRCoder;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using static BBDownT.BBDownTUtil;
using static BBDownT.Core.Logger;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using BBDownT.Core.Util;

namespace BBDownT;

internal static class BBDownTLoginUtil
{
    private static async Task<LoginStatusResult> GetLoginStatusAsync(string qrcodeKey)
    {
        string queryUrl = $"https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key={qrcodeKey}&source=main-fe-header";
        using var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
        HTTPUtil.ApplyWebRequestHeaders(request, queryUrl, sendCookie: false, forceAuthenticatedProfile: true);
        request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");
        request.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");

        using var response = (await HTTPUtil.AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)).EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync();
        var setCookieHeaders = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToArray()
            : [];
        return new LoginStatusResult(responseBody, setCookieHeaders);
    }

    public static async Task LoginWEB(LoginQr qr)
    {
        string? qrImagePath = null;
        try
        {
            AuthenticatedWebProfileStore.Configure(Program.APP_DIR);
            Log("获取登录地址...");
            string loginUrl = "https://passport.bilibili.com/x/passport-login/web/qrcode/generate?source=main-fe-header";
            string url = JsonDocument.Parse(await HTTPUtil.GetAuthenticatedWebSourceAsync(loginUrl)).RootElement.GetProperty("data").GetProperty("url").ToString();
            string qrcodeKey = GetQueryString("qrcode_key", url);
            //Log(oauthKey);
            //Log(url);
            bool flag = false;
            Log("生成二维码...");
            using var qrCodeData = CreateQrCodeData(url);
            qrImagePath = await ShowQrCodeAsync(qrCodeData, qr);

            while (true)
            {
                await Task.Delay(1000);
                var loginStatus = await GetLoginStatusAsync(qrcodeKey);
                string w = loginStatus.ResponseBody;
                int code = JsonDocument.Parse(w).RootElement.GetProperty("data").GetProperty("code").GetInt32();
                if (code == 86038)
                {
                    LogColor("二维码已过期, 请重新执行登录指令.");
                    break;
                }
                else if (code == 86101) //等待扫码
                {
                    continue;
                }
                else if (code == 86090) //等待确认
                {
                    if (!flag)
                    {
                        Log("扫码成功, 请确认...");
                        flag = !flag;
                    }
                }
                else
                {
                    using var loginDoc = JsonDocument.Parse(w);
                    var loginData = loginDoc.RootElement.GetProperty("data");
                    string cc = loginData.GetProperty("url").ToString();
                    string? refreshToken = loginData.TryGetProperty("refresh_token", out var refreshTokenElement)
                        ? refreshTokenElement.GetString()
                        : null;
                    Log("登录成功");
                    var cookie = BBDownTCookieRefreshUtil.NormalizeLoginCookie(cc, refreshToken, loginStatus.SetCookieHeaders);
                    if (!BBDownTCookieRefreshUtil.HasRequiredLoginCookies(cookie))
                    {
                        throw new InvalidOperationException("登录响应缺少SESSDATA或bili_jct，未覆盖现有Cookie文件。");
                    }

                    await File.WriteAllTextAsync(Path.Combine(Program.APP_DIR, "BBDownT.data"), cookie);
                    break;
                }
            }
        }
        catch (Exception e) { LogError(e.Message); }
        finally { DeleteQrImage(qrImagePath); }
    }

    public static async Task LoginTV(LoginQr qr)
    {
        string? qrImagePath = null;
        try
        {
            string loginUrl = "https://passport.snm0516.aisee.tv/x/passport-tv-login/qrcode/auth_code";
            string pollUrl = "https://passport.bilibili.com/x/passport-tv-login/qrcode/poll";
            var parms = GetTVLoginParms();
            Log("获取登录地址...");
            byte[] responseArray = await (await HTTPUtil.AppHttpClient.PostAsync(loginUrl, new FormUrlEncodedContent(parms.ToDictionary()))).Content.ReadAsByteArrayAsync();
            string web = Encoding.UTF8.GetString(responseArray);
            string url = JsonDocument.Parse(web).RootElement.GetProperty("data").GetProperty("url").ToString();
            string authCode = JsonDocument.Parse(web).RootElement.GetProperty("data").GetProperty("auth_code").ToString();
            Log("生成二维码...");
            using var qrCodeData = CreateQrCodeData(url);
            qrImagePath = await ShowQrCodeAsync(qrCodeData, qr);
            parms.Set("auth_code", authCode);
            parms.Set("ts", GetTimeStamp(true));
            parms.Remove("sign");
            parms.Add("sign", GetSign(ToQueryString(parms)));
            while (true)
            {
                await Task.Delay(1000);
                responseArray = await (await HTTPUtil.AppHttpClient.PostAsync(pollUrl, new FormUrlEncodedContent(parms.ToDictionary()))).Content.ReadAsByteArrayAsync();
                web = Encoding.UTF8.GetString(responseArray);
                string code = JsonDocument.Parse(web).RootElement.GetProperty("code").ToString();
                if (code == "86038")
                {
                    LogColor("二维码已过期, 请重新执行登录指令.");
                    break;
                }
                else if (code == "86039") //等待扫码
                {
                    continue;
                }
                else
                {
                    string cc = JsonDocument.Parse(web).RootElement.GetProperty("data").GetProperty("access_token").ToString();
                    Log("登录成功: AccessToken=" + cc);
                    //导出cookie
                    await File.WriteAllTextAsync(Path.Combine(Program.APP_DIR, "BBDownTTV.data"), "access_token=" + cc);
                    break;
                }
            }
        }
        catch (Exception e) { LogError(e.Message); }
        finally { DeleteQrImage(qrImagePath); }
    }

    private static QRCodeData CreateQrCodeData(string url) =>
        new QRCodeGenerator().CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);

    /// <summary>
    /// 呈现二维码：默认只在终端画字符图；-img 改成把图片写进临时目录交给系统默认查看器，
    /// 并不再向终端打印。返回临时图片路径（没走 -img 时为 null）供收尾删除。
    /// 工作目录从此不再落 qrcode.png。
    /// </summary>
    private static async Task<string?> ShowQrCodeAsync(QRCodeData qrCodeData, LoginQr qr)
    {
        if (!qr.OpenImage)
        {
            if (Console.IsOutputRedirected)
            {
                LogWarn("输出已被重定向, 无法在终端绘制二维码, 请用 -img 保存图片后打开");
                return null;
            }
            Log("生成二维码成功, 请扫描终端打印的二维码");
            QrConsoleRender.Print(qrCodeData, qr.Style);
            return null;
        }

        // 文件名只放 Guid：qrcode_key 就编在二维码内容里，而 Logger 的脱敏管不到文件名。
        var imagePath = Path.Combine(Path.GetTempPath(), $"BBDownT-qrcode-{Guid.NewGuid():N}.png");
        qrCodeData.ModuleMatrix = PadQrMatrixForViewer(qrCodeData.ModuleMatrix, ViewerBottomPaddingModules);
        using var pngByteCode = new PngByteQRCode(qrCodeData);
        await File.WriteAllBytesAsync(imagePath, pngByteCode.GetGraphic(8, drawQuietZones: true));
        Log($"二维码图片已保存: {imagePath}");
        if (DesktopImageOpener.TryOpen(imagePath))
            Log("已调用系统默认图片查看器, 没弹出请手动打开上面的图片");
        else
            LogWarn("无法调用系统默认图片查看器(可能没有桌面环境), 请手动打开上面的图片");
        return imagePath;
    }

    private const int ViewerBottomPaddingModules = 4;

    /// <summary>
    /// 给图片查看器多留一圈底部白边：查看器窗口常在底部压住二维码，留白够宽才好扫。
    /// QRCoder 的 PngByteQRCode 只认正方形矩阵（宽高都取自行数），所以加高必然要同时加宽，
    /// 多出来的列左右均分以保持二维码居中；顶部仍是自带的 4 模块，底部 8 模块，仍是四边里最宽的。
    /// </summary>
    internal static List<BitArray> PadQrMatrixForViewer(IReadOnlyList<BitArray> modules, int extraModules)
    {
        var side = modules[0].Length;
        var leftOffset = extraModules / 2;
        var padded = new List<BitArray>(side + extraModules);
        for (var y = 0; y < side + extraModules; y++)
        {
            var row = new BitArray(side + extraModules);
            if (y < side)
            {
                for (var x = 0; x < side; x++) row[leftOffset + x] = modules[y][x];
            }
            padded.Add(row);
        }
        return padded;
    }

    private static void DeleteQrImage(string? imagePath)
    {
        if (imagePath is null) return;
        try
        {
            File.Delete(imagePath);
        }
        catch (Exception)
        {
            // 查看器可能还占着文件，而登录流程已经收尾，不值得为此抛错。
        }
    }

    private readonly record struct LoginStatusResult(string ResponseBody, string[] SetCookieHeaders);
}

/// <summary>
/// 登录子命令的二维码呈现选项：Style 只在终端渲染时用，OpenImage 时终端打印整个跳过。
/// </summary>
internal readonly record struct LoginQr(bool OpenImage, string? Style);
