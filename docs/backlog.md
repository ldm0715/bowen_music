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
| `search.md` | 当前搜索实现：综合分段、分类分页、热榜与本地历史、面板交互及验证 |
| `ui-refresh.md` | UI 设计与实施复盘；§8.3–8.5 记录渐变与透明播放区，§10 记录底部播放器，§11 记录自定义标题栏、主题弹层与居中登录，§12 记录侧栏及列表间距 |
| `archive/` | **阶段归档**：每个阶段解决了什么、踩过哪些坑、哪些判断被推翻了。**冻结文档**，有变化写活文档再另开一份 |

## 现状一句话

P0（协议探针）五轮实测 + **第六轮静态分析**做完，**播放链路已验证可用** —— 无损 FLAC 能拿到、`ffplay` 能解出。
**协议空白基本清零**：收藏写入的 body、移动端签名算法、评论的键名、下载接口的增量、`freeSign` 的来源
全部从 APK 的 Flutter AOT 里静态读出来了（零请求）。剩下的都是「读代码读得不够细」或「必须发一次请求验证」
的小尾巴，不再有「不知道去哪找」的项。

**P1（骨架与传输层）也已完成**：`Bodian.sln` + `Bodian.Core` + `Bodian.WinUI` + `Bodian.Core.Tests`
四个部分就位。

**P2 / P3 / P4 代码已完成（2026-09-30）**：
登录、搜索、播放、进度条之外，还有**歌词数据层**与**系统媒体控件**
（借壳方案已在 Win10 19045 上实测成立，见 `tech-stack.md` §4）—— 后者正是项目的原始动机。
落地点、偏离与踩过的坑见下面「P4 + P3」一节。

**P5（全屏歌词）已更新（2026-10-01）**：参考 LyciaMusic 重做沉浸布局，
透明歌词由 Win2D + Composition 绘制。逐字渐变、独立弹簧、长音浮动发光、滚轮与拖动浏览、
清晰浏览行及点击跳转已接入；已点亮部分不会因正常播放校正而回退。
用户已确认当前效果，进一步性能优化暂缓。当前实现见 `fullscreen-lyrics.md`；
下方 2026-09-30 的 P5 记录仅描述第一版，不能再当作当前架构与待办清单。

**P7 外壳与曲库已完成（2026-10-01，488 个测试全绿、构建 0 警告）**：
侧栏（`NavigationView`）取代了原来那个「一页一页往前走」的外壳，导航栈改成「根 + 详情」两层；
**发现页、排行榜、乐库、我喜欢的、最近播放（本地记录）、已购音乐、收藏的专辑、自建歌单**全部落地，
专辑详情与曲目可播。设计稿见 [`library-sidebar.md`](library-sidebar.md)，
乐库那条链路的协议与踩坑见 [`../reverse/findings/07-musiclib.md`](../reverse/findings/07-musiclib.md)。

**搜索增强已完成（2026-10-02）**：默认综合结果按返回分节展示，“更多”进入单曲、歌单、专辑或歌手 tab；
热榜与本地历史在搜索框下方的非模态面板中展示，复用主题浮窗的半透明背景，修复定位、关闭和输入焦点。
歌手作品页与分类分页已接入。590 项 Core 测试及 WinUI 构建通过，用户完成本轮界面测试。
实现说明见 [`search.md`](search.md)。

**下一步的先手是 P1.5 的透明悬浮窗 spike**（仍未做）。P5 **没有等它** ——
`lyrics-ui.md` §3 本来就把两条路线拆开了：主歌词页走 Win2D、桌面歌词条走 XAML 裁剪，
所以 spike 的结论只影响 P6。**它是现在排在第一位风险项**，建议接着做。

## 底部播放栏待接入（2026-10-01）

通栏进度分界、居中播放控制和窄窗口布局已经落地，设计与验证见 `ui-refresh.md` §10。
以下两个按钮目前均为禁用的占位入口，功能尚未完成：

| 项 | 当前状态 | 后续接入 |
| --- | --- | --- |
| 播放列表 | `PlaylistButton` 已预留，`IsEnabled="False"` | 展示现有 `PlayQueue`，接入队列中的曲目选择，与上下首使用同一队列 |
| 音质切换 | `QualityButton` 已预留，仍显示实际音质，`IsEnabled="False"` | 接入可用档位选择与播放解析，切换后继续展示服务端实际返回的音质 |

## 本轮 UI 手动验收（2026-10-01）

### 全屏歌词后续性能验证

当前交互与效果已确认，先保持本轮行为。待后续专门优化时再检查：

| 项 | 验证重点 |
| --- | --- |
| 帧率与长帧 | 区分绘制耗时、合成调度和首轮排版；按窗口尺寸、DPI、可见行数和长音数比较 |
| 高亮与弹簧 | 优化不能让正常播放高亮回退，也不能让错峰等待中的行冻结或跳变 |
| 浏览与点击 | 显示锚点附近的浏览行保持清晰，点击实际命中句子后跳到对应时间 |
| 设备丢失 | 透明合成表面的资源重建路径已接入，尚未强制模拟验收 |

