# 侧栏「创建的歌单」：新建歌单与刷新

2026-10-03 实现。协议结论见
[`bodian-api-reference.md`](bodian-api-reference.md) §2.4 与
[`../reverse/findings/11-share-playlist-crud.md`](../reverse/findings/11-share-playlist-crud.md) §2。

「创建的歌单」此前是只读的：展开态是 `PaneFooter` 里的列表，收起态是轨上一颗图标点开的浮层，
两处都只能看。这一轮补上**新建歌单（含隐私歌单）**与**刷新**，
并给隐私歌单加了封面右下角的**锁标记** —— 没有它，隐私歌单与普通歌单在列表里看不出区别。

顺带修掉一个缺陷：这一段原来的显隐判据是「有没有歌单」（`hasPlaylists`），
**零歌单的账号两处入口一起消失** —— 而那正是最需要「新建歌单」的时候。

---

## 1. 接口（POST / DELETE 已实测）

```
POST service/playlist   signed   新建歌单
     Body: {"name": "<名称>", "private": <bool>}
```

实测结论（2026-10-03，桌面 `win` 头 + 桌面签名）：

| 项 | 结果 |
| --- | --- |
| 回执 | `{"code":200,"msg":"success","data":{"id":100167230}}` —— **`data` 里只有 `id`** |
| `private` | 传 `true` → 读回 `isPrivate: 1`；传 `false` → `0` |
| 新歌单位置 | `service/playlist/userCreate` 的 `playLists` **第 0 位**（该列表按 id 降序） |
| 空名字 / 50 字名字 | 服务端**都照建**。空白只能客户端自己挡，长度不该设上限 |
| `DELETE service/playlist` | `{"playlistIds":[a,b]}` → 200、`data: {}`，支持批量（界面入口见 [`playlist-detail.md`](playlist-detail.md) §6） |

**回执只有 id** 这一点推翻了静态推断（原来以为会回完整歌单对象），它决定了下面「本地插入」的做法。

---

## 2. 实现分层

### Core（`src/Bodian.Core/`）

| 文件 | 内容 |
| --- | --- |
| `Api/Endpoints.cs` | `PlaylistCrud = "service/playlist"`。注释写明三条共用这条裸路径、靠 method 区分，以及它与 `PlaylistMusic`（动歌单里的歌）不是一回事 |
| `Api/Dto/Requests/CreatePlaylistBody.cs` | 请求体。**键是 `private`（JSON 布尔）**，与响应回的那个 `isPrivate`（数字）不是一回事，写错会静默建出公开歌单 |
| `Api/BodianJsonContext.cs` | 登记 `CreatePlaylistBody`（响应侧的 `PlaylistDto` 早已登记） |
| `Api/IBodianApi.cs`、`Api/BodianApi.cs` | `CreatePlaylistAsync(name, isPrivate)` → `Task<long>`。骨架照 `WritePlaylistMusicAsync`：Trim 后非空校验 → `RequireAuthenticated()` → 记 revision → POST signed → 校验 revision → 取回执里的 id |
| `Models/Playlist.cs` | 新增 `IsPrivate`（bool）。来自响应的 `isPrivate` —— 服务端给的是**数字**，`MapPlaylist` 里归一成布尔 |

两处刻意没做：

- **没有长度上限**。实测服务端对 50 字的名字照建，客户端加上限只会挡住合法输入。
- **没有复用 `MapPlaylist`**。回执只有 id，构造不出完整对象 —— 所以返回 `long` 而不是 `Playlist`，
  由调用方决定怎么用（现在是拿 id + 名字本地拼一行，见 §3）。

### UI（`src/Bodian.WinUI/`）

