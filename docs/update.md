# 检查更新与安装

启动自动检查、应用内下载安装包、发布说明展示。**2026-10-14 定稿并随 0.2.0 落地。**

相关：[`release.md`](release.md)（三产物的打包与发布）、[`settings.md`](settings.md)（关于分区所在的那一页）、[`tray.md`](tray.md)（同为「关到托盘还算活着」的那一族，本方案要跟它对齐关闭语义）。

---

## 1. 改动前的现状与缺口

> 本节记录 2026-10-14 之前的状态；落地后的实现清单见第 6 节。

现有实现只有一条路：拉 `releases/latest` → 解析 `tag_name` → 比版本号 → 给一个 `html_url`，
用户点「前往下载」由系统浏览器打开发布页。`IAppUpdateService` 的注释明确写着**只由用户点按触发，不做自动检查**。

它是对的，但缺三件事：

| 缺口 | 后果 |
| --- | --- |
| 不自动检查 | 用户不点「检查更新」就永远不知道有新版本 |
| 不显示发布说明 | 用户点开浏览器才知道这一版改了什么，且 `changes.md` 的正文要在网页上再读一遍 |
| 不能应用内下载 | 安装版用户要自己在三个产物里挑，容易下错变体 |

另外有一处**实现上的隐患**：`GitHubReleaseUpdateService` 复用 API 那条 `HttpMessageHandler`，
而 `HttpClient.Timeout` 设的是 **8 秒**。这是「整个请求」的超时，含读 body —— 直接拿它下
50 MB 的安装包会在 8 秒时被切断。下载必须换一条通道。

**版本比较本身没有问题。** 实测远端 latest 是 `v0.1.0`，与 `Directory.Build.props` 的 `<Version>0.1.0</Version>`
相同（程序集版本 `0.1.0.0`），`latest <= current` 判真，显示「已是最新版本（0.1.0）」—— 正确。
发 `v0.2.0` 后会按预期显示「发现新版本 0.2.0」。**要改的是它能做什么，不是它算得对不对。**

## 2. 两个角色分开：安装版走应用内下载，便携版只引导

这是本次方案的主线。

判据是**当前进程所在目录里有没有 `Uninstall.exe`**：

| 进程类型 | 判据 | 下载按钮的行为 |
| --- | --- | --- |
| **安装版** | 目录里有 `Uninstall.exe`（NSIS 写进去的） | 应用内下载 `BowenMusic_x.y.z_x64_setup.exe`，下完给「立即安装」 |
| **便携版** | 没有 `Uninstall.exe` | **不做应用内下载**，开浏览器到 Releases 页面，让用户自己挑 zip |

**为什么便携版不代下。** 便携版有三个变体（自包含 / 框架依赖 / 以及安装版），用户的用法是
「解压到某个目录、替换掉旧的」，这件事应用做不了 —— 直接改写自己被运行的那个目录，
失败方式包括文件占用、权限、留下半新半旧的一堆文件。而 `setup.exe` 是**自动安装**的：
它能自己关掉运行中的实例、自己落到固定位置、自己写卸载项。**能自动作的只有安装版，所以只给安装版做。**

这也避开了「凭空多一个检测自包含 / 框架依赖的判据」——那个判据（看有没有 `coreclr.dll`）
本身是可靠的，但既然便携版一律引导到网页，就不需要它了。

判定写成**纯函数**（输入一个 `Func<string, bool>` 的存在性判据），不直接摸文件系统，好离线单测：

```
AppInstallKind.Detect(fileExists) :
    fileExists("Uninstall.exe")  → Installed
    else                         → Portable
```

## 3. 交互

### 3.1 一个对话框承载全部状态

「关于」页那颗「检查更新」与启动自动检查**打开的是同一个 `UpdateDialog`**，
由 `UpdateViewModel` 的状态驱动，按钮文案与可用性跟着状态走：

