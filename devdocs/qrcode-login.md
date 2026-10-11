# 登录二维码的终端呈现

实现日期：2026-10-11。解决 `login` 的二维码太大、小屏幕显示不全，并把落盘行为从工作目录移走。

## 问题

- 旧的 `ConsoleQRCode` 每个模块写两个字符宽、一行高（`██`），ECC 为 Q 的登录码正好是版本 10（57 个码模块 + QRCoder 自带的 8 个留白模块 = 65），于是输出 **130 列 × 65 行**。
- 旧实现在每行结尾把 `BackgroundColor` 设成深色，背景会一路涂到行尾，观感比实际更大。
- 旧实现把 `qrcode.png` 写进**当前工作目录**（通常就是用户的下载目录），且只在登录成功时 `File.Delete`，取消、过期、异常都会留下垃圾文件。

## 功能与入口

- 仅 CLI：`login` / `logintv` 两个子命令各自带 `-img, --qr-image` 与 `-qs, --qr-style <compact|block>`。
- 这两个参数**不进 `AllOptions`**（配置文件白名单），**也不进 `MyOption`**（`ServeRequestOptions : MyOption` 会被 HTTP `/add-task` 原样接收），配置文件里写它们会被判为无法识别的配置项。
- 默认 `compact`：半角块 `▀▄█` 配空格，一行文本压两行模块，宽高各减半；登录码实测 **65 列 × 33 行**。
- `block` 退回双整块 `██`（130 列 × 65 行），只用于终端字体渲染不出 U+2580/2584 的情况。
- `-img` 只打开图片，不打印字符码；图片写进系统临时目录后交给系统默认查看器。
- 两种方式都不再在工作目录或任何固定位置留下 `qrcode.png`。

## 渲染

| 上 / 下模块 | 字符 | 说明 |
| --- | --- | --- |
| 浅 / 浅 | `' '` | 只露出背景色 |
| 深 / 深 | `'█'` | 前景色填满 |
| 深 / 浅 | `'▀'` | 前景色涂上半 |
| 浅 / 深 | `'▄'` | 前景色涂下半 |

- 前景（深）= `ConsoleColor.Black`、背景（浅）= `ConsoleColor.White`，整幅图只设一次，无需逐字符切换；每行写完立刻 `ResetColor`，修掉行尾涂色。
- 模块行数恒为奇数（17+4·version），末行与一条虚拟的**浅色**行配对，下半格天然成为留白，不补深色。
- quiet zone 传 0：QRCoder 的 `QRCodeData.ModuleMatrix` 自带 4 模块留白（已验证：9 字符 URL 的 v1 码矩阵是 29 = 21+8），再加只会白白多出好几行。
- 65 个模块在终端上压到 33 行已是下限；24 行以下的终端必须用 `-img`。

## 图片与查看器

- 路径 `Path.Combine(Path.GetTempPath(), $"BBDownT-qrcode-{Guid.NewGuid():N}.png")`：文件名只放 Guid，因为 `qrcode_key` 就编在二维码内容里，而 `Logger.RedactSensitiveText` 不脱敏文件名。
- `GetGraphic(8, drawQuietZones: true)` 之前先把矩阵补成更大的正方形：`PadQrMatrixForViewer` 追加 4 行浅色行，并把每行右侧补到同样宽度（多出的列左右均分以保持二维码居中）。于是留白变成顶部 4、左右 6、底部 8 模块——底部仍是四边里最宽的，但比初版的 12 收敛了三成。
- QRCoder 的 `PngByteQRCode` **只认正方形矩阵**（宽高都取自行数），行数大于列数会抛 `IndexOutOfRangeException`；所以“只加高”做不到，必须同时加宽。这一点由 `PaddedMatrix_EncodesAsSquarePng` 钉住。
- 最终图片 69 × 69 模块 × 8 px = 552 × 552 px，约 1 KB。
- `DesktopImageOpener.TryOpen`：Windows `UseShellExecute = true` 交给文件关联；macOS `open`、Linux `xdg-open` 且 `UseShellExecute = false`，走 `ArgumentList` 避免手工转义。fire-and-forget，绝不 `WaitForExit`（`xdg-open` 会挂住，而轮询必须继续）。
- 调用失败（SSH、容器、没有关联程序）只打一行警告，已打印的图片路径仍可手动打开，登录流程继续。
- 流程收尾（成功、过期、异常）尽力 `File.Delete` 临时图片；查看器占用时删除失败不报错。
- 输出被重定向时跳过字符码并提示改用 `-img`（沿用 `ProgressBar` 里 `Console.IsOutputRedirected` 的既有约定）。

