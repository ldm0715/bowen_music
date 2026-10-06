# 曲目行内的歌手 / 专辑跳转

2026-10-06。

在此之前，播放条第二行已经把歌手名与专辑名做成了可点链接（点歌手进歌手详情、点专辑进专辑详情），
但曲目列表行里的这两个字段一直是纯文本 —— 想跳过去只能展开行尾的「更多」菜单，多点两下。

这一轮把播放条那套跳转搬到行模板上。**没有新增任何取数逻辑**，全部复用既有实现。

## 1. 两个入口，一份逻辑

| 点哪里 | 落到哪 |
| --- | --- |
| 歌手名 | `ArtistPickerDialog.ShowPickerAsync` —— 唯一且有效的歌手直接跳，合唱弹选择框。缺艺人明细时走 `TrackActionsViewModel.OpenArtistAsync` 的三级降级（`Track.Artists` → 详情补查 → 按 `&` 拆串） |
| 专辑名 | `TrackActionsViewModel.OpenAlbum()`，`AlbumId <= 0` 时不可点 |

判据与播放条逐条对齐：**歌手看名字非空**（有没有有效 id 交给降级链条去辨，最坏只是弹出几格点不动的卡片，
绝不会跳错人），**专辑看 `AlbumId > 0`**（0 表示服务端没给这个字段，本地历史重建的曲目就是这样）。

`TrackRow` 加 `CanOpenArtist` / `CanOpenAlbum` 两个判据。它们含「是不是多选态」这一位，
所以挂在 `IsSelectionMode` 的 `[NotifyPropertyChangedFor]` 上 —— **多选态下链接退化成普通文字**，
与行尾「更多」按钮在多选态收起同一口径：那时点行是勾选，行里不该再开第二个入口。

## 2. `Controls/TrackLink.xaml(.cs)`

一份行模板有两处用到这段交互（`TrackListView.xaml` 与 `SearchPage.xaml` 的 `OverviewTrackTemplate`），
各写一份就是要同步的复制品 —— 项目里 `TrackMoreButton` 就是为了同样的理由抽出来的控件，这里照同一个路子。

两个依赖属性：`Row`（`TrackRow`）与 `Kind`（`TrackLinkKind.Artist` / `Album`）。
`Kind` 只负责切「显示哪一格」，格子里是链接还是普通文字由模板绑 `Row.CanOpenX` 决定。

**歌手与专辑各写一套元素，而不是一个按钮靠 `Kind` 分叉**：两者点不动时的灰不同（次级 / 三级），
可点判据也不同。一个元素要在绑定里按 `Kind` 分叉，就得给控件实现 INPC 去算私有计算属性；
两套并列则是纯声明式的。

**服务走 App 资源键 `BodianTrackActions`**，与 `TrackMoreButton` 同一处例外 ——
控件是 XAML 实例化的，构造函数拿不到 DI 容器。

## 3. 颜色与播放条同源

需求是「用播放条同款的样式」，而播放条用的是**内联 `Hyperlink`**，颜色取自
`Themes/Theme.xaml` 覆写的三个键：

| 键 | 浅色 | 深色 |
| --- | --- | --- |
| `HyperlinkForeground` | `#9E000000` | `#C5FFFFFF` |
| `HyperlinkForegroundPointerOver` | `#FF007A57` | `#FF00F3B0` |
| `HyperlinkForegroundPressed` | 同上 | 同上 |

`HyperlinkButton` 走的是**另一套**键（`HyperlinkButtonForeground*`，常态还是强调色、悬停另加一层底色），
两套色值并不相同。所以要两边一模一样，就得自己写一份 `ControlTemplate`：里面只放一个 `ContentPresenter`，
常态与悬停 / 按下分别绑到上面那三个键 —— 底色自然就没有了，换主题两边也一起变。

造型上还有三处必须显式写：`Padding="0"`（默认取 `ButtonPadding(11,5,11,6)`，不清零文字右移、行高也涨）、
`FontSize` 给字号 token（按钮默认是正文那一档）、`HorizontalAlignment` 与 `HorizontalContentAlignment`
同为 `Left`（两者同向才会「文字多长、热区多长」，不然点列里的空白也算点链接）。

