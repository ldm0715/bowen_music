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
| `archive/` | **阶段归档**：每个阶段解决了什么、踩过哪些坑、哪些判断被推翻了。**冻结文档**，有变化写活文档再另开一份 |

## 现状一句话

P0（协议探针）五轮实测 + **第六轮静态分析**做完，**播放链路已验证可用** —— 无损 FLAC 能拿到、`ffplay` 能解出。
**协议空白基本清零**：收藏写入的 body、移动端签名算法、评论的键名、下载接口的增量、`freeSign` 的来源
全部从 APK 的 Flutter AOT 里静态读出来了（零请求）。剩下的都是「读代码读得不够细」或「必须发一次请求验证」
的小尾巴，不再有「不知道去哪找」的项。

**P1（骨架与传输层）也已完成**：`Bodian.sln` + `Bodian.Core` + `Bodian.WinUI` + `Bodian.Core.Tests`
四个部分就位。

**P2 / P3 / P4 代码已完成（2026-09-30，294 个测试全绿、0 跳过、构建 0 警告）**：
登录、搜索、播放、进度条之外，现在还有**歌词**（数据层 + 一个最小歌词面板）与**系统媒体控件**
（借壳方案已在 Win10 19045 上实测成立，见 `tech-stack.md` §4）—— 后者正是项目的原始动机。
落地点、偏离与踩过的坑见下面「P4 + P3」一节。

**下一步的先手是 P1.5 的透明悬浮窗 spike**（仍未做）：它决定 P5/P6 的歌词窗方案要不要改。
歌词主界面（P5，Win2D 逐字）现在可以开工了 —— P4 已把模型与解析铺好。

## 做事的规矩

1. **动手前先说明要做什么**，尤其是外部请求、写操作、装工具、改配置。多步操作先给清单（含预计请求数/副作用），等确认再做
2. **别把第三方服务当压测。** 涉及 API 的探测要节流；先想能不能用零副作用的方法解决
3. **写操作要先能读。** 没有读回确认的写操作等于盲写

**探针工具**：`tools/Bodian.Probe`，已编译在 `tools/Bodian.Probe/bin/Debug/net10.0-windows/bodian-probe.exe`。`--help` 看用法，`whoami` 看当前会话。

---

## A. 协议空白 —— **第六轮（纯静态、零请求）已全部解完**

第六轮换了工具链（`blutter` 解析 Flutter AOT 快照），本节原本「需要发请求」的项**全部改用静态分析解决**。
工具链、靶子、复跑步骤与完整证据链在 [`../reverse/README.md`](../reverse/README.md)，
结论已回写 `bodian-api-reference.md` 对应章节与第 10 节第 6 轮。

**靶子限制（重要）**：能反编译的只有 **`apk/波点音乐_5.2.5.apk`（arm64 / Dart 2.19.6）**。
5.9.8 只有 **armeabi-v7a**，blutter 处理不了；官方 PC 客户端的 `app.so` 是 **x64**，而 blutter
**没有 x64 代码分析后端**（`sourcelist.cmake` 无条件编译 arm64 的分析器，`src/` 下没有 `_x64` 版本）。
所以下面所有结论都是**移动端**的，桌面端只有一份间接证据。**下次要反编译最新版，必须先确认拿到 arm64 包。**

### A1. ✅ `service/collect` 的 body 字段 —— 已解

```
POST /api/service/collect    Body: {"source": <int>, "sourceId": [<int>...], "op": 1|2, "uid": <int>}
```

来源是 `PlayListModel::doCollectSongList` 里 `Map._fromLiteral` 的 8 槽字面量数组，`uid` 落在 `[7]`
与 `ArrayStore r1[7]` 吻合；`sourceId` **静态确认是 List**，与上一轮「传数组才过解析」的实测一致。
详见 `bodian-api-reference.md` 3.3 节与 [`reverse/findings/01-collect-write.md`](../reverse/findings/01-collect-write.md)。

**残留**：`op` 的 1/2 方向；`source` 被强制成 `6` 的那个分支；
**以及这是移动端报文 —— PC 客户端也调这个端点且实现不同，本轮无法分析 PC 端。**

### A2. ✅ 取消 —— 客户端根本不消费 `paytagindex`

第六轮在全工程搜 `paytagindex`：**0 命中**。无论它编码什么，客户端不做判断，
本项目也不该用它推断账号权限。「用 VIP 账号对免费曲/付费曲各取一次对比」这条待办**作废**。

### A3. ✅ 已定论：**没有增量**

