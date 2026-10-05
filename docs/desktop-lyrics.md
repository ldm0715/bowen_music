# 桌面歌词实现与验收

**状态：2026-10-05 已实现，用户确认本轮效果并要求提交。** P6 已接入正常桌面歌词入口；透明悬浮窗的基础验证于 2026-10-04 完成。

## 1. 入口与外观

播放条的「桌面歌词」按钮负责开关悬浮窗，其选中态采用动态主题资源，主题切换后同步更新；点击歌曲封面仍进入沉浸歌词页。桌面歌词与主播放器共享歌曲、播放状态和收藏状态，首次打开才创建窗口，关闭入口隐藏窗口并停止后台绘制，重新打开复用实例。主窗口退出时销毁歌词窗口及其原生拉伸边缘。

| 项目 | 当前行为 |
| --- | --- |
| 悬停背景 | 鼠标进入后显示整窗 60% 不透明的黑色背景；离开约 180 ms 后隐藏背景和工具栏 |
| 默认尺寸 | 宽 760 DIP，可在 420–1000 DIP 内拉伸；高度由字号与行数决定，并限制在屏幕可用区域内 |
| 文字 | 默认字号 42 DIP，范围 20–96；未唱文字固定不透明白色，已唱文字采用所选高亮色 |
| 高亮颜色 | 默认青绿 `#00E5BF`；不提供白色选项，旧版白色高亮设置会转换为青绿 |
| 双行 | 默认关闭；打开后显示当前句与下一句，支持两行居中或上行居左、下行居右 |
| 长句 | 建排版或调整窗口宽度时一次性适配宽度，播放进度不移动或缩放字形 |
| 顶部按钮 | 字号、高亮颜色、双行、对齐、穿透、锁定、关闭，均使用 Fluent System Icons |
| 底部按钮 | 上一首、播放／暂停、下一首、收藏／取消收藏，复用主播放器命令和状态 |
| 空态 | 有歌曲但无歌词时显示「暂无歌词」；没有歌曲时显示「波点音乐」 |

悬停操作区始终预留布局空间，显隐不会改变文字位置。字号和颜色面板位于窗口内部；字号滑块拖动时先更新设置，松开后调整窗口高度，避免面板随窗口变化移动或关闭。

## 2. 穿透、锁定与窗口操作

- **穿透开启**：歌词与空白区域可点击下面的窗口，只有操作按钮、设置面板和拉伸边缘保留交互。开启状态使用实心光标图标，关闭状态使用轮廓图标，提示文字明确显示当前状态。
- **穿透关闭**：可以从歌词与空白区域拖动窗口；按钮、滑块等控件不触发窗口拖动。
- **锁定**：禁止移动和拉伸，所有按钮及拉伸边缘隐藏，只保留解锁入口。
- **屏幕吸附**：拖动接近当前显示器可用区域的四边时，在 16 DIP 范围内吸附。拖动、拉伸、字号变化和恢复位置都限制完整窗口留在可用区域内。

### 原生拉伸边缘

右侧 16 DIP 区域由一个属于歌词窗口的原生命中条处理，避免 XAML 输入线程与原生窗口过程竞争光标。命中条使用 `STATIC` 类、`SS_NOTIFY`、`WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`，以 1/255 alpha 保留鼠标命中。

`SS_NOTIFY` 与显式的 `WM_NCHITTEST → HTCLIENT` 保证鼠标按下落到拉伸区域。只有有效按下才建立捕获；拉伸以按下时的窗口位置、宽度和物理像素为锚点，固定左上角及高度。移动事件和光标轮询共同处理拖动，松开、丢失捕获、锁定和隐藏都会结束手势。

`TrackMouseEvent` 监听离开边缘。横向光标仅由命中条设置；离开或取消操作时收回本控件留下的光标，不覆盖其他窗口已设置的文本或手形光标。屏幕边缘吸附后，鼠标往回拖动仍能缩小窗口。

## 3. 透明与置顶

当前窗口形态复用本机验证过的组合，详细记录见 [tech-stack.md](tech-stack.md) 的透明悬浮窗一节：

