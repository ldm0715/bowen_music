# 波点 WinUI 3 客户端 · 技术栈选型

**核查日期：2026-09-30。** 本文所有版本号、包名、issue 状态均已用 NuGet API / GitHub API 逐一核实。标注「未查证」的项是调研中确实没找到证据的，**不要当事实引用**。

目标环境：Windows 10 Pro 19045（22H2）、WinUI 3、unpackaged、音频走 libmpv。

---

## 1. 运行时

| 项 | 选型 | 理由 |
| --- | --- | --- |
| .NET | **10（LTS）** | .NET 8 与 9 **同时在 2026-11-10 EOL**（只剩一个多月），两者都要升，不如直接上 10（EOL 2028-11-14） |
| .NET SDK | **10.0.401** / 运行时 **10.0.12**（2026-09-08） | 当前最新 |
| Windows App SDK | **2.5.1**（2026-09-16） | 当前 Stable。1.5/1.6/1.7 已 **Out of Support**；1.8 已进入 Maintenance 且 2026-09-24 停止服务 |
| TFM | `net10.0-windows10.0.26100.0` | 保守兼容老机器可退到 `19041.0`，SMTC 与 WinRT 投影都够用 |
| C# | **14** | .NET 10 默认 |

> **WinAppSDK 2.x 用的是 SemVer**：2.1.3 / 2.2.0 / 2.5.1 都是 `2.0` 大版本内的 minor，**不是「5 个大版本」**。查官方文档只看 `windows-app-sdk-2-0` 那一页。

---

## 2. 包清单

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
  <UseWinUI>true</UseWinUI>
  <LangVersion>14.0</LangVersion>
  <Nullable>enable</Nullable>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <WindowsPackageType>None</WindowsPackageType>
  <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
  <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
  <EnableMsixTooling>true</EnableMsixTooling>
