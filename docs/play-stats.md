# 播放统计上报（播放次数 / 时长 / 排行）

2026-11-13。协议来自 5.9.8 arm64 静态反汇编；2026-11-14 实网确认写入、单位与服务端去重，
同日完成客户端实现。

## 0. 结论

「最近播放」和「播放统计」是两条独立链路：

| 功能 | 写入接口 | 本项目状态 |
| --- | --- | --- |
| 最近播放 | `playlist/history/add` | 已实现并验证 |
| 播放次数 / 时长 / 排行 | `log.kuwo.cn/music.yl`（移动端格式） | 已实现，待实网复验 |

官方 PC 端只发桌面格式的 `music.yl`，统计不采纳；移动端格式的 `music.yl` 实测会计入。
客户端在**播完或切歌时**按移动端格式上报。

## 1. 接口（历史记录：record，最终未采用）

> 以下 `record` 内容是排查过程，客户端最终实现的是 §9 的 `music.yl`。

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

## 2. 官方触发时机（record，历史记录）

> 本节是 `record` 的比例规则，客户端最终未采用；实际实现见 §4 与 §8。

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
- 统计是离线聚合，不会立即生效；
- 当时还没有确认单位，也没有看到聚合结果。

### 3.1 后续对照实验（2026-11-16 / 11-17）

之前的「聚合结果」结论**已被后续实验推翻**，保留记录如下。

| 时间 | 写入 | 结果 |
| --- | --- | --- |
| 11-15 | 客户端 `record` 1 条（229 秒） | 11-16 统计未变 |
| 11-15 | 探针 `record` 143 秒、215000（毫秒） | 11-16 统计未变 |
| 11-16 | A：`record` + `plat=android` + `ver=5.9.8` + 移动签名，307 秒 | 11-17 统计未变 |
| 11-16 | B：`music.yl` 移动端 `encv=1`，275 秒 | 11-17 统计未变 |
| 11-16 | C：`record` + `plat=android` + `ver=5.9.8` + 桌面签名，254 秒 | 11-17 统计未变 |
| 11-16 | D：`record` + `plat=android` + 默认 `ver=1.1.7`，305 秒 | 11-17 统计未变 |

所有请求都返回 `200`，但 `playcnt / playTime` 始终停在 `1162 / 191038`。

**修正后的结论：**

1. **`200` 只表示接口接收，不表示统计计入。**
2. 11-14 观察到的 `+8 / +1480` 与「黑色毛衣」次数变化，应归因于官方手机端的播放，
   不是本项目或探针的写入。
3. 桌面头、移动头、移动签名、`music.yl` 这四种复刻方式都**没有**被统计采纳。
4. 单位（秒 / 毫秒）、去重规则、字段语义全部回到**未确认**状态。
5. 官方统计的真实触发条件仍未找到。下一步必须抓官方手机端一次完整播放的流量，
   逐字节对比请求路径、请求头、query 与 body。

## 4. 实现（2026-11-19）

### 4.1 发送链路

| 文件 | 内容 |
| --- | --- |
| `Services/Implementations/PlayLogComposer.cs` | 按官方移动端格式拼管道串，XOR 密钥后再 Base64 |
| `Services/Implementations/PlayTelemetry.cs` | `POST http://log.kuwo.cn/music.yl?encv=1`，纯 HTTP、无签名 |
| `Services/Abstractions/IPlayTelemetry.cs` | `ReportCompleted` / `ReportSwitched` |
| `PlaybackCoordinator` | 切歌记上一首、播完记当前，失败静默 |

### 4.2 字段

