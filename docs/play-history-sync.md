# 播放历史同步（手机端「最近播放」）

2026-11-13。协议结论来自 5.9.8 arm64 静态反汇编，并于同日实网验证 `add` / `page`；
证据文件在 `reverse/android-5.9.8-arm64/blutter/asm/allin/`。

**P0 / P1 均已实现并验证（2026-11-13）**：歌曲、歌单、专辑三类历史都会在起播时异步上报；
桌面请求头 + 桌面签名的真实写入已在 `page` 的三个 `sourceType` 与手机端最近播放中确认。

## 0. 结论

手机端的「最近播放」有一条**登录态的服务端同步链路**：`playlist/history/add`。官方 PC
客户端没有接这条链路（`reverse/pc/app.so` 里连路径字符串都没有），所以官方桌面端的播放
也不会出现在手机端。本项目原来同样只写本地 `history.json`，因此存在同样的空缺；
现在两类历史都已接上。

实现范围：

1. **歌曲**：登录态下，起播后异步上报（`sourceType=1`）。
2. **歌单 / 专辑**：播放上下文变化时上报（`sourceType=2/3`），来源参数由详情页透传。

---

## 1. 协议

### 1.1 接口

| 能力 | 方法 | 路径 | 参数 |
| --- | --- | --- | --- |
| 写历史 | `POST` | `playlist/history/add` | JSON body `{sourceType, items}` |
| 读历史 | `GET` | `playlist/history/page` | `{sourceType, pn, rn:200}` |
| 删历史 | `DELETE` | `playlist/history/del` | `{sourceType, items}` |

完整 URL 是 `https://bd-api.kuwo.cn/api/` + 路径，即
`/api/playlist/history/add`。**注意没有 `service/` 前缀**，与收藏、歌单那族接口不在同一段。

**`page` 的 `total` 实测上限是 500。** 基线已经是 500 时，新增一条不会让它变大，
验证判据要看 `list` 的第一条，不要看 `total`。

证据：

- `service/user_api_service.dart:10-124`：`addPlayHistory`，`method: "post"`。
- `service/user_api_service.dart:141-290`：`queryPlayHistory`，默认 `method: "get"`、`rn = 200`。
- `service/user_api_service.dart:333-440`：`delPlayHistory`，`method: "delete"`。

### 1.2 `sourceType` 的含义

外层的 `sourceType` 表示**历史条目的种类**，不是歌单自身的 `source`：

| 值 | 含义 | 证据 |
| --- | --- | --- |
| `1` | 歌曲 | `play_hist_model.dart:2633-2636`（`r2 = 1`） |
| `2` | 歌单 | `play_hist_model.dart:1101-1104`（`r2 = 2`） |
| `3` | 专辑 | `play_hist_model.dart:1130-1133`（`r2 = 3`） |

### 1.3 `items` 的形状

官方上报的字段极少，服务端自己补曲名、艺人、封面：

| 种类 | 单条 item |
| --- | --- |
| 歌曲 | `{"sourceId": <musicId>}` |
| 歌单 / 专辑 | `{"sourceId": <id>, "playlistType": <SongList.sourceType>}` |

- 歌曲：`play_hist_model.dart:2523-2560`，map 里只有 `sourceId` 一个键。
- 歌单 / 专辑：`play_hist_model.dart:1833-1859`、`:4303-4336`。
- `playlistType` 取自 `SongList.sourceType`，即接口歌单对象里的 `sourceType`
  （`song_list.dart` 的 `toJson` 把 `field_2b` 映射成 `"sourceType"`）。

### 1.4 读接口的响应

`playlist/history/page` 的信封是 `{total, list}`，`list` 的元素交给 `Song.fromJson`
解析，即**完整的歌曲对象**，不是 `{sourceId}` 那种极简形状
（`play_hist_model.dart:5573-5630`、`data/details_songlist_data.dart:410-543`、
`:1705` 的 `Song::fromJson`）。

### 1.5 官方触发时机

`music_model.dart:3751-3756` 在**起播时**调用 `PlayHistModel.addHistory(song, currentSongList)`：

1. `addHistorySong` 写本地 SQLite，并把本地未同步的记录批量补传（最多 500 条）到 `sourceType=1`；
2. `addHistPlayList` 把这首歌所属的歌单 / 专辑写到 `sourceType=2/3`；
3. 只有登录态才走服务端（`dealSyncAddList` 里的 `isLogin` 判断），未登录只落本地。

