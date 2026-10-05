# 播放队列与播放模式

2026-10-03：播放栏那颗「播放列表」按钮从 `IsEnabled="False"` 的占位改成真入口，
同时补上此前完全不存在的一块：**播放模式**（顺序播放 / 列表循环 / 列表随机）。

在此之前，队列是一个**只能整页替换**的 `List<Track>` 快照，下一首是纯顺序、到头即停，
没有任何循环或随机逻辑，也没有「加入队列」这样的概念 —— 点行就是「整页入队并从这一首开始」。

> **2026-10-04 更新：点行的语义反过来了。** 现在是「把这一首**追加到队尾**并立即播它」，
> 队列里原有的歌全部保留（`EnqueueAndPlayAsync`）。整页替换只剩「播放全部」一个入口，
> 见 [`track-list-toolbar.md`](track-list-toolbar.md) §1。下面 §1 / §3 / §5 里凡是「整页入队」
> 的措辞都已按新语义改写，但**整页替换本身仍然是 `Replace` 的语义**，只是不再由点行触发。

## 1. 队列的内部结构：排列 + 游标

`src/Bodian.WinUI/Playback/PlayQueue.cs` 从「单个 `_index`」改成三个字段：

```
_items    : List<Track>    队列本体，**顺序就是抽屉里给用户看到的顺序**
_order    : List<int>      _items 下标的一个排列
_cursor   : int            _order 中的位置，-1 表示空队列
_mode     : PlayMode
```

`Current => _items[_order[_cursor]]`。

**为什么不是「洗牌打乱 `_items`」**：那样抽屉里的顺序会跟着随机变，用户上一眼看到的第 5 首
下一眼跑到第 12 首去了。把「显示顺序」和「播放顺序」拆成两份数据，
随机模式就只是换一条 `_order`，`_items` 全程不动。

- 顺序播放与列表循环时 `_order` 是恒等排列。
- 列表随机时 `_order` 是洗过牌的排列，**且轮转到当前曲目开头** —— 切到随机模式的那一瞬间
  当前曲目不能跳走。
- 切回非随机模式时按当前曲目重建恒等排列，**当前曲目同样不动**，只是它在 `_items` 里的
  真实下标成了新游标。

> ★ **增删曲目必须同时重映射 `_order` 里的下标**，这是本类最容易错的地方：
> 往 `_items` 的第 i 处插入，会让所有 `>= i` 的下标失效；删除则会让 `> i` 的整体前移。
> 出错不会立刻炸，只会让「下一首」跳到意料之外的地方。单测从**外部行为**上验这条不变量
> （顺序模式走一遍必须不重不漏地覆盖每一首），而不是去读那个私有字段。

队列的变更方法有六个，都触发 `Changed`（**只有一个例外，见下**）：

| 方法 | 语义 |
| --- | --- |
| `Replace(items, startIndex)` | 整页替换并落到第 N 首（**现在只由「播放全部」用**） |
| `Append(track)` | 加到队尾；**队列里已有同一首（按 Id）时不加，返回 `false` 且不抛 `Changed`** |
| `AppendRange(items)` | 批量追加，返回实际加进去几条；去重、跳过 `Id <= 0`、**整批只抛一次 `Changed`** |
| `InsertNext(track)` | 插到当前曲目之后 |
| `MoveToItem(itemIndex)` | 跳到第 N 首（抽屉点行用） |
| `RemoveItem(itemIndex)` | 删第 N 首 |

> ★ **`Append` 是唯一会「什么都不做」的变更方法。** 它返回 `bool` 就是为了让调用方
> 知道这次到底加没加进去 —— 行菜单的「加入播放队列」据此把提示从「已加入播放队列」
> 换成「这首歌已经在播放队列里」，而不是骗人说加成功了。

`Append` / `AppendRange` / `InsertNext` 都在**空队列上做了兜底**：把这一首放上去并落游标。
`MoveToItem` / `RemoveItem` 越界时返回 `false`。

**为什么 `AppendRange` 不能逐条走 `Append`**：队列面板是整表重建的，每抛一次 `Changed` 就重建一次，
几百首的列表会卡住。整批合并成一次事件是它存在的全部理由。

## 2. 三种播放模式

`src/Bodian.Core/Models/PlayMode.cs`。只有三个成员，**没有单曲循环**。