| 位置 | 改动 |
| --- | --- |
| `MainWindow.xaml` | 展开态「创建的歌单」标题行、浮层标题行各加按钮：刷新 `E72C` + 新建 `E710`（浮层里是 刷新 · 新建 · 收起 三颗）。样式与浮层那颗 ✕ 同一套：28×28、透明底、无边框、`FontIcon` 12 号 |
| `MainWindow.xaml.cs` | `OnCreatePlaylistClick` / `OnRefreshPlaylistsClick`（两处共用同一对处理器）、`ShowCreatePlaylistDialogAsync`；`UpdateSidebarPaneMode` 的门控判据换成 `_login.IsAuthenticated`；`OnAccountChanged` 与 `ShowLogin` 各加一次 `_sidebar.Reset()` |
| `ViewModels/SidebarViewModel.cs` | `CreateAsync`、`IsBusy` / `CanRefresh`、`CreateErrorText`、`Reset()`；`LoadAsync` 失败时**不再清空列表** |
| `Controls/SidebarPlaylistList.xaml`、`Controls/PlaylistListView.xaml` | 封面由单个 `Border` 换成 `Grid`，右下角叠一枚锁角标（`SizePrivateBadge = 16`，半透明黑底 + 白锁 `E72E`），由 `Formats.Visible(IsPrivate)` 控制显隐 |
| `Themes/Tokens.xaml` | 新增 `SizePrivateBadge = 16`。32 与 48 两档封面共用同一个尺寸 —— 它是「有 / 没有」的标记，不该随封面放大 |

---

## 3. 几个决定的理由

**零歌单也要有入口。** 显隐判据从「有没有歌单」换成「有没有登录」。
原来的 `hasPlaylists = Count > 0 || ErrorText != null` 把「空」当成了「没什么可显示」，
而新建入口让「空」也成为一种要显示的状态。失败那两行重试入口仍由 `ErrorText` 单独驱动，与这里正交。

**新行是本地拼的，不重拉列表。** 回执只有 id，但其余字段本来就确定：新歌单没有封面、曲目数 0，
而 `userCreate` 按 id 降序、新建的必然在首位（实测）。所以
`Playlists.Insert(0, new Playlist { Id, Name })` 就够了 —— 点完立刻看得到，不依赖一次额外请求，
也就没有「建成了但刷新失败」那种中间态。`SourceType` 留 0（服务端没给）：
侧栏点进详情走的是固定的 `SidebarPlaylistSource = 5`，不读这个字段。

**新建失败不写 `ErrorText`。** 那个属性驱动「歌单加载失败，点击重试」那一行，
一次新建失败写进去，侧栏会显示成整段列表拉不到。所以单开 `CreateErrorText`，在失败对话框里显示。

**`LoadAsync` 失败不再清空列表。** 原来是 `Clear()` + 写 `ErrorText`：对启动时的自动拉取无害
（本来就没东西），对用户主动点的刷新则是倒退 —— 手上有数据，一次网络抖动就让列表变空。
跨账号的清理改由 `Reset()` 承担，`OnAccountChanged` 与 `ShowLogin` 各调一次。

**浮层里建完不关浮层。** 浮层那份列表绑的就是 `_sidebar.Playlists`，新行当场出现在最上面 ——
这就是成功反馈；收起态（48 DIP 图标轨）没有别的地方能显示它。

**`TrackActionsViewModel` 没动。**（2026-10-04 注：这个类后来因批量动作改过，
`TrackActionsService` 现在还兼任 `ITrackBatchActions`；下面这条关于歌单缓存生命周期的结论不受影响。）
曲目行「添加到歌单」那份歌单缓存是**每行新建、行回收即丢**的
（`TrackActionsService.Create`），生命周期只有该行菜单打开期间，新建歌单后基本会自然刷新。
只有「同一行反复开合菜单」这种窄情形会看到旧列表，不值得为它上一套版本号失效机制。

**对话框的校验靠 `IsPrimaryButtonEnabled`**，不用 `PrimaryButtonClick` + deferral：
名字为空时主按钮就是灰的，回车与点击都无效，不必把已经弹出的对话框再拦住。
默认按钮是「创建」—— 与清空队列、取消收藏那类破坏性操作「默认落在取消」相反，
用户是按了 + 才进来的，这是个建设性动作。

**对话框必须显式设 `RequestedTheme`。** 代码构造的 `ContentDialog` 不在可视树里，
**不会继承 `ShellRoot` 上那个 `RequestedTheme`** —— 不设就永远跟随系统，
应用内切成深色时它还是一块白板。队列抽屉、歌单浮层那几个自绘面板同理，都各自绑了。

