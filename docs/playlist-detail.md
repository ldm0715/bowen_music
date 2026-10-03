# 歌单详情页头部

2026-10-03 实现。

**问题**：歌单详情页从做出来那天起就只有一个「歌单名 + N 首」的标题行加一个收藏按钮 ——
没有封面、没有播放量、没有创建者、没有简介，也没有「播放全部」。专辑详情页早就是完整头部了，
两个页面在同一个外壳里观感差着一档。同时收藏按钮对所有歌单都显示，自己创建的也挂着「收藏」。

**关键发现**：数据一直都在手边。`service/playlist/info/{id}?source=` 这个请求**本来就在发**
（为了拿收藏态），返回的是**扁平歌单对象**，15 个字段里只读了 `collectTime` 一个，其余全丢掉。
这一轮基本上是「把已经在手的数据用起来」，只多了一个分享链接和一段「播放全部」。

---

## 1. 接口

`GET service/playlist/info/{id}?source=<s>` —— 与文档
[`bodian-api-reference.md`](bodian-api-reference.md) §2.2 记的是同一条。要点：

| 项 | 结论 |
| --- | --- |
| 响应形状 | **扁平**歌单对象，没有 `playlistInfo` 之类的子键 |
| 实测字段 | `id name pic creatorId creatorName creatorIcon description praise musicCount isPrivate playNum lastPlayTime collectTime sourceType collectedCnt`（15 个） |
| `source` | **必填**。不带回 `-10 参数错误`；填错回 `code 200` + `data: {}`，**不报错** |
| 收藏判据 | `collectTime` 存在即已收藏（**不是 `isFond`**） |
| `praise` 与 `collectedCnt` | 实测样本里相等，大概率同一个数。映射只消费 `collectedCnt`，`praise` 当兜底 |
| 样本 | `fixtures/playlist-info-collected.json`、`fixtures/playlist-info-not-collected.json` |

**⚠️ `source` 只实测过 `4`。** `5`（自建歌单 /「我喜欢」）与 `13`（发现页）**没有实测证据**。
若这两个值上服务端也回 `data: {}`，头部会退化成「只有名字、封面、曲目数」——
创建者行与简介折叠区不出现，副标题只拼得出「N 首」。这是本页唯一没底的地方。

**歌单分享链接**（文档 §2.8 链接模板表）：

```
https://h5app.kuwo.cn/m/bodian/collection.html?uid={分享者uid}&playlistId={id}&source={source}
```

**`source` 是歌单这条模板独有的参数**，其余三条分享链接都没有它。必须原样传进详情页的那个值，
**不要写死 `4`** —— 猜错只会得到一条打不开的链接，没有任何一处会报错。
歌单同样**不做分享上报**（`shareSource` 只实测过 `0` 歌曲与 `1` 歌手）。

---

## 2. 实现分层

### Core（`src/Bodian.Core/`）

| 文件 | 内容 |
| --- | --- |
| `Api/Dto/PlaylistDto.cs` | 补 5 个字段：`playNum` / `praise` / `collectedCnt` / `lastPlayTime` / `collectTime` |
| `Api/Dto/CollectionPayloads.cs` | **删掉 `PlaylistInfoDto`** —— 它只有 7 个字段，是 `PlaylistDto` 的真子集，留着就是第二个要同步的 DTO |
| `Models/Playlist.cs` | 扩展详情专属字段（创建者、简介、播放数、收藏数、`CollectTime`）+ 派生 `HasDescription` / `HasCreator` / `IsCollected` |
| `Api/BodianApi.cs` | 新 `GetPlaylistInfoAsync`（**取代** `IsPlaylistCollectedAsync`）+ 私有 `MapPlaylistInfo` |
| `Api/IBodianApi.cs` | 同步换签名 |
| `Services/ShareLinks.cs` | 新 `BuildPlaylistLink(playlistId, uid, source)` |

三个值得记住的点：

- **`MapPlaylistInfo` 用 `with` 叠在 `MapPlaylist` 上**，不另写一份 —— 名字兜底、封面转 `Uri`、
  `SourceType` 不归一化这几条规矩只需维护一处。**`MapPlaylist` 本身没动**，列表路径继续只映射 5 个字段。
