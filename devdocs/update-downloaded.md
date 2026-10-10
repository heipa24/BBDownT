# 追更已下载番剧（update-downloaded / ud）

实现日期：2026-10-11。目标是让 `-p ALL -dd` 下载过的番剧可以一条命令补新集，且能只刷新弹幕或元数据。

## 功能与入口

- CLI：`BBDownT update-downloaded [<目录>] [根命令的全部选项]`，短名 `ud`。`<目录>` 缺省为当前目录（回落顺序：位置参数 → `--work-dir` → 当前目录）。
- 无 HTTP 入口：扫描目录不落在 `MyOption` 上，`ServeRequestOptions` 继承不到它，服务器任务无法触发追更。
- 只支持两种独占刷新模式：`--danmaku-only`（只刷弹幕）与 `--metadata-only`（只刷元数据）。`--sub-only`/`--cover-only`/`--video-only`/`--audio-only`/`--download-all`/`--interactive` 在 `UpdateDownloaded.Validate` 里直接拒绝；`--only-show-info` 允许当干跑。

## bbdown.json 的两个新字段

`Program.WriteBbdownMetadata` 新增 `BbdownMarkers(SelectPage, DanmakuFormats)` 入参，只在有值时写键：

| 键 | 值 | 来源 |
|---|---|---|
| `selectPage` | 用户传入的 `-p` 原串 | `myOption.SelectPageSpecified` |
| `danmakuFormats` | 生效格式串，如 `xml,ass` | `downloadDanmaku`（`-dd` 或 `--danmaku-only`）+ `ParseDownloadDanmakuFormats` |

不写 `null`/`false`：写元数据总是先读回既有 JSON 再合并，缺省即保留历史值，因此一次不带 `-dd` 的重跑不会把已记录的弹幕标记抹掉。`selectPage` 额外只增不降级（`ShouldRecordSelectPage`）：记成 `ALL` 之后，用户单独补下一集（`-p 3`）会把 `3` 写回来，但那等于把目录从"按全集下载过"里踢出去，之后 `ud` 不再自动追更它，所以 `ALL` 粘性保留。三个调用点（`--danmaku-only` 分支、正常下载完成、`HandleExistingOutput`）都收到同一份 markers，所以旧目录在首次追更时即被补写新字段。

`BbdownMarkers` 挂在 `DownloadPagesAsync` 里构造，因为"本次是否覆盖全部分P"只有在那里才知道（`GetSelectedPages` 返回 `null` 即 ALL）；`downloadDanmaku` 本来就是 `DownloadPageAsync` 的入参。

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
