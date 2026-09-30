# 波点 WinUI 客户端 · 未完成事项

**写于 2026-09-30，给接手的新会话用。** 这份只列「还没做的」，已完成的不在这里。

## 先读

| 文档 | 为什么 |
| --- | --- |
| `roadmap.md` | **阶段划分与进度，先读这份** |
| `bodian-api-reference.md` | 接口主文档。**第 10 节**是五轮实测记录，**第 8 节**是还没验证的协议事实 |
| `bodian-api-inventory.md` | 逆向勘查记录，查「这个路径从哪来」时看 |
| `tech-stack.md` / `lyrics-ui.md` | P1 之后要用 |
| `dev-environment.md` | 本机环境（三个非默认路径） |

## 现状一句话

P0（协议探针）五轮实测做完，**播放链路已验证可用** —— 无损 FLAC 能拿到、`ffplay` 能解出。
剩下两处协议空白、一处需要换工具链的逆向；**客户端工程从 P1 起一行代码都没有**。

## 做事的规矩

1. **动手前先说明要做什么**，尤其是外部请求、写操作、装工具、改配置。多步操作先给清单（含预计请求数/副作用），等确认再做
2. **别把第三方服务当压测。** 涉及 API 的探测要节流；先想能不能用零副作用的方法解决
3. **写操作要先能读。** 没有读回确认的写操作等于盲写

**探针工具**：`tools/Bodian.Probe`，已编译在 `tools/Bodian.Probe/bin/Debug/net10.0-windows/bodian-probe.exe`。`--help` 看用法，`whoami` 看当前会话。

---

## A. 协议空白（需要发请求）

### A1. `service/collect` 的 body 字段 —— 优先级最高

唯一协议未知的写操作。已确认：POST + JSON body；`source` 是 query 参数；`sourceId` 存在且是集合类型。

**验证判据已经就绪**：`GET service/collect/multipleState?source=4&sourceIds=<id>` → `{"result":[{"id":..,"collect":false}]}`

**关键：不要盲试字段名。** 上次连发十次全部失败。按 `bodian-api-reference.md` 3.3 节的方法论走：

1. **空参数调用一次** → 服务端是 Spring，必填参数缺失时会原样返回参数名和类型：
   `Required request parameter 'source' for method parameter type int is not present`
2. 逐项补齐，每次都能拿回下一个缺失项
3. 参数表齐了再发写请求

另有用信息：Jackson 反序列化失败返回 **`HTTP 400 JSON parse error`**，服务层异常返回 **`HTTP 500`**。两者能区分「字段名不存在」与「字段名存在但类型不对」。

### A2. `paytagindex` 复核

第 10 节第 1 轮证明它**不随曲目变化**（5 首完全相同的 `{L:0,H:1,S:2,F:3,HR:4,ZP:6,DB:7,AR501:8,ZPGA201:9,ZPGA501:10,ZPLY:11}`），但样本是同批次的 5 首。
**待办**：用 VIP 账号（小号是活动 VIP）对免费曲 / 付费曲各取一次，彻底排除「仍编码账号状态」。

### A3. `service/music/download/{info,config}` 是否比 `audioUrl` 有增量

从没测过。同一首歌两个接口都调，对比码率与地址 —— 可能返回更高码率或下载专用配置。

### A4. 其余小项

| 项 | 说明 |
| --- | --- |
| `service/collect/sort` 参数 | 排序后读 `collect/4/list` 确认顺序变化 |
| `zp` / `bcms` 两档的有效 `br` | `20000kzp`、`22000kmgg` 都被降级到 128k mp3，命名规律未找到 |
| `payInfo.local_encrypt` 语义 | 对比有无该字段的曲目行为 |
| `freeSign` 的签发接口 | 出处已定位到看广告流程（`freeSign` 在两个 APK 的 AOT 里都紧挨着 `downLoadTaskListen_onReward`），但**没有任何接口签发它**。唯一可能是 `service/advert/watch` 的回调，需要真的看广告才能触发 —— 可能做不了 |

---

## B. 需要换工具链的逆向（离线，零请求）

### B1. 移动端签名算法 —— **这一项是根**

`bodian-api-reference.md` 1.3 节实测：**签名校验由 `ver` 请求头控制**。`ver ≤ 3.0.0`（含官方 PC 端现用的 `1.1.7`）完全不校验；`ver ≥ 3.5` 强制校验，返回 `439 sign invalid`。

本项目对签名算法的实现在强制校验下**是错的** —— 实测十种变体（盐的位置、body 的 md5 内外层、path 三种形态、大小写）全部被拒。

