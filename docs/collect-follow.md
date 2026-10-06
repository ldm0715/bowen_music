# 收藏歌单与关注歌手

2026-10-03 实现。协议结论见
[`../reverse/findings/13-collect-playlist-follow-artist.md`](../reverse/findings/13-collect-playlist-follow-artist.md)。

**歌单 ≠ 专辑**：这是两个不同的概念。「收藏的专辑」入口早就有了，这一轮补的是**收藏歌单**，
外加把歌手页「关注」与专辑详情页「收藏」两个占位按钮接上。

---

## 1. 接口（全部实测定案）

| 能力 | 接口 |
| --- | --- |
| 收藏歌单 · 读 | `GET service/collect/4/list`（混合列表，按 `sourceType == 4` 过滤） |
| 收藏歌单 · 写 | `POST service/collect` `{source:4, sourceId:[id], op:1\|2, uid}`（**无 token**） |
| 收藏歌单 · 判据 | 详情 `GET service/playlist/info/{id}?source=` 的 **`collectTime`** 是否存在（**不是 `isFond`**） |
| 收藏专辑 · 写 | `POST service/collect` `{source:6, sourceId:[id], op:1\|2, uid}`（**`source` 是 `6` 不是 `4`**，无 token） |
| 收藏专辑 · 判据 | `GET service/collect/multipleState?source=6&sourceIds=<id>` 的 **`collect`** 布尔（**专辑详情里没有标志**） |
| 关注歌手 · 读 | `GET service/collect/7/list`（`artistList`） |
| 关注歌手 · 写 | `POST service/collect` `{source:7, sourceId:[id], op:1\|2, uid, token}`（**多 token**） |
| 关注歌手 · 判据 | 详情**没有** follow 字段，靠上面那份列表的本地集合 |

**`op`：`1` = 收藏/关注，`2` = 取消。** 静态反汇编（5.2.5 与 5.9.8 一致）与真机往返双证。

---

## 2. 实现分层

### Core（`src/Bodian.Core/`）

| 文件 | 内容 |
| --- | --- |
| `Api/Dto/Requests/CollectBody.cs` | `service/collect` 请求体。<b>`token` 用 `WhenWritingNull` 表达「不带」</b>——收藏歌单与关注歌手共用它 |
| `Api/Dto/CollectionPayloads.cs` | `CollectedPlaylistsPayload` / `FollowedArtistsPayload`（歌单详情原来另有 `PlaylistInfoDto`，**2026-10-03 已并进 `PlaylistDto`**，见 [`playlist-detail.md`](playlist-detail.md)） |
| `Api/Endpoints.cs` | `PlaylistInfo(id)`、`CollectSourcePlaylistAlbum=4`、`CollectSourceArtist=7`、两个 `sourceType` 常量 |
| `Api/BodianApi.cs` | `GetCollectedPlaylistsAsync` / `GetFollowedArtistsAsync` / `SetPlaylistCollectedAsync` / `SetArtistFollowedAsync`。**读收藏态的方法 2026-10-03 换成了 `GetPlaylistInfoAsync`**（一次拿元数据 + 收藏态） |
| `Services/Implementations/FollowedArtistsService.cs` | 歌手关注态的会话级集合（拉取时机与失效规则同 `LikedSongsService`） |

两个注意点：

- **收藏专辑的过滤写成了「排除 `sourceType == 4`」而不是「等于 6」** —— 专辑条目**可能不带
  `sourceType`**，写成 `== 6` 会把那种形状漏掉（`AlbumApiTests.CollectedAlbums_AcceptsAlbumShapedItems` 守它）。
- `FollowedArtistsService.SetFollowedAsync` **写之前先同步一次**：否则首次写之后本地集合是残缺的
  （只含刚写进去的那个 id），别的歌手会被误判成未关注。

### UI（`src/Bodian.WinUI/`）

| 位置 | 改动 |
| --- | --- |
| `MainWindow.xaml(.cs)` | 「我的音乐」组下新增侧栏项「收藏的歌单」 |
| `Views/CollectedPlaylistsPage.xaml(.cs)`、`ViewModels/CollectedPlaylistsViewModel.cs` | 新页面，与「收藏的专辑」同形 |
| `Views/FollowedArtistsPage.xaml(.cs)`、`ViewModels/FollowedArtistsViewModel.cs` | 关注的歌手页（**2026-10-06 新增**，见文末）。侧栏项同样加在「我的音乐」组下 |
| `Controls/PlaylistListView.xaml(.cs)` | 歌单行列表控件，镜像 `AlbumListView` |
| `Views/PlaylistDetailPage.xaml(.cs)`、`ViewModels/PlaylistDetailViewModel.cs` | 页头新增**收藏按钮（两态）** |
| `Views/ArtistDetailPage.xaml(.cs)`、`ViewModels/ArtistDetailViewModel.cs` | 「关注」由占位改成**两态** |
| `Views/AlbumDetailPage.xaml(.cs)`、`ViewModels/AlbumDetailViewModel.cs` | 「收藏」由占位改成**两态**（判据走 `multipleState`） |
| `Formats.cs` | `CollectLabel/Glyph`、`FollowLabel/Glyph`（状态是 `bool?`，`null` = 还没判定出来，按未收藏/未关注显示） |