`download/info` 接受与 `audioUrl` **同一套 `br`/`format`**，只是多了 `down=110`/`isMv`/`type`；
`download/config` 是无参 GET，返回配额；`callback/success` 是 POST `{type, musicId}` 的上报。
**本项目继续用 `audioUrl`，不引入这三个接口。** 详见 3.2 节。

### A4. 其余小项

| 项 | 结果 |
| --- | --- |
| `service/collect/sort` 参数 | ✅ `POST service/collect/sort?source=4`，body `{"ids": [...]}` |
| `zp` / `bcms` 两档的有效 `br` | ⚠️ **改判**：规律已实证为 `<码率>k<格式>`（样本 `"48kaac"`），且 `br` 由服务端下发的 `audios[]` 拼出，客户端没有第二套映射表。被降级说明**服务端拒绝该组合**，不是客户端填错名字。静态已到头，只能实测 |
| `payInfo.local_encrypt` 语义 | ✅ **作废**：客户端零消费点（只出现在 `PayInfo` 的 toJson/fromJson 里），没有可观察行为。本项目按纯透传处理 |
| `freeSign` 的签发接口 | ✅ **不存在**：它是服务端在 `payInfo` 里下发的字段，客户端只透传（`fromJson` 读进来 → 防盗链参数表带回去）。看广告之后服务端才带上它 —— 这正好解释了「找不到签发接口」 |
| **（新增）**评论的键名 | ✅ **是 `moduleType`（=2）+ `moduleId`**，不是 `musicId`/`resourceId` —— 解释了上一轮 16 个端点全返回空 |

---

## B. 需要换工具链的逆向（离线，零请求）—— **第六轮已做完**

### B1. ✅ 移动端签名算法 —— **已还原**

**结论**：实现是 `HttpUtils::encryptParam`，而且**名字有误导性 —— 它是签名，不是加密**。
触发条件：`method` 是 **post / delete / put** 且 `content-type` 是 `application/json`；
**GET 请求根本不签名**。

与桌面版（`bodian-api-reference.md` 1.3 节）最大的差异，也就是 `439` 的根因：

> **移动端的排序串覆盖整条 URL 的字母数字**（host + path + query 揉在一起，去掉非 `[a-zA-Z0-9]` 后升序），
> 而桌面端只签 **query 串**、把 path 单独拼在末尾。上一轮试的十种变体**没有一种覆盖「整条 URL 排序」这一支**。

完整流程、字符串常量、剩余三处未确证的细节（`seed` 末段取 `uri` 的哪个成员、`kpk` 的值、body 摘要的分支条件）
都在 [`reverse/findings/03-sign-mobile.md`](../reverse/findings/03-sign-mobile.md)。

**⚠️ 版本标注**：取自 **5.2.5**，不是最新版（5.9.8 是 arm32，blutter 处理不了）。
盐 `kuwotest` 在两版里都各出现一次、收藏族标识符形态一致，所以算法大概率没变，**但这是推断**。

**它仍然阻塞** P8 评论与「`ver` 能不能跟」—— 还原出来了不等于验证过了，
要证实必须发一次 `ver ≥ 3.5` 的请求（属写探测，账号风控要先想清楚）。

<details>
<summary>这条是怎么走通的（上一轮的方法学留档，别重复走）</summary>

**已知线索**（上一轮已验证）：

- 网络层**在 Dart / Flutter AOT**，不在原生 dex —— 5 个 `classes*.dex`（36.5 MB）搜 `service/collect`、`comments/v3` 命中 0
- salt 是 `kuwotest` —— 在 5.2.5 与 5.9.8 的 `libapp.so` 里各出现一次，和桌面端同一个盐
- 收藏族的标识符：`CollectSourceInfo`、`collectId`、`collectType`、`_collectSongList`、`_setSubmitCollect`、`OP_COLLECT` / `OP_UNDO_COLLECT`

**两条走不通的路**：

- **字符串相邻法** —— Dart AOT 不保证字面量相邻，能捞到碎片但兄弟键不在旁边，拼不出完整请求
- **ARM64 `ADRP+ADD` xref 扫描** —— Dart 用对象池 `LDR Xn, [Xpool, #off]` 加载字符串，不走 ADRP，扫不到引用者

**第六轮证明换 `blutter` 是对的**：上面那些线索全部变成了可直接读出的代码，
连原始 Dart 文件路径（`allin/service/api_service.dart`、`allin/models/playlist/play_list_model.dart`…）都还原出来了。

