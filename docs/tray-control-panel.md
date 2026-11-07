# 托盘效率面板

2026-11-07 设计稿。状态：第一版已实现，待手动验收（构建 0 错误；托盘窗口尺寸、Slider 拖动与主题重绘必须实测）。

相关：[`tray.md`](tray.md) §5（现有托盘菜单）、[`audio-effects.md`](audio-effects.md)、[`play-queue.md`](play-queue.md)、[`settings.md`](settings.md)。

---

## 1. 目标

托盘右键菜单增加一种「效率面板」形态：不打开主窗口，直接在右键面板里调音量、播放模式、音效、音质、播放队列和主题。

外观设置里增加「托盘菜单」一项，两档：

| 档位 | 行为 |
| --- | --- |
| 简洁（默认） | 保持现在的原生菜单：正在播放、转台、模式子菜单、窗口开关、设置/重启/退出 |
| 效率面板 | 右键弹出一块固定尺寸的控制面板，包含上面这些能力，并内联音效/音质/队列 |

默认必须是「简洁」：旧配置没有这个字段，读出来按简洁处理，行为与升级前一致。

## 2. 硬约束：托盘库只认 MenuFlyout

`H.NotifyIcon.WinUI 2.4.1` 的 `ContextMenuMode="SecondWindow"` 实现（`TaskbarIcon.ContextMenu.WinUI.SecondWindow.cs`）有三条不能绕过的限制：

1. `PrepareContextMenuWindow` 里直接 `((MenuFlyout)ContextFlyout).Items`。**`ContextFlyout` 换成普通 `Flyout` 会类型转换失败，右键没有菜单。**
2. 每次弹出/测量时，它把所有非分隔项强制写成 `Height = 32`、`Padding = 11,0`，再逐项 `Measure` 求窗口大小。窗口尺寸**只在弹出那一刻算一次**。
3. 它给每个非子菜单项挂一个 `Tapped` 处理器，点击即 `flyout.Hide()` 并藏窗口。现有 `TrayMenuItem` 模板在根 `Grid` 上把 `PointerPressed` / `Tapped` 标成 `Handled`，正是为了挡住这一条，实现「菜单内连续操作」。

由此推出本方案的四条设计底线：

- 面板只能是**一个 `TrayMenuItem` 的 `Body`**，整个面板作为这一项的内容，不能换成普通 Flyout。
- 高度用 `MinHeight` 顶住库强写的 `Height=32`。现有「正在播放」行用 `MinHeight=50` 已验证这条路可行；面板约 500，需要先做静态骨架验证。
- **面板所有状态必须同高。** 窗口尺寸在打开时定死，切页时内容变矮会露空白、变高会裁掉。所以内容区固定高度，内部各自滚动。
- **面板内不许再开 Flyout / MenuFlyout。** SecondWindow 是独立窗口，弹二级 Flyout 会让它失活，库随即把整个面板收掉。音效、音质、队列必须内联，不能复用现有 `AudioEffectsPanel` / `AudioQualityPicker` 的外层 Flyout。

## 3. 面板形态

新增 `Controls/TrayControlPanel.xaml`，宽固定 336 DIP，总高固定 500 DIP，全部内容作为 `TrayMenuItem.Body`。宿主仍是 `MainWindow.xaml` 里那颗 `TaskbarIcon`。

```
┌──────────────────────────────────────────┐
│ [封面36] 歌名                    [♥]     │
│          歌手                            │
│     ⏮   ▶/⏸   ⏭   [模式]                │
│  🔊 ──────●─────────────  65%            │
├──────────────────────────────────────────┤
│  主题   跟随系统 | 浅色 | 深色            │
│  音质   标准 | HQ | SQ                   │
├──────────────────────────────────────────┤
│  音效  |  播放列表                        │
├──────────────────────────────────────────┤
│            当前页内容（固定高）           │
├──────────────────────────────────────────┤
│  [🪟 🎤]                    [⚙ ↻ ⏻]     │
└──────────────────────────────────────────┘
```

- 音量行是横向滑条（本面板的已确认例外），左侧图标负责静音，右侧显示百分比。
- 播放模式是一颗单键循环按钮，与播放条那颗同一个 `CyclePlayModeCommand`；不把四种模式一次性铺开。
- 选中态统一用 `AccentFillColorSelectedTextBackgroundBrush` 品牌强调底，不用几乎看不见的
  `SubtleFillColorSecondaryBrush`。
- 音效页：顶部一张「总开关」行卡（图标 + 音效名 + ToggleSwitch），下面是并列的
  「经典音效 / 增强音效」RadioButton 卡片（Padding 12,8，圆角 4，选中强调色描边），
  再下面是带前缀图标的标准 ComboBox 选择预设。
