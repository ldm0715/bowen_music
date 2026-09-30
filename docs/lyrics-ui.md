# 波点 WinUI 3 客户端 · 歌词界面方案

**核查日期：2026-09-30。** 本文的仓库 star 数、许可证、文件路径均已用 GitHub API 核实。

---

## 0. 前提：逐字歌词在数据层已经成立

`bodian-api-reference.md` 2.6 节确认：歌词接口返回的 `data.content` 是 Base64 明文，逐字轨用 `<a,b>` 标记，经 `[kuwo:N]` 八进制系数还原后可直接得到标准 AWLRC 毫秒时间轴。

**也就是说后端已经给了逐字时间轴，卡拉OK 高亮不是「能不能做」的问题，只是「怎么画好看」的问题。** 数据这么好，界面做丑了纯属浪费——这是本文存在的理由。

---

## 1. 三层架构

| 层 | 方案 | 许可 |
| --- | --- | --- |
| 统一模型 + 通用格式 | **`Lyricify.Lyrics.Helper` 0.2.0**（NuGet 直引） | Apache-2.0 |
| 波点 / 酷我私有解码 | **自己写**（无任何库支持） | — |
| 渲染 · 主歌词页 | **Win2D 几何裁剪** | 移植 **BetterLyrics / HyPlayer**（GPL-3.0） |
| 渲染 · 桌面歌词条 | **XAML 双 `TextBlock` + `RectangleGeometry` 裁剪** | 移植 KugouMusic.NET (MIT) 或 BetterLyrics |

### 解析层：Helper 不支持 AWLRC

`Lyricify.Lyrics.Helper` 覆盖 LRC / QRC / KRC / YRC / TTML / Spotify / Musixmatch 等，但**不支持 AWLRC**（行内 `<...>` 逐字标签）——逐行读它的 `Parsers/LrcParser.cs`，`'<'` 字符出现 **0 次**，正则只认 `[mm:ss.xx]`。而波点解出来的恰恰就是 AWLRC 形态。

所以：

- **必须自己写 `BodianLyricParser`**：Base64 解码 → `[kuwo:N]` 系数还原 → `<a,b>` 逐字 → 产出 Lyricify 的 `SyllableLineInfo`。**Helper 在这里的价值是统一模型 + 逐字/逐行降级 + 歌词优化的白嫖，不是解析**
- 备选 `ModernLrc`（**MIT**，v1.2.0）**明确支持** Enhanced LRC，但**只支持 `net10.0`**（我们正好是），零依赖、可 AOT
- 另一个许可最宽松的备选是 `ALRC.Abstraction` 1.3.0（**CC0**，可无条件商用），但同样不支持 AWLRC

### 传输层：老酷我那套解密，波点**不需要**

老酷我歌词端点（`f=web`）需要 `tp=content` 头剥离 → **zlib inflate** → Base64 → **`yeelion` 循环 XOR** 一整套。**波点端点（`f=bodian`）返回 JSON，`data.content` 直接是 Base64 的明文 UTF-8 文本，只需一次 Base64 解码。**

**代码里只实现 Base64 路径。** 搬错那套是白干一天的量。

---

## 2. 酷我逐字解码

`bodian-api-reference.md` 2.6 节的公式已被人**独立逆向并公开**，有三处互相印证的实现。其中 `tomakino/LyricProvider` 的 `KuwoLyricParser.kt`（**Apache-2.0**，作者注明 1:1 还原 App 内 `LyricsParserImpl`）**明确声称支持波点音乐**。另外两处是 `lyswhut/lx-music-desktop` 的 `kw/lyric.js` 与 `MeoProject/lx-music-api-server` 的 `kw.py`。

**照着写没有任何法律问题（Apache-2.0），且比纯逆向靠谱。** 但有四处分歧，其中两处直接影响正确性：

