using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;

namespace BBDownT;

internal static class CommandLineInvoker
{
    private static readonly Argument<string> Url = new("url", description: "视频地址 或 av|bv|BV|ep|ss");
    private static readonly Option<bool> UseTvApi = new(["--use-tv-api", "-tv"], "使用TV端解析模式");
    private static readonly Option<bool> UseAppApi = new(["--use-app-api", "-app"], "使用APP端解析模式");
    private static readonly Option<bool> UseIntlApi = new(["--use-intl-api", "-intl"], "使用国际版(东南亚视频)解析模式");
    private static readonly Option<bool> UseMP4box = new(["--use-mp4box"], "使用MP4Box来混流");
    private static readonly Option<string> EncodingPriority = new(["--encoding-priority", "-e"], "视频及音频编码的选择优先级, 用逗号分割 例: \"hevc,av1,avc,flac,eac3,m4a\"；与 -q 同时使用时越靠前越优先");
    private static readonly Option<string> DfnPriority = new(["--dfn-priority", "-q"], "画质优先级,用逗号分隔 例: \"8K 超高清, 1080P 高码率, HDR 真彩, 杜比视界\"；与 -e 同时使用时越靠前越优先");
    private static readonly Option<bool> OnlyShowInfo = new(["--only-show-info", "-info"], "仅解析音视频和字幕信息，不下载；配合 --sub-only 仅列出字幕");
    private static readonly Option<bool> HideStreams = new(["--hide-streams", "-hs"], "不要显示所有可用音视频流");
    private static readonly Option<bool> Interactive = new(["--interactive", "-ia"], "交互式选择音视频和字幕；字幕支持多选");
    private static readonly Option<bool> ShowAll = new(["--show-all"], "展示所有分P标题");
    private static readonly Option<bool> UseAria2c = new(["--use-aria2c", "-aria2"], "调用aria2c进行下载(默认使用内置下载器, 需显式传入本参数才会启用aria2)");
    private static readonly Option<bool> NoAria2c = new(["--no-aria2"], "强制使用内置下载器(当前版本默认即为内置下载器, 保留该参数用于兼容旧命令)");
    private static readonly Option<string> Aria2cArgs = new(["--aria2c-args"], "调用aria2c的附加参数(默认包含\"-x16 -s16 -j16 --min-split-size=1M\"等, 使用时注意字符串转义)");
    private static readonly Option<bool> MultiThread = new(["--multi-thread", "-mt"], "使用多线程下载(默认开启)");
    private static readonly Option<string> SelectPage = new(["--select-page", "-p"], "选择指定分p或分p范围: (-p 8 或 -p 1,2 或 -p 3-5 或 -p ALL 或 -p LAST 或 -p 3,5,LATEST)");
    private static readonly Option<bool> SimplyMux = new(["--simply-mux"], "精简混流，不增加描述、作者等信息");
    private static readonly Option<bool> AudioOnly = new(["--audio-only"], "仅下载音频；与 --video-only 同时使用时下载两条独立主流并跳过混流，源不支持时失败");
    private static readonly Option<bool> VideoOnly = new(["--video-only"], "仅下载视频；与 --audio-only 同时使用时下载两条独立主流并跳过混流，源不支持时失败");
    private static readonly Option<bool> DanmakuOnly = new(["--danmaku-only"], "仅下载弹幕");
    private static readonly Option<bool> CoverOnly = new(["--cover-only"], "仅下载封面");
    private static readonly Option<bool> SubOnly = new(["--sub-only"], "仅下载字幕");
    private static readonly Option<bool> Debug = new(["--debug"], "输出调试日志");
    private static readonly Option<bool> SkipMux = new(["--skip-mux"], "跳过混流步骤");
    private static readonly Option<bool> SkipSubtitle = new(["--skip-subtitle"], "跳过字幕下载");
    private static readonly Option<bool> SkipCover = new(["--skip-cover"], "跳过封面下载");
    private static readonly Option<bool> MetadataOnly = new(["--metadata-only"], "输出文件已存在时仅更新其元数据(标题/描述/封面/章节/字幕)，不重新下载或重编码音视频流；会保留原文件已有的音频语言/字幕标题等标签，接口只返回试看片段时先校验原文件为完整正片再刷新(需要 ffprobe)");
    private static readonly Option<bool> JsonOnly = new(["--json-only"], "仅更新 bbdown.json 元数据(来源/分P/媒体信息)，不下载任何媒体、不调播放接口；与 update-downloaded 配合可为旧目录补全新字段(无需大会员)，也可单独登记视频");
    private static readonly Option<bool> ForceHttp = new(["--force-http"], "下载音视频时强制使用HTTP协议替换HTTPS(默认开启)");
    private static readonly Option<bool> DownloadDanmaku = new(["--download-danmaku", "-dd"], "下载弹幕");
    private static readonly Option<string> DownloadDanmakuFormats = new(["--download-danmaku-formats", "-ddf"], $"指定需下载的弹幕格式, 用逗号分隔, 可选 {string.Join('/', BBDownTDanmakuFormatInfo.AllFormatNames)}, 默认: \"{string.Join(',', BBDownTDanmakuFormatInfo.AllFormatNames)}\"");
    private static readonly Option<bool> SkipAi = new(["--skip-ai"], description: "跳过AI字幕下载(默认开启)");
    private static readonly Option<string> SubtitleLanguage = new(["--subtitle-language"], "筛选字幕语言，逗号分隔；zh/en 匹配语言族，zh-Hans/ai-zh 精确匹配");
    private static readonly Option<string> AiSubtitlePolicy = new(["--ai-subtitle-policy"], "字幕AI策略: exclude/include/prefer-human/only；显式指定时优先于 --skip-ai");