- 音质独立成行（不占页签）：标准 / HQ / SQ 三档做成一等三段选，与主题行同一形态；
  不可用档位按 `AudioQualityOption.IsEnabled` 置灰，文件大小走悬停提示。
- 页签只剩「音效 / 播放列表」两页。
- 播放列表页：`Queue.Rows` 的紧凑列表，点行 `Queue.Play(Position)`，内部滚动。队列的 `Rows` 只在
  `IsOpen` / `IsMiniPlayerOpen` 打开时重建；面板因此新增 `IsTrayPanelOpen`，切到本页时置位并强制
  `RefreshNow()`，切走或面板收起时置回 `false`。
- 底部图标行拆成两组，中间用弹性空白隔开：左组是窗口开关（小窗、桌面歌词），右组是应用动作（设置、重启、退出）。两组各自连续排列，不平均摊满整行。左组直接执行 ToggleCommand；右组抛事件给 `MainWindow`，因为要走现有的 `_dismissTrayMenu` 与收尾链。

## 4. 数据来源

| 区块 | 绑定 |
| --- | --- |
| 封面 / 歌名 / 歌手 | `Player.CoverImage`、`Player.NowPlayingTitleText`、`Player.NowPlayingArtistText` |
| 收藏 | `Player.Statistics.IsFavorite`、`Player.ToggleFavoriteCommand` |
| 转台 | `Player.PreviousCommand` / `TogglePlayPauseCommand` / `NextCommand`，`CanGoPrevious/CanGoNext` |
| 音量 | `Player.Volume`（TwoWay）、`Player.IsMuted`；静音按钮调 `Player.ToggleMute()` |
| 模式 | `Player.SetPlayModeCommand` + `PlayMode` 枚举参数；选中态用 `IsSequentialMode` 等四个属性 |
| 主题 | `Theme.IsSystem/IsLight/IsDark`；点击调 `Theme.Select(AppTheme.X)` |
| 音效 | `Effects.IsEnabled`、`Effects.VisiblePresets`、`Effects.SelectPresetCommand` |
| 音质 | `Player.QualityOptions`、`Player.SwitchQualityAsync`、`Player.QualityStatus` |
| 队列 | `Queue.Rows`、`Queue.Play(int)`、`Queue.IsEmpty` |
| 底部动作 | 左组：`MiniPlayer.ToggleCommand`、`DesktopLyrics.ToggleCommand`；右组：设置/重启/退出抛事件 |

面板控件以依赖属性接收 `PlayerViewModel`、`AudioEffectsViewModel`、`PlayQueueViewModel`、`ThemeViewModel`，避免把上百行 XAML 塞进 `MainWindow.xaml`。

## 5. 设置项与持久化

新增 `Bowen.Core/Models/TrayMenuStyle.cs`：

```csharp
public enum TrayMenuStyle
{
    Compact,
    Efficient,
}
```

`ThemeSettings` 增加 `TrayMenuStyle TrayMenuStyle { get; set; } = TrayMenuStyle.Compact;`，`Normalized()` 把未定义值收回 `Compact`。走现有 `ThemeSettingsJsonContext`，枚举仍写成字符串（`"Compact"` / `"Efficient"`），旧文件缺字段时反序列化为默认值。

`ThemeViewModel` 增加：

- `TrayMenuStyle TrayMenuStyle { get; }`
- `int TrayMenuStyleIndex { get; set; }`，给 ComboBox 双向绑定，`set` 里立刻落盘并 `OnPropertyChanged`
- `bool IsEfficientTrayMenu => TrayMenuStyle == TrayMenuStyle.Efficient`

`SettingsPage.xaml` 的「界面外观」卡片里，紧跟「主题」加一行：

- Header：`托盘菜单`
- Description：`简洁：轻量原生菜单；效率面板：右键直接调音量、模式、音效、音质与播放列表`
- RightContent：ComboBox，两项「简洁」「效率面板」

## 6. 运行时切换

`MainWindow.xaml` 里现有整组简洁菜单项和新的效率面板项各包一个可见性：

- 简洁组：`Visibility="{x:Bind local:Formats.VisibleWhenFalse(Theme.IsEfficientTrayMenu), Mode=OneWay}"`
- 效率面板项：`Visibility="{x:Bind local:Formats.Visible(Theme.IsEfficientTrayMenu), Mode=OneWay}"`

`MenuFlyout` 里 `Collapsed` 项 `Measure` 返回 0，库的 `MeasureFlyout` 算出的窗口尺寸会跟着当前可见项走。库在启动时把项实例搬进自己的内部 `MenuFlyout`，但 `x:Bind` 仍在项本身上，切换状态不需要重建菜单。