</PropertyGroup>
```

> **`RuntimeIdentifier` 是单数，且不可省略。** 开 `WindowsAppSDKSelfContained=true` 后不带 RID 构建会直接失败：
> `error : WindowsAppSDKSelfContained requires a supported Windows architecture.`
> 自包含要把对应架构的原生 DLL 拷进输出目录，没有 RID 就不知道该拷哪一份。
> 若要多架构出包，用**复数的 `<RuntimeIdentifiers>` 声明集合，但每次构建仍须指定单个 RID**（`dotnet build -r win-arm64`）。只写复数不指定 `-r` 一样会报上面那个错。**2026-09-30 实测。**

> **`WindowsAppSDKSelfContained` 不等于 .NET 的 `SelfContained`。** 当前默认构建仍使用系统的 x64 .NET 10 运行时，WinAppSDK 原生 DLL 才是随应用输出。不要将旧 .NET 自包含产物混入新输出；运行时配置和重新构建的说明见 [`dev-environment.md`](dev-environment.md) §4.2。

> **上面的片段不含 `UseWinUI`** —— 它在本节写作时被漏掉，已补在片段里。XAML 编译靠它，不能省。
> **完整的、实际构建验证过的项目文件在 [`transport.md`](transport.md) 第 1 节**（含三个 csproj 全文、`global.json`、中央包管理的位置约束）。本节只讲为什么这么选。

| 用途 | 包 | 版本 |
| --- | --- | --- |
| UI 框架 | `Microsoft.WindowsAppSDK` | **2.5.1** |
| MVVM | `CommunityToolkit.Mvvm` | **8.4.2** |
| 控件补充 | `CommunityToolkit.WinUI.Controls.SettingsControls` / `.Segmented` / `.Sizers` | **8.2.251219** |
| 行为 / 动画 / 媒体 | `CommunityToolkit.WinUI.Behaviors` / `.Animations` / `.Media` | **8.2.251219** |
| 托盘（2026-10-06 引入） | `H.NotifyIcon.WinUI` | **2.4.1**（MIT） |
| 窗口辅助 | `WinUIEx` | **2.9.3**（**已登记、未引用**，见下面「三个坑」第 2 条） |
| DI / Hosting / Logging | `Microsoft.Extensions.Hosting` / `.DependencyInjection` / `.Logging` | **10.0.12** |
| 凭据 | `System.Security.Cryptography.ProtectedData` | **10.0.12** |
| 日志 | `Serilog` / `Serilog.Extensions.Hosting` / `Serilog.Sinks.File` | **4.4.0** / **10.0.0** / **7.0.0** |
| 测试 | `xunit.v3` / `xunit.runner.visualstudio` | **4.0.1** / **4.0.0** |
| 歌词解析 | `Lyricify.Lyrics.Helper` | **0.2.0** |
| 歌词渲染 | `Microsoft.Graphics.Win2D` | **1.4.0** |
| 输出音频频谱（2026-10-02 引入） | `NAudio.Wasapi` | **2.2.1**（MIT） |
| 音频（路线 A，推荐） | `HanumanInstitute.LibMpv` | **0.10.1** |
| 音频（路线 B，备选） | `FlyleafLib` / `FlyleafLib.Controls.WinUI` | **3.11.11** / **1.4.11** |

### 三个坑

1. **`CommunityToolkit.WinUI.Controls`（裸名）这个包不存在。** 8.x 已拆成 SettingsControls / Segmented / Sizers / Primitives / ImageCropper 等多个子包，必须按需引。**7.1.2 那条线**（`UI.Controls.DataGrid` / `Markdown` / `Notifications` / `Connectivity`）是 **UWP/WinUI2 时代遗留**，不要在新项目用。旧仓库 `CommunityToolkit/WindowsCommunityToolkit` 已 **ARCHIVED**，新仓库是 `CommunityToolkit/Windows`。
2. **WinUIEx 目前只登记了版本、没有任何项目引用它。** 它的 `TitleBar` 控件已废弃（改用 WinAppSDK 1.7+ 自带的 TitleBar）；而原先指望它提供的**托盘与单实例，2026-10-06 都改由别的办法做了**：托盘用 `H.NotifyIcon.WinUI`（`WinUIEx` 没有托盘控件，`WindowManager` 也管不到单实例），单实例用命名互斥量 + 注册消息广播（见第 6 节的「单实例」，以及 [`tray.md`](tray.md)）。留着这条登记是为了 P8 的 DPI 感知尺寸等窗口辅助还有的挑，不是「准备用它做托盘」。
3. **DPAPI 在 .NET 10 下仍需独立的 NuGet 包**（`System.Security.Cryptography.ProtectedData`），不在基础框架内。

---

## 3. 音频引擎

没有哪个维护良好的库能同时满足「libmpv + WinUI 3 控件 + 纯音频 + SMTC 事件」，必须二选一：

| | 路线 A：`HanumanInstitute.LibMpv` **0.10.1** | 路线 B：`FlyleafLib` **3.11.11** |
| --- | --- | --- |
| 后端 | **libmpv** | FFmpeg + DirectX（**不是 libmpv**） |
| WinUI 3 控件 | ❌ 只有 Avalonia 包，需自己封装 service | ✅ 有 `FlyleafLib.Controls.WinUI` 1.4.11 |
| 纯音频 | ✅ headless 即可，音频不需要渲染窗口 | ✅ 明确支持 AudioOnly |
| 进度 / 时长事件 | ✅ libmpv property 事件完整 | ✅ Player 事件齐全 |
| License | **MIT** | LGPL-3.0-or-later |
| 维护 | 活跃（`mysteryx93/LibMpv-OpenGL`，2026-07 仍有推送） | 极活跃（`SuRGeoNix/Flyleaf`） |

**选路线 A**：保持 libmpv 原意，MIT 最干净，纯音频本就不需要渲染控件。自己包一层 `IPlaybackService`，从 libmpv property 事件拿进度/时长/状态喂给 SMTC 和 UI。**若后续封装成本失控再退到 B——因为隔了一层接口，换后端不动上层。**

### libmpv-2.dll 的来源与许可

- **当前来源**：项目自行编译的音频精简构建；mpv 固定为 commit `e470f8986e`，属于 0.41.0 系列。完整记录见 [`libmpv-audio-build.md`](libmpv-audio-build.md)。
- **回退基线**：`shinchiro/mpv-winbuild-cmake` 的固定 tag `20260928`，原社区 DLL 保留作验证样本编码与回退参考库。
- **许可**：mpv 本体是 LGPLv2.1+，但 shinchiro 的默认构建启用了 GPL-only 组件，**整包按 GPLv2+ 分发**。本项目是 GPL-3.0，直接用即可（GPLv2+ 的「或更高版本」允许按 GPLv3 使用）
- **红线**：**不要分发官方 PC 客户端里的 `E:\bodian\libmpv-2.dll`**——这条与许可无关
- **分发方式**：unpackaged 下把 dll 放进输出目录，`<None Include="..." CopyToOutputDirectory="PreserveNewest" />`。x64 与 arm64 需分别出包

### P2 实测 ✅（2026-09-30 · 路线 A 已验证可用）

`HanumanInstitute.LibMpv` **0.10.1 + .NET 10.0.12 + shinchiro 20260928 构建**实测通过，本节的选型成立。

| 验证项 | 结果 |
| --- | --- |
| 加载 `libmpv-2.dll` | ✅ `mpv.ClientApiVersion()` = `0x20005` |
| 播放本地文件 | ✅ `pcm_s16le`，`AO: [wasapi]` 建立，`time-pos` 正常递增，`EndFile(EndOfFile)` 准时 |
| **播放远程 CDN 直链** | ✅ 无损 FLAC 直链直接播，**不需要任何额外 http header**（默认 UA 即可） |
| 元数据 | ✅ 从流里读出 `Artist/Album/Title` |
| `duration` / `time-pos` | ✅ 269.75s，与 API 返回的 269 秒吻合（单位是**秒**） |
| 文件事件 | ✅ `StartFile` / `FileLoaded` / `EndFile` 都触发 |
| **`end=` per-file 选项** | ✅ 有效，播到指定秒数触发 `EndOfFile`（试听区间就靠它） |
| 原生库加载路径 | `MpvApi.RootPath` 默认即 `AppContext.BaseDirectory`，**拷到输出目录即可，无需设置** |

三条必须记进代码的坑：

1. **`MpvContext` 没有带参构造函数**（它只是 `MpvContextBase` 的空 partial 类），事件循环**固定**为
   `MpvEventLoop.Default` → `MpvSimpleEventLoop`。后者自己起 Task 跑 `mpv_wait_event`，够用，
   **不用也无法换成 `Thread` 版**。
2. **`MpvContext.LoadFile(path, ..., extraArgs:)` 有 bug，不能用。** 它的实现从 `index = 2` 开始写
   extra 参数，**覆盖了 flags 位**（应为 3），mpv 会报
   `Invalid flag for option loadfile: end=15` 并**放弃加载、静默回到 idle**——症状是
   `idle-active` 恒为 1、`time-pos` 读不到，很容易误判成「libmpv 加载不了」。
   绕法是直接发命令，用 mpv 原生的四段形式：
   ```csharp
   mpv.RunCommand(null, "loadfile", url, "replace", "0", "end=15");
   //                               ↑flags    ↑index ↑per-file options（逗号分隔）
   ```
   只传三个参数（不带 per-file options）时 `LoadFile` 是正常的。
3. `vo=null` / `vid=no` / `audio-display=no` / `keep-open=no` / `idle=yes` 这组 headless 选项在
   **构造之后**用 `SetPropertyString` 设置有效（构造时已经 `mpv_initialize` 过，所以走 property 而非 option）。

**关于 DLL 体积**：当前使用音频精简构建，**7.97 MiB，比原版减少 93.1%**，Client API 仍为
`0x20005`。构建、校验和、裁剪范围、播放验收、替换与回退及 Lua/ytdl 兼容调整统一见
[`libmpv 音频精简构建与维护`](libmpv-audio-build.md)。

---

## 4. SMTC

### 不能「原生注册」

- `SystemMediaTransportControls.GetForCurrentView()` 在桌面应用**必然抛 `Invalid window handle`**——所有 `XxxForCurrentView` 系列都依赖 `ApplicationView`
- 微软在 [WindowsAppSDK#127](https://github.com/microsoft/WindowsAppSDK/issues/127) 明确表态，并以 **`not_planned`** 关闭（2025-02-04）：*"no plans for improvements for unpackaged apps without app identity."* 2025-09 仍有用户追问 `GetForWindowId`，无回应
- 官方替代接口 `ISystemMediaTransportControlsInterop` **在 C# 投影里是 protected，拿不到**（C++ 可用）

### 借壳方案

创建一个**不参与实际播放**的 `Windows.Media.Playback.MediaPlayer`，先 `CommandManager.IsEnabled = false`，再从它的 `SystemMediaTransportControls` 属性拿到 SMTC 实例：

```csharp
private readonly Windows.Media.Playback.MediaPlayer _smtcHost = new();
private readonly SystemMediaTransportControls _smtc;