    static CommandLineInvoker()
    {
        AudioLanguage.AddValidator(result => result.ErrorMessage = AudioLanguageSelection.ValidateCode(result.GetValueOrDefault<string>()));
        SubtitleLanguage.AddValidator(result => result.ErrorMessage = SubtitleSelection.ValidateLanguage(result.GetValueOrDefault<string>()));
        AiSubtitlePolicy.AddValidator(result => result.ErrorMessage = SubtitleSelection.ValidatePolicy(result.GetValueOrDefault<string>()));
    }
    private static readonly Option<bool> VideoAscending = new(["--video-ascending"], "视频升序(最小体积优先)");
    private static readonly Option<bool> AudioAscending = new(["--audio-ascending"], "音频升序(最小体积优先)");
    private static readonly Option<bool> AllowPcdn = new(["--allow-pcdn"], "不替换PCDN域名, 仅在正常情况与--upos-host均无法下载时使用");
    private static readonly Option<string> Language = new(["--language"], "设置混流的音频语言(代码), 如chi, jpn等");
    private static readonly Option<string> AudioLanguage = new(["--audio-language"], "选择配音语言，精确匹配 -info 列出的代码；仅默认WEB/DASH模式，输出文件附加语言后缀");
    private static readonly Option<string> UserAgent = new(["--user-agent", "-ua"], "指定user-agent；登录态默认使用持久化浏览器配置，匿名默认使用随机user-agent");
    private static readonly Option<string> Cookie = new(["--cookie", "-c"], "设置字符串cookie用以下载网页接口的会员内容");
    private static readonly Option<string> AccessToken = new(["--access-token", "-token"], "设置access_token用以下载TV/APP接口的会员内容");
    private static readonly Option<string> WorkDir = new(["--work-dir"], "设置程序的工作目录");
    private static readonly Option<string> FFmpegPath = new(["--ffmpeg-path"], "设置ffmpeg的路径");
    private static readonly Option<string> Mp4boxPath = new(["--mp4box-path"], "设置mp4box的路径");
    private static readonly Option<string> Aria2cPath = new(["--aria2c-path"], "设置aria2c的路径");
    private static readonly Option<string> UposHost = new(["--upos-host"], "自定义upos服务器");
    private static readonly Option<bool> ForceReplaceHost = new(["--force-replace-host"], "强制替换下载服务器host(默认开启)");
    private static readonly Option<bool> SaveArchivesToFile = new(["--save-archives-to-file"], "将下载过的视频记录到本地文件中, 用于后续跳过下载同个视频");
    private static readonly Option<string> DelayPerPage = new(["--delay-per-page"], "设置下载合集分P之间的下载间隔时间(单位: 秒, 默认无间隔)");
    private static readonly Option<bool> DownloadAll = new(["--download-all"], "导出UP主空间TXT清单后，按清单逐条下载全部投稿");
    private static readonly Option<int> DelayPerVideo = new(["--delay-per-video"], "批量下载时视频之间的间隔秒数，默认10，0为不等待");
    private static readonly Option<string> FilePattern = new(["--file-pattern", "-F"], 
        $"使用内置变量自定义单P存储文件名:\r\n\r\n" + 
        $"<videoTitle>: 视频主标题\r\n" + 
        $"<pageNumber>: 视频分P序号\r\n" + 
        $"<pageNumberWithZero>: 视频分P序号(前缀补零)\r\n" + 
        $"<pageTitle>: 视频分P标题\r\n" + 
        $"<bvid>: 视频BV号\r\n" + 
        $"<aid>: 视频aid\r\n" + 
        $"<cid>: 视频cid\r\n" + 
        $"<dfn>: 视频清晰度\r\n" + 
        $"<res>: 视频分辨率\r\n" + 
        $"<fps>: 视频帧率\r\n" + 
        $"<videoCodecs>: 视频编码\r\n" + 
        $"<videoBandwidth>: 视频码率\r\n" + 
        $"<audioCodecs>: 音频编码\r\n" + 
        $"<audioBandwidth>: 音频码率\r\n" + 
        $"<ownerName>: 上传者名称\r\n" + 
        $"<ownerMid>: 上传者mid\r\n" + 
        $"<publishDate>: 收藏夹/番剧/合集发布时间\r\n" + 
        $"<videoDate>: 视频发布时间(分p视频发布时间与<publishDate>相同)\r\n" + 
        $"<apiType>: API类型(TV/APP/INTL/WEB)\r\n\r\n" + 
        $"默认为: {Program.SinglePageDefaultSavePath}\r\n");
    private static readonly Option<string> MultiFilePattern = new(["--multi-file-pattern", "-M"], $"使用内置变量自定义多P存储文件名:\r\n\r\n默认为: {Program.MultiPageDefaultSavePath}\r\n");
    private static readonly Option<string> Host = new(["--host"], "指定BiliPlus host(使用BiliPlus需要access_token, 不需要cookie, 解析服务器能够获取你账号的大部分权限!)");
    private static readonly Option<string> EpHost = new(["--ep-host"], "指定BiliPlus EP host(用于代理api.bilibili.com/pgc/view/web/season, 大部分解析服务器不支持代理该接口)");
    private static readonly Option<string> TvHost = new(["--tv-host"], "自定义tv端接口请求Host(用于代理api.snm0516.aisee.tv)");
    private static readonly Option<string> Area = new(["--area"], "(hk|tw|th) 使用BiliPlus时必选, 指定BiliPlus area");
    private static readonly Option<string> ConfigFile = new(["--config-file"], "读取指定的BBDownT本地配置文件(默认为: BBDownT.config)");//以下仅为兼容旧版本命令行, 不建议使用
    private static readonly Option<string> Aria2cProxy = new(["--aria2c-proxy"], "调用aria2c进行下载时的代理地址配置") { IsHidden = true };
    private static readonly Option<bool> OnlyHevc = new(["--only-hevc", "-hevc"], "只下载hevc编码") { IsHidden = true };
    private static readonly Option<bool> OnlyAvc = new(["--only-avc", "-avc"], "只下载avc编码") { IsHidden = true };
    private static readonly Option<bool> OnlyAv1 = new(["--only-av1", "-av1"], "只下载av1编码") { IsHidden = true };
    private static readonly Option<bool> AddDfnSubfix = new(["--add-dfn-subfix"], "为文件加入清晰度后缀, 如XXX[1080P 高码率]") { IsHidden = true };
    private static readonly Option<bool> NoPaddingPageNum = new(["--no-padding-page-num"], "不给分P序号补零") { IsHidden = true };
    private static readonly Option<bool> BandwithAscending = new(["--bandwith-ascending"], "比特率升序(最小体积优先)") { IsHidden = true };