| # | 分歧 | 影响 |
| --- | --- | --- |
| 1 | **`[kuwo:N]` 必须按八进制解析**（`parseInt(N, 8)`）。文档写了「N 是八进制数」但没强调这一步 | **写错整轨时间全废** |
| 2 | **逐字时间是「相对该行行首的偏移」还是「绝对值」**，文档没说清。参考实现标注是**相对偏移**（已在 4 个真实文件、4 组不同除数上验证：每行首字 `begin` 恒为 0），并额外保留了 `wordTimesAreAbsolute()` 自适应判定兜底 | **必须实测**。roadmap P0 里「肉眼核对逐字时间」要核的就是这件事 |
| 3 | `duration` 的算法：参考实现**不取绝对值**，只做 `if (end < begin) end = begin` 的钳制；文档写的是 `trunc(abs(a-b)/(2*durationFactor))` | 个别字时长可能偏 |
| 4 | 清理规则：`[kuwo:N]` 要剔除；行歌词 = 去掉所有 `<a,b>` 与 `[kuwo:...]` 后的文本；粘连的 `][` 要拆成独立行；**长度 < 6 的行跳过** | 边界情况 |

> 文档写的「无标签时默认 `N=11`」，参考实现的行为是**直接回退普通 LRC 解析**（不做逐字）。两条必有一条是错的，建议实测。

---

## 3. 渲染路线

### 核心约束：WinUI 3 的 `TextBlock` 没有任何逐字定位 API

`TextBlock` / `RichTextBlock` 只有 `Text` 和 `Inlines`。而且设了 `Inlines` 会**关闭 TextBlock 的 fast-path 文本渲染器**，长歌词列表逐字拆分明显掉帧。

**所以「XAML 原生做逐字」本质做不到**，必须落到「整行裁剪」或 Win2D。

### 四条路线对比

| 路线 | 做法 | 能力边界 |
| --- | --- | --- |
| **A · 双 TextBlock 裁剪** | 同位置叠两层文本，上层设 `Clip = RectangleGeometry`，逐帧改 `Rect.Width = 文字宽 × progress` | **只能做行内横向扫光**。无法单独变换某一个字，做不了「当前字放大 / 上浮 / 发光」。折行后「逻辑行」与「视觉行」不再一一对应，裁剪矩形要分行算——**这是最大的坑** |
| **B · LinearGradientBrush 渐变遮罩** | `TextBlock.Foreground` 设渐变，用 Storyboard 动画化 `GradientStop.Offset` | **不推荐**。渐变坐标空间是 TextBlock 的整个布局边界而不是单个字符，天然只适合「一行一条渐变」。且微软文档**只保证 `GradientStop.Color` 可被动画化**，`Offset` 能否动画**没有官方保证**——走这条路先写 10 行 demo 验证 |
| **C · Win2D 几何裁剪** ⭐ | `CanvasTextLayout` 排版 → `GetCharacterRegions()` 拿每字矩形 → 按进度构造高亮矩形 → `CanvasGeometry.CreateText` + `CombineWith(Intersect)` → `FillGeometry` | 真正的逐字能力：单字可独立裁剪、缩放、上浮、发光、模糊。跑在 `CanvasAnimatedControl` 的**独立渲染线程**，不占 UI 线程 |
| **D · Win2D 栅格化 + Composition 遮罩** | Win2D 渲到 `CanvasRenderTarget` → `CompositionMaskBrush` 叠进度遮罩 | **不建议**。Composition 的 opacity mask 必须是 `CompositionSurfaceBrush`，做渐变扫光得先把渐变渲进一张 surface，绕一大圈。**等于路线 C 的全部工作量再加一层包装** |

### 选择

- **主歌词页走 C**：逐字高亮、长音拖尾发光、当前行居中放大，都要单字级别的控制
- **桌面歌词条走 A**：只要横向扫光，**零 Win2D 依赖**，体积小、启动快

### 路线 C 的真实工作量不在渲染，在文本样式重建

字体回退（中英混排）、字距、行高、emoji 彩色字体、`TextTrimming`——这些 `TextBlock` 白送的，Win2D 全要手动对齐。**中文歌词混排是主要成本。**

不过项目定了 GPL-3.0，BetterLyrics 的 `Renderer/` 和 HyPlayer 的 `LyricRenderer/` 可以整块移植，能省掉很大一部分。**排期按「移植 + 适配」估，不是从零写。**

---

## 4. 目标效果：BetterLyrics 的歌词样式（硬需求）

`jayfunc/BetterLyrics`（2177★，WinUI3 + Win2D）的歌词观感是本项目的**硬性目标**，不是「参考之一」。技术上与我们完全一致，代码可直接移植（GPL-3.0）。

**下面是从它源码里读出的实际参数，实现时照这个对齐。** 全部来自 `src/BetterLyrics.DotNet/` 下的 `Core/Models/Settings/LyricsEffectSettings.cs` 与 `Core/Helpers/Lyrics/LyricsAnimator.cs`。

