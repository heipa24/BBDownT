namespace BBDownT.Core;

public interface IFetcher
{
    Task<Entity.VInfo> FetchAsync(string id);

    /// <summary>
    /// 整季级获取(md 链接等): 除正片外还包含全部番外 section。默认等同 FetchAsync。
    /// </summary>
    Task<Entity.VInfo> FetchSeasonAsync(string id) => FetchAsync(id);
}