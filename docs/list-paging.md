# 列表翻页：滚到末尾自动续加载

2026-10-03。原本每个分页列表都在页脚放一个「加载更多」按钮，手点才翻页。
桌面端每次都要把鼠标移到角落点一下，翻几页就很烦。现在改成滚到末尾自动续加载，
数据取完时在列表末尾显示一行「没有更多了哦~」。

覆盖 10 个页面的 12 处按钮，外加评论面板的「加载更多」「加载更多回复」两处。

## 触发机制：末尾容器被实现，不是 `ScrollViewer.ViewChanged`

发现页原先自己实现过一套 `ViewChanged` + 200px 提前量的滚动检测，这次**没有**把它推广出去，
换成了 `ListViewBase.ContainerContentChanging`（某个条目的容器被创建出来时触发）：
**末尾条目的容器被实现，就说明用户已经接近底部。**

`ViewChanged` 有三个绕不过去的硬伤：

| 问题 | 具体表现 |
| --- | --- |
| 找不到滚动器 | 它得先拿到模板里的 `ScrollViewer`。评论面板的回复列表初始 `Visibility=Collapsed`，折叠元素不进布局、模板不展开，`FindScrollViewer` 返回 `null`，订阅根本建立不起来，那一半的自动翻页直接失效 |
| 内容不足一屏时永不触发 | `ViewChanged` 只在滚动或缩放改变视口时触发。列表短于窗口时偏移量恒为 0、无从滚动，事件永远不来，会卡死在第一页 |
| 视觉树误命中 | 从页面根开始找会先撞上别的 `ScrollViewer`。`AlbumDetailPage` 那个专辑简介的滚动区就是列表的兄弟节点，会立刻被误判成「到底了」 |

`ContainerContentChanging` 挂在 `ListViewBase` 自身，不需要找任何东西，三个问题一次消掉。

触发点配合列表默认的 `ItemsStackPanel CacheLength=0.5`，末尾条目会**提前约半个视口**被实现 ——
提前量比原来的 200px 更足，不会等真滚到底才发请求、先给用户看一段空白。

## 内容不足一屏时会自动补页

短列表不足一屏的话，末尾容器不会再被实现，后面的数据就永远够不到了
（换成滚动触发之前，那个「加载更多」按钮正是这条逃生通道）。所以内容填不满视口时会连环补页，
直到填满或没有更多。

循环天然收敛：内容超过视口约 1.5 倍后末尾条目不再实现，事件也就不再来。
幂等靠两处挡：`AsyncRelayCommand` 在执行期间 `CanExecute` 返回 false，各 ViewModel 内部还有忙闲早退。

**发现页是这条行为最明显的地方**：首屏 4 个模块在高窗口下常常填不满，会多补一批（3 个请求）。
这是有意的取舍 —— 不补的话列表滚不动，模块 5–12 就锁死了。

## 结束提示挂在列表的 `Footer` 上

「没有更多了哦~」由 `Controls/PagingEndNote.xaml` 渲染，放进**列表自己的 `Footer`**，
跟着内容一起滚。钉在页面底部的话，提示与列表末尾之间会隔着一大片空白，看不出「是这个列表到底了」。

`TrackListView` / `AlbumListView` 各带一个 `Footer` 依赖属性，转给它内部那个列表 ——
页面只跟包装控件打交道，不必知道里面用的是哪个列表、也不用去猜视觉树。

> **实现细节**：`Footer` 不是用 `x:Bind` 转发的，而是在属性变更回调里直接推给内部列表。
> XAML 里属性元素与子内容的赋值顺序没有保证，`OneTime` 绑定时可能还没轮到 `Footer`。

失败时的「加载失败 + 重试」和结束文案放在同一个控件里，两者互斥（`LoadFailed` 为真时 `ShowEnd` 必为假）。

## 失败必须留重试入口

按钮还在的时候，某页加载失败后用户能再点一次；按钮没了，纯滚动触发会让用户**卡在底部无从重试**
（末尾容器已经实现过了，不会再触发事件）。所以各 ViewModel 都加了 `LoadFailed` / `ShowRetry`：

| ViewModel | 属性 |
| --- | --- |
| `PagedList<T>` | `ShowEnd` / `ShowRetry` / `LoadFailed`，一个类覆盖 8 个列表 |
| `PlaylistTracksViewModel` | 同上（我喜欢的、歌单详情） |
| `DiscoverViewModel` | 同上；另外给 `LoadMoreAsync` 补了 `try/catch` —— 它原先只有 `try/finally`，异常逃到 UI 线程就是闪退 |
| `SearchViewModel` | 同上；`ShowEnd` 按页签区分，「综合」不分页、恒为 false |