### 4.1 逐字高亮的机制

- 每个字挂 **`CropEffect`（`BorderMode = Hard`）+ `GaussianBlurEffect`（`Source = Crop`，`BorderMode = Soft`）** —— 见 `Models/Lyrics/RenderLyricsChar.cs`。**「长音拖尾发光」就是这个裁剪后高斯模糊的产物**，不是单独的光晕层
- 一行持有**原文 / 翻译 / 音译三套 `CanvasTextLayout`** 及各自的 `CanvasGeometry`；未播放态的着色走 `TintEffect` + `CompositeEffect`；描边/填充用 `CanvasCommandList` 缓存
- **CJK 与西文是两个独立字体族**（`fontFamilyCJK` / `fontFamilyWestern`），字号也分原文/音译/翻译三档 —— 中英混排的字体回退在这里是显式配置，不是自动行为
- 渲染分横排/竖排两条实现（`HorizontalLyricsLineRenderer` / `VerticalLyricsLineRenderer`），差异通过 5 个抽象方法分叉而非 `if`：
  - `CalculateRegionPlayProgress(regionIndex)` —— 逐区域进度
  - `GetPlayedCharCropRect(sourceCharRect, progressPlayed)` —— **已播放裁剪矩形**，逐字高亮的核心
  - `CreateGradientBrush(...)` —— 扫光渐变
  - `ApplyFloatOffset(rect, floatOffset)` —— 浮动位移
  - `ApplyNonAutoWrapOffset(rect, offset)` —— 不折行时的偏移

### 4.2 默认开启的效果与参数

| 效果 | 默认 | 参数 |
| --- | --- | --- |
| **远景模糊** | ✅ 开 | 模糊量 = **`5 × distanceFactor`**（越远的行越糊，上限 5） |
| **当前行放大** | ✅ 开 | 普通行 `0.75` → 当前行 `1.0`，按 `distanceFactor` 插值 |
| **长音发光** | ✅ 开 | 仅作用于**时长 ≥ 700ms 的音节**；量 = **8**（可自动调节） |
| **长音放大** | ✅ 开 | 同样 ≥ 700ms；量 = **115%**（可自动调节） |
| **浮动动画** | ✅ 开 | 量 = **8**，时长 = **450ms** |
| 淡出 / 边缘羽化 / 出视野隐藏 | ✅ 开 | — |
| 阴影 | ❌ 关 | — |

> **关键设计**：发光与放大**只给长音**（`LyricsEffectScope.LongDurationSyllable`，阈值 700ms），不是每个字都做。这是它与「所有字一起发光」那种廉价效果的差别所在。

### 4.3 滚动：Apple Music 的指数错峰曲线

滚动默认参数：`Top / Scroll / Bottom` 三段时长各 **500ms**，缓动 `Quad` + `Out`，延迟默认 0。

**真正决定观感的是错峰延迟曲线**，源码注释直接写了 *"Reference: Apple Music-like exponential stagger delay curve"*：

```
budget       = min(0.4, scrollDuration × 0.75)
staggerDelay = budget × (1 − exp(−visibleIndex × scrollBottomDelay / budget))
scrollDelay  = baseDelay + staggerDelay
```

以**首个可见行为波源**（delay = 0），越远的行延迟越大——所以滚动是**波浪式**的，不是整块平移。顶部与底部各有独立的时长/延迟，用来调出「上进下出」的不对称手感。

### 4.4 可选的进阶效果（默认关）

- **扇形展开**：`FanLyricsAngle = 30`
- **3D 透视**：X/Y/Z 角度 + `Lyrics3DDepth = 800`，另有自动 3D 模式
- **呼吸效果**：强度 80

### 4.5 排期归类

| 效果 | 排期 |
| --- | --- |
| 逐字扫光高亮、**长音拖尾发光**、长音放大、远景模糊、当前行放大、波浪式滚动 | **必做**（P5）—— 这几条就是「BetterLyrics 的观感」本身 |
| 桌面歌词条（透明 + 置顶 + 点击穿透） | **必做**（P6） |
| 扇形 / 3D / 呼吸等进阶效果 | P10（架构上留出口子即可，见 4.1 的抽象方法设计） |
| 专辑封面取色引擎、音频可视化与粒子背景、歌词分享卡片、任务栏 / 壁纸层歌词 | P10 |