| 字段 | 取值 |
| --- | --- |
| `RID` / `NA` / `AR` | 曲目 id、标题、艺人 |
| `DUR` / `SPT` | 曲目总时长（秒） |
| `PT` / `PLAY_TIME` / `PLAYPAGE_TIME` / `APP_TIME` | 实际已播位置（秒），夹在 `0..DUR` |
| `UI` | 当前账号 uid |
| `U` / `UUID` / `Q36` / `ANDROID_ID` / `OAID` | 本机 `devid` |
| `CT` | 本机时间 `yyyy-MM-dd HH:mm:ss` |
| `SRC` / `FROM` / `PLAT` / `CHANNEL` / `VER` | 按官方移动端模板（`bodplayer_ar_5.9.8` / `ar` / `oppo` / `5.9.8`） |

**为什么用移动端模板**：官方桌面格式的 `music.yl` 不被统计采纳；移动端格式的探针实测计入。

### 4.3 触发与失败

- 切歌：在 `StartAsync` 加载新曲目前记录上一首与当前位置；
- 播完：`OnEngineEnded` 记录当前曲目，已播位置等于总时长；
- 试听结束按切歌记录；
- 暂停不发；
- 未登录不发；
- 后台发送、5 秒超时、失败只记 Warning，不重试、不阻塞播放；
- `_completedTrackId` 避免「播完 + 自动续播」重复上报。

## 5. 验证

### 5.1 离线

- 日志字段：`ACT`、`RID`、`DUR`、`PT`、`PLAY_TIME`、`UI` 等；
- XOR + Base64 往返解码一致；
- 发送到 `log.kuwo.cn/music.yl?encv=1`；
- 未登录不发；
- 传输失败不外抛；
- 播完自动续播只报一次；音质切换不报。

### 5.2 实网

2026-11-19 的对照实验已经证明移动端格式的 `music.yl` 会被计入。客户端实现后仍需完整播一首，
等待下一次聚合确认。

## 6. 未决问题

1. 聚合周期：按天或按小时，具体时间未知。
2. 服务端是否按 `id + 时间窗` 去重。
3. 显式停止/退出时官方还会补一条，本项目暂未接入 `stop` 钩子。
4. `record` 是否还负责推荐等其他用途，本项目不再发送。

## 7. 与音乐月报的关系

**月报的读取端不是 `record`**，是：

- `play/month/report/info`（GET，`date=YYYYMM`，只读）
- `play/month/report/summary`（GET，`userId`，只读）
- `play/month/report/playlist`（POST，`{date}`，**写：服务端新建月度歌单**）

2026-11-15 用桌面请求头实测前两条均返回 200。`info` 在无数据月份里缺失，但
`isUpdate` 仍为 1，所以**不能用 `isUpdate` 判断报告是否存在**，唯一判据是 `data.info`。
字段、空态与页面方案见 [`audio-memory.md`](audio-memory.md)。

**写入端无法从客户端静态证明。** 安卓端同时存在两条播放上报：

| 链路 | 内容 |
| --- | --- |
| `service/music/record` | 结构化事件：`{id, src, currentDur, currentSpt, pSrc}` + `operate` |
| `log.kuwo.cn/music.yl` | 管道分隔日志：`ACT:BOD_PLAY_MUSIC`，含 `RID`、`DUR`、`NA`、`AR`、`PT`、`PLAY_TIME`、`PSRC`、`AQ`、`FMT` 等 |

官方 PC 端**只发 `music.yl`**，且 PC 二进制里没有 `play/month/report/*` 的任何字符串（PC 不读月报）。

**2026-11-13 实机对照（用户侧）：** 用官方 PC 客户端连续播放一个下午，账号的播放时长、
播放次数、最近一周与全部排行**都没有变化**。官方 PC 只发 `music.yl`，不发 `record` ——
这条证据说明：

- 官方 PC 的 `music.yl`（桌面字段集）**不负责**播放次数 / 时长 / 排行榜的计数；
- 移动端格式的 `music.yl` 负责：2026-11-19 的对照实验中，只有它对应的探针进入了周排行；
- `service/music/record` 的修正探针（`src` / `pSrc` 已按官方值）**没有进入统计**；
- 官方 PC 用户的播放天然不进这套统计，这是桌面端的既有缺口；
- 月报是否复用同一套聚合仍未验证，但当前证据指向 `music.yl`。