不需要等播完，也没有看到最短播放时长门槛。

---

## 2. 为什么不是另外两条链路

| 链路 | 结论 |
| --- | --- |
| `log.kuwo.cn/music.yl` | PC 的播放日志通道，字段是 `ACT:BOD_PLAY_MUSIC`，用于月报 / 归因，不写最近播放（`reverse/findings/17-play-telemetry.md`） |
| `service/music/record` | `ApiService.behavior` 的埋点，`operate` 是 `OP_PLAY_COMPLETE` / `OP_PLAY_SWITCH` / `OP_COLLECT` 这类，属于推荐 / 统计；PC 也不调用（`service/api_service.dart:190-234`、`:333-385`） |
| `playlist/history/add` | **手机端最近播放的实际写入点**，只在安卓 5.9.8 的 `playlist/history/*` 里出现 |

补 `music.yl` 或 `record` 都不会让手机端最近播放出现条目，不要绕这两条。

---

## 3. 与本地历史的关系

- **本地仍是展示数据源。** `IPlayHistoryStore` / `history.json` 继续负责桌面端「最近播放」页；
  服务端只负责跨端同步。两者互不替代。
- **本地写失败不影响上报，上报失败也不影响本地。** 两个动作都从 `Started` 之后触发，
  各自吞异常。
- **按账号隔离。** 上报使用当前会话的 `uid/token`；切号后只写新账号，不迁移旧账号的历史。
- **官方在登录后会把本地未同步的记录补传（最多 500 条）**。P0 不做补传：只上报本次运行中
  真正起播的曲目。补传列为 P2，避免第一次登录就把历史里几年的歌一次性打给服务端。

---

## 4. 实现方案（P0）

### 4.1 Core

| 文件 | 改动 |
| --- | --- |
| `Api/Endpoints.cs` | 新增 `PlayHistoryAdd = "playlist/history/add"`；读接口常量只在探针里用，可不进产品代码 |
| `Api/Dto/Requests/PlayHistoryBody.cs` | `PlayHistoryAddBody { int SourceType; PlayHistoryItem[] Items; }` 与 `PlayHistoryItem { long SourceId; int? PlaylistType; }`。`playlistType` 用 `WhenWritingNull`，歌曲条目不要序列化出这个键 |
| `Api/Dto/PlayHistoryPayloads.cs` | `PlayHistoryPagePayload { int Total; TrackDto[]? List; }`，只给探针 / 测试用 |
| `Api/IBodianApi.cs`、`Api/BodianApi.cs` | `Task AddSongPlayHistoryAsync(long musicId, CancellationToken)`；内部走通用私有方法，P1 直接复用 |
| `Api/BodianJsonContext.cs` | 注册 `PlayHistoryAddBody`、`PlayHistoryPagePayload` |
| `Services/Abstractions/IPlayHistorySync.cs`、`Services/Implementations/PlayHistorySync.cs` | 封装「登录判断 + 上报 + 吞异常」，便于单测 |

上报方法大致形状：

```csharp
public async Task AddSongPlayHistoryAsync(long musicId, CancellationToken cancellationToken = default)
{
    RequireAuthenticated();

    var body = new PlayHistoryAddBody
    {
        SourceType = 1,
        Items = [new PlayHistoryItem { SourceId = musicId }],
    };

    var envelope = await _transport.SendAsync(
        new BodianRequest
        {
            Path = Endpoints.PlayHistoryAdd,
            Verb = BodianHttpVerb.Post,
            Signed = true,
            JsonBody = JsonSerializer.Serialize(body, BodianJsonContext.Default.PlayHistoryAddBody),
        },
        BodianJsonContext.Default.JsonElement,
        cancellationToken).ConfigureAwait(false);

    _logger.LogDebug("播放历史上报：musicId {MusicId}，业务码 {Code}", musicId, envelope.Code);
}
```

注意：`uid` / `token` 由传输层统一补，body 里**不要**再写，官方报文里也没有这两个键。

### 4.2 同步服务

`PlayHistorySync` 的职责只有三件：判断登录态、调用 API、吞掉失败。

```csharp
public interface IPlayHistorySync
{
    void NotifyPlayed(long musicId);
}
```

- 未登录直接返回，不发请求。
- 用 `Task.Run` 或直接 `_ = ...` 异步发出，**不 await**，不阻塞起播。
- 失败只 `LogDebug` / `LogInformation`，不弹提示、不重试、不排队。
- 单次请求建议套一个短超时（例如 5 秒），避免连接卡住时留下悬挂任务。

