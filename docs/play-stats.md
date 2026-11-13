# 播放统计上报（播放次数 / 时长 / 排行）

2026-11-13。协议来自 5.9.8 arm64 静态反汇编，接口已实网打通但没有等到统计更新；
结论与待验证项都写在下面。

## 0. 结论

「最近播放」和「播放统计」是两条独立链路：

| 功能 | 写入接口 | 本项目状态 |
| --- | --- | --- |
| 最近播放 | `playlist/history/add` | 已实现并验证 |
| 播放次数 / 时长 / 排行 | `service/music/record` | **未实现**，所以统计不动 |

官方 PC 端也没有调用 `record`（抓包只看到 `log.kuwo.cn/music.yl`），所以官方桌面端的播放
同样不会进入这套统计。要补齐，客户端必须在**播完或切歌时**主动上报。

## 1. 接口

```text
POST service/music/record
Content-Type: application/json

{
  "musicList": [
    {
      "id": 118989,
      "src": false,
      "currentDur": 240,
      "currentSpt": 240,
      "pSrc": ""
    }
  ],
  "operate": 0
}
```

证据：

- 端点与 body：`service/api_service.dart:58-269`（`ApiService.behavior`）。
- item 的五个字段：`api_service.dart:565-710`。**不是完整歌曲对象**。
- `operate` 是整数，不是字符串（`api_service.dart:190-212`）。

### 1.1 `operate`

| 值 | 名称 | 含义 |
| --- | --- | --- |
| `0` | `OP_PLAY_COMPLETE` | 播放完成 |
| `1` | `OP_PLAY_SWITCH` | 切歌 |
| `5` | `OP_NONE` | 播放中 |
| `2` / `3` / `4` / `6` / `7` / `16` | 分享 / 收藏 / 搜索 / 删除 / 单曲循环 / 取消收藏 | 其它行为，本项目不涉及 |

## 2. 官方触发时机

### 2.1 播放完成

播放器状态到 `COMPLETED`（`AudioPlayerState` 值 `3`）时，发 `operate = 0`。
证据：`music_model.dart:29785-30425`（`onPlayerStateChanged` 的完成分支）。

### 2.2 切歌

按**已播比例**决定（`sendUserOp1`，`music_model.dart:22788-23166`）：

| 已播比例 | `operate` |
| --- | --- |
| < 30% | `1` 切歌 |
| 30% - 90% | `5` 播放中 |
| 90% - 99.5% | `0` 播放完成 |
| > 99.5% | 不单独发，等完成事件 |

这解释了为什么短播不会计入统计；也是我们实现时必须照抄的规则。

## 3. 探针结果（2026-11-13）

| 尝试 | 结果 |
| --- | --- |
| `currentDur/currentSpt` 按秒 | `200`，`user_data` 不变 |
| 按毫秒 | `200`，`user_data` 不变 |
| `--plat android --ver 5.9.8` | `200`，`user_data` 不变 |

基线：`playcnt = 1153`，`playTime = 189554`。十几分钟后仍然不变。

随后用**官方手机客户端**完整播一首，`user_data` 同样没有立即变化，说明这是
**服务端离线聚合**，不是请求被拒。真实生效时间未知，可能按小时或按天。

**因此：**

- `record` 报文被服务端接收这一点已经确认；
- `currentDur/currentSpt` 的单位、`src/pSrc` 的确切含义仍未确认；
- 统计是否最终会被这三条探针记录改变，需要过一段时间再读 `user_data` 对照。

## 4. 实现方案（尚未实现）

### 4.1 API 层

| 文件 | 改动 |
| --- | --- |
| `Api/Endpoints.cs` | `PlayRecord = "service/music/record"` 与三个 `operate` 常量 |
| `Api/Dto/Requests/PlayRecordBody.cs` | `PlayRecordBody { MusicList[], Operate }`、`PlayRecordItem { Id, Src, CurrentDur, CurrentSpt, PSrc }` |
| `Api/IBodianApi.cs`、`BodianApi.cs` | `AddPlayRecordAsync(musicId, durationSeconds, playedSeconds, operate)` |
| `Api/BodianJsonContext.cs` | 注册请求体 |

### 4.2 上报服务

新增 `IPlayTelemetry` / `PlayTelemetry`，与 `IPlayHistorySync` 同构：

- 未登录直接跳过；
- 立即返回，网络请求后台完成；
- 5 秒超时，失败只记 Debug 日志，不重试；
- 不阻塞播放。

接口：

```csharp
public interface IPlayTelemetry
{
    void ReportCompleted(Track track);
    void ReportSwitched(Track track, TimeSpan played);
}
```