## 命令接线

- `CommandLineInvoker.GetLoginCommand(name, description, action)` 工厂：beta4 不允许同一个 `Option` 实例挂到两个命令上（见 `devdocs/update-downloaded.md`），所以 login 与 logintv 各 new 一份参数。
- `Program.cs` 的子命令分发必须为 `login`/`logintv` **原样转传 args**：旧写法只把命令名回填给解析器，参数解析成功后会被静默丢掉，`-img` 会变成空操作。转传时不调 `BBDownTConfigParser.HandleConfig`，保持这两个子命令一直不读 `BBDownT.config` 的行为。
- `Program.InvokeCommandLineAsync` 新增 `login`/`loginTv` 两个可选委托参数，与既有的 `update`/`setUpdateSource` 注入方式一致，供入口级测试替换真实登录（否则会真的发 HTTP）。

## 代码所有权

| 模块 | 职责 |
| --- | --- |
| `BBDownT/QrConsoleRender.cs` | 选项校验、矩阵→字符行（纯函数）、写 Console 与颜色管理。 |
| `BBDownT/DesktopImageOpener.cs` | 跨平台调用系统默认图片查看器，失败返回 false。 |
| `BBDownT/BBDownTLoginUtil.cs` | 二维码生成、终端/图片两条呈现路径、临时图片收尾、轮询与落盘。 |
| `BBDownT/CommandLineInvoker.cs` | `GetLoginCommand` 工厂与两个参数声明。 |
| `BBDownT/Program.cs` | 命令注册、子命令参数转传。 |

移除：`BBDownT/ConsoleQRCode.cs`（每模块双整块、逐字符切色、行尾涂色的旧实现）。

## 验证

- `QrConsoleRenderTests`：四象限字符映射、奇数行虚拟浅色行、留白四边补齐、双整块样式、空矩阵、`ValidateStyle`/`UsesHalfBlocks`，以及用真实长度的登录 URL 断言紧凑输出正好是双整块的一半宽、一半高且不超过 80 列。
- `QrImagePaddingTests`：补边后的正方形尺寸与居中偏移、原有深浅模块一个不丢、补边后仍能编码成正方形 PNG。
- `CommandLineInvokerTests`：`-img`/`--qr-style` 绑定、缺省即紧凑、非法取值在处理器执行前被拒。
- `CommandLineEntryTests`：`login -img --qr-style block` 能穿透子命令分发到达处理器（守住转传漏洞）、非法取值退出码 1 且不发起登录。
- `ConfigParserTests`：配置文件里写 `--qr-image`/`--qr-style` 必须被拒。

```bash
dotnet build BBDownT.sln && dotnet test BBDownT.sln
```

2026-10-11 实测：Debug 全量 815 项通过、0 失败、3 跳过。真实接口跑通两条路径：默认路径在空目录里不产生任何文件（重定向时按预期提示改用 `-img`），`-img` 路径在 `%TEMP%` 生成约 1 KB 的 552 × 552 PNG、静区完整且底部最宽、查看器被成功调起。

验证限制：未用手机真机扫描终端字符码（尺寸、四象限映射与真实矩阵的成像结构经测试与转储核对，但终端字体是否把 `▀▄` 渲染成等宽只能人工确认）；`logintv` 的改动与 `login` 共用同一套代码路径，未单独跑真实 TV 登录。
