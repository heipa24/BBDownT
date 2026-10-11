# 追更已下载番剧（update-downloaded / ud）

实现日期：2026-10-11。目标是让 `-p ALL -dd` 下载过的番剧可以一条命令补新集，且能只刷新弹幕或元数据。

## 功能与入口

- CLI：`BBDownT update-downloaded [<目录>] [根命令的全部选项]`，短名 `ud`。`<目录>` 缺省为当前目录（回落顺序：位置参数 → `--work-dir` → 当前目录）。
- 无 HTTP 入口：扫描目录不落在 `MyOption` 上，`ServeRequestOptions` 继承不到它，服务器任务无法触发追更。
- 独占刷新模式：`--danmaku-only`（只刷弹幕）、`--metadata-only`（只刷元数据）与 `--json-only`（只迁移 bbdown.json，见下文）。`--sub-only`/`--cover-only`/`--video-only`/`--audio-only`/`--download-all`/`--interactive` 在 `UpdateDownloaded.Validate` 里直接拒绝；`--only-show-info` 允许当干跑。

## bbdown.json 的两个新字段

`Program.WriteBbdownMetadata` 新增 `BbdownMarkers(SelectPage, DanmakuFormats)` 入参，只在有值时写键：

| 键 | 值 | 来源 |
|---|---|---|
| `selectPage` | 用户传入的 `-p` 原串 | `myOption.SelectPageSpecified` |
| `danmakuFormats` | 生效格式串，如 `xml,ass` | `downloadDanmaku`（`-dd` 或 `--danmaku-only`）+ `ParseDownloadDanmakuFormats` |

不写 `null`/`false`：写元数据总是先读回既有 JSON 再合并，缺省即保留历史值，因此一次不带 `-dd` 的重跑不会把已记录的弹幕标记抹掉。`selectPage` 额外只增不降级（`ShouldRecordSelectPage`）：记成 `ALL` 之后，用户单独补下一集（`-p 3`）会把 `3` 写回来，但那等于把目录从"按全集下载过"里踢出去，之后 `ud` 不再自动追更它，所以 `ALL` 粘性保留。三个调用点（`--danmaku-only` 分支、正常下载完成、`HandleExistingOutput`）都收到同一份 markers，所以旧目录在首次追更时即被补写新字段。

`BbdownMarkers` 挂在 `DownloadPagesAsync` 里构造，因为"本次是否覆盖全部分P"只有在那里才知道（`GetSelectedPages` 返回 `null` 即 ALL）；`downloadDanmaku` 本来就是 `DownloadPageAsync` 的入参。

## bbdown.json v2 结构（2026-10-11 扩展）

写键核心收敛为 `Program.UpdateBbdownMetadata(dir, sourceId, title, markers, details, pageUpdates)`，`WriteBbdownMetadata` 是它的"单文件条目"封装。新增键与写值规则：

| 键 | 值 | 来源 | 写值规则 |
|---|---|---|---|
| `version` | `2` | 固定 | 每次写出都置 2，旧 v1 文件合并写入时自动升级 |
| `media.aid/bvid/epid/ownerName/pubTime` | view 级媒体元数据 | `Page`（view/season 接口） | 非空/`pubTime>0` 才写；合并进既有子对象 |
| `download.filePattern/multiFilePattern/encodingPriority/dfnPriority/audioLanguage/subtitleLanguage/aria2c` | 可重放参数快照 | `BbdownDownloadOptions.From(MyOption)` | 空串/false 不写；"最新非默认值覆盖"，第二次没给则保留旧值 |
| `pages[].title/cid/dur/res` | view 级分P数据 | `Page` | 始终可写（json-only/试看响应也有） |
| `pages[].quality/fps/codecs/audioCodecs` | 实际选中流 | playurl 选中的 `Video`/`Audio` | 试看响应（`parsedResult.IsPreviewOnly`）时不写，保留旧值 |
| `extra` | 自由扩展位 | — | 程序从不生成、从不删除 |

合并兼容性：`pages[]` 条目替换改为"克隆旧 JsonObject → 覆盖本次有值键 → 放回"，所以本次缺席的键（旧 `file`、旧流级信息）与双方都不认识的键（顶层与条目两级）都原样保留，外部脚本写入的数据不会被洗掉。

载体类型（均在 `Program` 内）：`BbdownPageDetails(Media, Stream, Download)` 每分P构造一次，`From(p, video, audio, includeStream, option)`；`includeStream=false` 表示本次没有可信的流信息。`DownloadPageAsync` 在试看校验之后构造，DASH 分支用选中轨道、DURL 分支没有轨道信息。

## --json-only 迁移模式

`BBDownT ud <目录> --json-only`（或根命令 `BBDownT <视频> --json-only`）：只更新 `bbdown.json`，不调 playurl、不写任何媒体文件。