| 模式 | `MoveNext` 到头 | `MovePrevious` 到头 | `QueueExhausted` |
| --- | --- | --- | --- |
| `Sequential` 顺序播放 | 返回 `false`，**停止** | 返回 `false` | 会触发（播放条提示「已经是最后一首了」） |
| `ListLoop` 列表循环 | 环绕回第一首 | 环绕回最后一首 | 永不触发 |
| `Shuffle` 列表随机 | 一轮走完**重新洗牌继续** | 返回 `false`（不往回绕） | 永不触发 |

`Sequential` 保持改动前的行为。后两者属于「列表」家族，都不会自然结束。

**随机模式一轮走完要重洗，且避开刚放完的那一首** —— 否则一轮的末尾与新一轮的开头接上，
同一首连放两遍。`Reshuffle` 里有一条显式的兜底：洗完若首位仍是刚放完那首，就与随机一个位置对调。

> ★ **模式判断收在 `PlayQueue.HasNext` / `HasPrevious` 里，不要在调用方各写一遍。**
> `SmtcManager` 与播放条都直接读这两个属性，收在这里，系统媒体控件（键盘多媒体键、
> 音量浮层里的上一首/下一首）就自动跟着模式变灰或变亮，一行都不用改。

## 3. 协调器上的命令

`src/Bodian.WinUI/Playback/PlaybackCoordinator.cs` 新增：

| 方法 | 说明 |
| --- | --- |
| `SetPlayMode` / `CyclePlayMode` | 设模式并**落盘**；按钮按 `顺序 → 循环 → 随机 → 顺序` 轮换 |
| `EnqueueAndPlayAsync(track)` | **点行的入口**：追加到队尾并立即播它；队里已有则不重复加，跳到原来那一份 |
| `PlayFromAsync(list, index)` | 整表替换并从第 N 首播。**现在只剩「播放全部」两个调用点** |
| `AddToQueueAsync(track)` | 加队尾，返回是否真的加进去了 |
| `AddToQueueAsync(tracks)` | 批量加队尾，返回实际追加数 |
| `PlayNextAsync(track)` | 插到当前之后 |
| `PlayQueueItemAsync(itemIndex)` | 跳到第 N 首 |
| `RemoveQueueItemAsync(itemIndex)` | 删第 N 首 |
| `ClearQueue()` | 清空 |

三条行为上的决定：

1. **队列原本为空时，加队的几条命令直接开播。** 否则加了没有任何反应，用户会以为没生效 ——
   而「队列是空的」恰恰是第一次用这些入口时最常见的状态。
   批量版的判断是「追加数 > 0 且原本为空」：一首都没加进去（全是重复）时不开播，
   空队列没什么可播的。
2. **删掉的正好是当前曲目时才换歌**，其余情况不碰引擎。界面上正在播放那一行的删除按钮是置灰的，
   正常走不到这个分支；留着是因为队列是共享状态。
3. **清空不打断播放。** 正在放的那一首自然放完 ——「清空列表」说的是列表，不是「停止播放」。

自动续播（引擎报「放完了」→ `AdvanceAfterEndAsync` → `NextAsync`）**一行没改**，
模式判断已经收在 `PlayQueue` 里。

## 4. 模式落盘

`IPlaybackSettingsStore` + `JsonPlaybackSettingsStore`，落到 `%LOCALAPPDATA%\Bodian\playback.json`。
写法与音质偏好那套逐字一致（先写 `.tmp` 再 `File.Move` 覆盖、枚举合法性校验、失败只记 warning）。

**刻意与 `audio-quality.json` 分开**：两个互不相干的小偏好共用一个文件只会让读写互相牵制。
它与 `devid.txt` / `session.dat` 不同，不受「必须与探针共用」那条约束。

## 5. 加入队列的入口：行菜单多两条

曲目行尾「更多」菜单从四项变六项，顺序固定：

```
我喜欢 · 下一首播放 · 加入播放队列 · 添加到歌单 · 查看歌手 · 查看专辑
```

**两条队列动作紧挨「我喜欢」，与写服务器的那两项用位置隔开** ——
「加入播放队列」与「添加到歌单」都带「列表 / 歌单」字样，挨着摆最容易被点错。
文案也刻意避开「歌单」二字。

接线沿用 `TrackActionsViewModel` 那一套：新增自制接口
`src/Bodian.WinUI/Services/IQueueSink.cs`（`PlayNextAsync` / `AddToQueueAsync`），
真实实现在 `TrackActionsService` 里落到协调器。
这样 `TrackActionsViewModel` 仍然只依赖 Core 模型与自制接口，**六条动作全都能离屏测**。

