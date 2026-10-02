# 搜索系统

更新于 2026-10-02。本文描述当前实现；接口实测依据见 [`bodian-api-reference.md`](bodian-api-reference.md) §2.2。

## 入口与悬浮面板

搜索入口位于主窗口标题栏的 `AutoSuggestBox`，未登录时随外壳隐藏。
聚焦或点击空搜索框会显示热榜与历史面板，保持当前页面和搜索框的键盘焦点。
输入非空关键词时收起面板，改为展示原生联想列表；联想请求有 250ms 防抖，更新输入会取消上一请求。
回车、搜索按钮、联想词、热榜词与历史词共用提交入口，只有提交非空关键词时才进入搜索结果页。

面板上方为搜索热榜，下方为搜索历史。热榜读取 `search/topic/word/list` 的 `hotWord`，
按 `sort` 排序，显示 `key`；不将运营位或外部跳转内容混入热榜。加载失败可点击标题重试。
历史仅记录已提交的关键词，按最近使用排序，忽略大小写去重，上限 20 条。
它以明文 JSON 保存在 `%LOCALAPPDATA%\Bodian\search-history.json`，重启可恢复，支持清空。
保存先写临时文件再替换，读写失败记录日志；坏文件读取按空历史处理，不删除原文件。

面板是根 `ShellRoot` 中最后一层的 `Border`，不是标题栏内部的模态 `Popup`。
定位由搜索框到根布局的变换计算，面板与搜索框左边缘、宽度一致，顶部距搜索框底部 6 DIP，
最大高度受窗口剩余空间限制。背景复用主题浮窗的 `ThemeFlyoutBackground`，
深浅主题均为 80% 不透明度，圆角复用 `RadiusMd`。
打开时不调用 `Focus`，也不创建阻挡搜索框的关闭遮罩。
点击面板和搜索框之外、键盘焦点移出这两个区域、按 Esc 或窗口失活都会关闭；
外部点击不标记为已处理，目标控件仍响应这一次点击。

## 默认综合结果与分类 tab

每次新提交都默认选中「综合」，仅请求一次 `search/comprehensive/v2/list?keyword=...`，
不并行补拉分类接口，也不传 `pn` / `rn`。页面有「综合、单曲、歌单、专辑、歌手」五个 tab。

综合接口的 `data.content` 是分节数组，每个对象直接以分类名作为列表键：

| 键 | 展示分类 | 元素 DTO |
| --- | --- | --- |
| `musicpage` | 单曲 | `TrackDto` |
| `songlistpage` | 歌单 | `SearchPlaylistDto` |
| `artistpage` | 歌手 | `SearchArtistDto` |
| `albumpage` | 专辑 | `AlbumDto` |

只显示接口实际返回的四类音乐数据，保持分节顺序和条目顺序；缺失或空的分类不显示标题。
`interestpage` 直达卡片与 `userpage` 用户信息不属于本次展示范围，解析时忽略。
每个非空分段都有「更多」，点击后选中对应分类 tab，以当前已提交的关键词请求分类首页。
综合结果不分页；分类 tab 才提供「加载更多」。

分类搜索分别调用 `search/music/list`、`search/playlist/list`、`search/album/list`、`search/artist/list`。
页号从 0 开始，显式传 `rn`，每次按请求页边界推进；空页结束，不根据 `total` 或短页推算终点。
单曲列表的 `total` 实测恒等于 `rn`；其他分类有总数，但分页仍统一由游标控制。
加载更多使用已提交的关键词，输入框里的未提交文本不会改变当前结果或下一页参数。
新搜索、切换 tab 会取消旧请求，旧响应不得追加到新的结果列表。

## 结果交互与字段映射

单曲沿用共享 `TrackListView`。综合页点播时队列来自返回的单曲预览；单曲 tab 点播时队列来自已加载的单曲结果。
歌单点击进入现有歌单详情，专辑进入现有专辑详情，歌手进入新歌手作品页。
歌手作品页提供歌曲与专辑两类，专辑按需加载，可重新加载、分页，并跳转专辑详情。

搜索歌单使用 `id` / `musicnum` / `source`，与详情的 `musicCount` / `sourceType` 不同，
因此使用独立 DTO；返回的 `source` 原样传入歌单详情和曲目请求，避免静默空歌单。
专辑兼容 `albumId` 与 `id`，歌手读取 `artistId` / `songNum` / `albumNum`。
所有新增响应类型登记在源生成 JSON 上下文，不依赖反射反序列化。
搜索、联想、热榜、综合及歌手作品请求按已验证的公开接口发送，不附加签名。
播放继续经过现有服务端权限检查。

共享 `PagedList<T>` 增加可选分页约定：默认仍从 1 开始，
歌手歌曲、歌手专辑及专辑详情曲目明确选择从 0 开始，修正原专辑详情漏取首页的问题。

## 验证与故障记录

- 590 项 Core 测试通过，包含分类字段映射、歌单来源透传、稀疏分页、联想过滤、历史持久化与综合解析。
- `fixtures/search-comprehensive-anon.json` 来自匿名真实响应，仅保留四类音乐结果；
  样本数量为单曲 30、歌单 5、歌手 3、专辑 5，不将这些数量作为产品限制。
- 歌手作品使用 `artist-336-music.json`、`artist-336-album.json` 真实响应回放；
  其他分类的构造测试明确用于字段契约验证，不冒充完整真实接口样本。
- WinUI 构建通过；保留既有 `AiPlaylistPage.xaml:28` 的 `WMC1506` 警告。
- 已实际启动确认主窗口成功创建且响应，UI Automation 验证搜索框可保持焦点并输入。
  用户完成本轮界面测试；未将每一种交互都宣称为自动化验证通过。

本轮遇到一次 PRI 资源打包失败：新 DLL 已生成而旧 XAML 资源未被替换，
启动时报 `XamlParseException` 和控件连接编号类型转换错误。完整构建重试成功后代码、资源同步，
已恢复启动。验证运行必须等构建成功，不能仅凭 DLL 已生成就运行中间产物。

复现命令：

```powershell
dotnet build src/Bodian.WinUI/Bodian.WinUI.csproj --no-restore --nologo -v minimal
dotnet run --project tests/Bodian.Core.Tests/Bodian.Core.Tests.csproj --no-restore
```

本机直接运行 xUnit 可执行测试宿主完成验证；本轮 `dotnet test --project ...` 返回零测试，
因此不以该命令的结果代替上述 590 项测试通过记录。