---

## 3. 两个按钮的两态与「取消」确认

- 未收藏/未关注 → 显示「收藏」/「关注」；已收藏/已关注 → 显示「已收藏」/「已关注」（图标一并换）。
- 状态来自：歌单看详情 `collectTime`；歌手看 `IFollowedArtistsService` 的本地集合。
- **取消操作（取消收藏 / 取消关注）先弹一次确认框**，收藏/关注直接做 —— 两边代价不对称。
- 弹窗与 `RecentPage` 的清空记录**同一档**：`ContentDialog` + `PrimaryButtonText` 是动作、
  `CloseButtonText`「再想想」、`DefaultButton = Close`（误触不该真把东西删掉）。
  弹窗放页面 code-behind 而非 ViewModel：那是界面决策，且 `XamlRoot` 拿不到 ViewModel 里去。

---

## 4. 验证

- 构建：`dotnet build Bodian.sln -c Debug` → 0 错误 0 警告。
- 离线测试：`877` 项通过（新增 `CollectionApiTests` 18 项、`FollowedArtistsServiceTests` 8 项）。
  跑法见 README（MTP 模式下必须带 `--project`）。
- 真机：**歌单**与**专辑**各做过一次收藏→读回→取消→读回的往返（账号均已还原）。
  专辑那次同时定死了「写用 `source=6`、判据走 `multipleState`」。

### 界面验收清单（手动）

| 项 | 验收内容 |
| --- | --- |
| 侧栏 | 「我的音乐」下出现「收藏的歌单」，点击能进页面并高亮 |
| 收藏歌单页 | 只列歌单（**不混专辑**）；点一行进歌单详情；滚到底出现「没有更多了哦~」 |
| 收藏的专辑页 | 反过来：**不再混进歌单** |
| 歌单详情 | 已收藏的歌单点进去按钮显示「已收藏」；点它弹确认框，确认后变回「收藏」 |
| 歌单详情 | 未收藏的点「收藏」直接收藏，按钮变「已收藏」，不弹框 |
| 歌手页 | 已关注的歌手显示「已关注」；点它弹确认框，确认后变回「关注」 |
| 专辑详情 | 已收藏的专辑显示「已收藏」→ 弹框确认后变「收藏」；未收藏的直接收藏、不弹框 |
| 收藏的专辑页 | 在上面收藏一张专辑后，回到该页应能看到它 |
| 按钮图标 | 「已收藏」是实心星、「收藏」是空心星；「已关注」是对勾、「关注」是人形 —— **字形若显示成方块要报我** |
| 未登录 | 点「收藏」「关注」应有「登录后…」提示，不崩 |
| 窄窗口 | 歌单详情页头的标题与收藏按钮不挤压 |
| 关注的歌手页 | 侧栏「我的音乐」下出现「关注的歌手」，点击能进页面并高亮 |
| 关注的歌手页 | 卡片网格是圆形头像；点右上切换按钮变行列表，**回搜索页歌手页签形态应同步改变** |
| 关注的歌手页 | 从本页点进歌手详情、取消关注后返回，该歌手应从列表里消失 |
| 关注的歌手页 | 未登录或没有关注时显示「还没有关注的歌手。」，不崩 |


## 2026-10-06 卡片视图

「收藏的专辑」与「收藏的歌单」各自多了一种形态：封面卡片网格。两页原先分别只有
`AlbumListView` / `PlaylistListView` 那套行列表。

- **切换按钮在标题右侧、刷新按钮的右边**，两页同形：刷新在左、视图切换在右。
  两件事性质不同（一个重取数据、一个只管怎么排），但都作用在下面这一屏上，所以并在一处。
- 列表与网格**都留在可视树上、只切 `Visibility`**（同一份集合喂两者，任一时刻只有一个在布局里，
  所以不会一边滚一边被另一边再触发一次翻页 —— 见 [`list-paging.md`](list-paging.md)）。