    internal class MyOptionBinder : BinderBase<MyOption>
    {
        protected override MyOption GetBoundValue(BindingContext bindingContext) =>
            Bind(bindingContext.ParseResult, bindingContext.ParseResult.GetValueForArgument(Url));

        /// <summary>
        /// 从解析结果装配 MyOption。子命令共用同一批 global option 实例，
        /// 因此这里只依赖 ParseResult 与所属命令，位置参数由调用方给出（子命令没有 url）。
        /// </summary>
        internal static MyOption Bind(ParseResult parseResult, string url)
        {
            var option = new MyOption
            {
                Url = url,
                EncodingPriorityFirst = CommandLineOptionOrder.EncodingPrecedesQuality(
                    parseResult.Tokens.Select(token => token.Value))
            };

            if (parseResult.HasOption(UseTvApi)) option.UseTvApi = parseResult.GetValueForOption(UseTvApi)!;
            if (parseResult.HasOption(UseAppApi)) option.UseAppApi = parseResult.GetValueForOption(UseAppApi)!;
            if (parseResult.HasOption(UseIntlApi)) option.UseIntlApi = parseResult.GetValueForOption(UseIntlApi)!;
            if (parseResult.HasOption(UseMP4box)) option.UseMP4box = parseResult.GetValueForOption(UseMP4box)!;
            if (parseResult.HasOption(EncodingPriority)) option.EncodingPriority = parseResult.GetValueForOption(EncodingPriority)!;
            if (parseResult.HasOption(DfnPriority)) option.DfnPriority = parseResult.GetValueForOption(DfnPriority)!;
            if (parseResult.HasOption(OnlyShowInfo)) option.OnlyShowInfo = parseResult.GetValueForOption(OnlyShowInfo)!;
            if (parseResult.HasOption(ShowAll)) option.ShowAll = parseResult.GetValueForOption(ShowAll)!;
            if (parseResult.HasOption(UseAria2c)) option.UseAria2c = parseResult.GetValueForOption(UseAria2c)!;
            if (parseResult.HasOption(NoAria2c)) option.NoAria2c = parseResult.GetValueForOption(NoAria2c)!;
            if (parseResult.HasOption(Interactive)) option.Interactive = parseResult.GetValueForOption(Interactive)!;
            if (parseResult.HasOption(HideStreams)) option.HideStreams = parseResult.GetValueForOption(HideStreams)!;
            if (parseResult.HasOption(MultiThread)) option.MultiThread = parseResult.GetValueForOption(MultiThread)!;
            if (parseResult.HasOption(SimplyMux)) option.SimplyMux = parseResult.GetValueForOption(SimplyMux)!;
            if (parseResult.HasOption(VideoOnly)) option.VideoOnly = parseResult.GetValueForOption(VideoOnly)!;
            if (parseResult.HasOption(AudioOnly)) option.AudioOnly = parseResult.GetValueForOption(AudioOnly)!;
            if (parseResult.HasOption(DanmakuOnly)) option.DanmakuOnly = parseResult.GetValueForOption(DanmakuOnly)!;
            if (parseResult.HasOption(CoverOnly)) option.CoverOnly = parseResult.GetValueForOption(CoverOnly)!;
            if (parseResult.HasOption(SubOnly)) option.SubOnly = parseResult.GetValueForOption(SubOnly)!;
            if (parseResult.HasOption(Debug)) option.Debug = parseResult.GetValueForOption(Debug)!;
            if (parseResult.HasOption(SkipMux)) option.SkipMux = parseResult.GetValueForOption(SkipMux)!;
            if (parseResult.HasOption(SkipSubtitle)) option.SkipSubtitle = parseResult.GetValueForOption(SkipSubtitle)!;
            if (parseResult.HasOption(SkipCover)) option.SkipCover = parseResult.GetValueForOption(SkipCover)!;
            if (parseResult.HasOption(MetadataOnly)) option.MetadataOnly = parseResult.GetValueForOption(MetadataOnly)!;
            if (parseResult.HasOption(JsonOnly)) option.JsonOnly = parseResult.GetValueForOption(JsonOnly)!;
            if (parseResult.HasOption(ForceHttp)) option.ForceHttp = parseResult.GetValueForOption(ForceHttp)!;
            if (parseResult.HasOption(DownloadDanmaku)) option.DownloadDanmaku = parseResult.GetValueForOption(DownloadDanmaku)!;
            if (parseResult.HasOption(DownloadDanmakuFormats)) option.DownloadDanmakuFormats = parseResult.GetValueForOption(DownloadDanmakuFormats)!;
            if (parseResult.HasOption(SkipAi)) option.SkipAi = parseResult.GetValueForOption(SkipAi)!;
            if (parseResult.HasOption(SubtitleLanguage)) option.SubtitleLanguage = parseResult.GetValueForOption(SubtitleLanguage);
            if (parseResult.HasOption(AiSubtitlePolicy)) option.AiSubtitlePolicy = parseResult.GetValueForOption(AiSubtitlePolicy);
            if (parseResult.HasOption(VideoAscending)) option.VideoAscending = parseResult.GetValueForOption(VideoAscending)!;
            if (parseResult.HasOption(AudioAscending)) option.AudioAscending = parseResult.GetValueForOption(AudioAscending)!;
            if (parseResult.HasOption(AllowPcdn)) option.AllowPcdn = parseResult.GetValueForOption(AllowPcdn)!;
            if (parseResult.HasOption(FilePattern)) option.FilePattern = parseResult.GetValueForOption(FilePattern)!;
            if (parseResult.HasOption(MultiFilePattern)) option.MultiFilePattern = parseResult.GetValueForOption(MultiFilePattern)!;
            if (parseResult.HasOption(SelectPage))
            {
                option.SelectPage = parseResult.GetValueForOption(SelectPage)!;
                option.SelectPageSpecified = true;
            }
            if (parseResult.HasOption(Language)) option.Language = parseResult.GetValueForOption(Language)!;
            if (parseResult.HasOption(AudioLanguage)) option.AudioLanguage = parseResult.GetValueForOption(AudioLanguage);
            if (parseResult.HasOption(UserAgent)) option.UserAgent = parseResult.GetValueForOption(UserAgent)!;
            if (parseResult.HasOption(Cookie)) option.Cookie = parseResult.GetValueForOption(Cookie)!;
            if (parseResult.HasOption(AccessToken)) option.AccessToken = parseResult.GetValueForOption(AccessToken)!;
            if (parseResult.HasOption(Aria2cArgs)) option.Aria2cArgs = parseResult.GetValueForOption(Aria2cArgs)!;
            if (parseResult.HasOption(WorkDir)) option.WorkDir = parseResult.GetValueForOption(WorkDir)!;
            if (parseResult.HasOption(FFmpegPath)) option.FFmpegPath = parseResult.GetValueForOption(FFmpegPath)!;
            if (parseResult.HasOption(Mp4boxPath)) option.Mp4boxPath = parseResult.GetValueForOption(Mp4boxPath)!;
            if (parseResult.HasOption(Aria2cPath)) option.Aria2cPath = parseResult.GetValueForOption(Aria2cPath)!;
            if (parseResult.HasOption(UposHost)) option.UposHost = parseResult.GetValueForOption(UposHost)!;
            if (parseResult.HasOption(ForceReplaceHost)) option.ForceReplaceHost = parseResult.GetValueForOption(ForceReplaceHost)!;
            if (parseResult.HasOption(SaveArchivesToFile)) option.SaveArchivesToFile = parseResult.GetValueForOption(SaveArchivesToFile)!;
            if (parseResult.HasOption(DelayPerPage)) option.DelayPerPage = parseResult.GetValueForOption(DelayPerPage)!;
            if (parseResult.HasOption(DownloadAll)) option.DownloadAll = parseResult.GetValueForOption(DownloadAll);
            if (parseResult.HasOption(DelayPerVideo)) option.DelayPerVideo = parseResult.GetValueForOption(DelayPerVideo);
            if (parseResult.HasOption(Host)) option.Host = parseResult.GetValueForOption(Host)!;
            if (parseResult.HasOption(EpHost)) option.EpHost = parseResult.GetValueForOption(EpHost)!;
            if (parseResult.HasOption(TvHost)) option.TvHost = parseResult.GetValueForOption(TvHost)!;
            if (parseResult.HasOption(Area)) option.Area = parseResult.GetValueForOption(Area)!;
            if (parseResult.HasOption(ConfigFile)) option.ConfigFile = parseResult.GetValueForOption(ConfigFile)!;
            if (parseResult.HasOption(Aria2cProxy)) option.Aria2cProxy = parseResult.GetValueForOption(Aria2cProxy)!;
            if (parseResult.HasOption(OnlyHevc)) option.OnlyHevc = parseResult.GetValueForOption(OnlyHevc)!;
            if (parseResult.HasOption(OnlyAvc)) option.OnlyAvc = parseResult.GetValueForOption(OnlyAvc)!;
            if (parseResult.HasOption(OnlyAv1)) option.OnlyAv1 = parseResult.GetValueForOption(OnlyAv1)!;
            if (parseResult.HasOption(AddDfnSubfix)) option.AddDfnSubfix = parseResult.GetValueForOption(AddDfnSubfix)!;
            if (parseResult.HasOption(NoPaddingPageNum)) option.NoPaddingPageNum = parseResult.GetValueForOption(NoPaddingPageNum)!;
            if (parseResult.HasOption(BandwithAscending)) option.BandwithAscending = parseResult.GetValueForOption(BandwithAscending)!;
            return option;
        }
    }