| 状态 | 主按钮 | 次按钮 | 关闭按钮 |
| --- | --- | --- | --- |
| 检查中 | — | — | 取消 |
| 已是最新 | — | — | 关闭 |
| 检查失败 | 重试 | — | 关闭 |
| 有新版本 · 安装版 | **下载更新** | 跳过此版本 | 稍后 |
| 有新版本 · 便携版 | **前往发布页面** | 跳过此版本 | 稍后 |
| 下载中 | 取消下载 | — | 禁用 |
| 下载完成 | **立即安装** | 打开所在文件夹 | 关闭 |

不做第二套界面：关于页内联显示一小行结果（「有新版本 v0.2.0」）就够，详情都在对话框里。
状态机放 `UpdateViewModel`，对话框只负责渲染 —— 与设置页「ViewModel 持真实状态、页面只绑」是同一套写法。

### 3.2 自动检查的时机与频次

- **每次启动检查一次**，一次运行内只自动弹一次。不节流、不记「上次检查时间」——
  整件事只有一个 GET，没便宜可占。
- **时机在外壳立起来之后**，不在 `OnLaunched` 里立刻发。加载过程中弹出一个模态框是最糟的形态。
  挂 `MainWindow.OnHostLoaded`（外壳已建、首个页面已导航），再等几秒让首屏动画跑完。
- **失败不打扰**：自动检查断网、接口变了都只记日志，不弹错误。检查失败是用户没做错任何事的场景，
  不值得给一个模态框。手动检查则一律给反馈。
- **自动检查可以关**（默认开）：关于页多一行开关，存 `config.json` 的 `update` section。

### 3.3 跳过此版本

`config.json` 的 `update` section 里存 `skippedVersion`（如 `"0.2.0"`）。自动检查拿到新版本后先比一次：

```
latest <= current            → 静默（本来就没人会弹）
latest <= skippedVersion     → 静默（用户点过「跳过此版本」）
否则                          → 弹对话框
```

**手动检查不看跳过**：用户主动点的，就该给结果，哪怕他之前跳过过。

### 3.4 关闭语义：跟「关到托盘」对齐

- **点到托盘的 ✕**：只是 `AppWindow.Hide()`，窗口没关，`Closed` 不触发 —— **下载继续**。
  用户还在用这个程序，把下到一半的任务掐掉是错的。
- **托盘菜单「退出」（真退出）**：`window.Closed` 里取消下载、删掉半截文件。
  这条与 `TearDownForShutdown` 挂在同一处，见 `App.xaml.cs` 的 `window.Closed` 处理器。

你说的「窗口被关闭也要取消」，按这个语义是**真退出才取消**。关到托盘不算关闭 ——
如果你要的是「隐藏也取消」，说一声，改成挂 `AppWindow.Hiding` 即可，但我不建议。

## 4. 下载

| 项 | 取值 |
| --- | --- |
| 落盘位置 | `%LOCALAPPDATA%\Bowen\updates\<文件名>` |
| 中间文件 | 先写 `<文件名>.part`，完成后 `File.Move` 改名；取消即删 `.part` |
| 超时 | **无限**，靠 `CancellationToken` 取消（`HttpClient.Timeout` 是整请求超时，不能设 8 秒） |
| 进度 | 读 `Content-Length`；拿不到就退化成不确定进度条（转圈） |
| 校验 | 下完对 SHA-256，**校验和从发布正文里那张表读**，不一致就删掉并报错 |

**为什么放 `updates\` 而不是系统的「下载」文件夹**：不往用户的下载目录里丢东西 ——
那里是他自己的地盘，应用静默写入会变成「咦这个文件哪来的」。对话框里给「打开所在文件夹」，
路径是可见的，想挪走随时能挪。

**安装包不会自动删除**：安装或解压完成后 `updates\` 里的文件仍在，留给用户回退。
设置 → 存储 → 「更新安装包」可以手动清理；只删文件、保留目录，
正在下载的 `.part` 被占用时跳过并在结果里说明。

**校验是白送的**：`changes.md` 的 `<!--CHECKSUMS-->` 被发布工作流换成了三个资产的
SHA-256 表格，而 `releases/latest` 的 `body` 字段就是这段正文。解析那张表（正则取
`| \`文件名\` | \`哈希\` |`）即可，不需要任何额外基建。这一条挡的是下载中途被中间设备改过、
或者 GitHub 那边资产被替换的场景 —— 不常见，但验证成本几乎为零。