> **实现建议**：直接移植 `LyricsLineRendererBase` 的五个抽象方法 + `HorizontalLyricsLineRenderer`，把 `LyricsEffectSettings` 的参数照抄成我们自己的配置模型。**不要重新设计这套参数体系**——它的默认值就是调好的结果，改了就得重新调。

---

## 5. 可移植的代码清单

### 首选来源（GPL-3.0，可直接移植）

| 来源 | 拿什么 |
| --- | --- |
| **jayfunc/BetterLyrics** | `Renderer/LyricsRenderer/` 整套（`LyricsLineRendererBase` 的 `CalculateRegionPlayProgress` / `CreateGradientBrush` / `GetPlayedCharCropRect` 抽象 + 横竖排两个实现）；`Models/Lyrics/RenderLyricsChar.cs` 的**每字符挂 `CropEffect` + `GaussianBlurEffect`**（长音拖尾发光的关键）；`Renderer/CompositionRenderer.cs` 的 `CanvasRenderTarget` 缓存；`Shaders/` 粒子与模糊；`Helpers/Lyrics/LyricsAnimator.cs` 的动画总调度 |
| **HyPlayer/HyPlayer** | `LyricRenderer/` 的分层架构：`Abstraction/Render/` + `RollingCalculators/`（滚动算法独立）+ `Animator/EaseFunctions/`（缓动库）+ `LyricLineRenderers/`。`BreathPointRenderingLyricLine`（**间奏呼吸点**，Apple Music 风格的关键细节）值得整块搬。**注意它是 UWP（`UseUwp=true`），UI 层别抄**——但 Win2D 的 `CanvasTextLayout` / `CanvasGeometry` 在 UWP 与 WinUI3 之间是同一套 API，算法可 1:1 移植 |
| **Anthonyy232/Nagi** | 多项目分层与 DI 写法（见 `tech-stack.md`） |

### 宽松许可来源

| 来源 | 拿什么 | 许可 |
| --- | --- | --- |
| **dotMorten/WinUIEx** | `TransparentTintBackdrop`、`HwndExtensions`（扩展样式读写）、`Region.cs`（`SetWindowRgn` 区域裁剪，**已正确处理 DPI 与屏幕坐标换算**） | **MIT** |
| **cnbluefire/HotLyric** | `HotLyric.Win32/Controls/` 歌词控件目录；桌面歌词窗的透明 / 点击穿透实现。**已停更 18 个月，WASDK 版本较老** | **MIT** |
| **cnbluefire/BlueFire.Toolkit.WinUI3** | `TextView/Controls/FormattedTextRenderer.cs` 等——**WinUI 3 的 XAML 拿不到 glyph run**，这层自封的 DWrite 绕不开 | **MIT** |
| **Linsxyx/KugouMusic.NET** | `KaraokeTextBlock.cs` 的裁剪逻辑（`PushClip` → WinUI 3 的 `UIElement.Clip`，一一对应）。**桌面歌词条走这条路线** | **MIT** |
| **kengwang/ALRC** | `ALRC.Abstraction` 1.3.0 歌词中间表示（备选） | **CC0** |
| **christosk92/WaveeMusic** | `src/apps/Wavee.Tests/Fixtures/lyrics/` 真实歌词样本，**直接拿来当解析器测试夹具** | MIT |

---

## 6. 许可边界

**项目自身是 GPL-3.0。** 但**必须区分 GPL 与 AGPL**：

> **AGPL-3.0 与 GPL-3.0 是单向兼容的**——GPL 代码可以并入 AGPL 项目，**反过来不行**。所以**一个 GPL-3.0 项目不能包含任何 AGPL-3.0 代码**。

