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
| `Api/Dto/CollectionPayloads.cs` | `CollectedPlaylistsPayload` / `PlaylistInfoDto`（`collectTime` 在这里）/ `FollowedArtistsPayload` |
| `Api/Endpoints.cs` | `PlaylistInfo(id)`、`CollectSourcePlaylistAlbum=4`、`CollectSourceArtist=7`、两个 `sourceType` 常量 |
| `Api/BodianApi.cs` | `GetCollectedPlaylistsAsync` / `GetFollowedArtistsAsync` / `IsPlaylistCollectedAsync` / `SetPlaylistCollectedAsync` / `SetArtistFollowedAsync` |
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