- **`Playlist` 上的详情字段默认值就是「这个来源没给」**，只有 `GetPlaylistInfoAsync` 会填。
  这与 `Album` 是同一个先例（它的 `ArtistText` / `ReleaseDate` / `Description` 也只有详情请求会填）。
  `CollectTime` 是**个人态**，方向上与其余字段不同 —— 留在这里的唯一理由是收藏态与元数据
  来自**同一个响应**，丢掉它再单独查一次是白跑一趟。**别把它当成歌单自身的属性去别处用。**
- **`GetPlaylistInfoAsync` 不再做登录校验。** 它取代的那个方法匿名时会直接返回 `null` 且不发请求；
  现在匿名**照发** —— 歌单详情是公开端点，创建者、简介、播放数匿名也该看得到，匿名时只是
  `collectTime` 不出现、`IsCollected` 落成 `false`，与「匿名不能收藏」自洽。

### UI（`src/Bodian.WinUI/`）

| 位置 | 改动 |
| --- | --- |
| `ViewModels/PlaylistTracksViewModel.cs` | 新 `LoadAllAsync`；`_coordinator` 改成 `protected Coordinator` 供子类排队列 |
| `ViewModels/PlaylistDetailViewModel.cs` | 详情加载、归属判定、播放全部、分享 |
| `Views/PlaylistDetailPage.xaml` | 3 行 Grid → 4 行，头部与专辑页同构 |
| `App.xaml.cs` | 工厂补 `BodianSession` 与 `IClipboardService` 两个依赖 |

**五个调用方一个都没改**（`MainWindow` / `CollectedPlaylistsPage` / `DiscoverPage` / `SearchPage`）——
它们传的 `source` 语义本来就是对的。

---

## 3. 三个判定

### 3.1 「是不是自己创建的歌单」

两条判据取**并集**：

1. 详情给了 `creatorId`，且等于当前账号 uid（`long.TryParse(_session.Uid, ...)` 后比 ——
   session 里 uid 是 `string`，DTO 里是 `long`）。这条覆盖「从搜索结果点进自己创建的公开歌单」，
   那条路径的 `source` 不是 5，只有 creatorId 认得出。
2. 调用方给的就是账号歌单的 `source`（`5`）。侧栏「创建的歌单」与账号歌单清单都传 5，且只有它们传 5。
   这条**不依赖任何网络结果**，详情拿不到时仍然成立。

**两条都不成立时按「别人的歌单」处理**（显示收藏按钮）。走到这一步的只可能是发现页 / 收藏列表 /
搜索点进来的 —— 那里藏掉按钮会让这个页面的主要用途没有入口，而唯一确定的「自己的」入口已被第 2 条兜住。

自己的歌单**不显示收藏按钮，也不显示创建者行**（那行只会写着「我」，是噪音）。
归属在**构造函数里先算一次**，否则自己的歌单会先闪一下收藏按钮再消失。

### 3.2 收藏成功后不回拉详情

写成功后只就地更新本地状态，**不重打请求**：

- 全局收藏数在界面上是缩写过的（`2w1+`），±1 根本看不出来，只有小歌单（十几收藏）才看得见；
- 服务端的累计计数多半有延迟，重拉一次要等一个来回还可能拿到旧数；
- 真正要立刻正确的是**按钮状态**，本地已经知道了。

所以 `SetCollectedAsync` 把本地那份计数 ±1 再重拼一次副标题就够（`BumpCollectedCount`）。

### 3.3 副标题的数字格式

`177 首 · 565w7+ 播放 · 2.1w 收藏`，缺的部分自动省掉。大数走
`CommentCountLabel.Format` —— 与歌手页头部的粉丝数（`484w8+ 粉丝`）**是同一套缩写**，
同一屏里出现两种「w 缩写」才是真的乱。

> 它的规则是「超过一万后取到千位并加 `+`」，所以 5657990 是 **`565w7+`** 而不是 `565.8w`。
> 需求当时举的例子是后者 —— 这里有意选了复用现有规则。要改成一位小数就得另写一个格式化器。

### 3.4 状态行的去留

页头原来那行状态文案（`StatusText`）**没有删**，只是加了显隐条件：

