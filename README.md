[![img](https://img.shields.io/github/stars/LOVAHE/BBDownT?label=%E7%82%B9%E8%B5%9E)](https://github.com/LOVAHE/BBDownT)  [![img](https://img.shields.io/github/last-commit/LOVAHE/BBDownT?label=%E6%9C%80%E8%BF%91%E6%8F%90%E4%BA%A4)](https://github.com/LOVAHE/BBDownT/commits)  [![img](https://img.shields.io/github/release/LOVAHE/BBDownT?label=%E6%9C%80%E6%96%B0%E7%89%88%E6%9C%AC)](https://github.com/LOVAHE/BBDownT/releases)  [![img](https://img.shields.io/github/license/LOVAHE/BBDownT?label=%E8%AE%B8%E5%8F%AF%E8%AF%81)](https://github.com/LOVAHE/BBDownT)  [![Build Latest](https://github.com/LOVAHE/BBDownT/actions/workflows/build_latest.yml/badge.svg)](https://github.com/LOVAHE/BBDownT/actions/workflows/build_latest.yml)  [![CodeQL](https://github.com/LOVAHE/BBDownT/actions/workflows/codeql.yml/badge.svg?branch=v2)](https://github.com/LOVAHE/BBDownT/actions/workflows/codeql.yml)

> 本项目仅供个人学习、研究和非商业性用途。使用本工具时，需自行确保遵守相关法律法规，特别是与版权相关的法律条款。开发者不对因使用本工具而产生的任何版权纠纷或法律责任承担责任。请谨慎使用，并仅在有合法授权的情况下使用相关内容。

# BBDownT
一个命令行式哔哩哔哩下载器. Bilibili Downloader.

本项目接手自 [BBDown](https://github.com/nilaoda/BBDown)，后续项目名称、命令和文档均以 `BBDownT` 为准。

# 注意
本软件混流时需要外部程序：

* 普通视频：[ffmpeg](https://www.gyan.dev/ffmpeg/builds/) ，或 [mp4box](https://gpac.wp.imt.fr/downloads/)
* 杜比视界：ffmpeg5.0以上或新版mp4box.

# 快速开始

> 本仓库是上游 [LOVAHE/BBDownT](https://github.com/LOVAHE/BBDownT) 的 fork，`main` 分支基于上游 `v2` 分支开发，除上游功能外还包含一批上游尚未发布的新功能：下载时写出 `bbdown.json` 元数据、已存在成片只刷元数据（`--metadata-only`）、已下载番剧批量追更（`update-downloaded`/`ud`）、`--update` 用本机 nupkg 重新打包重装等，详见下文使用教程。**本仓库不发布任何预编译产物**：nuget.org 与 GitHub Releases 上的 `BBDownT` 都是上游版本，不含本仓库的改动；需要使用本仓库的代码时请按下文自行构建。

## 自行构建

环境要求：.NET SDK 9（本仓库 CI 使用 9.0.317）。

### 直接运行

```
dotnet build BBDownT.sln
dotnet run --project BBDownT -- "https://www.bilibili.com/video/BV1qt4y1X7TW"
```

### 打包为 nupkg 并安装为 dotnet tool

本仓库的工程已配置 `PackAsTool`，打出的 nupkg 就是 dotnet tool 包，命令名为 `BBDownT`：

```
dotnet pack BBDownT/BBDownT.csproj -c Release -o artifacts
```

产物为 `artifacts/BBDownT.<版本号>.nupkg`（版本号取自 `BBDownT/BBDownT.csproj`，当前为 `BBDownT.2.1.6.nupkg`，包 ID 与 nuget.org 上的同名包一致）。

安装为全局命令（用 `--source` 而**不是** `--add-source`）：

```
dotnet tool install --global --source ./artifacts BBDownT
```

> `--add-source` 只是**追加**源，nuget.org 仍会参与解析。上游已把 `BBDownT` 发到了 nuget.org（当前最新 `2.1.7`），而 `dotnet tool install` 默认解析所有源中的最高版本，因此会装到上游的 `2.1.7` 而不是本地的 `2.1.6`。`--source` 会用给定源**替换**全部源，只从 `./artifacts` 解析。

只想在单个目录内使用时，可以装成局部工具：

```
dotnet tool install --tool-path ./tools --source ./artifacts BBDownT
./tools/BBDownT.exe "https://www.bilibili.com/video/BV1qt4y1X7TW"
```

重新打包后更新，或卸载：

```
dotnet tool update --global --source ./artifacts BBDownT
dotnet tool uninstall --global BBDownT
```

*PS: 本地包的 ID 与命令名和 nuget.org 上的 `BBDownT` 完全一致，不能与上游包同时安装。若之前装过上游版本，请先 `dotnet tool uninstall --global BBDownT` 再装本地包；从上游高版本回退到本地低版本时需要追加 `--allow-downgrade`。另外本地包版本号（`2.1.6`）与上游在 nuget.org 上的同名版本相同，无法再靠 `--add-source ./artifacts --version <版本>` 区分来源，要装本地包只能用上面的替换源或 `--configfile` 方式。安装时若提示找不到包，通常是 NuGet 官方源被镜像拦截或网络不可达，可以补一个只含本地源的 `nuget.config` 后改用 `--configfile` 安装：*

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="artifacts" />
  </packageSources>
</configuration>
```

```
dotnet tool install --global --configfile ./nuget.config BBDownT
```

### 重新打包并重装 (--update)

以 dotnet tool 形式安装后，可以让程序重新打包本机源码并重装：

```
# 首次执行一次: 记录包含 nupkg 的源目录（换机器或移动仓库后需重新记录）
BBDownT --set-update-source ./artifacts

# 重新打包并重装
BBDownT --update
```

*PS: 两个参数都需单独使用，不能与视频地址或其他参数一起传入。`--update` 只在以 dotnet tool 安装（全局或局部）时有效，独立二进制与 `dotnet run` 形态会拒绝并给出 `dotnet pack` + `dotnet tool install` 的手动指引；源目录取 `--set-update-source` 的记录，未记录时会尝试从安装位置推断仓库内的 `artifacts`，只使用本机 nupkg 源，不从上游拉取任何文件；源目录位于仓库内时先重新打包，不在仓库内时跳过打包；重装后会比对落地 `BBDownT.dll` 与源包的 SHA-256，比对不过就按失败处理（常见于旧程序集仍被正在运行的 BBDownT 占用）；Windows 下从 store 运行时转为分离式后台进程，结果写入 `BBDownT.update.result.json`，下次启动时汇报。更新前会自动备份 `BBDownT.data`/`BBDownTTV.data`/`BBDownTApp.data`/`BBDownT.config`/`BBDownT.archives`/`BBDownT.web.json`（cookie 与配置），更新后原样放回，不会丢失。*

### 发布独立二进制

指定 `-r` 时会启用 Native AOT：
```
dotnet publish BBDownT -r win-x64 -c Release -o artifact
```
产物为 `artifact/BBDownT.exe`（其他平台是无扩展名的 `artifact/BBDownT`），改名后命令名随之变化。可用 RID 与上游 CI 保持一致：`win-x64`、`win-arm64`、`osx-x64`、`osx-arm64`、`linux-x64`、`linux-arm64`。

Native AOT 需要系统的 C 链接器：Windows 需 Visual Studio C++ 生成工具，Linux 需 clang/lld 及对应的 sysroot。

Linux 产物还要注意 glibc 兼容性：上游 CI 在 Ubuntu 18.04 容器内构建以兼容较老的发行版，`linux-arm64` 还依赖专用的交叉编译容器，本地一般无法直接完成，建议直接使用上游 Release 产物。

### 运行测试

```
dotnet test BBDownT.sln
```

# 开始使用
目前命令行参数支持情况
```
Description:
  BBDownT是一个免费且便捷高效的哔哩哔哩下载/解析软件.

Usage:
  BBDownT <url> [command] [options]

Arguments:
  <url>  视频地址 或 av|bv|BV|ep|ss

Options:
  -tv, --use-tv-api                                            使用TV端解析模式
  -app, --use-app-api                                          使用APP端解析模式
  -intl, --use-intl-api                                        使用国际版(东南亚视频)解析模式
  --use-mp4box                                                 使用MP4Box来混流
  -e, --encoding-priority <encoding-priority>                  视频及音频编码的选择优先级, 用逗号分割 例: "hevc,av1,avc,flac,eac3,m4a"；与 -q 同时使用时越靠前越优先
  -q, --dfn-priority <dfn-priority>                            画质优先级,用逗号分隔 例: "8K 超高清, 1080P 高码率, HDR 真彩, 杜比视界"；与 -e 同时使用时越靠前越优先
  -info, --only-show-info                                      仅解析音视频和字幕信息，不下载；配合 --sub-only 仅列出字幕
  --show-all                                                   展示所有分P标题
  -aria2, --use-aria2c                                         调用aria2c进行下载(默认使用内置下载器, 需显式传入本参数才会启用aria2)
  --no-aria2                                                   强制使用内置下载器(当前版本默认即为内置下载器, 保留该参数用于兼容旧命令)
  -ia, --interactive                                           交互式选择音视频和字幕；字幕支持多选
  -hs, --hide-streams                                          不要显示所有可用音视频流
  -mt, --multi-thread                                          使用多线程下载(默认开启)
  --simply-mux                                                 精简混流，不增加描述、作者等信息
  --video-only                                                 仅下载视频；与 --audio-only 同时使用时下载两条独立主流并跳过混流，源不支持时失败
  --audio-only                                                 仅下载音频；与 --video-only 同时使用时下载两条独立主流并跳过混流，源不支持时失败
  --danmaku-only                                               仅下载弹幕
  --sub-only                                                   仅下载字幕
  --cover-only                                                 仅下载封面
  --debug                                                      输出调试日志
  --skip-mux                                                   跳过混流步骤
  --skip-subtitle                                              跳过字幕下载
  --skip-cover                                                 跳过封面下载
  --metadata-only                                              输出文件已存在时仅更新其元数据(标题/描述/封面/章节/字幕)，不重新下载或重编码音视频流；会保留原文件已有的音频语言/字幕标题等标签，接口只返回试看片段时先校验原文件为完整正片再刷新(需要 ffprobe)
  --force-http                                                 下载音视频时强制使用HTTP协议替换HTTPS(默认开启)
  -dd, --download-danmaku                                      下载弹幕
  -ddf, --download-danmaku-formats <download-danmaku-formats>  指定需下载的弹幕格式, 用逗号分隔, 可选 xml/ass, 默认: "xml,ass"
  --skip-ai                                                    跳过AI字幕下载(默认开启)
  --subtitle-language <subtitle-language>                      筛选字幕语言，逗号分隔；zh/en 匹配语言族，zh-Hans/ai-zh 精确匹配
  --ai-subtitle-policy <ai-subtitle-policy>                    字幕AI策略: exclude/include/prefer-human/only；显式指定时优先于 --skip-ai
  --video-ascending                                            视频升序(最小体积优先)
  --audio-ascending                                            音频升序(最小体积优先)
  --allow-pcdn                                                 不替换PCDN域名, 仅在正常情况与--upos-host均无法下载时使用
  -F, --file-pattern <file-pattern>                            使用内置变量自定义单P存储文件名:
  
                                                               <videoTitle>: 视频主标题
                                                               <pageNumber>: 视频分P序号
                                                               <pageNumberWithZero>: 视频分P序号(前缀补零)
                                                               <pageTitle>: 视频分P标题
                                                               <bvid>: 视频BV号
                                                               <aid>: 视频aid
                                                               <cid>: 视频cid
                                                               <dfn>: 视频清晰度
                                                               <res>: 视频分辨率
                                                               <fps>: 视频帧率
                                                               <videoCodecs>: 视频编码
                                                               <videoBandwidth>: 视频码率
                                                               <audioCodecs>: 音频编码
                                                               <audioBandwidth>: 音频码率
                                                               <ownerName>: 上传者名称
                                                               <ownerMid>: 上传者mid
                                                               <publishDate>: 收藏夹/番剧/合集发布时间
                                                               <videoDate>: 视频发布时间(分p视频发布时间与<publishDate>相同)
                                                               <apiType>: API类型(TV/APP/INTL/WEB)
  
                                                               默认为: <videoTitle>
  -M, --multi-file-pattern <multi-file-pattern>                使用内置变量自定义多P存储文件名:
  
                                                               默认为: <videoTitle>/[P<pageNumberWithZero>]<pageTitle>
  -p, --select-page <select-page>                              选择指定分p或分p范围: (-p 8 或 -p 1,2 或 -p 3-5 或 -p ALL 或 -p LAST 或 -p 3,5,LATEST)
  --language <language>                                        设置混流的音频语言(代码), 如chi, jpn等
  --audio-language <audio-language>                            选择配音语言，精确匹配 -info 列出的代码；仅默认WEB/DASH模式，输出文件附加语言后缀
  -ua, --user-agent <user-agent>                               指定user-agent；登录态默认使用持久化浏览器配置，匿名默认使用随机user-agent
  -c, --cookie <cookie>                                        设置字符串cookie用以下载网页接口的会员内容
  -token, --access-token <access-token>                        设置access_token用以下载TV/APP接口的会员内容
  --aria2c-args <aria2c-args>                                  调用aria2c的附加参数(默认包含"-x16 -s16 -j16 --min-split-size=1M"等, 使用时注意字符串转义)
  --work-dir <work-dir>                                        设置程序的工作目录
  --ffmpeg-path <ffmpeg-path>                                  设置ffmpeg的路径
  --mp4box-path <mp4box-path>                                  设置mp4box的路径
  --aria2c-path <aria2c-path>                                  设置aria2c的路径
  --upos-host <upos-host>                                      自定义upos服务器
  --force-replace-host                                         强制替换下载服务器host(默认开启)
  --save-archives-to-file                                      将下载过的视频记录到本地文件中, 用于后续跳过下载同个视频
  --delay-per-page <delay-per-page>                            设置下载合集分P之间的下载间隔时间(单位: 秒, 默认无间隔)
  --download-all                                               导出UP主空间TXT清单后，按清单逐条下载全部投稿
  --delay-per-video <delay-per-video>                          批量下载时视频之间的间隔秒数，默认10，0为不等待
  --host <host>                                                指定BiliPlus host(使用BiliPlus需要access_token, 不需要cookie, 解析服务器能够获取你账号的大部分权限!)
  --ep-host <ep-host>                                          指定BiliPlus EP host(用于代理api.bilibili.com/pgc/view/web/season, 大部分解析服务器不支持代理该接口)
  --tv-host <tv-host>                                          自定义tv端接口请求Host(用于代理api.snm0516.aisee.tv)
  --area <area>                                                (hk|tw|th) 使用BiliPlus时必选, 指定BiliPlus area
  --config-file <config-file>                                  读取指定的BBDownT本地配置文件(默认为: BBDownT.config)
  --update                                                     使用本机 nupkg 重新打包并重装 dotnet tool；需单独使用
  --set-update-source <set-update-source>                      记录用于 --update 重装的 nupkg 源目录（含 BBDownT.*.nupkg）；需单独使用
  --migrate                                                    手动迁移程序目录中的旧版BBDown配置、登录文件和下载记录
  --api-token <api-token>                                      服务器API鉴权Token，监听非本机地址且未配置时会自动生成
  --version                                                    Show version information
  -?, -h, --help                                               Show help and usage information


Commands:
  login                        通过APP扫描二维码以登录您的WEB账号
  logintv                      通过APP扫描二维码以登录您的TV账号
  serve                        以服务器模式运行
  ud, update-downloaded <dir>  按各目录 bbdown.json 记载的来源链接追更已下载番剧; 已下完的集会跳过下载, 只补新集并刷新弹幕/元数据
```

`login`/`logintv` 除继承上面的全局参数外，还各自带有两个二维码呈现参数（`BBDownT login --help` 的节选）：

```
Usage:
  BBDownT <url> login [options]

Options:
  -img, --qr-image                                             用系统默认图片查看器打开二维码，此时不再在终端打印
  -qs, --qr-style <qr-style>                                   终端二维码渲染方式: compact/block；compact(默认)用半角块, 宽高各减半, block 用双整块 ██, 供渲染不出半角块的终端使用；与 -img 同时使用时忽略
  ...
```

# 使用教程

<details>
<summary>配置文件 (NEW)</summary>

---

在`2.1.4`或更高版本中，BBDownT支持读取本地配置文件以简化命令行的手动输入。

如果没有指定`--config-file`，则默认读取程序同目录下的`BBDownT.config`文件；若指定该参数，则读取对应文件。

普通启动只读取BBDownT自己的配置文件，不会自动探测、迁移旧版BBDown的配置、登录文件或下载归档。

一个典型的配置文件:
```config
#本文件是BBDownT程序的配置文件
#以#开头的都会被程序忽略
#然后剩余非空白内容程序逐行读取，对于一个选项，其参数应当在下一行出现

#例如下面将设置输出文件名格式
--file-pattern
<videoTitle>[<dfn>]

--multi-file-pattern
<videoTitle>/[P<pageNumberWithZero>]<pageTitle>[<dfn>]

#下面设置下载多个分P时，每个分P的下载间隔为2秒
--delay-per-page
2

#开启弹幕下载功能
--download-danmaku
```

配置文件同样作用于 `ud` 追更子命令：写在这里的 `-dd`、`-p` 等相当于每次追更都带上它们，命令行上显式给出的同名参数优先。注意 `-dd` 写在配置里会让所有目录都刷新弹幕，覆盖"按各目录记录的用法判断"这一默认行为。

</details>

<details>
<summary>迁移旧版配置和登录信息</summary>

---

如果需要继续使用旧版 BBDown 的配置、登录信息或下载归档，请单独执行：
```
BBDownT --migrate
```

迁移程序所在目录中的文件，映射关系如下：

| 旧文件 | 新文件 |
| --- | --- |
| `BBDown.config` | `BBDownT.config` |
| `BBDown.data` | `BBDownT.data` |
| `BBDownTV.data` | `BBDownTTV.data` |
| `BBDownApp.data` | `BBDownTApp.data` |
| `BBDown.archives` | `BBDownT.archives` |

如果目标文件已经存在，程序会跳过该项，不会覆盖目标文件。迁移成功后，原文件会改名为带有`.migrated-时间戳`后缀的备份文件。迁移过程中出现错误时，命令会返回非零状态。

`--migrate`必须单独使用，不能与视频地址或其他参数同时使用。

</details>

<details>
<summary>下载归档 (--save-archives-to-file)</summary>

---

启用 `--save-archives-to-file` 后，已下载记录写入程序目录的 `BBDownT.archives`；新记录使用 `aid:cid`，每个分P独立归档。旧的 aid 记录保留：仅在原始视频页列表确认该 aid 只有一个分P时沿用；多P或页数未知时逐页检查，不把旧记录视为所有分P均已完成。只选一个分P也不会改变原始视频的页数判断。

</details>

<details>
<summary>自定义输出文件名格式 (NEW)</summary> 

---

在`2.1.4`或更高版本中，BBDownT支持自定义合并时的文件名组成。
|  代码   | 含义  |
|  ----  | ----  |
`<videoTitle>`|视频主标题
`<pageNumber>`|视频分P序号
`<pageNumberWithZero>`|视频分P序号(前缀补零)
`<pageTitle>`|视频分P标题
`<bvid>`|视频BV号
`<aid>`|视频aid
`<cid>`|视频cid
`<dfn>`|视频清晰度
`<res>`|视频分辨率
`<fps>`|视频帧率
`<videoCodecs>`|视频编码
`<videoBandwidth>`|视频码率
`<audioCodecs>`|音频编码
`<audioBandwidth>`|音频码率
`<ownerName>`|上传者名称(下载番剧时，该值为"")
`<ownerMid>`|上传者mid(下载番剧时，该值为"")
`<publishDate>`|收藏夹/番剧/合集发布时间(yyyy-MM-dd_HH-mm-ss)
`<videoDate>`|视频发布时间(分P视频发布时间与`<publishDate>`相同)
`<apiType>`|API类型（TV/APP/INTL/WEB）

</details>

<details>
<summary>WEB/TV鉴权</summary>  

---
  
扫码登录网页账号：
```
BBDownT login
```
然后按照提示操作。登录成功后会同时保存用于刷新Cookie的`ac_time_value`，后续运行时会在需要时自动刷新本地Cookie。

二维码默认用半角块`▀▄█`画在终端里，宽高各只有旧版双整块的一半（登录码约65列×33行，旧版是130列×65行）；终端字体渲染不出半角块时用`-qs block`退回双整块：

```
BBDownT login -qs block
```

屏幕太小放不下字符码时用`-img`，程序会把二维码写进系统临时目录并调用系统默认图片查看器打开（此时不再打印字符码；无桌面环境如SSH、容器下会打印图片路径，可自行取出打开）：

```
BBDownT login -img
```

两种方式都不会再在工作目录生成`qrcode.png`。

扫码登录云视听小电视账号：
```
BBDownT logintv
```
然后按照提示操作，二维码呈现参数同上。

*PS: `-img`依赖系统的默认图片查看器（Windows的文件关联、macOS的`open`、Linux的`xdg-open`），调用失败时会打印图片路径而不是报错中断登录；若字符码显示花乱，多半是终端字体的问题，换`-qs block`或`-img`即可*

手动加载网页cookie：
```
BBDownT -c "SESSDATA=******" "https://www.bilibili.com/video/BV1qt4y1X7TW"
```
如需手动Cookie也支持自动刷新，请同时包含`bili_jct`和`ac_time_value`。旧版本生成的`BBDownT.data`如果缺少`ac_time_value`，需要重新执行一次`BBDownT login`。

手动加载云视听小电视token：
```
BBDownT -tv -token "******" "https://www.bilibili.com/video/BV1qt4y1X7TW"
```

</details>

<details>
<summary>APP鉴权</summary>  

---

> TV登录产生的`access_token`也可以给APP接口使用。可复制`BBDownTTV.data`到`BBDownTApp.data`使程序自动读取.

目前程序无法自动获取鉴权信息，推荐通过**抓包**来获取.

在请求Header中寻找键为`authorization`的项，其值形为`identify_v1 5227************1`，其中的`5227************1`就是token(access_key)

获取后手动通过`-token`命令加载, 或写入`BBDownTApp.data`使程序自动读取.
  
```
BBDownT -app -token "******" "https://www.bilibili.com/video/BV1qt4y1X7TW"
```

</details>

<details>
<summary>常用命令</summary>  

---

下载普通视频：
```
BBDownT "https://www.bilibili.com/video/BV1qt4y1X7TW"
```
使用TV接口下载(粉丝量大的UP主基本上是无水印片源)：
```
BBDownT -tv "https://www.bilibili.com/video/BV1qt4y1X7TW"
```
当分P过多时，默认会隐藏展示全部的分P信息，你可以使用如下命令来显示所有每一个分P。
```
BBDownT --show-all "https://www.bilibili.com/video/BV1At41167aj"
```
选择下载某些分P的三种情况：
* 单个分P：10
```
BBDownT "https://www.bilibili.com/video/BV1At41167aj?p=10"
BBDownT -p 10 "https://www.bilibili.com/video/BV1At41167aj"
```
* 多个分P：1,2,10
```
BBDownT -p 1,2,10 "https://www.bilibili.com/video/BV1At41167aj"
```
* 范围分P：1-10
```
BBDownT -p 1-10 "https://www.bilibili.com/video/BV1At41167aj"
```
下载番剧全集：
```
BBDownT -p ALL "https://www.bilibili.com/bangumi/play/ss33073"
```
下载完成后输出目录里会写出 `bbdown.json`，之后出了新集可以直接追更，不用再记链接：
```
BBDownT ud "D:\Anime"
```
`ud` 只接受目录作为位置参数（不接视频地址）；详见"追更已下载的番剧"。

</details>

<details>
<summary>配音语言选择</summary>

查看视频当前及可选的配音语言。配音列表位于音视频流列表之后，默认版本标记为 `[默认]` 并在终端中以绿色显示；AI 版本标记为 `[AI]`，显式选择其他版本时另标记 `[当前]`：

```bash
BBDownT -info "BVxxxx"
```

如果列表中包含 `en-US`，可以选择这个版本，或仅保存它的音频：

```bash
BBDownT --audio-language en-US "BVxxxx"
BBDownT --audio-language en-US --audio-only "BVxxxx"
```

- 语言代码以该视频的 `-info` 列表为准，上面的 `en-US` 只是示例。一次选择一个代码，不区分大小写，但必须完整匹配；`en` 不会自动匹配 `en-US`。
- 不指定时沿用接口默认版本；需要原声时，选择列表中对应原声的代码。此功能使用平台已有配音，不生成翻译或配音。
- 目前仅支持默认 WEB 模式下的 DASH 音视频流，不能与 `-tv`、`-app`、`-intl`、`--sub-only`、`--cover-only`、`--danmaku-only` 一起使用。
- 找不到语言或接口未确认返回该版本时直接报错，不会静默改回默认版本。选择后重新获取整套音视频地址，视频画面也可能随平台翻译版本变化。
- 显式选择会在最终输出及主音视频临时文件名中加入后缀，如 `视频.audio-en-us.mp4` 或 `视频.audio-en-us.m4a`，防止和其他配音版本混淆。
- 配合 `--save-archives-to-file` 时，显式配音版本不读写默认配音的下载归档；常规混流模式由带语言后缀的输出文件判断是否已下载，`--skip-mux` 仍走已有原始流下载流程。
- `--language` 仍只设置封装文件的音轨语言标签，例如 `--language eng`。字幕仍由 `--subtitle-language` 和 AI 字幕策略选择，不会因切换配音自动改选。
- 参数也可写入配置文件。`-ia` 仍选择音视频规格，配音语言通过 `--audio-language` 指定。

</details>

<details>
<summary>字幕选择</summary>

国内视频字幕使用新版字幕接口，自动解析编码后的字幕地址，不再回退到旧版播放器、视频详情或 gRPC 字幕接口。国际站继续使用其独立字幕接口。

---

查看可用字幕及筛选结果，不下载字幕文件：
```
BBDownT -info --sub-only "https://www.bilibili.com/video/BV1qt4y1X7TW"
```

下载中英文字幕，同语言优先选择普通(CC)字幕，没有时保留AI字幕：
```
BBDownT --sub-only --subtitle-language zh,en --ai-subtitle-policy prefer-human "https://www.bilibili.com/video/BV1qt4y1X7TW"
```

`--subtitle-language`用逗号分隔多个语言，不区分大小写。`zh`、`en`会匹配同一语言族的字幕，例如`ai-zh`、`en-US`；`zh-Hans`、`ai-zh`等带连字符的代码只匹配对应语言。不指定时不限制字幕语言。

`--ai-subtitle-policy`支持以下选项：

| 选项 | 含义 |
| --- | --- |
| `exclude` | 排除AI字幕 |
| `include` | 包含AI字幕 |
| `prefer-human` | 同语言族有普通(CC)字幕时跳过AI字幕，否则保留AI字幕 |
| `only` | 仅下载AI字幕 |

不指定策略时沿用`--skip-ai`，默认跳过AI字幕；显式指定策略时优先使用该策略。语言和策略参数也可以写入配置文件。

手动选择需要下载的字幕，并允许AI字幕出现在候选中：
```
BBDownT --sub-only -ia --skip-ai false "https://www.bilibili.com/video/BV1qt4y1X7TW"
```
然后按照提示输入从1开始的字幕序号，支持逗号列表、范围、`ALL`或`NONE`。直接回车保留所有候选，输入错误会重新询问。使用`-info`时只显示列表，不进行交互。

`-p`、文件名模板和`--skip-subtitle`同样适用；没有符合条件的字幕时会给出提示。同语言存在多条字幕时，文件名会增加轨道标识，避免互相覆盖。

*PS: 程序先筛选语言，再应用AI策略，因此指定`zh-Hans`不会自动改选`ai-zh`。普通(CC)是平台分类，不保证人工校对；来源未知的字幕在`prefer-human`策略下会保留，但不会替代AI字幕。*

</details>

<details>
<summary>只刷新已存在文件的元数据 (--metadata-only)</summary>

---

`--metadata-only` 在输出文件已存在时不再下载音视频，而是以该文件为输入重新封装，只写回最新元数据(标题/描述/封面/章节/字幕)，音视频流保持 `-c copy` 搬运。刷新写入同目录的 `.partial` 暂存文件，成功后才原子替换；ffmpeg 失败、暂存文件缺失或暂存时长与原文件对不上时，原文件保持字节级不变。

刷新会先用 ffprobe 读回原文件已有的容器与流级标签(音频语言、字幕标题等)再写回，因为封装命令关闭了元数据继承；`--language`(音轨语言标签)未指定、字幕接口不再返回同一条字幕时，不读回来就会被清成 `und`。`--simply-mux` 与 `--skip-mux` 下不做标签透传(前者本就不写这些标签，后者不触发刷新)。

接口只返回试看片段(付费内容 + 会员过期)时，元数据仍来自未被门控的番剧信息接口，因此可以继续刷新，但必须先确认已存在的文件是一集完整正片：文件存在且非空、其时长接近接口给出的整集时长(容差 `max(3s, 1%)`)。任一条件不满足就停止并说明原因，绝不把试看片段洗成"已下载完成"，也不写下载归档。文件名模板若含 `<dfn>`/`<res>`/`<fps>`/`<videoCodecs>`/`<videoBandwidth>`/`<audioCodecs>`/`<audioBandwidth>` 这些来自轨道的占位符，试看响应拼不出原来的文件名，同样会被拒绝。

用 `--save-archives-to-file` 且该分P已记录在归档中时，程序会在分P调度阶段直接跳过整个分P，`--metadata-only` 不会执行；需要重刷元数据时请去掉该参数或清理对应归档行。

要一次刷新一个目录下所有已下载的番剧，用 `BBDownT ud <目录> --metadata-only`：它会按各目录 `bbdown.json` 记载的分P自动处理，不需要逐个目录重跑（见"追更已下载的番剧"）。

</details>

<details>
<summary>追更已下载的番剧 (update-downloaded / ud)</summary>

---

下载时写出的 `bbdown.json` 现在还会记录本次用法：显式给了 `-p` 时记为 `selectPage`，开了 `-dd` 时把生效的弹幕格式记为 `danmakuFormats`。`update-downloaded`（短名 `ud`）就是靠这两个字段判断"这个目录当初是怎么下的、要不要顺带刷弹幕"，然后按记录的来源链接重跑一次下载。已下完的集会被既有的"输出文件已存在则跳过"逻辑跳过，因此一次追更只会补下新集。两个标记只增不清：`selectPage` 记成 `ALL` 之后，后来单独补下一集（`-p 3`）不会把它降级回窄范围，`danmakuFormats` 也不会因为某次没带 `-dd` 而消失。

`bbdown.json` 的完整结构（脚本也可以直接读它定位来源）：

| 键 | 说明 |
| --- | --- |
| `sourceId` | 原样记录的下载输入（链接或 `av`/`BV`/`ep`/`ss`） |
| `url` | 由 `sourceId` 归一出的来源链接，追更重跑用的就是它；两者都没有时该目录会被追更跳过 |
| `title` | 视频/番剧标题 |
| `updatedAt` | 最近一次写入时间（含时区偏移） |
| `selectPage` | 当初显式传入的 `-p`，如 `ALL`、`1,3-5`；没传过 `-p` 则不写 |
| `danmakuFormats` | 当初下载的弹幕格式，如 `xml,ass`；没下过弹幕则不写 |
| `pages[]` | 每个分P一条：`page` 分P序号、`file` 成品文件名、`xml`/`ass` 弹幕文件名（磁盘上存在才写） |

`ud` 的位置参数是**目录**，不接视频地址；链接一律从各目录的 `bbdown.json` 读取，省略位置参数时依次回落到 `--work-dir` 与当前目录。

```
# 追更仓库根下的所有番剧目录
BBDownT ud "D:\Anime"

# 只追更某一部: 直接把该番剧目录传进去（其下有 bbdown.json）
BBDownT ud "D:\Anime\葬送的芙莉莲"

# 只要新弹幕，不碰音视频
BBDownT ud "D:\Anime" --danmaku-only

# 只刷新已下载集的元数据（标题/简介/章节/字幕）
BBDownT ud "D:\Anime" --metadata-only

# 干跑: 只打印每个目录将执行什么，不落盘
BBDownT ud "D:\Anime" --only-show-info
```

入选与刷新规则：

- 目录入选需要 `bbdown.json` 记载的分P数量大于 1（旧版文件的证据）**或** `selectPage` 为 `ALL`。显式带上 `-p ALL` 时该筛选作废，所有含 `bbdown.json` 的目录都会被处理。
- 弹幕刷新需要 `danmakuFormats` 存在（当初用了 `-dd`）**或** 每个分P都记载了 `xml`/`ass`（旧版文件的证据）。显式带上 `-dd` 时所有入选目录都会刷弹幕。当初带 `-dd` 下载的目录，追更时下的新集会自带弹幕。
- 入选目录若标记为全集下载（或旧文件的分P数大于1），追更会自动按 `-p ALL` 执行以取到新集；显式传入的 `-p` 优先。
- `--danmaku-only` 与 `--metadata-only` 是"只刷新已下载条目"的独占模式：只处理 `bbdown.json` 里已记载的那些分P，不会去取新集。`--sub-only`、`--cover-only`、`--video-only`、`--audio-only`、`--download-all`、`--interactive` 与 `ud` 同时使用会直接报错。
- 独占模式下记载的集号若在当前接口里已经不存在（撤集、`ss` 重新映射到别的季、目录改名），只会告警跳过该集，不影响该目录的其余分P。

执行位置由传入的目录决定，恒为"含 `bbdown.json` 的目录的父目录"：默认多P模板把成品写在 `视频标题\[P01]分P标题.mp4` 下，只有在这个父目录里执行才会命中既有成品并跳过。因此把番剧目录本身传给 `ud` 与把它的父目录传给 `ud` 是等价的，后者只是会顺带处理兄弟目录。

追更时如果上游改了标题、或自定义的 `-F`/`-M` 与当初下载时不一致，程序会拼出另一个文件名而认不出既有成品，从而重下一遍；保持与当初一致的模板就不会出现这种情况。

逐目录串行执行，目录之间等待 `--delay-per-video`（默认 10 秒）；单个目录失败只记录并继续，结束时汇总"成功 n，失败 f"，有失败则以非零码退出。旧版 `bbdown.json` 在首次追更后会被自动补写上新字段。

</details>

<details>
<summary>下载个人空间全部投稿</summary>

---

直接输入UP主的空间地址，可以导出该UP主的全部投稿视频地址：
```
BBDownT "https://space.bilibili.com/123456"
```
视频地址会保存到工作目录下的`UP主名称的投稿视频.txt`文件中。

如果你需要自动下载这些视频，可以加上`--download-all`：
```
BBDownT --download-all "https://space.bilibili.com/123456"
```
程序会先保存完整的视频地址文件，保存成功后再按顺序逐个下载。

默认每个视频下载完成后等待10秒再下载下一个，你可以使用如下命令调整间隔：
```
BBDownT --download-all --delay-per-video 15 "https://space.bilibili.com/123456"
```
将`--delay-per-video`设为`0`可以取消等待。如果一个视频有多个分P，分P之间的间隔仍然使用`--delay-per-page`设置。

其他下载参数也可以一起使用，例如只下载音频，并保存到指定目录：
```
BBDownT --download-all --audio-only --work-dir "./downloads" "https://space.bilibili.com/123456"
```

如果某个视频下载失败，程序会等待指定间隔后继续下载下一个，最后统一提示失败数量。有失败项时，命令会返回失败状态。

*PS: 这里只会读取本次生成的txt文件，暂不支持直接传入已有的txt文件。与`--only-show-info`同时使用时只导出视频地址。*

</details>

<details>
<summary>API服务器</summary>

启动服务器（自定义监听地址和端口）：

```shell
BBDownT serve -l http://0.0.0.0:12450
```

不带 `-l` 时默认监听 `http://127.0.0.1:23333`；监听非本机地址时会启用 API Token（`--api-token` 指定或写入配置文件，未配置时启动自动生成）。任务队列、下载根目录、允许任务传入的自定义项等服务器配置见 [json-api-doc.md](./json-api-doc.md) 的"服务器配置"一节。

API服务器不支持HTTPS配置，如果有需要请自行使用nginx等反向代理进行配置

API详细请参考[json-api-doc.md](./json-api-doc.md)

</details>

# 演示
![1](https://user-images.githubusercontent.com/20772925/88686407-a2001480-d129-11ea-8aac-97a0c71af115.gif)

下载完毕后在当前目录查看MP4文件：

![2](https://user-images.githubusercontent.com/20772925/88478901-5e1cdc00-cf7e-11ea-97c1-154b9226564e.png)

# 致谢

* https://github.com/nilaoda/BBDown
* https://github.com/bggRGjQaUbCoE/PiliPlus
* https://github.com/Shane32/QRCoder
* https://github.com/icsharpcode/SharpZipLib
* https://github.com/protocolbuffers/protobuf
* https://github.com/grpc/grpc
* https://github.com/dotnet/command-line-api
* https://github.com/FFmpeg/FFmpeg
* https://github.com/gpac/gpac
* https://github.com/aria2/aria2