实际短时帧率及可选诊断方式见 `fullscreen-lyrics.md`，尚不承诺所有场景稳定 60 fps。

2026-10-02 已完成通栏进度、真实音频频谱、水面封面倒影及普通窗口/全屏统一的自动收起，
用户已确认当前实现，记录见 `fullscreen-lyrics.md`。后续新增检查点：

| 项 | 当前范围与后续验证 |
| --- | --- |
| 频谱采样范围 | 当前分析默认输出设备的混合音频；如需仅分析本客户端，接入独立 PCM 数据 |
| 输出设备变化 | 当前在采样启动时选择默认设备，播放中切换设备的自动重连尚未实现 |
| 频谱与倒影性能 | 频谱上限 30 fps，倒影 20 fps；后续分别测量开销，不改变已确认的歌词和交互 |

### 主题与登录

侧栏收起背景、曲目列表左间距、滚动条避让与时间列行内留白已调整，记录见 `ui-refresh.md` §12。
主题弹层和居中登录的代码已完成，默认输出目录构建通过（0 错误、1 个既有 `WMC1506` 警告）。
按用户要求，以下最终界面验收由用户手动进行：

| 项 | 验收内容 |
| --- | --- |
| 主题弹层通透度 | 浅色与深色均使用 80% 不透明度，检查实际观感；竖排选项只以背景表示选中，弹层位于按钮正下方 |
| 居中扫码登录 | 退出登录后检查弹窗居中、二维码刷新及状态提示、深浅主题显示；扫码成功后弹窗关闭并进入“我喜欢的” |

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

**靶子限制（2026-10-01 更新）**：

| 靶子 | 能不能反编译 |
| --- | --- |
| **`android-5.9.8-arm64`（最新版，Dart 3.11.5）** | ✅ **能**。用户换成了 arm64-v8a 的包，产物在 `reverse/android-5.9.8-arm64/blutter/`（3144 个 `.dart`），复跑脚本 `reverse/tools/run_blutter_598.bat` |
| `apk/波点音乐_5.2.5.apk`（arm64 / Dart 2.19.6） | ✅ 能（仍在用） |
| 官方 PC 客户端的 `app.so`（x64） | ❌ 不能 —— blutter **没有 x64 代码分析后端**（`sourcelist.cmake` 无条件编译 arm64 的分析器，`src/` 下没有 `_x64` 版本） |

> **此前「5.9.8 是 armeabi-v7a、blutter 处理不了、只能用 5.2.5 代替」的说法已过时。**
> 乐库（`play/music/library/*`）、专辑、排行榜、发现页都是在**最新版**上解出来的 ——
> 那三套标识符在 5.2.5 里一个都没有（见 `findings/07-musiclib.md`）。

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

完整流程、字符串常量、剩余未确证的细节（**`body` 摘要的分支条件**）
都在 [`reverse/findings/03-sign-mobile.md`](../reverse/findings/03-sign-mobile.md)。

**⚠️ 版本标注**：本节主体取自 **5.2.5**；**2026-10-01 已用 5.9.8 arm64 重新核对并覆盖了两处结论**：
`kpk` 参数**不存在**、`seed` 末尾是**整条 URL**（不是早期猜测的 `.path`）。
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

### ✅ P5 主歌词页（逐字渲染）—— 代码完成（2026-09-30）

> 以下是第一版历史记录。2026-10-01 已替换主歌词页和渲染宿主，当前状态及后续工作见
> [`fullscreen-lyrics.md`](fullscreen-lyrics.md)。下方“本轮留下的收尾项”是当时的记录，已实现的逐字渐变、
> 长音字形绘制和封面背景不再属于当前未完成项。

**新增**：`Core/Models/Lyrics/LyricTimeline.cs`（音节级定位）、`LyricEffectMath.cs`（动效数学）、
`Core/Playback/LyricsPlaybackClock.cs`（把 5Hz 的位置上报插值成每帧连续）、`WinUI/LyricRenderer/`
（设置 / 排版 / 设备资源 / 滚动动画 / 渲染器）、`Controls/LyricsCanvasView`、`Views/LyricsPage`
（左信息 + 右歌词，整页铺满内容区、播放条保留）。

**外壳**：`Frame` → `ContentControl` + 返回栈（`INavigationService` 加 `GoBack`/`CanGoBack`，
`Reset` 清栈）。歌词从覆盖层改成真页面，返回时挂回**原来那个 `SearchPage` 实例**，搜索结果不丢。

**引入 Win2D 1.4.0，净增 2.79 MiB**（原估 4.5–5 MiB 里多算了「构建期 CsWinRT 投影」那 1.5–2 MiB，
实际不存在）。对照 `libmpv-2.dll` 的 115 MiB 是 2.4%。