`ShowEnd` 与 `ShowRetry` 是算出来的、没有自己的存储字段，所以任何会影响它们的状态一变
（`HasMore` / `IsBusy` / `LoadFailed`）都要一并通知，否则末尾提示不会刷新。

## 「播放全部」要先把剩下的页拉完（`PagedList.LoadAllAsync`，2026-10-03）

自动续加载带来一个新问题：**首屏只有一页**。专辑详情进来只加载一页（实测 5 首），
这时点「播放全部」，队列里就只有那 5 首 —— 11 首的专辑有 6 首永远播不到。

所以 `PagedList<T>` 增加了 `LoadAllAsync(maxPages = 20)`：一页一页拉到 `HasMore` 为假，
再交给播放协调器排队列。三条返回语义值得记住：

| 情况 | 返回 | 调用方该做什么 |
| --- | --- | --- |
| 真的拉到底 | `true` | 正常排队列 |
| 撞页数上限 / 中途某页失败 | `false` | **照常播已加载的那些**，不要报错 |
| 进来时就有加载在进行 | `false` | 同上 —— `LoadMoreAsync` 在忙时会早退，硬循环只会空转到上限 |

**失败也算「没拉全」**：首屏就失败时 `HasMore` 停在 `false`（`ReloadAsync` 开头会清掉它），
只看 `!HasMore` 会把「什么都没拉到」误判成「拉完了」。所以返回的是 `!HasMore && !LoadFailed`。

调用点是专辑详情的「播放全部」（`AlbumDetailViewModel.PlayAllAsync`），
它另有一个自己的 `IsPlayingAll` 状态位防重复点击 —— 与列表的 `IsBusy` 不是一件事。

## 状态文案不再带「（滚动加载）」

`StatusText` 曾经在还有下一页时补一句「（滚动加载）」。那是自动翻页刚上线时用来提示行为变化的，
现在列表末尾本来就有「没有更多了哦~」，这句成了噪音，已从 `PagedList<T>`、
`PlaylistTracksViewModel`、`DiscoverViewModel`、`SearchViewModel` 四处去掉，只留条数。

## 挂载点

| 位置 | 挂法 |
| --- | --- |
| 我喜欢的、歌单详情、专辑详情、榜单详情、歌手详情的歌曲、搜索的「单曲」页签 | `TrackListView` / `AlbumListView` 的 `HasMore` + `LoadMoreCommand` + `Footer` |
| 发现页、搜索的「歌单/专辑/歌手」页签 | `VirtualizedListView` 上直接挂 `AutoPaging` 附加属性，`Footer` 就地写 |
| 歌手详情的专辑 | 原生 `GridView`（格子尺寸在代码里按可用宽度算，见 [`ui-refresh.md`](ui-refresh.md) §16.3），挂附加属性 + `GridView.Footer` |
| 音乐库大类详情 | 专辑用原生 `GridView`，同样挂附加属性。`GridView` 的默认模板确实把 `Header`/`Footer` 转发给了 `ItemsPresenter`（查过 WinUI 的 `generic.xaml`） |
| 评论面板 | 两个列表各挂一份，绑各自的 `HasMore`/`LoadMoreCommand`。它的收尾文案用面板自己的配色，没走 `PagingEndNote` |

**不要误挂**：`HomeSectionView` 里的横向卡片列表（`VerticalScrollMode=Disabled`）、
音乐库大类页的横向「子类」列表 —— 它们整组一起被实现，挂上就会立刻乱翻页。
`BangListPage` / `RecentPage` / `AI 歌单页` 本来就不分页，不挂。

## 手动验收

| 项 | 验证重点 |
| --- | --- |
| 评论面板进回复详情 | `RepliesList` 是初始折叠的那个列表，是「换触发机制」的主要验证点，最容易出问题 |
| 短列表 | 不足一屏的歌单/专辑会自动补满，不卡在第一页 |
| 发现页开屏 | 多补一两批而已，不会一口气拉完 12 个模块 |
| 逐页滚到底 | 不重复请求、不跳页（**歌手专辑页尤其要看**，页号基数写错时症状正是"第一页重复一遍"）；取完后末尾出现居中小字，且紧贴最后一行 |
| 歌手专辑页 | 卡片数等于该歌手的真实专辑数，滚到底出现「没有更多了哦~」；页签条切到「歌曲」「介绍」再切回来，滚动位置保留 |
| 音乐库大类详情 | 专辑用的是 `GridView`，`Footer` 渲染是这套方案里最没把握的一处，实际看一眼 |
| 断网后滚到底 | 出现「加载失败 + 重试」，点了能重拉失败的那一页 |
| 歌手详情 | 两节（两个页签）的提示各自跟着自己的列表走，不串 |