## 8. 触发规则以官方为准

不引入第三方客户端的额外门槛（例如 30 秒最短播放或每小时限额）。官方按实际播放行为记录：

- 切歌：记录**上一首**的已播位置；
- 播完：记录当前曲目，已播位置等于总时长；
- 暂停：只发按钮点击事件，不发播放日志；
- 停止/退出：官方会补一条，本项目暂未接入；
- 未登录不上报；
- 失败只丢这一条，不重试、不阻塞播放。

## 9. 官方移动端 `music.yl` 实抓（2026-11-18）

用 Reqable 协同模式抓到官方 Android 5.9.8 的一次完整播放。**这条报文与 PC 抓到的不是同一个数据结构。**

### 9.1 请求形态

```
POST http://log.kuwo.cn/music.yl?encv=1
Content-Type: application/x-www-form-urlencoded
User-Agent: Dart/3.11 (dart:io)
Accept-Encoding: gzip
```

- **query 只有 `encv=1`**，没有 `uid` / `token`；
- 身份在 body 的 `UI:<uid>` 字段；
- body 是 XOR(密钥 `Z9o+Lw@H`) 再 Base64，与 PC 的纯 Base64 不同。

### 9.2 解码后的字段（已脱敏）

```text
2%09<SRC:bodplayer_ar_5.9.8|ACT:BOD_PLAY_MUSIC|T:1|RID:118989|VID:|DUR:252|
NA:黑色毛衣|AR:周杰伦|PT:223|AQ:3|PLAY_TIME:224|PLAYPAGE_TIME:224|APP_TIME:224|
LSRC:我的->音乐->上一次播放列表|PSRC:playing_more->专辑详情页|MV:0|PATH:|SRC:|
FG:1|LOCK:2|DELAY:7742|BLKCNT:0|DELAY_FIX:7742|FMT:flac|SCENE:0|PTYPE:0|FTYPE:1|
PLAY_STATE:0|PLAY_MODE:0|RECOMMEND_MODE:-1|PLAYER_THEME:poster|ERROR_CODE:0|LOW_DEV:0|
free_mode:0|SPT:252|STARTPlAY_MODE:1|MUSIC_EFFECT:清澈人声|TID:|cut_song_type:2|
BR:2000|CACHE:0|SPEED:5887000|LAST_SPEED:4448.0|ERROR_DESC:success|HTTPHOST:127.0.0.1|
HTTPCODE:200|RESPONSE_CODE:200|HTTPURL:<redacted>|PROXY_TYPE:NONE_PROXY|HEADSET_CONNECT:0|
PROD:bodplayer|FROM:bodplayer_ar_5.9.8_oppo|CHANNEL:oppo|VER:5.9.8|VER_CODE:598|
BUILD_NO:485|PLAT:ar|DEV:<redacted>|OSV:16|CT:2026-11-18 13:00:48|CIP:<redacted>|
U:<redacted>|UUID:<redacted>|Q36:<redacted>|ANDROID_ID:<redacted>|OAID:<redacted>|
UI:<uid>|NE_TYPE:WIFI|IS_VIP:1>
```

切歌时还会发一条 `ACT:PAGE_EXP`（按钮曝光），与播放统计无关。

### 9.3 与之前探针的差异

之前的探针缺了以下关键项：

- `SRC` 写成 `bodplayer_win_1.1.7`，官方是 `bodplayer_ar_5.9.8`；
- 缺 `PLAYPAGE_TIME`、`APP_TIME`、`SPT`、`UI`、`U`、`UUID`、`Q36`、`OAID` 等；
- query 里多带了 `uid` / `token`，官方没有。

### 9.4 对照实验

2026-11-18 当天：

- 官方手机端播放了「黑色毛衣」，作为对照组；
- 用官方模板发了一条「以父之名」（id `93164603`，151 秒）的修正探针。