1. `OverlappedPresenter.SetBorderAndTitleBar(false, false)` 去掉标题栏与边框。
2. 清除 `WS_CAPTION | WS_THICKFRAME`，设置 `WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`。
3. `DwmExtendFrameIntoClientArea` 与一像素区域的 `DwmEnableBlurBehindWindow` 开启逐像素混合。
4. 拦截 `WM_ERASEBKGND` 自行填黑，避免系统背景擦除破坏透明。
5. `TransparentBackdrop` 只管理 DWM 和窗口子类化生命周期，不创建系统背板画刷。

首次显示需要激活一次以启动 WinUI 渲染，之后使用 `AppWindow.Show(false)`。Windows 11 去圆角属性在 Windows 10 上允许失败，Windows 10 的细白边由清除窗口边框样式处理。

约 30 ms 的 `DispatcherQueueTimer` 查询光标位置并控制悬停与 `WS_EX_TRANSPARENT`。约 500 ms 检查置顶标记，仅在标记丢失时调用 `SetWindowPos(HWND_TOPMOST, ..., SWP_NOACTIVATE)`；设置面板打开及拖动时不反复调整置顶。隐藏窗口后所有窗口定时器停止。

## 4. 固定字形与逐字扫色

`DesktopLyricsCanvasView` 创建独立 Win2D 设备和透明 `CompositionDrawingSurface`，`DesktopLyricsRenderLoop` 在后台线程复用歌词页的 `FramePacer(120)` 和 `LyricsPlaybackClock`。歌曲位置通过同一时钟模型从引擎低频上报插值，当前行直接由文档时间轴查询。

`DesktopLyricsLineRenderer` 将每行字形栅格化一次，缓存固定 alpha 遮罩；逐帧只更新不透明颜色纹理，再通过 `AlphaMaskEffect` 合成字形。扫色使用实际字形宽度和音节时间轴，两端颜色的 alpha 均为 255。长音不做上浮、缩放、抖动或发光变换。

文字原点对齐物理像素；合成视觉尺寸匹配绘图表面的物理像素尺寸，避免亚像素缩放。播放进度只改变颜色，暂停、空态和未变化的画面不反复清空重画。

窗口尺寸变化先合并约 90 ms，再重建绘图表面和两行缓存。暂停时渲染线程也会等待到期并补画完整帧，不只依赖 UI 定时器唤醒。字号、宽度、高度、DPI 或双行状态变化都会重建相关排版；`DesktopLyricsLayoutMetrics` 根据当前视口容纳完整双行，避免放大后再缩小时下一行被旧行高裁掉。

## 5. 设置、位置与歌词激活

| 文件 | 保存内容 | 实现 |
| --- | --- | --- |
| `%LOCALAPPDATA%\Bodian\desktop-lyrics.json` | 字号、高亮色、双行、对齐、锁定、穿透 | `JsonDesktopLyricsSettingsStore`，源生成 JSON，临时文件原子替换 |
| `%LOCALAPPDATA%\Bodian\desktop-lyrics-window.json` | X、Y、宽度及保存时高度 | `DesktopLyricsPlacementStore` 转发到独立路径的 `JsonWindowPlacementStore` |

位置为物理像素，宽度保存为 DIP；读取时忽略保存的高度，由当前字号和行数重新计算。无位置记录时显示在主屏底部居中。坏设置逐项规范化，文件无法读取时使用默认值并保留原文件。

`DesktopLyricsViewModel.IsEnabled` 驱动 `LyricsViewModel.IsDesktopLyricsOpen`，与沉浸歌词页的 `IsOpen` 并列：

```csharp
private bool IsActive => IsOpen || IsDesktopLyricsOpen;
```

任一入口打开就获取当前歌曲歌词并复用仓库缓存；换歌时刷新，两处都关闭时不主动取词。

## 6. 代码结构