| 情况 | 显示 |
| --- | --- |
| 副标题拼出来了 | 隐藏 —— 副标题里已经有「N 首」，两个「177 首」是重复 |
| 详情没拿到（`data: {}` 或请求失败） | 显示 —— 这时它是「加载失败：…」与「这个歌单里还没有歌」**唯一的出口** |

---

## 4. 验证

- 构建：`dotnet build Bodian.sln -c Debug` → **0 错误**，1 个既有 `WMC1506` 警告（`AiPlaylistPage`）。
- 离线测试：**942** 项通过（原 923）。新增/改写：

| 测试 | 内容 |
| --- | --- |
| `PlaylistApiTests` +5 | 两份真机 fixture 的 15 字段映射；`playNum` / `collectedCnt` / `creatorId` / `IsCollected` 真假各一 |
| `CollectionApiTests` 改 3 增 2 | 收藏判据；**匿名照发请求**（与旧方法相反）；`data:{}` → `null`；`source` 原样进 query |
| `ShareLinkTests` +5 | 歌单分享模板、`source` 必须保留、uid 转义、匿名 uid、非正 id 抛 |
| `PlaylistTracksLoadAllTests`（新，7 项） | 基类 `LoadAllAsync`：拉到底 / 单页 / 撞上限 / 中途失败 / 首屏失败 / 空列表 / 非正上限 |

- **真机未做。** `source=5` / `13` 下 `playlist/info` 能否返回数据没有实测，见 §1 的警告。

### 界面验收清单（手动）

| 项 | 验收内容 |
| --- | --- |
| 自建歌单（侧栏） | 头部有封面与「N 首 · M 播放」；**没有收藏按钮**，也**没有创建者行** |
| 别人的歌单（收藏 / 发现页 / 搜索） | 有创建者行（头像 + 名字，**不可点是故意的** —— 本项目没有用户主页）；播放全部 / 收藏 / 分享三个按钮都在 |
| 已收藏的状态 | 按钮显示「已收藏」；点它弹确认框，确认后变回「收藏」，副标题的收藏数跟着变 |
| 播放全部 | 177 首的歌单连播 177 首，**不是只播首屏那 30 首** |
| 分享 | 剪贴板得到 `collection.html?uid=…&playlistId=…&source=…`；发现页进来的 `source` 是 13 而不是 4 |
| 简介 | 能展开；长文在 240 高内滚动，不把曲目列表顶出屏幕 |
| 加载失败 | 断网进来应看到「加载失败：…」，而不是一行空白 |
| 页脚 | 「重新加载」与忙碌环还在（那是歌单页比专辑页多的东西） |
| 窄窗口 | 标题与按钮组不挤压、不换行 |
| 更多按钮的位置 | 页头**右上角**一颗 `⋯`，**不与**「播放全部 / 收藏 / 分享」同排 |
| 更多菜单 | 两项都带图标（铅笔 / 垃圾桶）；**向左展开**，不越出窗口右边界 |
| 更多按钮的可见性 | 自建歌单上**有**；收藏 / 发现页 / 搜索点进来的别人的歌单上**没有** |
| 编辑 | 点「编辑」回一句「编辑歌单还没做」，不崩 |
| 删除 | 「更多」→ 删除 → 确认框默认落在「取消」；确认后侧栏那一行消失、页面切到「我喜欢的」 |
| 删除失败 | 断网时点删除 → 提示「删除失败，请稍后再试。」，页面留在原地 |

---

## 5. 没做的事

- **编辑歌单没做**：本页「更多」里留了入口，点了只回一句提示 —— `PUT service/playlist`
  至今未实测，连它的 `id` 键都是按数组槽序推断的，试错的代价是改坏别的歌单。
  （隐私歌单的新建入口在侧栏，见 [`create-playlist.md`](create-playlist.md)；删除见 §6。）
- **「我喜欢的」页没动**（`FavoritesPage` / `FavoritesViewModel`）：它是独立页面，不经过
  `PlaylistDetailPage`，要不要也加头部另说。