下一次聚合后按 `playcnt / playTime` 增量判断 `music.yl` 是否计入统计。

### 9.5 仍未确认

移动端还会调用 `service/music/record`。要确认统计到底来自哪条链路，需要继续用 Reqable 解密
`bd-api.kuwo.cn` 的 HTTPS 流量，抓一次完整播放中的 `service/music/record` 请求，与第 1 节
的报文逐字段比对。

### 9.6 官方发送时机（静态 + 实抓）

`MusicModel::sendPlayLog` 的调用点：

| 位置 | 时机 |
| --- | --- |
| `MusicModel::play` | 开始播放新曲目前，记录**上一首**（切歌） |
| `MusicModel::stop` | 停止播放时，记录当前曲目 |
| `onPlayerStateChanged` 的 `COMPLETED` 分支 | 自然播完 |

实抓补充：

- **暂停不发** `BOD_PLAY_MUSIC`，只发 `ACT:BOD_BTN_CLK | btn_name:btnPlay`；
- 切歌时抓到的播放日志是**上一首**，`PT:223`、`PLAY_TIME:224`、`DUR:252`、`SPT:252`；
- 与客户端当前实现（起播时记录上一首 + 播完记录）一致；
- **缺口**：官方在显式 stop（停止播放/退出）时还会补一条，本项目目前没有对应钩子。

结论：客户端“切歌 + 播完”的时机是对的；暂停不需要发；如果要完全对齐，后续补一个 stop 钩子。

### 9.7 官方 `record` 实抓（2026-11-18）

Reqable 解密的手机端 `service/music/record`：

```text
POST https://bd-api.kuwo.cn/api/service/music/record?uid=<uid>&token=<token>
Content-Type: application/json

{
  "musicList": [
    {
      "id": 118988,
      "src": "",
      "currentDur": 248,
      "currentSpt": 248,
      "pSrc": "playing_more->专辑详情页"
    }
  ],
  "operate": 0
}
```

与之前实现的差异：

| 字段 | 官方 | 之前客户端 |
| --- | --- | --- |
| `src` | **空字符串** `""` | 布尔 `false` |
| `pSrc` | **非空来源串**，如 `playing_more->专辑详情页` | 空字符串 |
| `currentDur/currentSpt` | 秒，248 / 248 | 秒（一致） |
| `operate` | 整数 `0`（一致） | `0` |

另外，Reqable 显示的 wire payload 不是明文 JSON，说明手机端对请求体做了额外加密；桌面端目前发的是明文 JSON。
这条差异是否影响服务端采纳统计，尚未验证。

已按官方字段补发一条对照（`id 381467353`，288 秒，`src:""`，
`pSrc:"playing_more->专辑详情页"`），等待下一次聚合。

### 9.8 对照结果（2026-11-19）

聚合后的变化：

| 指标 | 之前 | 之后 | 增量 |
| --- | --- | --- | --- |
| `playcnt` | 1162 | 1166 | +4 |
| `playTime` | 191038 | 191525 | +487 秒 |

周排行新增：

| 曲目 | id | 来源 |
| --- | --- | --- |
| 以父之名 (抖音版) | 93164603 | **本项目的修正 `music.yl` 探针** |
| 四面楚歌 | 118988 | 官方手机端 |
| 枫 | 118987 | 官方手机端 |
| 黑色毛衣 | 118989 | 官方手机端（次数 2→3） |

**修正 `record` 探针（id 381467353）没有出现。**

结论：

1. **移动端格式的 `music.yl` 会被统计采纳。**
2. **桌面明文 `record`（即使 `src`/`pSrc` 改成官方值）没有被采纳。**
3. 统计的真实数据源至少是 `music.yl`；`record` 不是必要条件，或者手机端还做了我们没复刻的请求体加密。
4. 后续实现应以 `music.yl` 为准，`record` 可以先不发。
