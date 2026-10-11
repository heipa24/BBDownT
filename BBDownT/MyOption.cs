using System.Collections.Generic;

namespace BBDownT;

internal class MyOption
{
    public string Url { get; set; } = default!;
    public bool UseTvApi { get; set; }
    public bool UseAppApi { get; set; }
    public bool UseIntlApi { get; set; }
    public bool UseMP4box { get; set; }
    public string? EncodingPriority { get; set; }
    public string? DfnPriority { get; set; }
    internal bool EncodingPriorityFirst { get; set; }
    public bool OnlyShowInfo { get; set; }
    public bool ShowAll { get; set; }
    public bool UseAria2c { get; set; }
    public bool NoAria2c { get; set; }
    public bool Interactive { get; set; }
    public bool HideStreams { get; set; }
    public bool MultiThread { get; set; } = true;
    public bool SimplyMux {  get; set; } = false;
    public bool VideoOnly { get; set; }
    public bool AudioOnly { get; set; }
    public bool DanmakuOnly { get; set; }
    public bool CoverOnly { get; set; }
    public bool SubOnly { get; set; }
    public bool Debug { get; set; }
    public bool SkipMux { get; set; }
    public bool SkipSubtitle { get; set; }
    public bool SkipCover { get; set; }
    public bool MetadataOnly { get; set; }
    /// <summary>
    /// 只更新 bbdown.json 元数据, 不调 playurl、不写任何媒体文件; 与 update-downloaded 配合可迁移旧目录。
    /// </summary>
    public bool JsonOnly { get; set; }
    /// <summary>
    /// json-only 模式的写出目录: ud 场景由 BuildChild 直通 bbdown.json 所在目录, 免疫文件名模板漂移。
    /// 不来自命令行, 故 internal 不进 API JSON。
    /// </summary>
    internal string? BbdownJsonDir { get; set; }
    public bool ForceHttp { get; set; } = true;
    public bool DownloadDanmaku { get; set; } = false;
    public string? DownloadDanmakuFormats { get; set; }
    public bool SkipAi { get; set; } = true;
    public string? SubtitleLanguage { get; set; }
    public string? AiSubtitlePolicy { get; set; }
    public bool VideoAscending { get; set; } = false;
    public bool AudioAscending { get; set; } = false;
    public bool AllowPcdn { get; set; } = false;
    public bool ForceReplaceHost { get; set; } = true;
    public bool SaveArchivesToFile { get; set; } = false;
    public string FilePattern { get; set; } = "";
    public string MultiFilePattern { get; set; } = "";
    public string SelectPage { get; set; } = "";
    internal bool SelectPageSpecified { get; set; }
    /// <summary>
    /// 追更独占模式下只处理 bbdown.json 已记载的这些分P；不来自命令行，故 internal 不进 API JSON。
    /// </summary>
    internal List<int>? UpdateRecordedPages { get; set; }
    public string Language { get; set; } = "";
    public string? AudioLanguage { get; set; }
    public string UserAgent { get; set; } = "";
    public string Cookie { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string Aria2cArgs { get; set; } = "";
    public string WorkDir { get; set; } = "";
    public string FFmpegPath { get; set; } = "";
    public string Mp4boxPath { get; set; } = "";
    public string Aria2cPath { get; set; } = "";
    public string UposHost { get; set; } = "";
    public string DelayPerPage { get; set; } = "0";
    public bool DownloadAll { get; set; }
    public int DelayPerVideo { get; set; } = 10;
    public string Host { get; set; } = "api.bilibili.com";
    public string EpHost { get; set; } = "api.bilibili.com";
    public string TvHost { get; set; } = "api.snm0516.aisee.tv";
    public string Area { get; set; } = "";
    public string? ConfigFile { get; set; }
    //以下仅为兼容旧版本命令行，不建议使用
    public string Aria2cProxy { get; set; } = "";
    public bool OnlyHevc { get; set; }
    public bool OnlyAvc { get; set; }
    public bool OnlyAv1 { get; set; }
    public bool AddDfnSubfix { get; set; }
    public bool NoPaddingPageNum { get; set; }
    public bool BandwithAscending { get; set; }

    internal MyOption ForBatchVideo(string url, string workDir)
    {
        // All options are value types or immutable strings. Preserve explicit
        // selection/order flags while isolating mutations made for one video.
        var copy = (MyOption)MemberwiseClone();
        copy.Url = url;
        copy.DownloadAll = false;
        copy.WorkDir = workDir;
        // 追更的单次限制不随批量复制扩散
        copy.UpdateRecordedPages = null;
        copy.BbdownJsonDir = null;
        // SetUpWork has already normalized these legacy switches on the parent.
        // Replaying them would append filename suffixes or proxy arguments again.
        copy.AddDfnSubfix = false;
        copy.NoPaddingPageNum = false;
        copy.Aria2cProxy = "";
        copy.OnlyHevc = copy.OnlyAvc = copy.OnlyAv1 = false;
        copy.BandwithAscending = false;
        return copy;
    }
}
