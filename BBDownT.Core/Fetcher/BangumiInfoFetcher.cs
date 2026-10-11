using BBDownT.Core.Entity;
using System.Text.Json;
using static BBDownT.Core.Entity.Entity;
using static BBDownT.Core.Util.HTTPUtil;

namespace BBDownT.Core.Fetcher;

public class BangumiInfoFetcher : IFetcher
{
    public Task<VInfo> FetchAsync(string id) => FetchAsync(id, url => GetWebSourceAsync(url));

    public Task<VInfo> FetchSeasonAsync(string id) => FetchSeasonAsync(id, url => GetWebSourceAsync(url));

    internal static Task<VInfo> FetchAsync(string id, Func<string, Task<string>> fetch)
        => FetchCore(id, fetch, seasonMode: false);

    internal static Task<VInfo> FetchSeasonAsync(string id, Func<string, Task<string>> fetch)
        => FetchCore(id, fetch, seasonMode: true);

    private static async Task<VInfo> FetchCore(string id, Func<string, Task<string>> fetch, bool seasonMode)
    {
        id = id[3..];
        string api = $"https://{Config.EPHOST}/pgc/view/web/season?ep_id={id}";
        string json = await fetch(api);
        using var infoJson = JsonDocument.Parse(json);
        var result = infoJson.RootElement.GetProperty("result");
        string cover = result.GetProperty("cover").ToString();
        string title = result.GetProperty("title").ToString();
        string desc = result.GetProperty("evaluate").ToString();
        string pubTimeStr = result.GetProperty("publish").GetProperty("pub_time").ToString();
        long pubTime = string.IsNullOrEmpty(pubTimeStr) ? 0 : DateTimeOffset.ParseExact(pubTimeStr, "yyyy-MM-dd HH:mm:ss", null).ToUnixTimeSeconds();

        List<Page> pagesInfo;
        string index;
        if (seasonMode)
        {
            // 整季模式(md 链接): 正片 + 全部番外 section 一起解析, 各 section 分集的主标题
            // 带 [section名] 后缀, 下载时会落到独立的 标题[section名] 目录; 正片保持原标题。
            pagesInfo = [];
            index = "";
            if (result.TryGetProperty("episodes", out JsonElement mainEpisodes)
                && mainEpisodes.ValueKind == JsonValueKind.Array)
            {
                var (pages, selectedIndex) = BangumiPageMapper.Map(
                    mainEpisodes.EnumerateArray(), id, allowMissingPublicationTime: false,
                    startIndex: pagesInfo.Count + 1);
                pagesInfo.AddRange(pages);
                if (selectedIndex != "") index = selectedIndex;
            }
            if (result.TryGetProperty("section", out JsonElement sections))
            {
                foreach (var section in sections.EnumerateArray())
                {
                    if (!section.TryGetProperty("episodes", out JsonElement sectionEpisodes)
                        || sectionEpisodes.ValueKind != JsonValueKind.Array) continue;
                    string sectionName = section.GetProperty("title").ToString();
                    var (pages, selectedIndex) = BangumiPageMapper.Map(
                        sectionEpisodes.EnumerateArray(), id, allowMissingPublicationTime: false,
                        startIndex: pagesInfo.Count + 1,
                        sectionTitle: (title + "[" + sectionName + "]").Trim(),
                        pvSection: sectionName.Contains("PV", StringComparison.OrdinalIgnoreCase));
                    pagesInfo.AddRange(pages);
                    if (selectedIndex != "") index = selectedIndex;
                }
            }
        }
        else
        {
            var pages = result.GetProperty("episodes").EnumerateArray();

            //episodes为空; 或者未包含对应epid，番外/花絮什么的
            if (!(pages.Any() && result.GetProperty("episodes").ToString().Contains($"/ep{id}")))
            {
                if (result.TryGetProperty("section", out JsonElement sections))
                {
                    foreach (var section in sections.EnumerateArray())
                    {
                        if (section.ToString().Contains($"/ep{id}"))
                        {
                            title += "[" + section.GetProperty("title").ToString() + "]";
                            pages = section.GetProperty("episodes").EnumerateArray();
                            break;
                        }
                    }
                }
            }

            (pagesInfo, index) = BangumiPageMapper.Map(pages, id, allowMissingPublicationTime: false);
        }

        var info = new VInfo
        {
            Title = title.Trim(),
            Desc = desc.Trim(),
            Pic = cover,
            PubTime = pubTime,
            PagesInfo = pagesInfo,
            IsBangumi = true,
            IsCheese = true,
            Index = index
        };

        return info;
    }
}