> `IQueueSink.AddToQueueAsync` 返回 `Task<bool>`（加进去了没有），调用方据此换提示文案。

`musicId <= 0` 时两条都置灰，与「查看专辑」缺 `albumId` 时同一套处理。

## 5.1 另一条入口：列表工具栏

列表级的两条队列动作在 `Controls/TrackListToolbar` 上，走另一个自制接口
`Services/ITrackBatchActions.cs`（同样由 `TrackActionsService` 兼任，走 App 资源键
`BodianTrackActions`）：

| 按钮 | 行为 |
| --- | --- |
| 全部加入播放列表（普通态） | 把列表里**已加载的**全部追加到队尾，不打断正在播的 |
| 加入播放列表（多选态） | 只追加选中的那些 |

两条都只碰队列、不碰引擎当前曲目。详见 [`track-list-toolbar.md`](track-list-toolbar.md) §4。

## 6. 界面

### 6.1 播放栏传输组变五键

```
播放模式 · 上一首 · 播放/暂停 · 下一首 · 播放列表
```

右侧工具区只剩 `音质 · 歌词 · 音量`。几何与图标见 [`ui-refresh.md`](ui-refresh.md) §18。

### 6.2 右侧抽屉

面板**不在播放条里**，而是挂在主窗口第 1 行（内容区）的覆盖层上 ——
抽屉要盖住内容区，而播放条自己就占着窗口最下面那一行，在这一层放不下。
所以 `PlayerBar` 只抛一个 `PlaylistRequested` 事件，开合归 `MainWindow` 管。

抽屉只覆盖内容区：**标题栏与播放条都露在外面**，拉着队列时还能搜索、还能暂停切歌。
收起方式四个：点空白处、`Esc`、右上角 ×、再点一次「播放列表」按钮。

控件与模型：

| 文件 | 职责 |
| --- | --- |
| `Controls/PlayQueuePanel.xaml(.cs)` | 面板本体：头部（标题 / 计数 / 清空 / 收起）+ 列表 |
| `ViewModels/PlayQueueViewModel.cs` | 整表重建、三个动作（切歌 / 删单曲 / 清空）、`IsOpen` |
| `ViewModels/PlayQueueRow.cs` | 一行：序号 / 封面 / 标题 / 歌手 / 时长 / 可否删除 |

三处讲究：

1. **整表重建，不做增量同步。** 队列变动不频繁，而增量同步要自己维护一套「哪一行对应哪个下标」
   的影子状态 —— 队列那边刚因为下标重映射踩过一遍，这里没必要再来一次。
2. **抽屉开着的时候才重建。** 队列每次切歌都会触发 `Changed`，而重建会清空再填满
   `ObservableCollection`、让 `ListView` 整个重新测量。抽屉没拉开就没人看，这份力气白花。
3. **重建排到 dispatcher 的下一轮。** 队列很可能就是在面板自己的事件处理里被改的
   （点行切歌、点行尾的删除），同步重建等于在 `ItemsControl` 处理事件的过程中把它脚下的集合抽掉。

**正在播放那一行的删除按钮置灰**：删掉当前曲目之后「要不要自动切下一首」没有不别扭的答案；
想跳过它就点下一首。界面这样定，`PlayQueue.RemoveItem` 就永远不会删到游标位置。

**清空要弹确认框**，默认落在「取消」上 —— 与项目里取消收藏、清空播放记录同一套写法。

## 7. 图标：三个自绘路径

Segoe MDL2 Assets 有 `RepeatAll`（E8EE）与 `Shuffle`（E8B1），**唯独没有「顺序播放」** ——
常被当成「列表」的 E8FD 其实是 `BulletedList`。按用户要求三个图标全部自绘成 `PathIcon`，
同一 24 单位视框，笔锋才一致。

| 图标 | 来源 |
| --- | --- |
| 列表循环 | Fluent System Icons 的 `arrow_repeat_all_24_regular` |
| 列表随机 | Fluent System Icons 的 `arrow_shuffle_24_regular` |
| 顺序播放 | 同一视框下手绘的一对右向箭头（去掉回环） |

许可：**Fluent System Icons 是 MIT**，只取了两个图标的路径数据（几何轮廓），
未取字体文件、未取图标资源包，与本项目 GPL-3.0 相容。

