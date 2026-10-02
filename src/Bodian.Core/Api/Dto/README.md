# DTO 层

**这一层是 `internal` 的，UI 不直接碰。** 对外暴露的领域模型在 `Bodian.Core/Models/`，
由 `BodianApi` 负责映射。

## 目录

| 位置 | 内容 |
| --- | --- |
| `Dto/` | 响应体 |
| `Dto/Requests/` | **请求体**（P2 新增）。签名覆盖的是「即将发出的精确字节」，所以请求体同样由调用方走源生成序列化，**不要手拼 JSON 字符串**——那样字段顺序会变成隐性约定 |

`Requests/AudioUrlBody` 里**刻意没有 `format` 属性**：实测带上 `format=flac` 会让服务端静默降级到 320k mp3 而业务码仍是 200。类型里没有这个字段，就传不出去。


## 这里的 DTO 是照着 fixture 写的，不是照着文档写的

每个属性都对着 `fixtures/` 下的真实响应核过类型。字段名与类型以 fixture 为准，
`docs/bodian-api-reference.md` 第 6 节只作参考——两处冲突时以 fixture 为准，并把文档改过来。

## 有意不写的 DTO（不是遗漏）

| 缺的类型 | 为什么 | 什么时候补 |
| --- | --- | --- |
| **评论删除** `comments/v2/del` | 参数来自反编译，删除尚未接入；真实写端点均留给用户手动验证 | 后续接入 |
| **歌单** `PlaylistDto` 及各家列表 payload | 没有歌单 fixture | P7 |
| **下载** `service/music/download/{info,config}` | 已定论本项目不引入这三个接口（用 `audioUrl` 即可），见 `bodian-api-reference.md` 3.2 节 | 不补 |
| `searchTag.jumpInfo` | 实测是空对象 `{}`，语义未知 | 有语义时再补 |

**别顺手补全。** 没有 fixture 的 DTO 写了也无法验证，只会在 P2 埋一个「看着像真的」的坑。

## 两条源生成硬限制（会直接卡住设计）

1. **不支持开放泛型** —— 不能写 `JsonSerializable(typeof(ApiEnvelope<>))`。
   所以**信封不做 JSON 反序列化**：`BodianHttpTransport` 用 `JsonDocument` 手工取
   `code` / `msg` / `reqId`，再把 `data` 子树交给 `JsonSerializer.Deserialize(JsonElement, JsonTypeInfo<T>)`。
2. **`object` / `JsonElement` 属性会退化** —— 不要把不确定的字段声明成 `JsonElement`，
   那等于把 JSON 漏进上层。

## 三个必须记住的坑

- **`payInfo` 的字段类型跨接口不一致**：`refrain_start` / `refrain_end` / `limitfree` 在
  `service/music/info` 里是**字符串**，在 `search/music/list` 里是**数字**。
  靠 `BodianJsonContext` 的 `NumberHandling.AllowReadingFromString` 解决，**不要写自定义转换器**。
  配套有一条双 fixture 对照测试守着它。
- **同一个响应里有三种时间单位**：曲目 `duration` / `mvduration` / `audition.*` 是**秒**，
  `payInfo.refrain_*` 是**毫秒**。所以属性名一律带 `Seconds` / `Ms` 后缀，让单位错配在调用点显形。
- **`audios[].bitrate` 与 `.size` 都是字符串**，`size` 还带单位（`"52.83Mb"`，`zp` 档是占位串 `"zpMb"`）；
  **`audios[]` 的顺序不按档位高低排**（实测首元素是 `bcms`、`ff` 排第六），选档必须按 `level` 查表。

## 评论响应与请求 DTO（2026-10-02）

`SongCommentsPayload` 对照匿名 GET 捕获的 `fixtures/comments-new-118990.json`、`comments-hot-118990.json` 实现。样本只保留公开展示字段，不包含请求凭据或额外的 `userInfo` 信息。列表使用 `plat: android`、`ver: 1.1.7`，无需签名，固定 `rn=30`。回复 DTO 同时对照 `comments-replies-867666.json`，兼容嵌套 `userInfo`。发布与点赞请求 DTO 依据歌曲界面的完整离线调用链实现，点赞使用 `comments/v3/like` 的 `op=1/2`，回复额外带 `parentId`。自动测试仅用离线替身，真实发布／回复、点赞／取消已由用户手测；助手没有发送这些写请求。`TrackDto.Comment` 为可空数值，字段缺失不误报零评论，供播放页角标消费。
