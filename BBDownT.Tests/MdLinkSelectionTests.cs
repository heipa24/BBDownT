using BBDownT.Core.Entity;
using static BBDownT.Core.Entity.Entity;

namespace BBDownT.Tests;

public class MdLinkSelectionTests
{
    private static VInfo CreateBangumi(int pageCount, string selectedIndex, params int[] pvPageIndexes)
    {
        var pvSet = pvPageIndexes.ToHashSet();
        return new VInfo
        {
            Title = "测试番剧",
            Desc = "",
            Pic = "",
            PubTime = 0,
            IsBangumi = true,
            PagesInfo = Enumerable.Range(1, pageCount)
                .Select(i =>
                {
                    var page = new Page(i, "100", $"{i}", $"{100 + i}", $"EP{i}", 60, "", 0);
                    page.pvSection = pvSet.Contains(i);
                    return page;
                })
                .ToList(),
            Index = selectedIndex
        };
    }

    [Theory]
    [InlineData("https://www.bilibili.com/bangumi/media/md28230042")]
    [InlineData("https://www.bilibili.com/bangumi/media/md28230042/?spm_id_from=333")]
    [InlineData("https://www.bilibili.com/bangumi/media/MD28230042")]
    [InlineData("md28230042")]
    public void MdSource_IsDetected(string input)
    {
        Assert.True(Program.IsBangumiMdSource(input));
    }

    [Theory]
    [InlineData("https://www.bilibili.com/bangumi/play/ep340931")]
    [InlineData("https://www.bilibili.com/bangumi/play/ss34543")]
    [InlineData("ep340931")]
    [InlineData("ss34543")]
    [InlineData("BV1xx411c7mD")]
    [InlineData("mdx")]
    [InlineData("md")]
    [InlineData("")]
    public void NonMdSource_IsNotDetected(string input)
    {
        Assert.False(Program.IsBangumiMdSource(input));
    }

    [Fact]
    public void MdLink_WithoutExplicitPage_DefaultsToAll()
    {
        var option = new MyOption { Url = "https://www.bilibili.com/bangumi/media/md28230042" };
        var vInfo = CreateBangumi(3, "3");

        Assert.Null(Program.GetSelectedPages(option, vInfo, option.Url));
    }

    [Fact]
    public void MdLink_WithoutExplicitPage_ExcludesPvPages()
    {
        var option = new MyOption { Url = "https://www.bilibili.com/bangumi/media/md28230042" };
        var vInfo = CreateBangumi(6, "1", pvPageIndexes: 6);

        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, Program.GetSelectedPages(option, vInfo, option.Url));
    }

    [Fact]
    public void MdLink_WithoutExplicitPage_AllPvFallsBackToAll()
    {
        var option = new MyOption { Url = "md28230042" };
        var vInfo = CreateBangumi(2, "1", 1, 2);

        Assert.Null(Program.GetSelectedPages(option, vInfo, option.Url));
    }

    [Fact]
    public void MdLink_WithExplicitAll_IncludesPvPages()
    {
        var option = new MyOption
        {
            Url = "https://www.bilibili.com/bangumi/media/md28230042",
            SelectPage = "ALL",
            SelectPageSpecified = true
        };
        var vInfo = CreateBangumi(6, "1", pvPageIndexes: 6);

        Assert.Null(Program.GetSelectedPages(option, vInfo, option.Url));
    }

    [Fact]
    public void MdLink_WithExplicitPage_UsesSelection()
    {
        var option = new MyOption
        {
            Url = "https://www.bilibili.com/bangumi/media/md28230042",
            SelectPage = "2",
            SelectPageSpecified = true
        };
        var vInfo = CreateBangumi(3, "3");

        Assert.Equal(new[] { "2" }, Program.GetSelectedPages(option, vInfo, option.Url));
    }

    [Fact]
    public void EpLink_WithoutExplicitPage_StillSelectsIndex()
    {
        var option = new MyOption { Url = "https://www.bilibili.com/bangumi/play/ep340931" };
        var vInfo = CreateBangumi(3, "3");

        Assert.Equal(new[] { "3" }, Program.GetSelectedPages(option, vInfo, option.Url));
    }

    [Fact]
    public void ResolvePageTitle_PrefersSectionTitleOverride()
    {
        var vInfo = CreateBangumi(2, "1");
        vInfo.Title = "来自深渊 深沉灵魂的黎明";
        var mainPage = vInfo.PagesInfo[0];
        var sectionPage = vInfo.PagesInfo[1];
        sectionPage.sectionTitle = "来自深渊 深沉灵魂的黎明[玛露尔可的日常]";

        Assert.Equal("来自深渊 深沉灵魂的黎明", Program.ResolvePageTitle(vInfo, mainPage));
        Assert.Equal("来自深渊 深沉灵魂的黎明[玛露尔可的日常]", Program.ResolvePageTitle(vInfo, sectionPage));
    }
}