_smtcHost.CommandManager.IsEnabled = false;      // 必须先禁用自动集成
_smtc = _smtcHost.SystemMediaTransportControls;  // 唯一的可行入口
_smtc.IsEnabled = true;

var updater = _smtc.DisplayUpdater;
updater.Type = MediaPlaybackType.Music;          // 必须设，否则抛异常
updater.MusicProperties.Title  = "...";
updater.MusicProperties.Artist = "...";
updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(coverUrl));  // webp 要先转 JPEG
updater.Update();

_smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
_smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties {
    StartTime = TimeSpan.Zero, MinSeekTime = TimeSpan.Zero,
    Position = pos, MaxSeekTime = dur, EndTime = dur
});
```

- **`ButtonPressed` 事件不在 UI 线程**，回调必须 `DispatcherQueue.TryEnqueue` 回主线程
- 前提是 TFM 带 WinRT 投影（`net10.0-windows10.0.*`），**不需要**额外装 `CsWinRT` 包

### unpackaged 下的应用名

**SMTC 面板默认显示 exe 文件名，不是应用名。** 两件事都要做：

1. 在 `%APPDATA%\Microsoft\Windows\Start Menu\Programs\` 放一个指向 exe 的**快捷方式**——SMTC 取的是**快捷方式的名字与图标**，exe 自带的无效。可在安装器或首次启动时创建
2. `SetCurrentProcessExplicitAppUserModelID`（shell32 P/Invoke）——影响任务栏分组与 toast 归属

**已知瑕疵**（社区实测）：来源名称偶尔更新有延迟；`AppMediaId` 相同的两个应用，只有都关闭后 SMTC 会话才释放；点击 SMTC 卡片无法跳回应用窗口（**没有可用事件**）。

### 落地实测（2026-09-30，P3）

以上方案已实现并跑通，`src/Bodian.WinUI/Playback/SmtcManager.cs`。四条**实测修正**：

1. **借壳在 Win10 19045 上成立** —— 先用一次性 spike（`BODIAN_SMTC_SPIKE=1` 门控的假数据）验证过：媒体浮层能显示会话。这条是全阶段的地基，值得先花半天证伪。
2. **封面不需要转码。** 酷我 CDN **按需生成尺寸与格式**：同一路径把 `.webp` 换成 `.jpg` 就是 200 `image/jpeg`（对 4 张专辑、两种路径前缀实测）。改写逻辑在 `Bodian.Core/Media/CoverArtUrl.cs`（当时只处理 `.kuwo.cn` 的 `/star/albumcover/<尺寸>/` 形状）。
   2026-10-06 扩展为同时处理 `/star/starheads/<尺寸>/`，供界面请求歌手缩略图；未知路径仍保持原样。
   界面图片加载失败时的腾讯旧封面备用地址与缓存重试规则，见 [`settings.md`](settings.md) §4.6。
   **不要走 WIC 转码**：Win10 **不预装** WebP 编解码器（要装商店的 WebP Image Extensions，Win11 才预装），转码方案会在开发机上一直成功、换台干净 Win10 就静默失败。
3. **`MinSeekTime` / `MaxSeekTime` 不设就没有拖动。** 只给 `StartTime`/`EndTime`/`Position` 的话 SMTC **不会发** `PlaybackPositionChangeRequested`，面板上的进度条拖不动。
4. **快捷方式必须用 `IShellLinkW` + `IPropertyStore`**：`WScript.Shell` 写不了属性存储，也就设不了 `PKEY_AppUserModel_ID`，名字与图标依然不对。几个坑：
   - **先 `Save` 落盘 → 写属性 + `Commit` → 再 `Save` 一次**。属性存储是在 `Save` 时序列化进 `.lnk` 的，只 Commit 不 Save 的话文件里根本没有那个属性。
   - **读 `.lnk` 的属性不要用 ShellLink 对象上 QI 出来的那个 `IPropertyStore`**：`Load` 之后直接 `GetValue` 取到的是未载入的空值。用 `SHGetPropertyStoreFromParsingName(路径, ...)`。
   - **`PROPVARIANT` 别声明成显式布局的 struct**：`out PropVariant` 编组回来的值是坏的（`vt` 读出来不是 31），表现为「每次都判定缺 AUMID 而重写快捷方式」。用裸内存（`AllocCoTaskMem` + `Marshal.WriteInt16/WriteIntPtr`，x64 下联合体在偏移 8）最稳。
   - 名字与 AUMID 用 `AppIdentity`（`Bodian` / `Bodian.WinUI`）。名字取自**快捷方式文件名**，所以快捷方式叫 `Bodian.lnk`；`Get-StartApps` 里应当看到 `Bodian` + AppID `Bodian.WinUI`。
   - **图标本轮没做**：`roadmap.md` 的分发红线写明「含图标」不得打包官方客户端资产，需要时自绘。
   - 幂等、失败只记 Warning、可用 `BODIAN_SKIP_SHELL_REGISTRATION` 关掉、撤销 = 删那个 `.lnk`。

### 只读会话（做「正在播放」类功能时）

`GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` 在桌面应用**完全可用，不需要 identity**。现成封装：`Dubya.WindowsMediaController`。

---

## 5. 工程结构

```
Bodian.sln
├── src/Bodian.Core/            net10.0，零 UI 依赖 —— 可独立单测
│   ├── Api/                    transport（签名 / 请求头 / 信封解析）、DTO
│   ├── Lyrics/                 BodianLyricParser、AwlrcParser
│   ├── Models/Lyrics/          统一歌词模型
│   └── Services/{Abstractions,Implementations}
├── src/Bodian.WinUI/           net10.0-windows，WinUI 3
│   ├── Views/ ViewModels/ Controls/ Converters/ Styles/ Strings/ Messages/
│   ├── Playback/               AudioEngine / MusicPlayer / PlayQueueManager / SMTCManager / SharedPlaybackState
│   └── LyricRenderer/
└── tests/Bodian.Core.Tests/    xunit.v3
```

**`Bodian.Core` 不带 `-windows` TFM 是关键**：协议、签名、歌词解码全部可单测，不依赖 UI 线程。

参考实现的许可边界（详见 `lyrics-ui.md`）：`Anthonyy232/Nagi`（**GPL-3.0，代码可移植**）提供多项目分层与 DI 写法；`LanZhan-Harmony/...TheUntamedMusicPlayer`（**AGPL-3.0，只能借结构**）提供 `Playback/` 五件套拆分与 `Messages/` 消息解耦。

三条值得照搬的模式：

1. **「一个实现类注册多个窄接口」**。一个 `LibraryService` 同时实现 `ILibraryReader` / `ILibraryWriter` / `ILibraryScanner`，注册时逐个 `AddSingleton<ILibraryReader>(sp => sp.GetRequiredService<LibraryService>())`
2. **`Playback/` 拆成五件套**：`AudioEngine`（裸引擎 + P/Invoke 隔离）/ `MusicPlayer`（门面）/ `PlayQueueManager`（队列）/ `SMTCManager` / `SharedPlaybackState`（共享状态）
3. **`Messages/` 消息类做跨 VM 通信**，避免 ViewModel 互相引用。歌词时间偏移调整正好用得上

### 导航

**自研 `INavigationService` 包一层内置 `Frame`**，不引第三方框架。WinUI 3 生态里没有维护良好的专用导航库。

### 配置存储

`%LOCALAPPDATA%\<App>\settings.json` + `System.Text.Json` 源生成器。**不要用 `Windows.Storage.ApplicationData.Current`**——unpackaged 下直接抛异常。

---

## 6. 打包（unpackaged）

```xml
<WindowsPackageType>None</WindowsPackageType>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