**播放条那一侧的构造抽成了 `Controls/InlineHyperlink.cs`**，原来它是 `PlayerBar.xaml.cs` 里的一个私有方法。
两处各写一份迟早会在色值或下划线上漂移，`PlayerBar` 的两处调用点改走共用工厂，行为不变。

## 4. 已知问题：行内链接的光标不稳（未解决）

**现象**：鼠标在链接文字上移动时，光标在手型与箭头之间来回切换。列表与播放条都有，
且**鼠标静止不动时不发生，只在移动时发生**。

**排查过的**（都无效）：

| 尝试 | 结果 |
| --- | --- |
| 内联 `Hyperlink` + `TextBlock` 不撑满 | 无效 |
| `HyperlinkButton` + 默认模板 | 无效 |
| 自定义模板 + `ContentPresenter Background="Transparent"` + 文字 `IsHitTestVisible="False"` | 无效 |
| 在 `TrackLink` 上设 `ProtectedCursor` | 无效 |
| 在 `HyperlinkButton` 子类里重写 `OnPointerEntered` / `OnPointerMoved` 重新断言光标 | 无效 |

**已排除**：项目里的四处 Win32 窗口子类化（`TransparentBackdrop` 只拦 `WM_ERASEBKGND`、
`SingleInstanceCoordinator` 只处理激活消息、session watch 只处理 `WM_QUERYENDSESSION`）都不碰光标。
官方 issue [#10357](https://github.com/microsoft/microsoft-ui-xaml/issues/10357) /
[#10529](https://github.com/microsoft/microsoft-ui-xaml/issues/10529)（`ExtendsContentIntoTitleBar`
导致光标事件错乱、按钮悬停闪烁）的复现步骤都要求「存在子窗口」，而实测时桌面歌词窗口没有开过。

**尚未排除的**：`MainWindow` 在沉浸模式切换时会**动态改标题栏**（对 `SetTitleBar` 传
`AppTitleBar` / `_lyricsTitleBar` / `null`），同时 `ExtendsContentIntoTitleBar = true` 常开。
这条路径是窗口级的，与「列表和播放条表现一致、控件层怎么改都没反应」吻合。
**验证方法**：把 `ConfigureTitleBar()` 里的 `ExtendsContentIntoTitleBar` 临时关掉跑一次，
看光标还抖不抖。

**另一个待做的对照**：换个应用（浏览器）悬停链接。若那边也抖，则是系统 / 驱动层面的问题，
与本项目无关。

## 5. 验证

`dotnet build` 0 错误、1 个既有 `AiPlaylistPage.xaml:28` `WMC1506`；
离线测试 **1254** 项通过（未新增用例 —— 改动都在 XAML 与控件层，离屏测不到）。

界面部分按惯例由用户手动验收：

| 项 | 验证重点 |
| --- | --- |
| 八页列表 | 搜索（歌曲页签）、我喜欢、歌单详情、专辑详情、歌手详情、榜单详情、最近播放、AI 歌单：点行内歌手名进歌手页，点专辑名进专辑页 |
| 搜索综合页签 | 走的是另一份行模板（`OverviewTrackTemplate`），两处同样要能跳 |
| 合唱曲目 | 点歌手弹选择框；单一歌手直接跳 |
| 回归 | **点名字不应开始播放这一行**（行本身是点行播放） |
| 颜色 | 常态灰、悬停强调色、**无悬停底色**，与播放条第二行一致；深浅主题各看一次 |
| 多选态 | 两个名字都退成灰字点不动，点整行仍然只勾选 |
| 最近播放 | 曲目由本地历史重建（`AlbumId = 0`、无艺人明细）：专辑名是不可点的灰字，歌手名可点、点击后由降级逻辑补查 |
| 布局 | 长歌手串不越过列边界压到时长列；滚出视野再滚回来状态不错乱 |