## 5. 发布说明怎么显示

`releases/latest` 的 `body` 就是 `changes.md` 里那一版段落。切法是：

```
从 "## <版本号>" 之后开始，到 "### 校验" 之前结束   → 要显示的部分
"### 校验" 及其后的校验和表格                      → 丢掉（对用户是噪音，应用自己用得上）
```

渲染成一组 `TextBlock`，**不引第三方 markdown 库**：

| Markdown | 渲染成 |
| --- | --- |
| `### 新增` | 一行 SemiBold 的小标题 |
| `- **x** —— y` | 一行项目符号，行内 `**x**` 拆成粗体的 `Run` |
| 其他非空行 | 正文段 |
| 表格行、空行 | 跳过 |

`changes.md` 的正文只用到这几种语法（`###` 小标题 + `-` 项目符号 + `**粗体**`），
一个约 80 行的解析器就够。**引 `MarkdownTextBlock` 这类库要拖进一整套 markdown 解析与
XAML 渲染，而本项目对体积是计较的**（见 `size-optimization.md`）—— 为三种语法付这个代价不划算。

抽成纯函数 `ReleaseNotesFormatter.Parse(body, version)` 返回结构化的行，可离线单测。

## 6. 改动清单

**2026-10-14 已全部落地。** 下面按实际的文件写。

### Core

| 文件 | 改动 |
| --- | --- |
| `Models/AppUpdate.cs` | `DownloadUri` 改名 `ReleasePageUri`（那个地址给人看的是发布页，不是下载直链）；加 `ReleaseNotes` / `Assets` |
| `Models/AppUpdateAsset.cs` | **新增**：`Name` / `SizeBytes` / `DownloadUri` / `Sha256` |
| `Models/AppInstallKind.cs` | **新增**：枚举 + `AppInstallKindDetector.Detect` + `AppUpdateAssetPicker.Select` |
| `Models/AppUpdateDownload.cs` | **新增**：`AppUpdateDownloadStatus` / `AppUpdateProgress` / `AppUpdateDownloadResult` |
| `Models/ReleaseNotes.cs` | **新增**：行模型 + `Parse` |
| `Models/UpdatePreferences.cs` | **新增**：`AutoCheck`（默认 `true`）/ `SkippedVersion` / `IsSkipped` |
| `Models/AppUpdateOptions.cs` | 加 `ReleasePageUri`（由 `Owner`/`Repository` 推出来） |
| `Services/AppPaths.cs` | 加 `UpdatePreferencesFile`（旧 `update.json`，迁移用）与 `UpdateDownloadDirectory`（`updates\`） |
| `Services/Abstractions/IAppUpdateService.cs` | 加 `DownloadAsync(asset, progress, ct)`；去掉「不做自动检查」那句（现在做） |
| `Services/Abstractions/IUpdatePreferencesStore.cs` | **新增** |
| `Services/Implementations/GitHubReleaseUpdateService.cs` | 解析 `assets[]`（含 `digest`）与 `body`；`DownloadAsync` 走**独立的无限超时 `HttpClient`** |
| `Services/Implementations/JsonUpdatePreferencesStore.cs` | **新增**，照着 `JsonInputMethodSettingsStore` 写 |
| `Services/UpdatePreferencesJsonContext.cs` | **新增**，源生成的 JSON 上下文 |

### WinUI

| 文件 | 改动 |
| --- | --- |
| `ViewModels/UpdateViewModel.cs` | **新增**，单例：状态机、自动检查、下载与取消、跳过、安装 |
| `Controls/UpdateDialog.cs` | **新增**，把面板包成对话框、按状态改写三颗按钮的静态宿主 |
| `Controls/UpdatePanel.xaml(.cs)` | **新增**，对话框的内容体 |
| `Controls/ReleaseNotesView.xaml(.cs)` | **新增**，第 5 节那个渲染器 |
| `ViewModels/AboutViewModel.cs` | 砍掉自己那份检查更新状态，只剩图标 / 版本号 / 声明 / 两个外链 |
| `ViewModels/SettingsViewModel.cs` | 转发一个 `Update` |
| `Views/SettingsPage.xaml(.cs)` | 「检查更新」改为弹对话框；加「自动检查更新」开关与版本行的新版本提示 |
| `MainWindow.xaml.cs` | 注入 `UpdateViewModel`；`OnHostLoaded` 里触发启动检查；订阅 `UpdateFound` / `ExitRequested` |
| `App.xaml.cs` | 注册新服务与单例；`window.Closed` 里取消下载 |

### 测试

`AppUpdateTests` 扩展（assets 解析、`digest` 与正文表格的先后、安装形态判定、挑包、跳过判定），
新增 `AppUpdateDownloadTests`（落盘、进度、校验、取消、失败、文件名收敛）、
`ReleaseNotesTests`（正文切分与行解析）、`UpdatePreferencesStoreTests`。
全部离线：HTTP 仍是桩，判定与解析都是纯函数。**全量 1638 项通过**（本轮新增 44 项）。

## 7. 明确的取舍

- **不做静默自动更新。** 本项目是 unpackaged，没有 MSIX 身份，装不了 `PackageManager` 那套
  （`GitHubReleaseUpdateService` 的注释里已写明）。而且「应用自己把自己换掉」这件事在没有
  MSIX 的前提下只能靠外部安装器，风险与收益不成比例。**下载完由用户点「立即安装」。**
- **不做断点续传。** 安装包 50 MB，重下一次的成本可以接受；续传要处理 `Range`、
  断点文件校验、服务端不支持时的回退，是另一个量级的东西。
- **不引 markdown 库**，理由见第 5 节。
- **托盘菜单不加「检查更新」入口。** 用户没要，且「关于」页已经有一个清晰的位置。
- **不改 `AppUpdateOptions` 的字段语义**，只加一个 `ReleasePageUri`（由 `Owner`/`Repository` 推出来）。
  它是更新源的唯一配置点这条约束不变。

## 8. 核查与尚未验证的部分

### 已经对过账的（2026-10-14，拿真实的 v0.1.0 Release 验的）

- **`releases/latest` 的 `body` 就是 `changes.md` 里那一段。** 首行是
  `## 0.1.0 — 2026-10-08`，末尾是 `### 校验` 加三行校验和 —— 与源文件逐项一致。
  行尾是混的（标题与空行 `\r\n`、表格行 `\n`），切分因此按行 `Trim()` 而不是按 `\r\n` 拆。