    /// <summary>
    /// 根命令的全部选项，顺序与 --help 展示一致。注册为 global option 后子命令可复用同一批实例，
    /// 因此 MyOptionBinder.Bind 在子命令解析结果上依然逐项可用。
    /// </summary>
    private static readonly Option[] AllOptions =
    [
        UseTvApi,
        UseAppApi,
        UseIntlApi,
        UseMP4box,
        EncodingPriority,
        DfnPriority,
        OnlyShowInfo,
        ShowAll,
        UseAria2c,
        NoAria2c,
        Interactive,
        HideStreams,
        MultiThread,
        SimplyMux,
        VideoOnly,
        AudioOnly,
        DanmakuOnly,
        SubOnly,
        CoverOnly,
        Debug,
        SkipMux,
        SkipSubtitle,
        SkipCover,
        MetadataOnly,
        JsonOnly,
        ForceHttp,
        DownloadDanmaku,
        DownloadDanmakuFormats,
        SkipAi,
        SubtitleLanguage,
        AiSubtitlePolicy,
        VideoAscending,
        AudioAscending,
        AllowPcdn,
        FilePattern,
        MultiFilePattern,
        SelectPage,
        Language,
        AudioLanguage,
        UserAgent,
        Cookie,
        AccessToken,
        Aria2cArgs,
        WorkDir,
        FFmpegPath,
        Mp4boxPath,
        Aria2cPath,
        UposHost,
        ForceReplaceHost,
        SaveArchivesToFile,
        DelayPerPage,
        DownloadAll,
        DelayPerVideo,
        Host,
        EpHost,
        TvHost,
        Area,
        Aria2cProxy,
        OnlyHevc,
        OnlyAvc,
        OnlyAv1,
        AddDfnSubfix,
        NoPaddingPageNum,
        BandwithAscending,
        ConfigFile
    ];

