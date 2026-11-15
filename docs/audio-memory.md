# 音波回忆（本周最爱 / 听歌月报）

2026-11-15。三条只读端点用桌面请求头 + 桌面签名实测通过；写端点尚未调用。
本文记录协议结论、页面方案与实现边界。

## 0. 结论

- 新增侧栏根页「音波回忆」，页内两个 tab：`本周最爱` / `听歌月报`。
- `本周最爱` 用 `ucenter/playdata/play_ranking` 的 `type = 1`，返回完整歌曲对象 + `playcnt`。
- `听歌月报` 先用 `play/month/report/summary` 的 `monthDesc` 定位最近有数据的月份，
  再读 `play/month/report/info?date=YYYYMM`。
- **`isUpdate` 不能判断报告是否存在**：无数据的月份同样返回 `isUpdate = 1`，但没有 `info`。
  判据只有一个：`data.info` 在不在。
- `listenTotal` 是**分钟**，但以数字字符串返回（如 `"90"`），不是格式化文案。
- `musicTop10` 是**简化歌曲对象**，没有音源档位，不能直接进播放器；播放前必须用
  `service/music/info` 补全。
- `albumTop10` 是**封面 URL 字符串数组**，没有专辑 id 与名字，只能做封面墙。
- `play/month/report/playlist` 是写操作，服务端自己建歌单，参数只有 query `date`。

## 1. 端点

| 用途 | 方法 / 路径 | 参数 | 状态 |
| --- | --- | --- | --- |
| 最近一周排行 | `GET ucenter/playdata/play_ranking` | `userId`、`type = 1` | ✅ 200 |
| 月报预览 | `GET play/month/report/summary` | `userId` | ✅ 200 |
| 月报详情 | `GET play/month/report/info` | `date = YYYYMM` | ✅ 200 |
| 新建月度歌单 | `GET play/month/report/playlist` | query `date=YYYYMM` | ⚪ 未实测（写） |

日期格式实测为 `202610`。反编译里是「年 + `padLeft(月, 2, "0")`」直接拼接，没有分隔符。

## 2. 响应结构

样本已脱敏，未提交个人听歌记录。

### 2.1 `play_ranking`

```text
data.musicList[]
  ├─ 完整歌曲对象（与搜索列表同构，含 audios / payInfo / duration / albumId ...）
  └─ playcnt : int
```

- 列表按 `playcnt` 降序，服务端已排好。
- 没有总数、没有 `total`；总次数由客户端对 `playcnt` 求和。
- `type = 1` 是「最近一周」；另一个值对应「全部时间」，本次未探。

### 2.2 `summary`

```text
data.monthReport
  ├─ listenTotal : string   // 分钟，数字字符串
  ├─ listenChain : string   // 例如 "比上月\n+12%"
  ├─ pic         : string   // 月报封面
  └─ monthDesc   : string   // 例如 "10月听歌"，没有年份
data.weekReport
  ├─ id / name / artist / artistId / albumPic / albumPic120
  └─ 简化单曲对象，没有 duration / album / audios
```

`monthDesc` 只给月份，不带年份。定位年份的规则：取其中的月份数字，
如果大于当前月份就按上一年算（12 月在 1 月打开就是去年）。

### 2.3 `info`

```text
data.isUpdate : int          // 无数据月份也是 1，不能当存在性判据
data.info     : object|null  // 唯一判据
```

`info` 字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `monthDate` | string | `"2026-10-01"`，月份年份以它为准 |
| `playTotal` | int | 播放次数 |
| `playChain` | string | 播放次数环比文案 |
| `playContent` | string | 播放入口的一句话文案 |
| `playTop1MusicName` / `playTop1Artist` | string | 播放次数最多的曲目 |
| `listenTotal` | string | 听歌时长，**分钟**，数字字符串 |
| `listenChain` | string | 时长环比文案 |
| `listenContent` | string | 听歌入口的一句话文案 |
| `listenTop1MusicName` | string | 听歌时长最多的曲目名 |
| `musicTop10[]` | array | 简化歌曲对象：`id / name / artist / album / albumPic / playCount` |
| `albumTop10[]` | string[] | **只有封面 URL**，没有 id 与名字 |
| `playTop2AlbumPic[]` | string[] | 播放次数前二的封面 |
| `listenTop2AlbumPic[]` | string[] | 听歌时长前二的封面 |
| `artistName` / `artistPic` | string | 最爱歌手 |
| `artistMusicName` / `artistMusicPic` | string | 歌手代表曲 |
| `artistContent` | string | 歌手入口的一句话文案 |
| `songNum` | int | 曲目数 |
| `timeRank` | int | 含义未确认，v1 不展示 |

### 2.4 `playlist`（未实测）

