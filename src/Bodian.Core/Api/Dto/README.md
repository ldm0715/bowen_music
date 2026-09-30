# DTO 层

**这一层是 `internal` 的，UI 不直接碰。** 对外暴露的领域模型在 `Bodian.Core/Models/`，
由 `BodianApi` 负责映射。

## 这里的 DTO 是照着 fixture 写的，不是照着文档写的

每个属性都对着 `fixtures/` 下的真实响应核过类型。字段名与类型以 fixture 为准，
`docs/bodian-api-reference.md` 第 6 节只作参考——两处冲突时以 fixture 为准，并把文档改过来。

## 有意不写的 DTO（不是遗漏）

| 缺的类型 | 为什么 | 什么时候补 |
| --- | --- | --- |
| **评论** `comments/v2/*`、`comments/v3/*` | **`fixtures/` 里没有任何评论样本。** 文档 3.1 节有参数名与路径，但**没有响应体**——照着写就是凭记忆断言，且无法验证。另外 P8 仍阻塞在移动端签名上（`ver ≥ 3.5` 才强制校验） | P8，拿到真实响应之后 |
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