- **`assets[]` 里有 `digest` 字段**（`sha256:…`），与正文表格里的值**逐字相同**。
  实现以它为主、正文表格为兜底，两条路都有单测。
- **三个产物的文件名**是 `BowenMusic_<版本>_x64_setup.exe` / `_x64_portable.zip` /
  `_x64_noruntime_portable.zip`，所以挑安装包按后缀 `_x64_setup.exe` 匹配，
  不拿版本号去拼串（tag 的 `v` 前缀、第四段的有无都不影响）。
- **版本比较那条路本来就通**：远端 `v0.1.0` 对本地 `0.1.0.0` 判 `UpToDate`，
  发下一版会按预期变成 `UpdateAvailable`。

### 还没验的

- **下载 50 MB 的实际观感**（进度条是否跟得上、耗时多久）。回调已经按整百分点去过重，
  一次 50 MB 的包最多刷 100 次界面，理论上不会有问题，但没在真机上跑过。
- **安装器在「应用刚 `Process.Start` 之后立刻退出」这个时序下的行为。** NSIS 脚本本身
  会先检测并关闭运行中的实例，理论上不会卡住，但要在干净机器上走一遍才算数。
- **整个 UI 没有跑过**（按项目规矩，前端验证交给人工）。需要人工过的：
  关于页三个按钮与开关的排布、对话框在六种状态下的按钮文案、发布说明的粗体与项目符号、
  深浅两套主题下的观感、以及「启动时登录框正开着」那条降级路径（应落到通知条而不是静默）。