`ReportSwitched` 内部按第 2.2 节的比例换算 `operate`；`ReportCompleted` 固定 `0`。

### 4.3 调用点

都在 `PlaybackCoordinator`：

1. `OnEngineEnded`：整曲播完时 `ReportCompleted(CurrentTrack)`。
   **试听不算完成**，按切歌规则上报（试听片段占比很小，会落到 `operate = 1`）。
2. `StartAsync` 加载新曲目之前，记录上一首的 `CurrentTrack` 与引擎当前位置；
   新曲目起播后，如果上一首不是刚播完的，调用 `ReportSwitched(上一首, 已播时长)`。

**不额外加门槛**：只要发生切歌就按比例上报，不设 30 秒最短播放，也不设小时限额。

需要一个小状态 `_completedTrackId`：`OnEngineEnded` 记下 id，`StartAsync` 发现上一首就是它
就跳过切歌上报，避免「完成 + 切歌」重复计数。

### 4.4 字段取值

| 字段 | 取值 |
| --- | --- |
| `id` | `Track.Id` |
| `src` | 固定 `false`（官方取 `Song.isUserDefault`，本项目没有对应概念） |
| `currentDur` | 曲目总时长，秒，四舍五入 |
| `currentSpt` | 已播位置，秒，夹在 `0..currentDur` |
| `pSrc` | 空字符串 |

单位暂定秒。若聚合结果证明是毫秒，只改这一处换算。

## 5. 验证

### 5.1 离线

- 报文形状：`operate` 为整数、item 恰好五个键；
- 比例映射：`10% → 1`、`50% → 5`、`95% → 0`、`100% → 0`；
- 未登录不发请求；
- 传输异常不外抛；
- 播完自动续播只报一次，不重复。

### 5.2 实网

先记录 `user_data` 基线，再用客户端完整播一首，等待聚合后对比 `playcnt` / `playTime`。
聚合不是实时的，**至少隔天再看**，不能按秒级判断。

## 6. 未决问题

1. 聚合周期：官方手机端也没有立即生效，具体延迟未知。
2. 单位：秒还是毫秒。
3. `src` / `pSrc` 是否必填，空值是否影响计数。
4. 统计是否只认 `operate = 0`，还是 `1` / `5` 也计入时长。
5. 服务端是否按 `id + 时间窗` 去重。

## 7. 与音乐月报的关系

**月报的读取端不是 `record`**，是：

- `play/month/report/info`
- `play/month/report/playlist`
- `play/month/report/summary`

这三条都是只读，客户端只负责展示。

**写入端无法从客户端静态证明。** 安卓端同时存在两条播放上报：

| 链路 | 内容 |
| --- | --- |
| `service/music/record` | 结构化事件：`{id, src, currentDur, currentSpt, pSrc}` + `operate` |
| `log.kuwo.cn/music.yl` | 管道分隔日志：`ACT:BOD_PLAY_MUSIC`，含 `RID`、`DUR`、`NA`、`AR`、`PT`、`PLAY_TIME`、`PSRC`、`AQ`、`FMT` 等 |

官方 PC 端**只发 `music.yl`**，且 PC 二进制里没有 `play/month/report/*` 的任何字符串（PC 不读月报）。

**2026-11-13 实机对照（用户侧）：** 用官方 PC 客户端连续播放一个下午，账号的播放时长、
播放次数、最近一周与全部排行**都没有变化**。官方 PC 只发 `music.yl`，不发 `record` ——
这条证据说明：

- `music.yl` **不负责**播放次数 / 时长 / 排行榜的计数；
- 这些统计至少依赖 `service/music/record`；
- 官方 PC 用户的播放天然不进这套统计，这是桌面端的既有缺口；
- 月报如果与这些统计共用聚合口径，那么**只补 `music.yl` 不够，必须补 `record`**。

`music.yl` 仍然可能是日志、归因、内容分析的另一条管道，但不再是统计的主链路候选。

对客户端的含义：**要实现播放次数 / 时长 / 排行，`service/music/record` 是必须的。**
月报是否完全等同这套聚合仍未单独验证，但当前证据不支持「只补 `music.yl`」。

## 8. 触发规则以官方为准

不引入第三方客户端的额外门槛（例如 30 秒最短播放或每小时限额）。官方只按已播比例决定
`operate`，客户端照抄即可：

- 播完 → `0`；
- 切歌：`<30%` → `1`，`30%-90%` → `5`，`>90%` → `0`；
- 未登录不上报；
- 失败只丢这一条，不重试、不阻塞播放。