> ★ **路径数据必须规范化后才能喂给 WinUI。** Fluent 的 SVG 用 `a.75.75` 这种
> **隐式分隔**（两个数字之间没有分隔符，靠第二个小数点断句），SVG 允许，但 WinUI 的
> `Geometry` 解析器（沿用 WPF 那套）比 SVG 严。出错是**运行期**抛在 `PlayerBar` 加载上，
> 编译期看不出来。
> 所以把全部命令重发成绝对形式、命令与数字之间一律显式逗号/空格，
> 并逐像素比对规范化前后的渲染结果，确认几何没变。

缩放靠外面那层 `Viewbox`（路径按 24 单位画，显示尺寸取 `SizeIconMd = 20`），
`PathIcon` 自己给同样的 24×24 自然尺寸 —— **显式写死这两个数，是为了不依赖
`PathIcon` 到底缩不缩放**。

## 8. 验证

WinUI 构建通过，0 错误、0 警告（`AiPlaylistPage.xaml:27` 那条既有 `WMC1506` 本轮未出现）；
**923 项离线测试通过**（此前 877，本轮新增 46）：

| 新增测试 | 覆盖 |
| --- | --- |
| `PlayQueueTests`（27 项） | 三种模式的进度真值表（含单曲队列）、增删后的排列重映射不变量、空队列边界、`Changed` 触发时机 |
| `PlayQueueCommandTests`（14 项） | 六条命令的队列副作用、空队列直接开播、清空不打断播放、切模式后自动续播走对分支、模式落盘与重启读回 |
| `TrackActionsViewModelTests`（+5 项） | 菜单六项及顺序、两条动作确实调到 `IQueueSink`、缺 id 置灰、离线宿主不假装成功 |

> **2026-10-04 更新：**`PlayQueueTests` 与 `PlayQueueCommandTests` 又在
> [_track-list-toolbar.md_](track-list-toolbar.md) §1 那一轮里扩过（追加去重、`AppendRange`、
> `EnqueueAndPlayAsync`），上表记的是本轮当时的状态。当前总数见那份文档的 §7。

界面部分按惯例由用户手动验收：

| 项 | 验证重点 |
| --- | --- |
| 播放栏布局 | 模式在最左、播放列表在下一首右边；窗口拉窄到 < 900 DIP 不重叠 |
| 三个图标 | 20 DIP 下是否清晰、笔锋是否一致、切换时会不会跳 |
| 模式行为 | 顺序放到底停住；循环绕回第一首；随机一轮不重样且末尾不重复 |
| 模式记忆 | 重启后停在关机前那个模式 |
| 抽屉 | 四角圆角与右边距的观感；播放条露在外面；点空白 / Esc / × / 再点按钮都能收起 |
| 抽屉内 | 正在播放那行有起伏条且删除按钮灰；删非当前曲目不打断播放；清空弹确认框 |
| 行菜单 | 两条动作落位正确；空队列时直接开播 |
| 系统媒体控件 | 两种循环模式下上一首/下一首不会变灰 |

## 9. 本轮不做

| 项 | 原因 |
| --- | --- |
| 单曲循环 | 用户列的是三个模式。真要做：`PlayMode` 加一个成员、`PlayQueue.MoveNext` 加一个分支，图标现成（`E8ED` RepeatOne） |
| 队列拖拽排序 | 未要求 |
| 队列持久化 | 关机不保留队列，与改动前一致 —— 落盘的只有音质偏好与本题的模式偏好 |
| 抽屉内的「更多」菜单 | 抽屉只做切歌、删、清空三件事 |
| 歌词页控制台的模式按钮与队列入口 | 那套大号控制台复用同一组命令，但它跟着全屏歌词页走，播放条那时不可见；本轮只动底部播放栏 |

## 10. 2026-10-05：播到末尾再点播放

### 10.1 症状与根因

顺序播放放完列表最后一首后，引擎状态是 `Stopped`（`LibMpvPlaybackService.OnEndFile`），
而 mpv 因为 `keep-open=no` + `idle=yes` 已经把文件卸载了 —— **引擎里没有任何东西可播**。此时：

| 操作 | 现象 | 真正发生的事 |
| --- | --- | --- |
| 点播放键 | 没声音，但**歌词照滚** | `pause=false` 在 idle 的 mpv 上是**空操作**；`SetPause` 的乐观更新却把状态置成 `Playing`，歌词时钟据此开始按墙钟外推，而 idle 下读不到 `time-pos`、**根本不发进度事件**，没人能把它拉回来 |
| 拖进度条 | 弹「跳转失败：property unavailable」 | `time-pos` 在 idle 下不存在。更糟的是失败处理会把状态砸成 `Idle`，进度条跟着归零 —— 这才是「进度条异常」的来源 |
| 点某一行歌词 | 同上 | `LyricsViewModel.SeekToLineAsync` 走的是同一个 `time-pos` |