| 位置 | 职责 |
| --- | --- |
| `Bodian.Core/Models/DesktopLyricsSettings.cs` | 外观偏好、默认值与规范化 |
| `Bodian.Core/Models/DesktopLyricsWindowGeometry.cs` | 完整窗口约束、四边吸附、物理像素拉伸 |
| `Bodian.Core/Models/DesktopLyricsLayoutMetrics.cs` | 当前视口下的行高、字号和双行范围 |
| `Bodian.Core/Services/Abstractions/IDesktopLyrics*Store.cs` | 设置与位置存储接口 |
| `Bodian.Core/Services/Implementations/*DesktopLyrics*Store.cs` | 外观与独立位置记录 |
| `Bodian.WinUI/ViewModels/DesktopLyricsViewModel.cs` | 开关、偏好与持久化 |
| `Bodian.WinUI/Services/DesktopLyricsWindowHost.cs` | 窗口懒创建、显隐与退出销毁 |
| `Bodian.WinUI/Views/DesktopLyricsWindow.xaml(.cs)` | 布局、命中、悬停、设置面板、窗口操作 |
| `Bodian.WinUI/Services/DesktopLyricsResizeCursor.cs` | 原生边缘、捕获与光标恢复 |
| `Bodian.WinUI/Controls/DesktopLyricsCanvasView.cs` | 合成表面、播放时钟和资源恢复 |
| `Bodian.WinUI/LyricRenderer/DesktopLyricsRenderLoop.cs` | 帧节奏、缓存更新与后台绘制 |
| `Bodian.WinUI/LyricRenderer/DesktopLyricsLineRenderer.cs` | 字形遮罩、颜色纹理与长句适配 |
| `Bodian.WinUI/Controls/TransparentBackdrop.cs` | DWM 透明和背景擦除挂载点 |

主窗口、播放条和 `App.xaml.cs` 完成开关入口、依赖注入与退出清理；图标几何统一存于 `Themes/Icons.xaml`，来源与展示方式见 [icons.md](icons.md)。

## 7. 验证记录

- **2026-10-04，Windows 10 19045，125% 缩放**：基础透明、置顶、拖动和点击穿透验证完成，记录见 [tech-stack.md](tech-stack.md)。
- **2026-10-05，用户验收**：用户确认当前桌面歌词实现，并要求更新文档、提交改动。
- **构建**：正常启动目录构建通过，0 错误；保留既有 `AiPlaylistPage.xaml:28` 的 `WMC1506` 绑定警告。
- **回归**：仅含暂存文件的独立副本完整离线测试 **1131 项通过，0 失败、0 跳过**。覆盖设置和位置存储、旧白色高亮兼容、视口双行布局、物理像素拉伸、边缘吸附、放大后缩小以及播放时钟连续性。验证命令如下。
- **字形像素**：使用正式字形渲染器在 100%、125%、150%、200% DPI 下离屏绘制。每组连续 60 帧扫色的逐像素 alpha 一致；暂停后整图像素一致，切换高亮色不改变字形轮廓。窄窗双行均有文字像素，四次窄／宽循环恢复相同字形。该检查不依赖桌面截图。

```powershell
dotnet build src/Bodian.WinUI/Bodian.WinUI.csproj --no-restore
dotnet test --project tests/Bodian.Core.Tests/Bodian.Core.Tests.csproj --no-restore
```

本地字形验证产物位于忽略目录 `artifacts/desktop-lyrics-visual-review/`，不随产品提交。早期透明窗口试验与双层 `TextBlock` 代码保留在本地，正式入口不再引用。

回归操作顺序：进入／离开窗口观察背景；拖动字号；开关穿透；从右边缘拉宽再缩窄并确认两行；松开并移开鼠标确认箭头恢复；锁定后只保留解锁；拖动到屏幕四边确认吸附；关闭并重新打开确认偏好和位置；主窗口退出确认进程结束。

## 8. 已知边界

- 目标为横排歌词，不提供竖排。
- 长句适配窗口时可能小于用户选择的字号，播放过程中保持该行排版不变。
- 点击穿透通过整窗扩展样式动态开关实现；操作区保留命中。
- 置顶不保证覆盖独占全屏的 D3D 应用。
- Windows App SDK 升级后需要重新验证透明、点击穿透、原生拉伸边缘和退出清理。