不做本地 outbox：写历史不是关键业务，丢一条不影响播放，也不影响本地记录。

### 4.3 调用点

`src/Bowen.WinUI/Playback/PlaybackCoordinator.cs` 的 `StartAsync`，紧挨着现有的
`RecordHistory(track);`（`:1139-1141`）加一行：

```csharp
RecordHistory(track);
_historySync.NotifyPlayed(track.Id);
```

两个动作语义一致：都表示「这首歌真的播起来了」，而不是「用户点了它」。
试听（`status=3`）也会触发 `Started`，因此试听同样上报，与本地历史的行为保持一致。

构造函数与 DI：

- `PlaybackCoordinator` 增加一个 `IPlayHistorySync` 参数。
- `App.xaml.cs` 注册 `AddSingleton<IPlayHistorySync, PlayHistorySync>()`。

---

## 5. 去重与频率

- **歌曲**：每次 `Started` 上报一次。连续播放同一首也报，服务端的历史本来就是按时间排序的。
- **P1 的歌单 / 专辑**：只在上下文变化时上报（`(sourceType, id)` 与上一次不同），
  不要每首歌都发一次。官方在本地也做了「已经在第一位就不重复存」的判断。
- 不做客户端节流以外的去重，服务端自己会处理重复条目。

---

## 6. 失败与降级

| 情况 | 行为 |
| --- | --- |
| 未登录 | 直接跳过，不发请求 |
| 网络失败 / 超时 | 记日志，不影响播放，不重试 |
| 业务码非 200 | 记日志，不提示用户 |
| 登录态在请求途中切换 | 放弃本次结果，不重试 |
| 服务端拒绝（签名 / 请求头） | 只影响同步，不出现任何界面提示；上线前用探针确认 |

**任何情况下都不能让历史上报挡住起播，也不能让它的异常冒到播放链路。**

---

## 7. 风险与待验证

1. ~~**写入的是真实账号数据。**~~ 已用真实账号完成一次往返；后续联调仍建议用测试账号。
2. ~~**桌面请求头 / 桌面签名是否被接受。**~~ **已验证可用**（2026-11-13）：
   桌面头 + 桌面签名调用 `add` 成功，`page` 与手机端同时更新。
3. ~~**`playlistType` 的取值。**~~ **已验证**（2026-11-13）：条目里的 `playlistType`
   就是歌单/专辑对象自身的 `sourceType`。自建歌单实测为 `5`，专辑实测为 `6`；
   公开歌单（搜索来源）会是 `4` 或 `13`，P1 必须透传 DTO 里的值，不能写死。
4. ~~**有效播放门槛。**~~ 实测**没有观察到门槛**：起播即记录，点击播放后立刻出现在
   `page` 与手机端。客户端仍在 `Started`（真正播起来）时上报，被拒绝或没起播的曲目不写。
5. **重复条目。** 一次写入没有产生重复。同一首歌连续播放是否会追加第二条尚未验证。
6. **隐私。** 桌面播放同步到手机是用户可感知的行为。P0 默认开启（与手机端一致），
   是否增加设置开关列为 P2；若加，默认值仍为开。

---

## 8. 验证

### 8.1 离线单测

用假的 `IBodianTransport` 断言：

- 登录态下发出的 body 恰好是 `{"sourceType":1,"items":[{"sourceId":...}]}`；
  歌曲条目里**不能出现** `playlistType`。
- 未登录时 `SendAsync` 一次都不调用。
- 传输层抛异常时 `NotifyPlayed` 不抛、不阻塞调用方。
- 请求路径是 `playlist/history/add`、方法是 `POST`、`Signed == true`。

### 8.2 实网探针

先只读，确认接口在当前桌面头下可用：

```bash
dotnet run --project tools/Bodian.Probe -- call playlist/history/page sourceType=1 pn=1 rn=20 --signed
```

预期 `data` 里是 `{total, list}`，`list` 元素是完整歌曲对象。基线确认后再跑写入：

1. 记录 `page` 的第一条（`total` 已封顶 500，不能作为判据）；
2. 在应用里播放一首歌，等 2 秒；
3. 再查 `page`，确认新歌排在第一条。

探针必须使用已登录账号的已保存会话。

**2026-11-13 实测结果：**