若要 `PublishSingleFile`（仅 unpackaged + self-contained 支持），官方要求**必须同时给全**：

```xml
<WindowsPackageType>None</WindowsPackageType>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<SelfContained>true</SelfContained>
<EnableMsixTooling>true</EnableMsixTooling>
<IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
<PublishSingleFile>true</PublishSingleFile>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

缺 `EnableMsixTooling` / `WindowsPackageType=None` / `IncludeAllContentForSelfExtract` 会**报错**；缺另外两个会**告警**。

**unpackaged 的能力缺失**（官方明确列出）：无 App Installer 自动更新、无清单式后台任务注册、无清单式文件关联 / 协议注册、不能上架商店。

### 单实例

**命名互斥量 + `RegisterWindowMessage` 广播**，不引库、也不用 `AppInstance`。

理由是「关闭到托盘」把这件事的必要性提高了：主窗口的 ✕ 只是隐藏，进程会一直活着，所以第二次启动必须变成「唤醒已有实例」，否则会有两个托盘图标、两份播放引擎抢同一个 SMTC 会话。

做法：`Local\<AUMID>.SingleInstance` 判所有权 → 已有实例则 `AllowSetForegroundWindow(ASFW_ANY)` 让出前台权限，再 `PostMessage(HWND_BROADCAST, 注册消息)` → 主实例在主窗口的子类回调里收下，`ShowWindow + SetForegroundWindow` 带回前台。广播**能投递到隐藏的顶层窗口**，所以窗口藏在托盘里照样收得到。

`AppInstance` 是备选，不是首选：它的 `RedirectActivationToAsync` 是异步的且官方明确警告不能阻塞 STA，`Activated` 回调可能不在 UI 线程，而且它**不负责把窗口带回前台** —— 那一段一样要自己写。上面这三条正好全绕掉。

落地细节与实测记录见 [`tray.md`](tray.md)。

---

## 7. 其余选型

| 项 | 选型 |
| --- | --- |
| 日志 | 代码里统一注入 `Microsoft.Extensions.Logging.ILogger<T>`，**不直接依赖 Serilog 类型**；启动时把 Serilog 挂成 provider。单测可用 `NullLogger` |
| 序列化 | `System.Text.Json` + 源生成器（`JsonSerializerContext`），并开 `<JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>` |
| 网络 | **单例 `HttpClient` + `SocketsHttpHandler`**：`PooledConnectionLifetime = 2min`（解决 DNS 陈旧）、`AutomaticDecompression = All` |
| 测试 | `xunit.v3` 4.0.1（独立测试宿主，支持 `Microsoft.Testing.Platform`），保留 `xunit.runner.visualstudio` 供 IDE 集成 |
| 凭据 | **DPAPI**：`ProtectedData.Protect(bytes, entropy, DataProtectionScope.CurrentUser)` → 加密后落盘 |

### 透明悬浮窗（P1.5 实测 ✅ 2026-10-04，本机 Win10 19045）

`roadmap.md` 的 P1.5。结论：**逐像素透明 + 置顶 + 可拖动 + 透明处点击穿透，四者可以同时成立。**
下面这套组合是实测跑通的，不要凭印象改任何一条。

| 目标 | 做法 | 备注 |
| --- | --- | --- |
| 无边框无标题 | `OverlappedPresenter.SetBorderAndTitleBar(false, false)` | |
| 逐像素透明 | ① `DwmExtendFrameIntoClientArea` 传全零 `MARGINS`（整窗「玻璃」）② `DwmEnableBlurBehindWindow`，`DWM_BB_ENABLE \| DWM_BB_BLURREGION`，区域取退化的一像素 `CreateRectRgn(-2,-2,-1,-1)` ③ 用 `SetWindowSubclass` 拦 `WM_ERASEBKGND`，`FillRect` 填黑并**返回 1** | ①② 都返回 `S_OK`。**③ 最容易漏，漏了窗口就是一片纯色** |
| 分层窗口 | 扩展样式加 `WS_EX_LAYERED` | DWM 混合的必要条件 |
| 不进 Alt+Tab | 扩展样式加 `WS_EX_TOOLWINDOW` | |
| 不抢焦点 | 扩展样式加 `WS_EX_NOACTIVATE` | |
| 去残留边框 | 样式清掉 `WS_CAPTION \| WS_THICKFRAME`，再 `SetWindowPos(SWP_FRAMECHANGED)` | 少了最后一步改动不生效 |
| 置顶 | **定时**（约 500 ms）`SetWindowPos(HWND_TOPMOST, …, SWP_NOACTIVATE)` | **不要用 `OverlappedPresenter.IsAlwaysOnTop`，它会破坏穿透** |
| 点击穿透 | 定时（约 60 ms）`GetCursorPos` ＋ 元素屏幕矩形判断光标是否在可交互处，动态加减 `WS_EX_TRANSPARENT` | **整窗开关**，不是逐像素命中；光标在文字上时那一小块不透 |

**实测记录（PID 与数值取自 2026-10-04 的日志）**：

- 窗口形态回读：`分层=true 置顶=true 工具窗=true 不抢焦点=true`，DPI 缩放 1.25。
- 命中自检用 `WindowFromPoint` ＋ `GetAncestor(GA_ROOT)` 比对：
  **不穿透时**大字与空白两处都命中自己；**加上 `WS_EX_TRANSPARENT` 后两处都不再命中自己**。
  也就是「点会不会落到我身上」可以自动化验证，不必靠肉眼。
- 用户实测确认：窗口可拖动、透明观感正确、透明处点击确实穿透。

**WinUI 3 侧的两个坑（都实测过，别再试）**：

1. **不做 SystemBackdrop 也能透明。** 上面那三步就够了。想要「透明的系统背板画刷」这条在
   WinAppSDK 2.x 上走不通，见 §8。
2. `DWMWA_WINDOW_CORNER_PREFERENCE`（去圆角、顺带去掉细边框）在 Win10 上返回
   `E_INVALIDARG (0x80070057)`，是 **Win11 22000+ 专有**。调用方必须容忍失败，
   Win10 上靠上面「去残留边框」那一步解决白边。

---

### 2026-10-05 正式桌面歌词实现

桌面歌词已获用户确认，完整说明见 [`desktop-lyrics.md`](desktop-lyrics.md)。正式实现使用约 30 ms 光标轮询，
置顶标记丢失时才重排窗口；字号和颜色面板留在本窗口内。锁定后只保留解锁入口。

歌词由独立 Win2D + Composition 后台线程绘制，缓存固定字形 alpha 遮罩，逐帧更新颜色纹理，
暂停保留最后一帧。长句适配宽度后保持位置与字号；双行排版随视口、字号与 DPI 重建。

右侧 16 DIP 原生命中条必须启用 `SS_NOTIFY` 并显式命中 `HTCLIENT`，否则 `STATIC` 的默认命中会透给下方窗口。
边缘条统一管理捕获与拉伸光标，离开、取消、锁定和隐藏时清理；拖动距离按物理像素计算，
固定窗口左上角及高度，配合屏幕约束与 16 DIP 吸附。

早期透明试验入口不再接入应用；本地试验代码和截图保留在忽略的验证目录或未提交文件中。

---

## 8. 过时 / 已废弃（不要用）

| 项 | 状态 |
| --- | --- |
| `Mpv.NET`（hudec117） | NuGet 1.1.1 **已标记 Deprecated**，1.2.0 **unlisted**，GitHub 仓库**已 404** |
| `MpvIpcController` | 已被 `HanumanInstitute.LibMpv` 取代 |
| `CommunityToolkit.WinUI` 7.1.2 线 | UWP / WinUI2 遗留 |
| `CommunityToolkit/WindowsCommunityToolkit` 仓库 | **已 ARCHIVED**（新仓库 `CommunityToolkit/Windows`） |
| Windows App SDK 1.4 / 1.5 / 1.6 / 1.7 | 全部 **Out of Support** |
| Windows App SDK 1.8 | **Maintenance**，最终补丁 2026-09-24，服务期已结束 |
| `Windows.Storage.ApplicationData.Current` | unpackaged 下**不可用** |
| `Windows.Security.Credentials.PasswordVault` | 需要 package identity，unpackaged 下**不可用** |
| 所有 `XxxForCurrentView` 系列 API | 桌面应用**必然抛异常** |
| `LWA_COLORKEY`（SetLayeredWindowAttributes 的色键） | 微软判为 **by design 不支持**（[#8469](https://github.com/microsoft/microsoft-ui-xaml/issues/8469)，CLOSED `NOT_PLANNED`） |
| `OverlappedPresenter.IsAlwaysOnTop`（做悬浮窗时） | 实测**会破坏点击穿透**。改用定时 `SetWindowPos(HWND_TOPMOST, …, SWP_NOACTIVATE)` |
| `SetWindowRgn` 划区域做穿透 | 能用，但下拉 / 弹层一出现就得手工改区域。本项目走 `WS_EX_TRANSPARENT` 动态开关 |
| WinAppSDK 2.x 里「透明的 `SystemBackdrop` 画刷」 | **走不通**，三条路都堵：① 接口 `ICompositionSupportsSystemBackdrop.SystemBackdrop` 的类型是 `Windows.UI.Composition.CompositionBrush` ② `new Windows.UI.Composition.Compositor()` 在 WinUI 进程里抛异常（用的是 lifted 合成器）③ 拿 WinUI 自己的 compositor 造画刷再 `.As<>()` 转换会 `InvalidCastException`。**不需要它** —— 透明靠 DWM 扩帧 ＋ blur-behind ＋ 拦 `WM_ERASEBKGND` |

---

## 9. 未查证（不要当事实引用）

- Windows App SDK 2.x 的**官方最低 .NET 版本**（文档未列；模板支持 net8.0/9.0/10.0 是唯一线索）
- **Prism for WinUI 3** 的当前版本与维护状态
- **Template Studio for WinUI** 与 `VijayAnand.WinUITemplates` 的版本号
- `SetWindowCompositionAttribute` 在 WinUI 3 窗口上的行为——**没有任何 WinUI 3 场景下的证据或反例**
- 独占全屏（exclusive fullscreen）对 `WS_EX_TOPMOST` 悬浮窗的遮挡——只有 HotLyric README 的间接佐证
- `LibMpv.Client` 1.0.0 的来源仓库与 License（NuGet 上无 projectUrl、无 license、无仓库）→ **视为不可信，不要引**
- `Microsoft.Graphics.Win2D` 1.4.0 与 WinAppSDK 2.5.1 的**官方**兼容性声明（仅有第三方项目实证 WinAppSDK 2.2+ 可用）→ **动手前先做冒烟测试**