```text
GET play/month/report/playlist?date=202610
```

官方 `createReportPlaylist` 调的是 `HttpUtils.request` 的默认 GET，`date` 放在 query；
**不是 POST JSON body**。早期实现按 POST 发送会一直失败，2026-11-15 已修正。
服务端自己命名歌单（官方客户端随后提示「已为你新建月度歌单」），响应体形状与重复
调用是否幂等仍未验证。实现时只调用一次，并做按钮防抖。

## 3. 页面方案

### 3.1 位置

侧栏「我的音乐」组、`关注的歌手` 之后新增:

```text
音波回忆
[本周最爱] [听歌月报]
```

不改造 `FollowedArtistsPage`。那一页的滚动位置、共享封面返回落点都依赖现有结构，
把两个不相干的 tab 塞进去只会让页头名不副实。

### 3.2 本周最爱

```text
最近一周 · 共 N 首 / M 次                         [刷新]
┌──────────────────────────────────────────┐
│ #1 封面 120  歌名 / 歌手         播放 N 次 │
└──────────────────────────────────────────┘
#2  封面 48  歌名 / 歌手              N 次  [播放]
#3  ...
```

- 第一名叫 hero，列表从 #2 开始；只有一首时只显示 hero。
- 行内提供播放按钮，双击行也播放；不跳详情页。
- 空态：「最近一周还没有播放记录。」
- 失败与空态分开：失败给「加载失败」与重试，不显示成「没有记录」。

### 3.3 听歌月报

```text
2026年10月 ▾                              [刷新] [新建月度歌单]
──────────────────────────────────────────────────────────
[听歌时长 90 分钟] [↑ +12% 比上月] [播放 42 次] [听过 30 首]
[最长一天 81 分钟 / 10月2日] [单曲最多 6 次 / 歌名]
──────────────────────────────────────────────────────────
本月最爱歌手   (圆形头像) 田馥甄        常听 [封面] 渺小
  在10月陪伴你度过22分钟...
──────────────────────────────────────────────────────────
最爱排行
        [2]          [1]          [3]        <- 擂台，冠军居中且更高
  #4 ... #10 列表
最爱专辑
  倾斜相框封面墙（最多 10 张，无名字、不可点）
```

- 月份选择是「年份 + 月份」两级：年份从账号注册年到最近月报年，月份只列出该年实际有
  `info` 的月份。
- 注册年来自 `ucenter/users/pub/{uid}` 的 `userInfo.createTime`（手机端
  `ReportPickerWidget.registerDate()` 用的也是它）；注册当年从注册月开始，当前年只探测到当前月。
- 接口没有「有数据的月份列表」，客户端在允许范围内逐月查询 `info`，按年份缓存结果；
  没有 `info` 的月份不会出现在下拉里。
- 首次进入：`summary` → 解析 `monthDesc` → `info`。`summary` 失败或没有
  `monthReport` 时退回当前月。
- 时长变化单独成卡：从 `listenChain` 拆出「比上月」与 `+12%` 两段。
- 「最长一天」卡从 `listenContent`（形如 `10-02与音乐共度81分钟`）拆出日期与分钟数。
- 「单曲最多」卡取擂台冠军的曲名与 `playCount`。
- 歌手卡用 `artistPic` 做圆形头像，`artistMusicPic` / `artistMusicName` 做「常听」。
- 前三名用擂台卡，第 4 名起回到行列表。
- `musicTop10` 的曲目行可以显示 `playCount`；点击播放时先用
  `GetTrackAsync(id)` 补全 `Track`，再交给 `PlaybackCoordinator`。
  直接用简化对象会被 `HasPlayableQuality = false` 挡成「无可用音质」。
- `albumTop10` 只有封面 URL，用轻微倾斜的相框墙展示；不能显示名字或跳转。
- 空态按月份区分：
  - 当前月：`本月音波报告还在路上，请耐心等待～`
  - 历史月：`这个月音波强度不足，报告空空如也`
- `新建月度歌单`：
  - 只有 `info` 存在时可用；
  - 先弹确认框，说明会在账号里新建一个歌单；
  - 请求中禁用按钮，防重复建；
  - 成功提示「已为你新建月度歌单」，失败提示「歌单生成失败」。

### 3.4 加载与账号

- 两个 tab 都懒加载：第一次切到才请求，切回来不重拉。
- 内容常驻可视树、只切 `Visibility`，切 tab 不丢滚动位置。
- 换账号：两个 tab 一起清空并回到未加载状态。
- 每个 tab 自己的工具栏与刷新按钮，不共用页面级刷新。

### 3.5 导出海报

月报工具栏的「导出海报」提供两种版式，统一使用「波纹音乐」自己的视觉：