- 卡片样式与搜索结果那三个页签**是同一套**（`CollectedAlbumCardTemplate` / `CollectedPlaylistCardTemplate`，
  与 `SearchPage.xaml` 里的两份、`LibraryCategoryPage.xaml` 的内联专辑卡是**复制关系**），
  尺寸口径见 [`search.md`](search.md) § 结果工具栏与视图切换。
- 卡片上**没有隐私锁**（列表行模板有那一颗）：这两页的歌单本来就是收藏来的，私密的根本收藏不了，
  挂一颗只会让人以为「这个是我的私密歌单」。
- 歌单的曲目数画在**封面右下角那个角标**里，与专辑卡同形（歌单不像专辑有「艺人」那一行可放，
  单独占一行会让这张卡比专辑卡空一块），歌单名因此放宽到两行。
- **开关是全局的**，与搜索结果共用同一个 `view-mode.json`：在这两页切了卡片，
  回搜索页也是卡片。理由与实现见 [`search.md`](search.md) §「行列表 / 封面卡片」这一个开关。

> ★★ **卡片模板只能各页一份，不能挪进共享资源字典。** 踩过：`x:Bind` 放进独立的
> `ResourceDictionary` 会构建失败（`WMC1119: This Xaml file must have a code-behind class`）；
> 给字典补一个 `x:Class` + 空 code-behind 之后**编译能过，但运行时绑定全都不会生效** ——
> 文字与图片是空的，`Visibility` 绑定不生效则退回 `Border` 默认的 `Visible`，
> 症状是一颗本该收起的角标一直显形。**构建通过不等于能跑**，这类问题只有真启动一次才看得见。
> 改用 `{Binding}` 也走不通：那写不出 `Formats.CoverSource(...)` 这类函数调用，
> 封面的 Uri→ImageSource 转换会整条丢掉。

验收：两页都切到卡片、点卡片分别进专辑 / 歌手详情、滚到底出现「没有更多了哦~」、
窗口拉宽拉窄每行格数跟着变；重启后仍停在上次选的形态；两页与搜索页的形态始终一致。

## 2026-10-06 关注歌手列表页

关注歌手此前只有「歌手详情页里的一颗两态按钮」，列表本身没有界面 —— 账号下拉里那个数字
（`followArtistCount`）还是用户元数据里的统计值，点不开。本次补上侧栏根页
「关注的歌手」（`FollowedArtistsPage`），歌手用搜索结果那套圆形头像卡（`PersonPicture`）展示。

三处值得记下来的决定：

- **一次性端点套进了 `PagedList`**。`GetFollowedArtistsAsync` 没有分页（官方固定 `rn = 400`，
  一次全量），而 `PagedList<T>` 要的是游标模型。做法是 fetch 委托拿完结果后调一次
  `cursor.Advance(0)` —— 那是游标约定里「到底」的写法（收到 0 条即 `Exhausted`）。
  **推进游标本来就是 fetch 委托的责任**（`PagedList` 自己从不调 `Advance`），不标到底的话
  `HasMore` 会停在 true，列表末尾的自动翻页会再拉一次并追加一份重复的。
  换来的是一整套现成状态：`ReloadCommand` / `IsBusy` / `StatusText`（「共 12 位歌手」）/
  `EmptyText` / `ShowEnd`，页脚与 `ProgressRing` 的绑定跟兄弟页完全同形。
  **代价**：`ShowRetry` 的前提是 `HasMore`，这里恒为 false，所以加载失败时页脚给不出「重试」，
  只能靠标题下的 `StatusText` 说明 + 右上那颗常驻的刷新按钮。没有为一个恒 false 的分支去改共享的 `PagedList`。
- **视图开关走全局单例**，不另起一套：与搜索页歌手页签、收藏的专辑/歌单页共用
  `ViewModeService.UseGrid`，页内按钮同样是「刷新在左、切换在右」。所以在这一页切了卡片，
  搜索页也跟着变 —— 这是刻意的，见 [`search.md`](search.md)。
- **从歌手详情返回时自动重拉**。别的根页都是 `EnsureLoadedAsync`（只首次加载），这一页不行：
  详情页就在本页下面一层，用户点进去取消关注再回来是极常见的一条路，不重拉就会留着一个
  已经取关的人。判据是 code-behind 里的一个 `_detailOpened` 标志 —— 根页实例被
  `NavigationService._rootPages` 缓存、反复显示的是同一个实例，所以标志一定还在。
  只在「确实去过详情」时才 `ReloadAsync`，普通来回切侧栏不会白白重取。

歌手卡与行列表两份 `DataTemplate` 同样**只留在页面里**，且与 `SearchPage.xaml` 的两份是
复制关系（`x:Bind` 进共享 `ResourceDictionary` 的坑见 [`search.md`](search.md)）。

验收：见 §4 的「关注的歌手页」四行。