临时解包产物在 `%TEMP%\bodian_apk\`。**真正的靶子现在在仓库内**：`reverse/android-5.2.5/lib/arm64-v8a/`。

</details>

---

## C. 工程（纯本地，不被 A/B 阻塞）

### ✅ P1 骨架与传输层 —— 已完成（2026-09-30）

仓库现在有 `Bodian.sln` + `src/Bodian.Core` + `src/Bodian.WinUI` + `tests/Bodian.Core.Tests`，
以及 `tools/Bodian.Probe`（一次性探针，**不参与解决方案，保持可独立构建**）。
**落地设计稿与实现偏差见 [`transport.md`](transport.md)**，那里有 csproj 全文、类清单、
DTO 字段映射与验收命令。实测：解决方案 0/0、探针 0/0、**152 个测试全绿 0 跳过**、
WinUI 退出码 124、日志里凭据全被替换、探针会话仍可读。

P1 **没做**的：`IBodianApi` 门面与 `Models/` 领域模型 —— 推迟到 P2，
理由见 `transport.md` 第 10 节。歌单与评论 DTO 也推迟（无 fixture 可验证）。

### ✅ P4 歌词数据层 + ✅ P3 SMTC —— 代码完成（2026-09-30）

**P4 的一半在 P1 就做完了**（`BodianLyricPayload` 请求构造 + 一次 Base64、`KuwoFactorCodec` 八进制系数与
`DecodeWord`），这轮补的是后半段：

| 新增 | 内容 |
| --- | --- |
| `Core/Models/Lyrics/` | `LyricDocument` / `LyricLine` / `LyricSyllable` / `LyricKind`。**时间全部归一化成绝对值**，消费方不必碰系数 |
| `Core/Lyrics/BodianLyricParser.cs` | LRC 行解析：多时间戳展开、两位小数按厘秒、元数据行跳过、逐字音节切分 |
| `Core/Lyrics/LyricRepository.cs` | 内存 LRU（32 首，空结果也缓存）。理由：别把第三方服务当压测 |
| `Core/Api` | `BodianEnvelope` 多一个 `int? Lrcx`（歌词站顶层回显，其余端点为 null）；`IBodianApi.GetLyricAsync` / `GetLyricsAsync` |
| `Core/Media/CoverArtUrl.cs` | 封面 `.webp`→`.jpg` + 尺寸提升（SMTC 用），见 `tech-stack.md` §4 |
| `WinUI/Playback/SmtcManager.cs` | SMTC 借壳、元数据/状态/时间轴/按钮/拖动 |
| `WinUI/Services/StartMenuShortcutInstaller.cs` + `ShellLinkInterop.cs` | 开始菜单快捷方式 + AUMID（`Bodian.lnk`），见下 |
| `WinUI/ViewModels/LyricsViewModel.cs` + `Controls/LyricsPanel.xaml` | 最小歌词面板（逐行高亮 + 跟随 + 点行跳转），P5 会被 Win2D 版替换 |

**233 → 294 个测试**，0 跳过。构建 0 警告，冒烟 `exit=124`。

#### 三条刻意的偏离（与前文计划不同）

| 项 | 决定 | 理由 |
| --- | --- | --- |
| `Lyricify.Lyrics.Helper` | **不引入** | 它不支持 AWLRC（行内 `<a,b>`），解析本来就得自己写；而 Core 开了 `IsAotCompatible` + `TreatWarningsAsErrors`，未标 AOT 的第三方库会直接编译失败。少一个依赖、少一份体积 |
| 「长度 < 6 的行跳过」 | **不实现** | 实测会误删 3 行真实歌词（`词：周杰伦`/`曲：周杰伦`/`鼓：陈柏州`），63 行变 60 行。测试里有守门用例 |
| 已知有逐字轨但返回空串 | **仍退一次逐行版** | `lrc_info` 与歌词站不一致时，恪守「已知就不退」只会给用户一片空白，多一次请求更划算 |

#### 实测撞到的坑（别再踩）

| 坑 | 症状 | 处置 |
| --- | --- | --- |
| **`out PropVariant` 编组坏了** | 显式布局的 `PROPVARIANT` struct 用 `out` 取回来 `vt` 不是 31 → 每次都判定「快捷方式缺 AUMID」→ 每次启动重写 `.lnk` | `PROPVARIANT` 一律用裸内存（读写两侧），见 `tech-stack.md` §4 |
| **`IPropertyStore` 属性必须 `Save` 之后写、再 `Save` 一次** | 属性写进去了但 `.lnk` 里没有：`Get-StartApps` 显示 AppID 是 exe 路径而不是 `Bodian.WinUI` | 顺序：`SetPath` → `Save` → `SetValue`+`Commit` → `Save` |
| **读 `.lnk` 属性不能走 ShellLink 上 QI 的存储** | `Load` 后直接 `GetValue` 拿到空值 | 用 `SHGetPropertyStoreFromParsingName` |
| `IPersistFile.Load` 不调就读 | new 出来的 ShellLink 是空的，`GetPath` 返回空 → 每次重建 | 读之前先 `Load(path, STGM_READ)` |

#### 本轮留下的收尾项

| 项 | 说明 |
| --- | --- |
| **SMTC 验收结果** | **已通过（2026-09-30）**。用回读诊断（`BODIAN_SMTC_DUMP=1`）确认系统侧能看到：标题/歌手/专辑正确、状态 Playing/Paused/Stopped 正确、进度与可拖区间 `0–曲长` 正确、**封面是能取到字节的真实 JPEG（约 65 KB）**。Lyricify 能识别并能显示歌词。 |
| **Win10 上验不了「拖动」** | Win10 的媒体浮层**不画 seek 条**（只有上一首/播放暂停/下一首 + 封面）—— 那是 Win11 的媒体卡片或第三方浮层的功能。`MinSeekTime`/`MaxSeekTime` 已按规范上报（回读可见 0–曲长），但**本机没法用系统 UI 拖它**。要验这一条得换 Win11，或让一个会画 seek 条的消费方来拖。 |
| **`词：周杰伦` 这类不会出现在 Lyricify 里** | 不是缺陷：SMTC **没有歌词字段**，Lyricify 也没有接受外部歌词的接口。Lyricify 显示的歌词永远是它自己去 QQ音乐/网易云等词源搜来的（通常不含 credit 行）。**我们的歌词（含 credit）只出现在本客户端的歌词面板里。** |
| **两个"波点"会话会互相干扰** | 官方 PC 端在跑时，系统里会同时有 `bodian_pc.exe` 与 `Bodian.WinUI.exe` 两个会话。Lyricify 抓哪个不由我们决定 —— 验收时先关掉官方客户端，否则结论没法归因（本轮就撞到过：封面"传不过去"其实是抓错了会话）。 |
| 开始菜单快捷方式 | 已经写进本机开始菜单（`Bodian.lnk`，AUMID=`Bodian.WinUI`，`Get-StartApps` 可核对）。**撤销 = 删掉那个 `.lnk`**；不想让它写就设 `BODIAN_SKIP_SHELL_REGISTRATION=1` |
| spike 与诊断代码 | **已删**（`SmtcSpike.cs`、`SmtcInspector.cs` 与 `App.xaml.cs` 里的三处 `[SPIKE]` 调用）。要复验时照着 `tech-stack.md` §4 的「落地实测」一节重建即可 —— 借壳那几行就是全部秘密 |
| 图标 | 本轮没做（分发红线不许打包官方资产）。SMTC 面板与开始菜单里是默认图标 |

### ✅ P2 登录 + 播放最小闭环 —— 代码完成（2026-09-30）

**Core 侧**：`BodianApi`（搜索 / 详情 / `ResolvePlaybackAsync`）、`BodianLogin`
（二维码三步 + 身份校验 + 会话持久化）、`AudioQualityTable`（选档与降级判定）、
`Models/` 领域模型、`SessionIdentity`（P1 欠下的那个纯函数）、`VipStatus`（会员判读）。
**233 个测试全绿、0 跳过**（P1 是 152）。

**WinUI 侧**：`LibMpvPlaybackService`（headless libmpv，懒初始化）、`PlayQueue` +
`PlaybackCoordinator`、登录页 / 搜索页 / 底部播放条、自研导航、账号信息（昵称 / 头像 / VIP 徽标）。
搜索列表与播放条都显示封面、专辑与付费标识。

**实测中撞到的三个坑**（都已写进代码注释，别再踩）：

| 坑 | 症状 | 处置 |
| --- | --- | --- |
| `LibMpv.LoadFile(extraArgs:)` 的参数下标错（从 2 开始写，覆盖 flags 位） | mpv 报 `Invalid flag for option loadfile` 并**静默放弃加载**，表现为 `idle-active` 恒为 1 | 绕开该重载，自己发 `loadfile <url> <flags> <index> <options>` |
| 用 `TimeSpan.MinValue` 当节流哨兵值 | 首次比较算术溢出抛 `OverflowException`，异常逃出 mpv 的事件循环 Task 把它打死 —— 此后进度、文件事件全部消失，**且进程毫无异常迹象** | 改用 `TimeSpan?` 表达「还没上报过」 |
| P1 遗留的 DI 注册错误 | 启动即崩，退出码 `0xC000027B` | `HttpMessageHandler` 必须显式注册到抽象类型上（`CreateHandler` 返回的是 `SocketsHttpHandler` 这个具体类型，按它注册就解析不到） |

**P2 没做的**：歌词（P4/P5）、SMTC（P3）、歌单与收藏（P7）、评论（P8）、下载（P9）、
音质设置页、NavigationView 外壳。

**验收状态**：搜索、播放、暂停、进度拖动、上下首、音量、账号信息、列表与播放条的信息展示、
**扫码登录**（二维码渲染 → 轮询 → `authType=10` → 身份校验 → 会话落盘）均已人工验证通过。

#### P2 留下的一条待办

| 项 | 说明 | 怎么收 |
| --- | --- | --- |
| **`feeType` 判据未闭环** | 界面上的 VIP/付费徽标来自 `payInfo.feeType`（见 `PayTypeReader`）。但现有样本 5 条**全是付费曲**（`{vip:"1", song:"1"}`），**没有一首免费歌作对照**，所以「`vip=0` 就是免费」仍是推断 —— 徽标有误报可能。**它只用于展示，不参与任何权限判断** | 搜一首免费歌采一次样本（只读请求），核对 `feeType` 的实际形态，然后回来改 `PayTypeReader` 与其测试 |

#### XAML 的两个坑（写 `x:Bind` 时容易撞）

| 坑 | 症状 | 处置 |
| --- | --- | --- |
| **函数绑定不接受 `Converter`** | `{x:Bind local:Formats.HasX(...), Converter=...}` 报 `WMC1121`（`return type Boolean must match binding target type Visibility`）。属性绑定接受转换器，函数绑定不接受 —— 同一种语法，规则不同 | 让函数直接返回目标类型（如 `Visibility`） |
| **`Uri` 不能直接绑到 `Image.Source`** | 同样报 `WMC1121` | 用函数绑定转成 `ImageSource`（见 `Formats.CoverSource`） |

| 阶段 | 内容 | 粗估 | 依赖 |
| --- | --- | --- | --- |
| **P1.5** | 透明悬浮窗 spike（win10 上能否同时做到逐像素透明+置顶+点击穿透+可拖动） | 1–2 天 | ✅ P1 已就绪（**仍未做**） |
| **P2** | 登录 + 播放最小闭环 | 1–2 周 | ✅ 代码完成 |
| P3 起 | 见 `roadmap.md` 的阶段表 | | |

**P1.5 如果过不了**，P5/P6 的歌词方案要整个重估 —— 这正是把它提前的原因。
**它是现在排在第二位的风险**（协议风险已被 P0 消除），建议接着就做。

---

## D. 本机状态提醒

| 项 | 说明 |
| --- | --- |
| 探针会话 | **是登录态的**（小号 uid=50303440，活动 VIP）。`whoami` 查；不用了跑 `logout`。凭据用 DPAPI 加密存在 `%LOCALAPPDATA%\Bodian\session.dat` |
| devid | `%LOCALAPPDATA%\Bodian\devid.txt`。**别重新生成** —— 频繁变设备标识是账号风控的异常信号 |
| 临时解包文件 | `%TEMP%\bodian_apk\`，约 95 MB，**现在可以删了** —— 第六轮把靶子复制进了 `reverse/` |
| **逆向工作区** | **`reverse/`**，约 1.3 GB（blutter 的 Dart 源码与编译产物占大头）。结构与复跑步骤见 `reverse/README.md`。**要回收磁盘就删 `reverse/_tools/blutter/{dartsdk,build}`**（下次换 Dart 版本要重来） |
| Windows Terminal | 探针的二维码渲染用半块字符，终端要 UTF-8 字体 |

---

## 完成一项后要做的

改 `bodian-api-reference.md` 对应章节的标记（🟡 → ✅ 或 ❌），并在**第 10 节追加一条记录**（格式照抄前几轮）。这样下一轮接手的人不用重新验证。

**第六轮已经照这个做了**：`bodian-api-reference.md` 的 2.5 / 3.1 / 3.2 / 3.3 / 7.3 / 8 节都已更新，
第 10 节补了第 6 轮记录，原始证据留在 `reverse/findings/`。