| 版式 | 内容 | 数据 |
| --- | --- | --- |
| 月度总览 | 深色渐变、同心波纹、悬浮封面、玻璃统计卡 | `albumTop10`、`musicTop10[0]`、`songNum`、`playTotal`、`timeRank`、`listenTotal`、昵称与头像 |
| 歌曲总览 | 深色波形底 + 前 10 名榜单、均衡器柱 | `musicTop10`、月份 / 年份、榜首封面 |

- 用 Win2D `CanvasRenderTarget` 离屏渲染，不依赖窗口尺寸；导出 PNG，支持保存或复制到剪贴板。
- 海报支持明暗两套主题，默认跟随当前应用主题；预览对话框右上角可切换，切换后重新渲染。
- 暗色海报使用亮色图标 `RippleLight.png`，亮色海报使用暗色图标 `Ripple.png`。
- 月度总览固定 1080 × 1700；歌曲总览宽 1080，高度随曲目数增长。
- 头像是登录凭据里的 `Avatar`，昵称是 `Nickname`；头像加载失败时画昵称首字。
- 单张封面加载失败只画占位块，不中断整张海报。
- `timeRank` 按「超过 N% 用户」展示，缺失时显示 `—`。
- **不复刻官方海报**：不使用官方标题、磁带小票、条码或品牌色；用波纹、波形线与均衡器建立自己的识别。

## 4. 数据模型

```csharp
public sealed record PlayRankingEntry(Track Track, long PlayCount);

public sealed record MonthReport
{
    public required int Year { get; init; }
    public required int Month { get; init; }
    public string? ListenTotalMinutes { get; init; }
    public int? PlayCount { get; init; }
    public int? SongCount { get; init; }
    public string? ListenChain { get; init; }
    public string? PlayChain { get; init; }
    public string? ListenContent { get; init; }
    public string? PlayContent { get; init; }
    public string? ArtistName { get; init; }
    public Uri? ArtistCover { get; init; }
    public string? ArtistMusicName { get; init; }
    public Uri? ArtistMusicCover { get; init; }
    public string? ArtistContent { get; init; }
    public IReadOnlyList<ReportTrack> TopTracks { get; init; } = [];
    public IReadOnlyList<Uri> AlbumCovers { get; init; } = [];
}

public sealed record ReportTrack(
    long Id, string Title, string Artist, string? AlbumName, Uri? Cover, long? PlayCount);
```

`MonthReport` 的月份来自 `info.monthDate`，不要用请求参数回填 —— 服务端可能返回
与请求不同的月份，以响应为准。

## 5. 实现顺序

1. `Endpoints` + DTO + `IBodianApi` / `BodianApi` 三个读方法。
2. `MonthReportPeriod` 解析 `monthDesc`，配单测。
3. 页面骨架：侧栏项、导航映射、启动页选项、`IAccountScopedView`。
4. 本周最爱 tab。
5. 听歌月报 tab。
6. 写端点与确认框（单独提交）。
7. 更新 `play-stats.md`、`bodian-api-reference.md`、`bodian-api-inventory.md` 与本索引。

## 6. 验证

- 三个读端点：业务码 200，字段能反序列化；`info` 无数据时 `info` 为 `null`。
- 月份定位：`monthDesc = "10月听歌"` 在 11 月解析为当年 10 月；在 1 月遇到 12 月解析为上一年。
- 空态：当前月与历史月文案不同。
- 播放：月报 Top10 点击后能补全并起播，不报「无可用音质」。
- 换账号：两个 tab 数据清空，重新进入按新账号请求。
- 写操作：只在测试账号上调用一次，记录响应与歌单名；确认不能重复建。

## 7. 未决

1. `timeRank` 的统计范围（好友 / 全站）。
2. `play_ranking type` 的另一个取值是否确为「全部时间」。
3. `play/month/report/playlist` 的响应体、幂等性与歌单命名规则。
4. `listenTotal` 的单位为分钟是推断值，上线前用官方客户端对照一次。

## 8. 实现状态（2026-11-15）

- 三条读接口、DTO、领域模型与单测已完成；全套测试 1937 项通过。
- `AudioMemoryPage` 与两个页签已接入侧栏「音波回忆」，WinUI 构建通过。
- 月报的两种导出海报（月度总览 / 歌曲总览）已接入，保存 / 复制走既有剪贴板与文件选择器。
- 「新建月度歌单」已按反编译形状接到界面，但**尚未对真实账号调用**。
  上线前需要在一个测试账号上跑一次，确认歌单名、响应体与重复调用行为。
- 探针产生的 `fixtures/month-report-*.json` 与 `play-ranking-week.json` 含真实听歌记录，
  不进入版本控制；单测使用合成 JSON。