**对话框的模板内边距也要覆写，否则内容与按钮之间会空掉一大块。**
WinUI 的 `DefaultContentDialogStyle`（`generic.xaml`）里：

| 常量 | 值 | 后果 |
| --- | --- | --- |
| `ContentDialogPadding` | `24` 四边 | 内容区与按钮区**各用一次**：叠起来内容底下凭空多出 48，按钮区自身撑到 80 高（里面只有两个扁按钮） |
| `ContentDialogMinHeight` | `184` | 内容不够高时把对话框撑起来，多出的高度全堆在内容下方 |
| `ContentDialogMaxWidth` | `548` | 内容只有一栏时左右空掉一大片 |

所以两个对话框统一由 `MainWindow.CreateAppDialog` 造：主题 + `Padding` 收到上下 12 +
`MinHeight` 解掉 + 宽度 320 / 360。**以后再加对话框走这个方法。**

> **同类问题还有 5 处**（清空播放队列、清空播放记录、取消收藏歌单 / 专辑、取消关注歌手），
> 它们同样没有设 `RequestedTheme`。用户已知，打算后面连同提示一起统一处理，本轮不动。

**锁角标用固定的半透明黑底，不用主题色。** 封面什么颜色都有，
只有暗底加白字才能保证在浅色封面上也看得见。

---

## 4. 验证

- 构建：`dotnet build src/Bodian.WinUI/Bodian.WinUI.csproj -c Debug --no-restore` → 0 错误，
  1 个既有警告（`AiPlaylistPage.xaml:27` 的 `WMC1506`）。
- 离线测试：`dotnet test --project tests/Bodian.Core.Tests/Bodian.Core.Tests.csproj`
  → **951 项通过，0 失败 0 跳过**（新增 `PlaylistCreateApiTests` 9 项）。
- 实网：用 `tools/Bodian.Probe` 走过 创建 → 读回 → 删除 的往返（`private` 真 / 假各一次），
  外加空名字与 50 字名字两次边界，用完即删，**账号已还原**（自建歌单仍是 20 条）。
  探针为此补了 `--delete`（原先只有 `--post`，发不出 `DELETE`）。

### 界面验收清单（手动）

| 项 | 验收内容 |
| --- | --- |
| 展开态 | 「创建的歌单」标题行右侧有刷新与新建两颗按钮，不挤压标题 |
| 图标 | 两颗按钮的图标实际渲染出来（码位没渲染会是一片空白） |
| 零歌单账号 | 登录后这一段仍在（这是本次修的缺陷）；收起态轨上那颗图标也在 |
| 对话框 | 空名字时「创建」灰着、回车无反应；输入后变亮；**在输入框直接回车即提交** |
| 对话框紧凑度 | 宽度收在 360 内，不再像默认那样拉出一片 548 宽的空档 |
| 暗色模式 | 切到深色后打开新建对话框，它跟着变深（**这是本次修的 bug**） |
| 隐私标记 | 隐私歌单封面右下角有锁。拿「我的歌单」（本来就是隐私的）或新建时勾了隐私的对照 |
| 隐私开关 | 默认关闭；开启后创建，去官方客户端确认该歌单是隐私的 |
| 创建成功 | 新歌单出现在列表**最上面**；播放条上有「已创建「X」」提示；**没有**自动跳走 |
| 收起态 | 点轨上图标开浮层 → 标题行三颗按钮不挤；浮层里建完**浮层仍开着**、新行就在里面 |
| 刷新 | 点刷新列表重拉；**当前打开的歌单仍高亮**；连点两下只刷新一次、期间按钮是灰的 |
| 刷新失败 | 断网后点刷新 → **列表不被清空**，只多出「歌单加载失败，点击重试」一行 |
| 换号 / 登出 | 登出后看不到上一个账号的歌单名 |
| 添加到歌单 | 建完之后打开曲目行「更多 → 添加到歌单」，能立刻看到新歌单 |
| 窄窗口 | 800×560 下标题行两颗按钮不换行、不裁剪 |