**它同时阻塞**：

- **P8 评论** —— 评论端点属移动端，用桌面头打不通（16 个端点全部返回空数据或服务端异常，见 3.1 节）；换移动端头就撞签名
- **`ver` 能不能跟** —— 官方 PC 端目前仍是 `1.1.7`（`service/version/check/pc` 可查），暂时不用跟；但一旦这条线更新，全部请求会因为签名一起挂掉，且错误只有一个 `439`

**已知线索**（都已验证过，别重复）：

- 网络层**在 Dart / Flutter AOT**，不在原生 dex —— 5 个 `classes*.dex`（36.5 MB）搜 `service/collect`、`comments/v3` 命中 0
- salt 是 `kuwotest` —— 在 5.2.5 与 5.9.8 的 `libapp.so` 里各出现一次，和桌面端同一个盐
- 参数是**拼 query 字符串**构造的 —— 存在 `&source=`、`&sourceId=`、`&resourceId=` 这类碎片；`service/collect/sort?source=4` 把 query 直接写进了路径字面量
- 收藏族的标识符：`CollectSourceInfo`、`collectId`、`collectType`、`_collectSongList`、`_setSubmitCollect`、`OP_COLLECT` / `OP_UNDO_COLLECT`

**已排除的两条路**（别重复走）：

- **字符串相邻法** —— Dart AOT 不保证字面量相邻，能捞到碎片但兄弟键不在旁边，拼不出完整请求
- **ARM64 `ADRP+ADD` xref 扫描** —— Dart 用对象池 `LDR Xn, [Xpool, #off]` 加载字符串，不走 ADRP，扫不到引用者

**下一步要用正经工具**：解析 Flutter AOT 快照的 **`blutter`**，或 **Ghidra**。拿 Python 抠字节到不了。

**包已经解好了**：`apk/波点音乐_5.2.5.apk`（arm64-v8a）、`apk/波点音乐_5.9.8.apk`（armeabi-v7a）。
**临时解包产物**在 `%TEMP%\bodian_apk\`（`525_libapp.so` 27 MB、`598_libapp.so` 31 MB、`all.dex` 36.5 MB），做这项时能省一次解包。

---

## C. 工程（纯本地，不被 A/B 阻塞）

现在仓库里只有 `tools/Bodian.Probe`（一次性探针）+ `docs/` + `fixtures/`。**没有 `Bodian.sln`**。

| 阶段 | 内容 | 粗估 | 依赖 |
| --- | --- | --- | --- |
| **P1** | 骨架与传输层（`Bodian.Core` / `Bodian.WinUI` / `Bodian.Core.Tests`） | 3–5 天 | 无 |
| **P1.5** | 透明悬浮窗 spike（win10 上能否同时做到逐像素透明+置顶+点击穿透+可拖动） | 1–2 天 | P1 |
| P2 起 | 见 `roadmap.md` 的阶段表 | | |

**P1 的关键约束**（细节见 `tech-stack.md`）：

- `Bodian.Core` 用纯 `net10.0`、**不带 `-windows` TFM** —— 协议、签名、歌词解码全部可单测
- 打包形态已定 **unpackaged**；凭据只能走 **DPAPI**（`PasswordVault` 在 unpackaged 下不可用）
- 签名的黄金用例见 `fixtures/sign-golden.json`，**`verified` 是 `false`** —— 它只是候选形态，别当成已验证的事实

**P1.5 如果过不了**，P5/P6 的歌词方案要整个重估 —— 这正是把它提前的原因。

---

## D. 本机状态提醒

| 项 | 说明 |
| --- | --- |
| 探针会话 | **是登录态的**（小号 uid=50303440，活动 VIP）。`whoami` 查；不用了跑 `logout`。凭据用 DPAPI 加密存在 `%LOCALAPPDATA%\Bodian\session.dat` |
| devid | `%LOCALAPPDATA%\Bodian\devid.txt`。**别重新生成** —— 频繁变设备标识是账号风控的异常信号 |
| 临时解包文件 | `%TEMP%\bodian_apk\`，约 95 MB，可删（做 B1 时留着省事） |
| Windows Terminal | 探针的二维码渲染用半块字符，终端要 UTF-8 字体 |

---

## 完成一项后要做的

改 `bodian-api-reference.md` 对应章节的标记（🟡 → ✅ 或 ❌），并在**第 10 节追加一条记录**（五轮记录的格式照抄）。这样下一轮接手的人不用重新验证。