效率面板项用独立样式：`Width="Auto"`、`MinHeight="500"`，`Body` 根元素固定 `Width="336"`，避免继承主菜单的 `TrayMenuWidth=160`。

## 7. 主题切换

面板顶部三档段选，直接调 `Theme.Select()`，与设置页同一个入口。主窗口会照常走现有的主题过渡。

托盘 SecondWindow 的 `frame.RequestedTheme` 由库在 `TaskbarIcon.ActualThemeChanged` 时更新，历史上这里有过「主题不跟」的报告（见 `tray.md` 末尾）。面板根元素再显式绑一次 `RequestedTheme="{x:Bind Theme.RequestedTheme, Mode=OneWay}"` 作为兜底。**验收必须实际切换一次并看面板颜色是否立刻变**；如果只在收起重开后生效，就记为已知问题，不假装通过。

## 8. 交互规则

沿用现有约定：

- 面板内点击、拖动不收起，点面板外才收起。
- 「设置」「重启」「退出」例外：设置要把主窗口带回前台，重启/退出要走收尾链。三者由面板抛事件，`MainWindow` 设 `_dismissTrayMenu = true` 后执行现有命令，避免「把托盘窗口带回」的逻辑把面板又弹出来。
- 无曲目时：封面占位、标题「未在播放」、转台与收藏禁用。
- 切页不改变面板高度。

## 9. 风险与验证

| 风险 | 处理 |
| --- | --- |
| `MinHeight` 能否真的顶住库强写的 `Height=32` | 先做静态骨架（一个 500 高的空 Border），右键验证窗口尺寸，再接线 |
| Slider 在 `MenuFlyoutItem` 内的指针捕获与拖动 | 单独验证拖动音量不收起面板；失败时退回「音量行只留静音 + 用滚轮/上下键调」 |
| 面板内 `ListView` 吃掉滚轮、与菜单键盘导航冲突 | 队列页列表单独处理 `PointerWheelChanged`；只要求可用，不追求完整键盘导航 |
| 切页导致窗口尺寸变化 | 内容区固定高度，两页同高 |
| 主题切换后托盘面板不重绘 | 显式绑 `RequestedTheme`；仍不生效则记为已知问题 |
| 高 DPI（125% / 150%）下窗口尺寸与内容裁切 | 三个缩放档都过一遍 `MeasureFlyout` 的尺寸计算 |

手动验收清单：

1. 简洁模式与升级前完全一致，菜单内连续操作、点击外部收起都不回退。
2. 切到效率面板后，右键弹出尺寸正确，不裁切、不多留空白。
3. 音量拖动生效并落盘；静音按钮状态与主界面一致。
4. 播放模式单键循环，图标与主界面同步。
5. 音效开关、预设选择、音质切换、队列点行切歌都生效，面板保持打开。
6. 三档主题切换后，主窗口与面板同时变色。
7. 设置 / 重启 / 退出按预期收起面板并执行。
8. 深色 / 浅色、100% / 125% / 150% 缩放各过一遍。

## 10. 涉及文件

| 文件 | 改动 |
| --- | --- |
| `src/Bowen.Core/Models/TrayMenuStyle.cs` | 新增枚举 |
| `src/Bowen.Core/Models/ThemeSettings.cs` | 新增字段、默认值、`Normalized()` |
| `src/Bowen.WinUI/ViewModels/ThemeViewModel.cs` | 新增属性与落盘、`IsEfficientTrayMenu` |
| `src/Bowen.WinUI/Views/SettingsPage.xaml` | 「界面外观」加一行 ComboBox |
| `src/Bowen.WinUI/Controls/TrayControlPanel.xaml` / `.cs` | 新面板控件 |
| `src/Bowen.WinUI/MainWindow.xaml` | 托盘菜单分组、面板项、样式 |
| `src/Bowen.WinUI/MainWindow.xaml.cs` | 设置/重启/退出事件处理 |
| `docs/tray.md` | 补一节指向本文并记录实测结果 |

## 11. 已确认的决定

1. **音量在面板内用横向滑条**（2026-11-07 确认）。这是对现有「不许横向外显音量」规则的唯一例外，只限托盘效率面板；播放条、歌词页、MV 页维持原交互不变。面板里的静音仍走图标按钮，滑条只负责调数值。
2. **底部五颗图标拆成两组**（2026-11-07 确认）：左组小窗/桌面歌词，右组设置/重启/退出，中间留弹性空白。
3. **保留顶部「正在播放 + 收藏」行**（2026-11-07 确认）。