    /// <summary>
    /// 全部选项的别名，供配置文件校验使用：global option 不在 rootCommand.Options 里，
    /// 直接读命令树会漏掉整批参数。
    /// </summary>
    public static IReadOnlyList<string> OptionAliases { get; } =
        AllOptions.SelectMany(option => option.Aliases).Distinct(StringComparer.Ordinal).ToArray();

    public static RootCommand GetRootCommand(Func<MyOption, Task> action)
    {
        var rootCommand = new RootCommand
        {
            Url
        };

        foreach (var option in AllOptions) rootCommand.AddGlobalOption(option);
        rootCommand.SetHandler(async (myOption) => await action(myOption), new MyOptionBinder());

        return rootCommand;
    }

    private static readonly Argument<string> UpdateDownloadedDir =
        new("dir", "要追更的目录: 仓库根(扫描其直接子目录)或番剧目录本身(其下有 bbdown.json), 默认为当前目录")
        { Arity = ArgumentArity.ZeroOrOne };

    internal sealed record UpdateDownloadedRequest(MyOption Option, string Directory);

    /// <summary>
    /// 追更子命令。参数全部复用根命令的 global option 实例，因此参数集与根命令天然一致，
    /// 不需要重复声明一遍。
    /// </summary>
    public static Command GetUpdateDownloadedCommand(Func<MyOption, string, Task> action)
    {
        var command = new Command(
            "update-downloaded",
            "按各目录 bbdown.json 记载的来源链接追更已下载番剧; 已下完的集会跳过下载, 只补新集并刷新弹幕/元数据; --json-only 可只迁移 bbdown.json 不下载")
        {
            UpdateDownloadedDir
        };
        command.AddAlias("ud");
        command.SetHandler(async (request) => await action(request.Option, request.Directory), new UpdateDownloadedBinder());
        return command;
    }