| 仓库 | 许可 | 能否移植代码 |
| --- | --- | --- |
| **jayfunc/BetterLyrics** | GPL-3.0 | ✅ **可以**——逐字渲染、粒子背景、shader |
| **HyPlayer/HyPlayer** | GPL-3.0 | ✅ **可以**——`LyricRenderer/` 拆分最细的一份 |
| **Anthonyy232/Nagi** | GPL-3.0 | ✅ 可以——DI 写法、服务分层 |
| **theimpactfulcompany/Rise-Media-Player** | GPL-3.0 | ✅ 可以，但已停更近一年，参考价值有限 |
| **cnbluefire/HotLyric** | MIT | ✅ 可以 |
| **dotMorten/WinUIEx** | MIT | ✅ 可以 |
| **Linsxyx/KugouMusic.NET** | MIT | ✅ 可以 |
| **kengwang/ALRC** | CC0 | ✅ 可以（最宽松） |
| **amll-dev/applemusic-like-lyrics** | **AGPL-3.0** | ❌ **不可以**。视觉天花板，只能做视觉参考。注意真仓库在 `amll-dev/` 组织下，`Steve-xmh/applemusic-like-lyrics` 只有 5★、是重定向壳 |
| **LanZhan-Harmony/...TheUntamedMusicPlayer** | **AGPL-3.0** | ❌ **不可以**，只能借鉴目录结构与拆分思路 |
| **bodian-music-api** / **folia-major** | AGPL-3.0 | ❌ **不可以**（roadmap 原有红线） |
| **SPlayer-Dev/SPlayer** | AGPL-3.0 | ❌ 不可以 |
| **WXRIW/Lyricify-App** | 无源码 | 仓库里**只有 README / 图片 / i18n，没有代码**——它是闭源产品的信息发布页。**代码去 `Lyricify-Lyrics-Helper` 要**（Apache-2.0） |

> **两个容易搞错的点**：
> 1. `LanZhan-Harmony/...TheUntamedMusicPlayer` 的桌面歌词窗**架构最贴近我们的需求**（`Views/DesktopLyricWindow.xaml(.cs)` 完整实现了穿透/置顶/拖拽/触摸拖拽），但它**是 AGPL——只能读思路，不能抄**。别因为「看起来最合适」就动手。
> 2. AMLL 是视觉天花板，但 AGPL 让它彻底出局。好在 **BetterLyrics + HyPlayer 已覆盖逐字渲染的全部需求**，且技术栈与我们一致，移植性比 AMLL（React/WebGL）好得多。

### GPL 的对外义务

仓库需附**完整 GPL-3.0 文本**、**保留原作者版权声明**、**标注改动过的文件**。移植时**逐个文件记来源**，别等发布前再补。

---

## 7. 桌面歌词窗的四个坑

完整的技术背景见 `roadmap.md` 的 P6 与 P1.5 一节。这里只列最容易踩的：

1. **`WS_EX_TRANSPARENT` 是整窗穿透，不是「按像素 alpha 穿透」。** WinUI 用 Composition/D3D 渲染，窗口拿不到视频内存里的 alpha，**做不到「透明处穿透、文字处可点」**。解法是**动态切换**：50–100ms 定时器轮询 `GetCursorPos`，判断光标是否落在可见元素上（`TransformToVisual(null)` 算屏幕矩形），是则关穿透、否则开。
2. **不要用 `OverlappedPresenter.IsAlwaysOnTop`——它会破坏点击穿透。** 必须改用每 ~100ms 的 `SetWindowPos(hwnd, HWND_TOPMOST, ..., SWP_NOACTIVATE)` 兜底。**置顶正是桌面歌词的核心，这条是致命的。**
3. **`LWA_COLORKEY` 无效**（微软判为 by design）。`LWA_ALPHA`（整窗统一半透明）**有效**。
4. **Win10 / Win11 行为不同**：透明窗在 SDR 下有一条细白边（解法是去掉 `WS_DLGFRAME`）；Win11 专有 DWM 属性在 **Win10 19045 上无效**。目标机是 Win10，两条都要实测。

**另外两条结构性结论**：

- **透明不能靠 Mica / Acrylic**——那是 DWM 材质，给不了「文字浮在桌面上」。要自写一个「画透明色」的 `SystemBackdrop`
- **同进程同时承载 WinUI 3 和 WPF 不受支持**（想让歌词窗用 WPF 是捷径，但走不通）。用同进程的**第二个 WinUI 3 `Window`**

---

## 8. 测试夹具

解析器的单元测试用真实歌词样本，直接取 `christosk92/WaveeMusic` 的 `src/apps/Wavee.Tests/Fixtures/lyrics/`（**MIT**，含真实 `.krc` / `.lrc` 样本）。

`BodianLyricParser` 的测试要覆盖：八进制 `[kuwo:N]` 解析、相对/绝对偏移的两种情形、无标签时的回退行为、`][` 粘连行的拆分、长度 < 6 行的跳过。