- **收藏写入的 `source` 存疑，本轮没改**：`SetCollectedAsync` 把歌单**来源**当收藏**类别**传，
  发现页点进来的歌单（`source == 13`）会写成 `source=13`，而收藏写入按 findings §2.3 应该是**类别**（歌单恒 4）。
  证据只有 `sourceType=4` 的样本，未定案。最小改法是让 `BodianApi.SetPlaylistCollectedAsync`
  忽略调用方传的 `source`、恒用 `Endpoints.CollectSourcePlaylistAlbum`（`Endpoints` 是 internal，VM 传不了 4）。

---

## 6. 「更多」菜单：编辑与删除（2026-10-03）

页头**右上角**多了一颗「更多」（`⋯` 图标，32×32、透明底、无边框），菜单两项：编辑、删除。
**只在自己创建的歌单上出现**（`IsOwnPlaylist`，正好是收藏按钮 `CanCollect` 的反面）——
两项都只对自己的歌单成立，摆在别人的歌单上是误导。

**为什么不摆进按钮排**：「播放全部 / 收藏 / 分享」是这一页的主要动作，而「更多」是低频、
且带破坏性的管理入口，并排站着会诱导误点。所以它落在页头网格新增的第三列（`Auto`）、
垂直顶对齐；没有它时那一列宽度为 0，不影响左边的布局。

菜单两项都带图标（编辑 `E70F`、删除 `E74D`）。`MenuFlyout` 用
**`Placement="BottomEdgeAlignedRight"` 右对齐展开**：按钮贴在右上角，默认的 `Bottom`
会让菜单从它的左边缘往右铺，直接顶出窗口右边。宽度也收了一道 —— presenter 模板里的
`MinWidth` 是按「最宽的菜单项」算的（给二级菜单对齐用），两项短文案用不上。

背景与圆角是显式给的（`ThemeFlyoutBackground` + `RadiusMd`）：`MenuFlyout` 的默认背景是
系统那套半透明亚克力，与项目里其余菜单（都走 `ThemeFlyoutBackground`）不是一个观感。

### 6.1 删之前先补了传输层

`DELETE` 这个动词本项目**从来没发过**：`BodianHttpVerb` 一直只有 `Get` / `Post`
（此前所有写操作恰好都是 POST），传输层里是一句 `Verb == Post ? Post : Get`。
所以这一轮先给它加了 `Delete`，把那句三元换成 switch。

`service/playlist` 的删除端点 2026-10-03 实测：`DELETE` + `{"playlistIds":[a,b]}` → `200`、`data: {}`，
**一次能删多个**（本项目一次只传一个）。

### 6.2 删完怎么收尾

三件事，都不是 ViewModel 自己干的：

| 事 | 谁做 | 为什么 |
| --- | --- | --- |
| 把那一行从侧栏摘掉 | `SidebarViewModel.RemovePlaylist` | 写请求已经成功，为此再拉一次列表是白跑 |
| 换根离开这一页 | `MainWindow` | 自建歌单详情是从侧栏换根进来的**根页**，`GoBack` 无处可去；落点与启动一致（「我喜欢的」） |
| 提示「已删除「X」」 | ViewModel 的 `_notice` | 与收藏成功同一个出口 |

前两件需要一个通道：动作在页面里发生，而「侧栏要跟着变、当前页该去哪」只有外壳知道。
做法与 `INoticeSink` 一致 —— 定义 `IPlaylistLibrarySink`，`MainWindow` 实现它，
DI 里注册成指向同一个外壳实例（`AddSingleton<IPlaylistLibrarySink>(sp => sp.GetRequiredService<MainWindow>())`）。
延迟解析，不会与「外壳持有页面工厂」构成循环。

### 6.3 确认框

删除不可逆（歌单连里面的曲目一起没），所以**先确认**，且默认按钮落在「取消」上 ——
与清空播放记录、取消收藏同一档：误触不该真把东西删掉。

「编辑」点了只回一句「编辑歌单还没做」。

### 6.4 验证

- 构建 0 错误（1 个既有 `WMC1506`）。
- 离线测试 **957** 项通过。新增 `PlaylistDeleteApiTests` 5 项：DELETE 打裸路径、body 的 id 数组、
  空回执算成功、非正 id 与未登录都不发请求、在途换号则拒绝。
- 界面部分已由用户手动验收（2026-10-03）：确认框、侧栏那一行消失、换根回「我喜欢的」。