- **实现**：`DownloadPagesAsync` 在 plan 计算后、runner 之前早退（`Program.UpdateBbdownJsonOnly`）。页集合复用 `plan.Pages`（显式 `-p` 可收窄，缺省全集，新集也会建无 `file` 条目）。
- **目标目录**：ud 场景由 `BuildChild` 直通 `metadata.Dir`（`MyOption.BbdownJsonDir`，`ForBatchVideo` 会重置），免疫 `-F/-M` 漂移；根命令场景回落 `FormatSavePath` 推导。
- **语义**：迁移不改写用法标记（markers 传 `default`，`selectPage`/`danmakuFormats` 保留历史值）；`--only-show-info` 下是干跑不落盘；与 `--danmaku-only`/`--metadata-only` 互斥（`UpdateDownloaded.Validate`），与其他独占模式同给时 `HandleConflictingOptions` 显式忽略。它不算 artifact-only 模式，更新的是全部分P。

## 大会员门槛分析（为什么 json-only 无需大会员）

`bbdown.json` 的全部数据只有两个来源，都不经过 playurl：

1. **view/season 接口**（`VInfo`/`PagesInfo`）：`media` 各键与 `pages[].title/cid/dur/res`。`Page` 自带 `aid/cid/epid/title/dur/res/pubTime/ownerName/bvid`（`Entity.cs`），这些不是会员门槛数据。
2. **本地命令行参数**（`MyOption`）：`download` 快照与 markers。

playurl（`ExtractTracksAsync`，每分P内调用）才是会员门槛：无大会员时会员番剧返回 `IsPreviewOnly` 试看片段。因此：

- `--json-only` 全程不进 playurl → 无大会员（甚至无登录）即可完成旧目录迁移。
- `--metadata-only` 借 `CheckPreviewMetadataRefresh` 的 ffprobe 时长校验（既有文件时长 ≥ 整集，1% 容差）对完整正片放行 ffmpeg 元数据刷新，同样可用。
- 唯一拿不到的是流级字段（`quality/fps/codecs/audioCodecs`）；试看响应绝不能把这些值写进 bbdown.json（它们不代表当初下载的流），合并语义下旧值保留、无值缺省，不影响追更。

## 扫描与筛选

`BbdownMetadataReader.TryRead` 是纯 IO：坏 JSON、无 `bbdown.json`、既无 `url` 又无 `sourceId` 都返回 `null`（跳过该目录），绝不抛给调用方。

- 目录入选：`pages.Count > 1`（旧版文件没有 `selectPage`，多P目录本身就是全集证据）或 `selectPage == ALL`。显式 `-p ALL` 令该筛选作废。
- 弹幕刷新：`danmakuFormats` 存在，或每个 `pages[]` 条目都记载了 `xml`/`ass`。显式 `-dd` 强制刷新。
- 分P选择：入选原因是 `ALL` 或旧文件多P时，重放 `-p ALL` 以取到新集；显式 `-p` 优先。
- `--danmaku-only`/`--metadata-only` 把 `pages[]` 的页号交给 `MyOption.UpdateRecordedPages`，`DownloadPagesAsync` 用 `Program.RetainRecordedPages` 与接口当前分P取交集。不能拼成 `"1,2,3"` 丢给 `PageSelectionParser`：它对越界集号抛 `ArgumentException`，而撤集、`ss` 重新映射到别的季、目录改名都会让记载页号越界。

## 执行目录不变式

`ExecDir` 恒为"含 `bbdown.json` 的目录的父目录"。默认多P模板是 `<videoTitle>/[P<pageNumberWithZero>]<pageTitle>`，成品写在 `<父>/<标题>/` 下，`bbdown.json` 就在其中；只有 CWD 为父目录时 `ShouldUseMuxedOutputCache` 才命中既有成品，"跳过已下完的集"才成立。

因此 `<目录>` 传仓库根还是传番剧目录本身只影响处理范围，不影响落盘路径。产物文件名仍由**当前** `-F/-M` 决定，`pages[].file` 只用于页选择，不回填旧文件名——追更时应保持与当初一致的模板。

逐目录串行，目录间等待 `--delay-per-video`；单项失败只计数不中断，末尾抛 `InvalidOperationException` 汇总，错误展示与根命令共用 `Program.RunWithFailureReportingAsync`。

## 命令行基础设施改动

`System.CommandLine 2.0.0-beta4` 不允许同一 `Option` 实例挂到两个命令（`AddOption` 会设置 `Parent`），因此根命令的 60+ 个选项改由 `foreach (var o in AllOptions) rootCommand.AddGlobalOption(o)` 注册。global option 递归作用于子命令，子命令于是复用同一批实例，`MyOptionBinder.Bind(ParseResult, url)` 抽出来供两条入口共用（追更没有 url，传 `""`）。

两处连带改动：

- `BBDownTConfigParser` 的配置文件白名单原本读 `rootCommand.Options`，而 global option 不在其中，必须并入 `CommandLineInvoker.OptionAliases`，否则 `BBDownT.config` 里的 `-dd`/`-p` 会集体被判成无法识别的配置项（`ConfigParserTests.ConfigFile_SelectPageAndDanmaku_AreRecognisedAndMerged` 守这条）。
- `Program.InvokeCommandLineAsync` 的子命令分派：原先只有 `serve` 会转传完整 arg 列表，现在 `update-downloaded` 同走该分支（并把 `commandName` 传给 `HandleConfig` 以便按子命令上下文校验配置）。副作用是 `login`/`logintv`/`serve` 现在也接受全部根选项。