**测试 294 → 357**，0 跳过；构建 0 警告。其中最有价值的一条是遍历逐字版 63 行 × 全部音节的强不变量：
每个音节的起点必须精确落在它自己身上。

#### 三条刻意的偏离

| 项 | 决定 | 理由 |
| --- | --- | --- |
| 扫光实现 | **逐字上色**（`CanvasTextLayout.SetColor`），不用渐变+遮罩、更不用裁剪层 | 上游那套要经过离屏图与效果链，本项目连撞两次墙（见下）。代价：少了「一个字唱到一半时高亮停在字中间」的像素级过渡与扫光羽化 |
| 逐行版不触发长音效果 | 用文档版式（`LyricKind`）把关 | 逐行版整行就是一个音节、时长通常 ≥700ms，不把关会让**每一行**都整行放大。上游按行判断「有没有真实音节信息」，我们的模型里版式是文档级属性 |
| 颜色不读主题画刷 | 按 `ActualTheme` 直接定 | 读 `TextFillColorPrimaryBrush` 时出现过白字配白底（字都在，一个也看不见） |

#### 踩过的四个 Win2D 坑（都别再踩）

| 坑 | 症状 | 处置 |
| --- | --- | --- |
| **渲染线程读 XAML 属性** | `CreateResources` 里读 `ActualWidth` → `E_INVALIDARG` 直接带走进程；事件日志只给 `combase.dll` + 异常码 `0xC000027B`，堆栈全丢 | 视口尺寸由 UI 线程量好写进字段，渲染线程只读字段 |
| **用控件当资源创建者** | 排版是 `Update` 里懒建的，而用控件建资源只允许在 `CreateResources` 期间 | 传 `sender.Device`（`CanvasDevice`） |
| **`CanvasCommandList` 不能复用** | 「复用一个行缓冲、每帧改内容」→ 第一帧之后必抛 `cannot be called after the CanvasCommandList has been used as an image` | 变化的内容每帧新建、用完即弃 |
| **`CreateLayer` 的裁剪矩形语义不可靠** | 带着绘制变换把它传进去，图层表面落在**没变换**的位置上，当前行整行落在图层外 → 「被什么盖住了，一个字都看不见」 | 放弃裁剪，改逐字上色；单字变换改用「把长音音节拆出来单独画」 |

另外两条：**`CanvasAnimatedControl` 不认 `Background`，只认 `ClearColor`**（所以下面垫一层主题 Border 再把颜色读给 `ClearColor`）；
**渲染回调必须自己 catch**（渲染线程上的未处理异常会带走进程且不留堆栈），现在是记 Critical 并停掉循环。

#### 本轮留下的收尾项

| 项 | 说明 |
| --- | --- |
| 扫光的像素级过渡 | 现在是逐字推进（中文一个字≈一个音节，节奏一致）。要「半个字」的平滑过渡得上渐变画刷，作为后续打磨项 |
| 错峰系数 | **上游默认 0（关闭错峰）**，本项目开启且系数自定，**没有上游默认值可对照** |
| 长音效果的实现粒度 | 上游**逐字**挂 `CropEffect` + `GaussianBlurEffect`；本项目把同一行的长音字合到一张离屏图一起模糊，少了逐字裁剪层 |
| **「与 BetterLyrics 观感一致」这条验收已失效** | 参数是从一个完整设计里摘出来的，而那个设计的其余部分（专辑封面取色、双字体、描边）都没做；又没有对照基准，**不可证伪**。实际按「自己看着舒服」验收 |
| 设备丢失自恢复 | 冒烟时**没验**（`RaiseDeviceLost` 那一条）。S0 的冒烟页已按规矩删除，要复验按 `tech-stack.md` §4 重建 |
| 翻译轨 / 音译轨、竖排、扇形/3D/呼吸、封面取色 | 仍未做，见 `roadmap.md` 的 P10 |

#### 顺带修掉的 P3 遗留缺陷

`ShellLinkInterop` 里那个清理函数名写成了 `PropVariantClearRaw` 且没写 `EntryPoint`，
于是运行时去 ole32.dll 找这个**并不存在**的导出名 → 每次启动抛 `EntryPointNotFoundException`。
它的调用点都在 `finally` 里，异常一抛整个流程就断在那里：**开始菜单快捷方式被反复重写、
却永远写不进 AUMID**（`.lnk` 的属性块字节级可验证缺失）。
修好后实测：第一次启动写入成功，第二次识别为「已是最新」不再重写。

**注意**：`Get-StartApps` 在修复**前**也显示 `AppID = Bodian.WinUI` ——
所以它**从来不能作为这条验收的依据**（P3 那次「通过」是假阳性，来源未查明，怀疑是 shell 的 Start Menu 缓存）。

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
