# P7 · 侧栏与曲库：设计稿

**写于 2026-09-30。** 本文是 P7 第一刀（外壳侧栏 + 曲库**只读**）的设计定稿。
接口证据全部来自**静态解包，零请求**，证据链见 [`../reverse/findings/06-library-api.md`](../reverse/findings/06-library-api.md)。

> **2026-10-03 变更：「已购」入口与页面已移除。**
> 官方客户端与应用内都找不到购买单曲的入口，这一页没有可购买内容可显示，侧栏不再列它。
> §3.1 / §3.2 的接口证据保留（逆向结论不因客户端移除而失效），客户端侧的 `PurchasedPage` /
> `PurchasedViewModel` / DI 注册已删除，Core 的 `purchasedList` 接口、DTO、fixture 与测试保留。

> **乐库当前布局**：首页封面固定 150 DIP，分类页专辑封面固定 132 DIP；
> 悬浮背景四周留白 4 DIP，长专辑名最多两行并提供完整名称提示。
> 大类顶部的分类标签按内容区宽度自动换行，禁用横向滚动；项数放左侧，「精品 / 最新」Tab 放右侧。
> 实现细节和验证记录见 [`ui-refresh.md` §21](ui-refresh.md#21-乐库卡片与分类布局)。

**先读**：`roadmap.md` 的 P7 一节（阶段目标）、`transport.md`（传输层与 DTO 分层）、
`bodian-api-reference.md` §2.3 / §6.2（账号曲库与歌单元数据模型）。

---

## 0. 已定稿的决策（2026-09-30）

以下五条已经与用户逐条确认，**不再是待议项**。改它们要另开一轮。

| 决策 | 结论 | 理由 |
| --- | --- | --- |
| 侧栏「推荐」项 | **不放** | 与用户描述的侧栏一致；`service/finds/playlist` 用途未定，发现页已覆盖推荐内容 |
| 红心等写操作 | **本期不做** | 侧栏这一刀只读。写操作单独成一刀，先保证能读、能验、不碰账号数据 |
| 启动落点 | **我喜欢的** | 唯一全链路实测过的页；发现页排在最后一刀，落地前当首页会是空页 |
| 搜索入口 | **独立结果页**（侧栏不列它） | 2026-10-02 调整为标题栏常驻入口、提交时压栈，允许返回进入搜索前的页面 |
| 接口参数的取得方式 | **只靠解包，不发探测请求** | 用户明确要求；第五轮评论探测的教训也支持这条 |

---

## 1. 范围

| 做 | 不做 |
| --- | --- |
| 外壳换成侧栏（`NavigationView`） | **新建歌单**（2026-10-03 已补：侧栏入口 + 隐私开关 + 刷新，见 [`create-playlist.md`](create-playlist.md)） |
| 发现 / 我喜欢的 / 最近播放 / 收藏的专辑 / 自建歌单 五个入口 | **一切写操作**：加歌、删歌、红心、收藏写入 |
| 歌单详情页与曲目列表、点播 | 歌单排序、重命名、删除 |
| 最近播放的**本地记录** | 服务端播放历史（见 §6） |
| 导航模型从「一条栈」改成「根 + 详情」 | 发现页的块渲染（见 §5，押到最后一刀） |

**红心不做**是刻意的：它是写操作，且文档 §2.4 要求「写之前必须先校验歌单归属」。
把写操作混进这一刀，会让「读」的验收被「写坏了账号数据」的风险盖住。

---

## 2. 事实基础：官方桌面端的侧栏

从 `reverse/pc/app.so` 抽出的官方结构（证据见 findings/06 §8）：

```
home/
├── home_page.dart                 外壳
├── window_navigation_bar/         标题栏
├── left_menu/                     ★ 侧栏
│   ├── left_menu.dart / left_menu_item.dart
│   ├── left_my_collect_album.dart          收藏的专辑
│   ├── left_my_self_Playlist.dart          自建歌单
│   └── purchased_page/
│       ├── purchased_music_page.dart       已购（宿主，两个视图）
│       ├── purchased_single_view.dart      已购单曲
│       └── purchased_album_view.dart       已购专辑
├── right_content/                 内容区（right_content / right_home_body / inner_window）
└── player_bar/                    播放条（bar / controls / volume / progress）
```

侧栏图标资源（normal + selected 两态）**恰好 6 项**：

| 图标 | 项 |
| --- | --- |
| `leftmenu/discover.webp` | 发现 |
| `leftmenu/recommend.webp` | 推荐 |
| `leftmenu/like.webp` | 我喜欢的 |
| `leftmenu/recent.webp` | 最近播放 |
| `leftmenu/collect.webp` | 收藏的专辑 |
| `leftmenu/bought.webp` | 已购 |

即官方是「发现 / 推荐 / 我的音乐（我喜欢、最近播放、收藏、已购）/ 自建歌单」。

**本项目采纳的结构**（与用户要求一致，**不含「推荐」** —— 它和「发现」在本项目的接口面高度重合，
且 `service/finds/playlist` 的用途待定，先不铺开）：

```
发现
────────────────
我的音乐
  我喜欢的
  最近播放
  收藏的专辑
────────────────
创建的歌单        ← 运行时填充
  <歌单 1>
  <歌单 2>
```

---

## 3. 每项的接口映射

| 侧栏项 | 路径 | 参数 | 证据强度 |
| --- | --- | --- | --- |
| **发现** | `service/home/index` | **无参** | 路径+参数静态确认（两端） |
| ↳ 模块 | `service/home/module` | `moduleId` | 同上 |
| ↳ AI 歌单 | `service/home/aiPlaylistDetail?index=%d` | `index` | 同上 |
| **我喜欢的** | `service/playlist/fond` | `userId` | ✅ **P0 完整往返实测过** |
| ↳ 曲目 | `service/playlist/{id}/musicList` | `source=5`、`pn`(从 1)、`rn` | ✅ 同上 |
| **最近播放** | — | **本地记录**，见 §6 | 桌面端二进制确认无服务端读取路径 |
| **已购单曲** | `ucenter/pay/album/music/purchasedList` | `pn`、`rn` | 路径+参数+信封全静态确认（元素是普通曲目对象，无订单字段） |
| **已购专辑** | `ucenter/pay/album/purchasedList` | `pn`、`rn` | 同上（`purchasedList2` 是桌面端变体，形状未知，记作备选） |
| **收藏的专辑** | `service/collect/2` 或 `service/collect/6/list` | `userId`、`fromUid`、`pn`、`rn` | **端点与数组键都未实测**，见 §3.2 |
| **创建的歌单** | `service/playlist/userCreate` | `userId` | 路径+参数静态确认 |
| ↳ 曲目 | `service/playlist/{id}/musicList` | `source=5`、`pn`、`rn` | 静态确认 |

### 3.1 列表信封（第二轮静态提取，已是事实）

`data` 的形态与数组键**已经全部读出**（证据见 findings/06 §11），可直接写 DTO，不必猜：

| 端点 | `data` 形态 | 数组键 | 总数键 |
| --- | --- | --- | --- |
| `service/playlist/userCreate` | 对象 | `playLists` | `total` |
| `service/collect/4/list`（收藏歌单） | 对象 | `playLists` | `total` |
| `service/playlist/{id}/musicList` | 对象 | `list` | `total` |
| `service/playlist/fond` | **单个对象** | — | — |
| `ucenter/pay/album/music/purchasedList`（已购单曲） | 对象 | `musicList` | `size` |
| `ucenter/pay/album/purchasedList`（已购专辑） | 对象 | `albumList` | `size` |

**已购取哪条路径：取 Android 那一对，不用 `purchasedList2`。**
`purchasedList2` 全树只出现在桌面端二进制里，**移动端不存在，响应形状无从得知**；
而 `music/purchasedList` + `purchasedList` 这一对是端到端静态追通的（路径、参数、数组键、总数键全有）。
`purchasedList2` 记成备选，不是首选。

### 3.2 两处必须写进代码注释的不确定性

1. **收藏专辑：已解决（2026-10-01 实测）—— 走移动端的收藏歌单端点。**

   | 端点 | 实测 | 判断 |
   | --- | --- | --- |
   | `service/collect/2` | **HTTP 404** | 不存在（桌面端二进制里那条是**字节粘连的假阳性**） |
   | `service/collect/6/list` | 200，但 `data` 是空对象 `{}` | 弃用 |
   | **`service/collect/4/list`** | **200，`{playLists, total}`** | **采用** |

   依据是**用户实测**：移动端收藏歌单接口的内容与官方桌面端「收藏专辑」的效果一致。

   条目用 `AlbumDto` 接，两种形状都认（歌单归一化形状 `id` / 专辑形状 `albumId`）。
2. **`rn` 的语义存疑**。本轮静态读出来像「起始行号」，与 API 族惯例（页大小）不符。
   **先按惯例当页大小**，翻页时观察是否退化成重复数据。

> 第三轮的 `Dto/README.md` 规矩（DTO 照 fixture 写、不照文档写）在这里能落实一半：
> **信封字段名来自解析代码**（比文档强），**条目形状来自本机缓存**（见 §3.3），
> 但**没有一个端到端捕获的完整响应**。fixture 的构造方式要在测试里写明来源。

### 3.3 fixture 从哪来（零请求）

本机官方客户端的缓存里就有**真实响应样本**：

| 来源 | 内容 | 用途 |
| --- | --- | --- |
| `songDB.db` 的 `hist_song` 表 | 200 条曲目对象（`json` 列） | 曲目 DTO 的条目级 fixture |
| `<uid>_self_collect_songlist.db` 的 `song_list` 表 | 296 条歌单元数据对象 | 歌单元数据 DTO 的条目级 fixture |
| `hist/histPlaylist.json` | 12 条歌单元数据 | 同上（已确认字段与文档 §6.2 完全一致） |

**取样时必须脱敏**：`creatorIcon`（第三方头像 URL）、`creatorId`、`uid`、任何账号标识。
曲名/歌手/专辑可保留。

---

## 4. 导航模型

现在的 `INavigationService` 只有一条实例栈。侧栏要求「根（tab）+ 详情」两层语义。

### 4.1 根的判定

```
根 = 栈底那个页面（栈为空时就是当前页）
```

理由：从「我喜欢的」点进一个歌单详情，侧栏应**继续高亮「我喜欢的」** ——
而详情页是压在栈上的，栈底仍是「我喜欢的」。

### 4.2 侧栏点击 = 换根，不是压栈

新增一个语义：**点侧栏项走「换根」**。

- 目标根页**已在栈里** → **保留那个实例**（搜索结果、滚动位置都不丢），清空历史并将它设为唯一根页
- 否则 → 清空历史 + 新建根页

**为什么不能沿用 `Navigate` 的压栈语义**：在「我喜欢的 → 歌单详情」里点侧栏的「创建的歌单」，
压栈会得到 `[我喜欢的, 歌单详情, 创建的歌单]`，栈底仍是「我喜欢的」，侧栏高亮就错了。

`Reset<TPage>()` 保留原义（登录/登出这类「不该退回去」的跳转），不用于侧栏。

### 4.3 接口增补

```csharp
// INavigationService 新增
void NavigateRoot<TPage>() where TPage : Page;   // 侧栏点击：换根
Page? Root { get; }                              // 供侧栏同步高亮
bool CanGoBack { get; }                          // 标题栏返回按钮的可用状态
event EventHandler<Page>? Navigated;             // 当前显示的页面实例改变后触发
```

侧栏订阅 `Navigated`，用 `Root` 反查应高亮哪一项。
**高亮的映射表由外壳持有**（页面类型 → `NavigationViewItem`），不放进导航服务 ——
导航服务不该知道侧栏长什么样。
2026-10-02 增加 `INotifyPropertyChanged`，每次导航状态更新都通知 `CanGoBack`。
换根复用当前页时只清空历史，不触发 `Navigated` 或页面生命周期，但返回按钮仍能及时禁用。

### 4.4 动态项的选中

「创建的歌单」下的每个歌单是 `SidebarPlaylistList` 里的一行，点它 → `NavigateRoot<PlaylistDetailPage>()`
（2026-10-03 之前是动态 `NavigationViewItem`，改成 `PaneFooter` 里的列表，理由见 §5）。
因为根页是**每歌单一个实例**，详情页要能带参数构造（`playlistId`），
所以 `NavigateRoot<TPage>()` 需要支持**传入已构造好的实例**：

```csharp
void NavigateRoot(Page page);
```

DI 注册的 `PlaylistDetailPage` 是带参构造，外壳自己 `new` 出来再交给导航服务。
这也是 `Frame.Navigate` 那套类型解析用不了的同一个理由（见 `INavigationService` 的注释）。

---

## 5. 外壳改造

```
Window
└── NavigationView                      PaneDisplayMode=Left
    │                                   IsBackButtonVisible=Collapsed   ← 返回栈是自己的
    │                                   IsSettingsVisible=False
    ├── MenuItems
    │     发现 / 排行榜 / 乐库 / 我的音乐(Header) + 我喜欢/最近播放/收藏的专辑/收藏的歌单
    │     + 紧凑栏专用的「创建的歌单」图标（展开时收起）
    ├── PaneFooter                     ← 「创建的歌单」：Header + 自己内滚的列表
    └── Content
        └── Row 0: ContentControl(PageHost)     ← INavigationService 的宿主
            （播放条不在这里：它常驻根 Grid 第 2 行、通栏，见 ui-refresh.md §10）
```

- **满高**：`NavigationView` 铺满第 1 行，播放条在它下方通栏 → 侧栏到播放条上沿为止，不到窗口底。
- **「创建的歌单」在 `PaneFooter` 而不是 `MenuItems`**：`MenuItems` 整段共用一个滚动区，
  歌单一多会把整条侧栏顶出常驻滚动条。`PaneFooter` 在模板里是滚动区之外的 `Auto` 行，
  这一段自己滚、侧栏整体不动；高度由外壳按剩余空间算（见 [`ui-refresh.md`](ui-refresh.md) §19）。
  收起成 48 DIP 图标轨时这一段整段藏起来，换成轨上一颗图标 + 弹层。
- **返回按钮**：`IsBackButtonVisible=Collapsed`，用自己的返回栈。2026-10-02 改为标题栏搜索框
  左侧的统一返回按钮，32×32 DIP、间距 8 DIP，无历史时禁用；移除详情页的独立按钮。
  歌词页保留原来的 ↓ 收起按钮作为例外。返回后的焦点处理见 [`search.md`](search.md)。
- **账号信息**：从 `SearchPage` 顶部挪进 `AccountViewModel`（播放条与标题栏共享），
  入口最终落在标题栏右侧，不再占 `PaneFooter` —— 页脚现在归「创建的歌单」。
- **搜索**：`SearchPage` 是独立结果页，侧栏不列它。提交标题栏搜索框时用 `Navigate<SearchPage>()`
  压栈，保留进入前的页面，可用统一按钮逐级返回；侧栏高亮继续跟随栈底根页。
  点侧栏任一项仍清空历史并换根。
- 启动落点由 `Reset<SearchPage>()` 改成 `NavigateRoot<FavoritesPage>()`（即「我喜欢的」）。

---

## 6. 最近播放：本地记录

**为什么不用服务端接口**：官方桌面端**不用**（`reverse/pc/app.so` 里 `playlist/history`、
`history/page`、`playdata` 全 0 命中），历史落在本地 `songDB.db` 的 `hist_song` 表，
写入点是 `addHistorySong` 那条 SQL。移动端虽然有 `playlist/history/page`，
但它是移动端专属路径，且移动端自己也是「本地 + 网络」双轨（`playHistoryWithoutNet` 日志）。
桌面头能不能对上服务端历史，**静态无法回答**，而用户已明确不走请求探测。

**已落地（2026-09-30）**：

| 项 | 决定 |
| --- | --- |
| 存储 | `%LOCALAPPDATA%\Bodian\history.json`，**不用 SQLite** —— 数据量小、无查询需求，JSON 够 |
| 记什么 | `musicId` + `playedAt` + **曲目快照**（标题/歌手/专辑/封面/时长/**可播档位**）。存档位是为了能再次点播 —— 取音源要按档位算 `br` |
| 上限 | 500 条，超出丢最旧的 |
| 写入时机 | 订阅 `PlaybackCoordinator.Started`。**被拒绝的曲目没播过、不进历史；试听确实播了、该进** —— 那个事件的语义正好是这个分界 |
| 去重 | 同一 `musicId` 再次播放 → 提到最前，不新增条目 |
| 加密 | **不加密**。它不是凭据，也不含 `uid`；DPAPI 那套只服务 token |
| 与官方互通 | **不互通**。官方存在自己的 SQLite 里，本项目不读官方文件 |
| 写入方式 | **先写 `.tmp` 再原子替换**。直接写目标文件时，写到一半退出会留下半截 JSON，下次启动整段历史全丢 |
| 读失败 | 当空历史继续跑，**且不删坏文件** —— 一页空着比应用起不来好，文件留着还能人工查 |

**已知限制**：本客户端第一次播放之前的历史不会出现；与官方客户端的历史不互通。

新增文件：`Core/Models/PlayHistoryEntry.cs`、`Core/Services/Abstractions/IPlayHistoryStore.cs`、
`Core/Services/Implementations/JsonPlayHistoryStore.cs`、`Core/Services/PlayHistoryJsonContext.cs`、
`WinUI/ViewModels/RecentViewModel.cs`、`WinUI/Views/RecentPage.xaml(.cs)`。

**测试 384 → 401。** 其中两条值得单独说：枚举**写成字符串**（数字会随枚举重排而静默变成错的档位），
以及**损坏文件不抛不删**。

---

## 7. 实施步骤与验收

按「不确定性从大到小」排，每一步都能独立验收。

| # | 步骤 | 验收 | 状态 |
| --- | --- | --- | --- |
| 1 | **Core：歌单族的 DTO + 门面方法** | 用 §3.3 的本地样本当 fixture，单测跑通解析与分页游标 | ✅ 完成（2026-09-30） |
| 2 | **Core：导航栈语义** | 单测覆盖「换根 / 实例复用 / 栈底即根」三条 | ✅ 完成（2026-09-30） |
| 3 | **WinUI：`NavigationView` 外壳 + 账号下移 `PaneFooter`** | 侧栏可折叠；播放条在右侧不通栏；账号信息正常；启动落在首页 | ✅ 完成（2026-09-30） |
| 4 | **WinUI：我喜欢的页**（唯一全链路已验证项） | 出真实曲目、可点播、可翻页 | ✅ 完成（2026-09-30） |
| 5 | **WinUI：创建的歌单 + 歌单详情** | 列出全部自建歌单；点进详情出曲目并点播 | ✅ 完成（2026-09-30） |
| 6 | **Core + WinUI：已购 + 收藏的专辑** | 出数据即通过；**返回空/解析失败时界面必须明确说明**（见 §3.2） | ✅ 完成（2026-09-30）；**已购页与入口于 2026-10-03 移除**（见文首） |
| 7 | **最近播放（本地记录）** | 播放几首后列表按时间倒序出现；重启不丢；同曲重复播放提到最前 | ✅ 完成（2026-09-30） |
| 8 | **发现页**（最后一刀） | 见下 | ✅ 完成（2026-10-01） |

### 追加：排行榜独立成 tab（2026-10-01）

**侧栏多一项「排行榜」**（在「发现」下面）。数据来自两个端点：

| 用途 | 端点 | 实测 |
| --- | --- | --- |
| 榜单首页 | `service/home/bangNew` | **无参**；`data` 是**裸数组**，5 组共 22 个榜；每组 `{moduleName, moduleId, bangList}` |
| 单个榜 | `service/bang/{id}/musics` | 分页（`pn` 从 1）；**`total` 是 100**；默认回 20 首，`rn=10` 回 10 首 |

**三个必须记住的点：**

1. **`bangNew` 的 `data` 是裸数组**，不是 `service/home/index` 那种 `{moduleList: [...]}` 包一层。
   读错会得到「一个榜都没有」。
2. **「H5榜单」那组里的条目没有 `id`**（是外部 H5 链接）。映射时按 `id > 0` 滤掉，
   滤完那组就空了，所以还要再滤一次空组 —— 否则侧栏上会出现一个点进去什么都没有的分组。
   实测 5 组 22 个榜 → **4 组 20 个榜**。
3. **每个榜的预览只有 5 首，完整榜单是 100 首。**

**这一页整页只要 1 次请求**（`bangNew` 一次带回所有分组与预览），
**没有懒加载、也不分页**。榜里显示的就是那 5 首预览，
完整榜单按榜点进详情页取。

> **踩过的坑（记下来别再犯）**：最初的方案是「每个榜再请求一次 `rn=10` 拿前十首」，
> 那要 **20 次请求**才填满一页 —— 既违背「别把服务当压测」的底线，又因为守卫写错而整个卡住。
>
> 具体错在：`LoadMoreAsync` 的守卫是 `if (IsBusy || !HasMore) return;`，
> 而 `HasMore` **初始是 `false`**、只在加载结尾才赋值 —— 于是第一次调用直接返回，
> **内容一次都没加载过**。三个症状：只见榜名、状态停在「正在加载排行榜…」、
> 「更多」按钮的可见性绑在 `HasMore` 上所以永远不出现（看起来就是点不进去）。
>
> 改用预览之后整类 bug 消失。**守卫不要依赖「由被守卫的那个操作去赋值」的状态。**

**「更多」按钮**在预览非空时就出现（预览是被服务端截断过的，非空就一定有更多；
不用响应里的 `total` 判断，那个字段这一族并不总是给）。
点它——或者**点榜头整行**——压栈进 `BangDetailPage`（完整 100 首、可翻页）。
**用压栈不用换根**：榜详情是压在「排行榜」上面的详情页，侧栏该继续高亮「排行榜」。

**发现页里的排行榜模块已删除**（2026-10-01，按用户要求）。

排行榜既然是侧栏的独立一项、数据源是更全的 `home/bangNew`（带分组），
放在发现页里就是重复入口。所以：

- `HomeModule.IsSupported` 去掉了 type 2，**发现页不再请求它、也不再渲染它**；
- 随之下线的还有为它写的那一套：`ReadBangListAsync`、`HomeBangListPayload`、`HomeBangDto`、
  以及 `HomeSection.SourceId` + `HomeSectionView` 的「更多」按钮 + `DiscoverPage` 的
  「更多」处理。**这些改动之后就没有消费方了**，按本仓库「没有消费方的代码只是噪音」
  的规矩一并删掉，不留着「以后可能用得上」。

**新文件**：`Core/Models/Bang.cs`、`Core/Api/Dto/BangDto.cs`、
`WinUI/ViewModels/BangListViewModel.cs`、`BangDetailViewModel.cs`、`RankedTrack.cs`、`BangVisuals.cs`、
`WinUI/Views/BangListPage.xaml(.cs)`、`BangDetailPage.xaml(.cs)`。
**测试 431 → 444。**

### 追加：专辑可点 + 乐库分类（2026-10-01）

#### 专辑详情

**已购音乐 / 收藏的专辑里的专辑行现在可点**，进 `AlbumDetailPage`（简介 + 曲目）。

| 用途 | 端点 | 实测 |
| --- | --- | --- |
| 专辑详情 | `service/album/{id}` | **无 query**，`{ albumInfo: { name, pic, artist, showtime, info, musicCount, collectedCnt, … } }` |
| 专辑曲目 | `service/album/music/{id}` | 可分页（**`pn` 从 1**，2026-10-03 更正，见 [`bodian-api-reference.md`](bodian-api-reference.md) §1.6），`{ total, rn, resultList, pn }` |

**两个要点：**

1. **详情里不含曲目** —— 页面要发两次请求。列表页点进来时已经知道名字与封面，
   所以头部先显示得出来，简介等第二次请求。**简介实测是整篇专辑企划（几千字）**，
   用 `Expander` + 内层 `ScrollViewer` 折叠，否则会把曲目列表顶到屏幕外。
2. **曲目的数组键是 `resultList`** —— 与搜索同键、**与歌单的 `list` 不同**。
   读错会得到空列表而不是异常，界面上表现为「这张专辑是空的」。

#### 乐库（分类歌单）

侧栏**还没有加「乐库」这一项**（见下），Core 与页面已就绪：

| 用途 | 端点 | 实测 |
| --- | --- | --- |
| 分类树 | `service/category/list` | **无参**；6 组（主题/流派/语言/心情/场景/年代），每组带子分类 |
| 分类歌单 | `service/category/{id}/playlist` | 可分页（`pn` 从 1）；`{ playLists, total }`（某分类 2707 个歌单） |

**三个要点：**

1. **只有子分类的 id 能取歌单**（形如 77「网红」），顶层组的 id（形如 67「主题」）不是分类 id。
2. **响应里的 `customCategory`（推荐 / 歌单）不能用**：拿它的 id（1 / 2）去打歌单接口返回空，
   它在客户端里映射到别的端点。本项目有意不建模。
3. **信封与收藏歌单相同**（`playLists` + `total`，条目 `sourceType: 4`），
   所以复用了 `PlaylistListPayload` 与歌单详情页 —— 没有为它写新 DTO。

**测试 452 → 468。**

### 追加：个性化歌单可点进 AI 歌单（2026-10-01）

**发现页的「个性化歌单」（type 4）里，每一组其实是一个 AI 歌单** ——
模块只给 3 首预览，点标题能取到完整的（实测 **30 首**）。

| 用途 | 端点 | 实测 |
| --- | --- | --- |
| 模块预览 | `service/home/module?moduleId=1` | 4 组，每组 `{id, title, songs:[3 首]}` |
| 完整歌单 | `service/home/aiPlaylistDetail?index=N` | `{title, subTitle, bigTitle, musicList:[30 首]}`，**无分页** |

**关键发现：分组的 `id` 就是 `aiPlaylistDetail` 的 `index`。**
实测 `index=0` 返回的 `title` 与那一组的标题**逐字相同**（都是「潮趣日推」），
而它的 `songs` 正是那 30 首的前 3 首。

**一个容易写错的地方：不能靠「`id` 大于 0」判断这一组可不可点。**
type 11（你的主题歌单）的分组**压根没有 id 字段**，反序列化后是 `0`，
而 `index=0` 恰好是合法的 AI 序号 —— 判错的话，点「你的主题歌单」里任意一组
都会打开「潮趣日推」。所以**按模块类型判断**（只有 type 4 填 `AiIndex`），有专门一条测试守着。

**界面**：`HomeSectionView` 的标题在可点时用 `HyperlinkButton`、不可点时用 `TextBlock`
（**两套控件靠可见性切换，不是改按钮的可点状态** —— 纯文本配可点样式会让人以为坏了）。
点它压栈进 `AiPlaylistPage`，侧栏继续高亮「发现」。

**新文件**：`Core/Models/Home/AiPlaylist.cs`、`Core/Api/Dto/AiPlaylistPayload`、
`WinUI/ViewModels/AiPlaylistViewModel.cs`、`WinUI/Views/AiPlaylistPage.xaml(.cs)`。
**测试 445 → 451。**

> 顺带说一句：上一轮我把「组的 `SourceId` + 更多按钮」整套删了（因为排行榜模块下线），
> 这一轮又给 AI 歌单加回了同形的机制（`AiIndex` + 标题可点）。
> 两次都不是误判 —— 第一次确实没有消费方了，第二次有了新的、**不同语义**的消费方
> （那次是榜 id，这次是序号）。但值得记一笔：**删掉的机制可能在下一轮以别的形态回来。**

### 第 8 步：发现页（2026-10-01）

**架构是两段式**：`service/home/index` 只给布局（实测 12 个模块，只有 id/type/name），
每个模块的内容要再按 `service/home/module?moduleId=N` 单独拉。

**12 个模块里本项目渲染 6 个**（类型 2/3/4/5/10/11）；其余是轮播图、广告、波点实验室、
音乐日历、数字专辑馆、听点不一样的视频 —— 形状复杂且不是内容流。
**不支持的模块连请求都不发**（有测试守着）。

**关键陷阱：`songList` 这个键在 type 4/11 里是曲目分组、在 type 5 里是歌单卡片。**
所以**按 `type` 分派解析类型**，而不是按键名。四种信封各有自己的 DTO
（`HomeMusicListPayload` / `HomeSongGroupsPayload` / `HomeBangListPayload` / `HomePlaylistCardsPayload`），
归一成统一的 `HomeFeed` → `HomeSection` → `HomeCard`。

**懒加载**：首屏 4 个模块，滚到底再拉下一批（实测只有 6 个可渲染模块，
所以实际上会很快拉完 —— 分批的意义在布局变多时才显现）。单个模块失败只跳过它，
不让整页空白。

**fixture 是真实捕获的**（探针 `--save` 存的原始响应），这是曲库之外第一批端到端样本。

**新发现的一条未验证项**：发现页里的歌单 `sourceType` 实测是 **13**，不是文档说公开集合的 `4`。
本项目**原样透传**它（归一化成 4 会让取曲目填错 source、静默拿到空列表），
但 `source=13` 能否被 `musicList` 接受**还没实测**。

**第 1 步只做了歌单族**（`Playlist` / `PlaylistDto` / `userCreate` / `fond` / `playlist/{id}/musicList`），
**已购与收藏专辑挪到第 6 步**：它们要引入专辑模型，且证据最弱（见 §3.2），
和它们的页面放在一起做才不用先写一堆没有消费方的类型。

第 1、2 步的实际落地：

| 新增/改动 | 内容 |
| --- | --- |
| `Core/Models/Playlist.cs` | 领域模型，只映射有消费方的四个字段 |
| `Core/Api/Dto/PlaylistDto.cs`、`PlaylistPayloads.cs` | 条目与两个信封 |
| `Core/Api/BodianApi.cs` | `GetCreatedPlaylistsAsync` / `GetLikedPlaylistAsync` / `GetPlaylistTracksAsync` |
| `Core/Navigation/NavigationStack.cs` | 栈语义（见下） |
| `WinUI/Services/INavigationService.cs`、`NavigationService.cs` | 加 `NavigateRoot` ×2、`Root`、`Current`、`Navigated`、`INavigationIdentity` |
| `fixtures/playlists-userCreate.json`、`playlist-fond.json`、`playlist-tracks.json` | 脱敏 fixture，来源见文首说明 |

**测试 357 → 384，0 跳过；构建 0 警告。**

第 3–5 步的实际落地：

| 新增/改动 | 内容 |
| --- | --- |
| `WinUI/MainWindow.xaml(.cs)` | 外壳换成 `NavigationView`；侧栏项与「创建的歌单」一段（2026-10-03 起落在 `PaneFooter`，见 §5）；顶部常驻搜索框 |
| `WinUI/Controls/SidebarPlaylistList.xaml(.cs)` | 「创建的歌单」列表：32 DIP 封面 + 曲目数、自带内滚，页脚与紧凑栏弹层共用 |
| `WinUI/ViewModels/AccountViewModel.cs` | 账号卡片。<b>属性必须自己发通知</b>：侧栏在登录之前就构造好了，OneTime 绑定会永远停在空白 |
| `WinUI/ViewModels/SidebarViewModel.cs` | 「创建的歌单」那一段；失败写 `ErrorText` 而不抛 |
| `WinUI/ViewModels/PlaylistTracksViewModel.cs` | 「一个歌单的曲目列表」的共同部分（分页/点播/状态），子类只回答「展示哪个歌单」 |
| `WinUI/ViewModels/{Favorites,PlaylistDetail,Search}ViewModel.cs` | 三页 |
| `WinUI/Views/{Favorites,PlaylistDetail}Page.xaml(.cs)` | 两个新根页 |
| `WinUI/Controls/TrackListView.xaml(.cs)` | 曲目列表控件，当时三个页面共用同一套行模板（原先只搜索页有）。**2026-10-04 起已铺到八个页面**，序号列也多了一态（多选复选框），见 [`track-list-toolbar.md`](track-list-toolbar.md) |
| `WinUI/Services/INavigationService.cs` | 见 §4 |

**搜索入口的落地方式**：搜索框与单例 `SearchViewModel` 共用状态，避免「框里是 A、结果是 B」。
入口最初放在内容区顶部，2026-10-02 已移到主窗口标题栏，左侧增加统一返回按钮。
搜索页不再带搜索框，也不作为侧栏项；提交搜索时压栈，允许返回进入前的页面。
未登录时搜索框、返回按钮与侧栏一起隐藏。

第 6 步的实际落地：

| 新增/改动 | 内容 |
| --- | --- |
| `Core/Models/Album.cs`、`Core/Api/Dto/AlbumDto.cs`、`AlbumPayloads.cs` | 专辑条目与三套信封 |
| `Core/Api/BodianApi.cs` | `GetPurchasedSinglesAsync` / `GetPurchasedAlbumsAsync` / `GetCollectedAlbumsAsync` |
| `WinUI/ViewModels/PagedList.cs` | 通用的分页列表（游标 + 状态 + 两个命令），三个列表共用 |
| `WinUI/ViewModels/CollectedAlbumsViewModel.cs`、`Views/CollectedAlbumsPage.xaml(.cs)` | 收藏的专辑页（已购页于 2026-10-03 随入口一并删除） |
| `WinUI/Controls/AlbumListView.xaml(.cs)` | 专辑列表控件，**行不可点**（没有详情页，让行看起来可点却什么都不发生更糟） |
| `fixtures/purchased-singles.json`、`purchased-albums.json`、`collected-albums.json` | 见上表的风险行 |

**「已购音乐」做成上下两节**（单曲 / 专辑）而不是页签：少一套选中状态，两个列表都一眼能看见。
官方桌面端的「已购」也是这么组织的（`purchased_music_page` 下辖两个视图）。
（2026-10-03：这一页与其侧栏入口已整体移除，见文首；这段布局理由只在页面被恢复时才需要参考。）

**测试 401 → 413。**

#### 实施中发现的两处问题（都已修）

1. **设计错误**：`NavigateRoot` 原设计是「截断到目标所在位置」（见下）。
2. **重复请求**：启动时 `userCreate` 被发了**两次** —— `TryRestorePersistedSession()`
   会同步触发 `AccountChanged`（那里拉一次），`OnHostLoaded` 又拉了一次。
   实测日志里能直接看到同一秒两条「自建歌单 0 个」。现在侧栏只由 `AccountChanged` 负责，
   `OnHostLoaded` 只决定落在哪一页。

`NavigateRoot` 原设计是「截断到目标所在位置」。这是错的：栈底即根的规则下，
那会让目标**下面**的页面继续当根 —— 反例是「创建的歌单 → 某个歌单详情」之后点侧栏里那个歌单，
截断会把「创建的歌单」留在栈底，侧栏高亮的仍是列表页。
**改成「目标成为新的根、历史一律清空」**（实例仍复用），代价是换根后不可回退，
即标签页的行为。测试里有专门守这条的用例。

**第 8 步为什么最后**：`service/home/index` 返回的是**块列表**，
7 种块类型（`disc_banner` / `disc_song` / `disc_songlist` / `disc_ai_songlist` /
`disc_ranking` / `disc_lab` / `disc_big_ad`）的**字段名还没逐个提取**，
而且不知道服务端实际下发哪几种。先把前面 7 步做完，再按块文件里的 `fromJson` 逐块对齐。

> 第 8 步的第一次调用由**应用自身**发出（不是探针）—— 这与「不通过探测发请求猜参数」不冲突：
> 参数已经静态确定了，这里要看的是**响应长什么样**，那是运行期事实，无法靠解包获得。

---

## 8. 未验证项与风险

| 项 | 风险 | 兜底 |
| --- | --- | --- |
| 收藏专辑是**桌面端独有功能**（`favAlbumList` / `sourceType=6` 均已从二进制定下） | 安卓端没有这个功能，将来若要跟移动端对齐得另说 | 空状态把「没收藏过」与「这个查询没成功」都写出来；另留「重新加载」按钮 |
| 专辑条目**没有真实 fixture** | 键名判错时静默得到空字段 | 已购单曲的 fixture 条目是真的；两个专辑 fixture 的条目是照解包键构造的，测试注释里写明了这一点 |
| 已购只有移动端的形状证据 | 桌面端可能返回别的信封 | 先按 `musicList` / `albumList` 解析；失败时退一步试 `purchasedList2`，并把实际响应结构记进日志 |
| 分页总数键不统一（`total` vs `size`） | 翻页判断出错 | 每个 payload 显式声明自己的总数键，不共用基类字段 —— 实测这一族连键名都不统一 |
| `rn` 语义存疑 | 翻页重复 | 去重 + 观察；退化成重复就改用 `pn` 翻页 |
| 桌面头对 `service/home/*` 的接受度 | 发现页整体拿不到 | 发现页独立成一刀，失败不影响前 7 步 |
| **账号风控** | 打第三方服务 | 侧栏默认**懒加载**（点到哪项才请求哪项），不做启动时全量预取；沿用现有节流 |

**明确不做**：启动时并发拉取全部六个入口。六项一起发是六倍流量，且大多数用户只点一两项。

---

## 9. 与既有文档的关系

| 文档 | 要改什么 |
| --- | --- |
| `bodian-api-reference.md` | §4.6 的已购路径补 `/music/`（**已购单曲**是 `album/music/purchasedList`，`album/purchasedList` 是已购专辑）；§2.3 的 `collect/6/list` 标注「第三方来源，静态未命中」；§8 把「`service/collect/2` 的语义」保留为未解 |
| `bodian-api-inventory.md` | §8 的未解问题里，`playlist/history` 改为「移动端专属，桌面端为本地记录」 |
| `backlog.md` | P7 一节替换为本文件的步骤表 |
| `roadmap.md` | P7 一节的验收标准补上「侧栏」 |