> ★ **`SmtcManager` 里本来就修对了一半**：系统媒体控件的播放键有 `Idle or Stopped => 重播当前这首`
> 这条分支，播放条那颗键漏了。两份判断分散在两处，正是这次漏一处的直接原因。

### 10.2 判断收在协调器的 `PlayAsync`

新增 `PlaybackCoordinator.PlayAsync`（`src/Bodian.WinUI/Playback/PlaybackCoordinator.cs`），
**所有播放键都走它**：引擎处于 `Idle` / `Stopped` 时不再直接调引擎的 `PlayAsync`，而是重新解析音源再 loadfile
（CDN 直链带签名且有时效，旧的很可能已经过期）。`PlayerViewModel.TogglePlayPauseAsync` 与
`SmtcManager` 那两处局部判断都并到这一个入口，与 §2「模式判断收在 `PlayQueue` 里」是同一条理由。

重新加载的目标分两种，靠协调器上的私有标记 `_queueExhausted` 区分：

| 状态 | 重播目标 |
| --- | --- |
| 队列已经播到头（顺序模式放完最后一首） | **整个队列从第一首重来** |
| 其余（试听片段结束、加载失败后重试） | 重播当前这首 |

`_queueExhausted` 在 `NextAsync` 的 `MoveNext()` 失败分支里置上，在 `PlayCurrentAsync` 里清掉 ——
**任何一次新的播放尝试都算翻篇**（六条队列命令全经由它），不需要每个调用点自己记得清。

配套给 `PlayQueue` 加了 `MoveToStart()`：把游标拨回**排列的第一位**（`_cursor = 0`，不是
`MoveToItem(0)`）—— 前者是「播放顺序的第一位」，不依赖面板里的显示下标；顺序模式下 `_order` 是恒等排列，
两者等价，随机模式下回到本轮洗牌的第一位。

### 10.3 停止态拖进度条：无效但不报错

`PlayerViewModel.SeekToAsync` 与 `LyricsViewModel.SeekToLineAsync` 都加了 `Idle` / `Stopped` 门禁。
被挡下时 `PlayerViewModel` 会补发一次 `PositionSeconds` 通知把滑块拨回原位 —— 滑块已经被拖走了，
而引擎在这个状态下不会再发进度把它拉回来。

> ★ 补发通知能拨回滑块是**验证过的**，不是想当然：x:Bind 生成的
> `Update_ViewModel_PositionSeconds` 是无条件 `RangeBase.Value = obj`，没有缓存比对
> （`obj/.../Controls/PlayerBar.g.cs`）。若哪天它改成带缓存的形式，这里会静默失效。

**不去动** `OnEngineStateChanged` 对 `Stopped` 不复位进度的行为：末尾停下时播放条上显示的还是那首，
进度停在终点是如实的。

### 10.4 已知边界

`ClearQueue()` 清空后当前曲目自然放完，`MoveNext` 在空队列上同样失败，于是也会被标记成「已播到头」。
此时点播放没有可重播的第一首，是静默无操作 —— 清空队列本身就不该保留可播内容，有意留着。

### 10.5 验证

**1184 项离线测试通过**（此前 1177，本轮新增 7）：

| 新增测试 | 覆盖 |
| --- | --- |
| `PlayQueueTests`（3 项） | `MoveToStart` 回到第一位、已在第一位时仍返回 `true` 并抛一次 `Changed`、空队列返回 `false` |
| `PlayQueueCommandTests`（4 项） | 耗尽后按播放从第一首重来；未耗尽而引擎停下时重播当前这首；`Paused` 时只恢复不重载；一次新的点播会清掉「已播到头」 |

`FakePlaybackEngine.RaiseEnded()` 改成**先置 `Stopped` 再触发 `Ended`**，对齐真引擎
（`LibMpvPlaybackService.OnEndFile` 也是先 `SetState(Stopped)` 再抛事件）—— 不这样，
「放完之后按播放」永远走不到 Idle/Stopped 那条分支，也就验不出真机上为什么没声音。

界面部分按惯例由用户手动验收：末尾停下后点播放是否从第一首重来、拖进度条是否只弹回不报错、
点歌词行是否不再弹红条、暂停态点播放是否仍只续播不重载。