| 时点 | `total` | 第一条 |
| --- | --- | --- |
| 播放前 | 500 | 可爱女人（rid 215258） |
| 播放「黑色毛衣」后 | 500 | 黑色毛衣（rid 118989） |

手机端「最近播放」同步显示黑色毛衣；没有出现重复条目。

### 8.3 手动验收

| 项 | 验收内容 |
| --- | --- |
| 登录后播放 | 播一首歌后，手机端最近播放出现该曲目 |
| 未登录播放 | 不产生任何网络请求，本地历史照常记录 |
| 断网播放 | 歌照常播，控制台只有一条调试日志 |
| 连续切歌 | 每首都在手机端出现，顺序正确 |
| 切号 | 新账号的最近播放只包含新账号的播放 |
| 试听 | 试听曲目同样进入最近播放（与本地历史一致） |
| 播放本地历史 | 从最近播放页重播一首，不应产生重复异常 |
| 性能 | 起播耗时可观测差异为 0（上报不 await） |

---

## 9. P1：歌单与专辑（已实现）

**协议已验证（2026-11-13）**：分别向 `sourceType=2`（歌单）与 `sourceType=3`（专辑）
上报了已有条目，两者都顶到对应 `page` 的第一条。使用的报文：

```json
{"sourceType":2,"items":[{"sourceId":97634652,"playlistType":5}]}
{"sourceType":3,"items":[{"sourceId":87758985,"playlistType":6}]}
```

`playlistType` 必须透传歌单/专辑对象自身的 `sourceType`，不要写死。公开歌单实测为 `4`，
自建歌单为 `5`，专辑为 `6`。

**实现（2026-11-13）：**

| 位置 | 改动 |
| --- | --- |
| `Bowen.WinUI/Playback/PlaybackOrigin.cs` | `PlaybackOrigin(Kind, Id, SourceType)`，`Kind` 值 2/3 与历史 `sourceType` 一致 |
| `PlaybackCoordinator` | `PlayFromAsync` / `EnqueueAndPlayAsync` 增加带 `origin` 的重载；记住当前来源，只在来源变化时上报一次 |
| `PlaylistTracksViewModel` | 行内点播带上 `PlaylistId` + `PlaylistSource`；「我喜欢的」与公开歌单都走同一套 |
| `PlaylistDetailViewModel` | 「播放全部」带上同一个来源 |
| `AlbumDetailViewModel` | 行内点播与「播放全部」带上 `Album.Id` + `Album.SourceType` |
| `Album` 模型 | 新增 `SourceType`，由 `MapAlbum` 从 DTO 透传；为 0 时不上报专辑历史 |

去重规则：同一歌单/专辑内连续切歌只发一次；切到没有集合上下文的入口（搜索、排行榜、
最近播放）会清掉来源，之后重新进入歌单会再次上报。

**端到端验证（2026-11-13）**：从歌单详情播放后 `sourceType=2` 第一条变为该歌单
（公开歌单，`playlistType=4`）；从专辑详情播放后 `sourceType=3` 第一条变为该专辑。
手机端「最近播放」同步更新。

P0 不包含，原因是没有现成的播放上下文：

- `Track` 只有 `AlbumId`，没有歌单 id；
- `PlayFromAsync(IReadOnlyList<Track>, index)` 与 `EnqueueAndPlayAsync(Track)` 都不知道
  「这一列歌是从哪个歌单点进来的」。

建议做法：

1. 新增 `PlaybackOrigin`（`Kind`、`Id`、`SourceType`），其中 `Kind` 用 2/3 对应歌单/专辑；
2. 给 `PlayFromAsync` / `EnqueueAndPlayAsync` 加重载，接收可选的 `PlaybackOrigin`；
3. 歌单详情页用 `Playlist.Id` + `Source`，专辑详情页用 `Album.Id` + `Album.SourceType`；
4. `PlaybackCoordinator` 记住当前 origin，`Started` 时只有 origin 变化才上报；
5. 队列从磁盘恢复后 origin 丢失 —— 此时不报歌单/专辑，等用户重新从详情页播放时恢复。

P1 的前置条件是第 7 节第 3 条的 `playlistType` 探针结论。

---

## 10. 后续（P2）

- 登录后补传本地未同步历史（官方上限 500 条/次）。
- 设置页增加「同步播放历史到账号」开关。
- 读接口 `playlist/history/page` 接入桌面端「最近播放」页，替换纯本地数据源；
  需要先解决「服务端条目与本地快照字段不一致」的映射问题。