    internal class UpdateDownloadedBinder : BinderBase<UpdateDownloadedRequest>
    {
        protected override UpdateDownloadedRequest GetBoundValue(BindingContext bindingContext) => new(
            MyOptionBinder.Bind(bindingContext.ParseResult, ""),
            bindingContext.ParseResult.GetValueForArgument(UpdateDownloadedDir) ?? "");
    }

    /// <summary>
    /// 扫码登录子命令。这两个参数只服务于本地终端交互，故不进 AllOptions 也不进 MyOption：
    /// 前者是配置文件的白名单，后者会被 HTTP 任务接口原样接收。
    /// beta4 不允许同一个 Option 实例挂到两个命令上，所以 login 与 logintv 各要一份，用工厂保证声明一致。
    /// </summary>
    internal static Command GetLoginCommand(string name, string description, Func<LoginQr, Task> action)
    {
        var imageOption = new Option<bool>(
            ["--qr-image", "-img"],
            "用系统默认图片查看器打开二维码，此时不再在终端打印");
        var styleOption = new Option<string>(
            ["--qr-style", "-qs"],
            $"终端二维码渲染方式: {string.Join('/', QrConsoleRender.AllStyleNames)}；compact(默认)用半角块, 宽高各减半, block 用双整块 ██, 供渲染不出半角块的终端使用；与 -img 同时使用时忽略");
        styleOption.AddValidator(result => result.ErrorMessage = QrConsoleRender.ValidateStyle(result.GetValueOrDefault<string>()));
        var command = new Command(name, description);
        command.AddOption(imageOption);
        command.AddOption(styleOption);
        command.SetHandler(context => action(new LoginQr(
            context.ParseResult.GetValueForOption(imageOption),
            context.ParseResult.GetValueForOption(styleOption))));
        return command;
    }
}
