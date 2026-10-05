# 波点音乐 WinUI 3 客户端

非官方第三方桌面客户端，目标平台 Windows 10 / 11。

> **当前状态（2026-10-04）：P0–P5、P7 与 P8 评论主流程完成，1021 个离线测试通过。**
> 已支持登录、综合与分类搜索、热榜与本地搜索历史、播放、SMTC、曲库与歌单、评论与回复；
> 搜索默认分段显示综合结果，“更多”进入分类 tab，入口面板与主题浮窗使用相同的半透明背景。
> 搜索框左侧统一提供返回按钮（间距 8 DIP），支持逐级返回；歌词页保留原来的 ↓ 收起按钮。
> 音质支持标准 / HQ / SQ 三档；160 DIP 菜单保留真实大小、以背景表示选中，播放器只显示档位名。
> 歌词页入口紧邻评论按钮，切换保留进度与暂停状态并记住偏好。
> 播放器按「封面 → 歌曲信息 → 收藏／分享」紧凑排列；两个入口显示全站数量角标，暂不执行点击动作。
> 歌词页左下角显示相同入口，VIP／付费标识移到顶部标题旁。
> 高级三档依赖官方手机客户端，本客户端不提供；范围说明见 [`docs/audio-quality-audit.md`](docs/audio-quality-audit.md)。
> 搜索说明见 [`docs/search.md`](docs/search.md)。歌词界面已替换为参考 LyciaMusic 的全窗口沉浸设计，
> 包含透明封面背景、逐字渐变、弹簧滚动、长音效果、滚动浏览和点击歌词跳转，
> 并加入通栏进度条、真实音频频谱、水面封面倒影，以及普通窗口和全屏统一的操作栏自动收起。
> 窗口、动态绘制、导航和图片加载已按 120 fps 目标优化，验证范围与实际限制见
> [`docs/performance.md`](docs/performance.md)；歌词交互说明见 [`docs/fullscreen-lyrics.md`](docs/fullscreen-lyrics.md)。
> 评论界面支持主题、数量角标、面板内图片缩放／拖动、文字发布和点赞，见 [`docs/comments-ui.md`](docs/comments-ui.md)。
> 分页列表改为滚到末尾自动续加载，取完后列表末尾提示「没有更多了哦~」，
> 机制与各页挂载点见 [`docs/list-paging.md`](docs/list-paging.md)。
> 侧栏新增「收藏的歌单」（与「收藏的专辑」是两个概念）；歌单详情的收藏、专辑详情页的收藏、
> 歌手页的关注三处都改成两态，取消操作先弹确认框；协议结论见 [`docs/collect-follow.md`](docs/collect-follow.md)。
> 歌单详情页补成与专辑页同构的头部（封面、播放量、创建者、简介、播放全部 / 收藏 / 分享），
> **收藏按钮只对别人的歌单出现**；实现与验收清单见 [`docs/playlist-detail.md`](docs/playlist-detail.md)。
> 播放栏传输组扩为五键（播放模式 · 上一首 · 播放/暂停 · 下一首 · 播放列表），
> 播放模式支持顺序播放 / 列表循环 / 列表随机三种并记住选择；「播放列表」按钮打开的是
> 内存播放队列的右侧抽屉，可切歌、删单曲、清空，曲目行的「更多」菜单另有
> 「下一首播放」与「加入播放队列」两条入口。队列结构、模式语义与图标来源见
> [`docs/play-queue.md`](docs/play-queue.md)。
> **八个歌曲列表页右上角新增列表工具栏**（全部加入播放列表 / 多选 / 刷新），多选后可批量
> 加入播放列表、加入喜欢、加入歌单，自建歌单与「我喜欢的」还能批量移出；页脚那颗「重新加载」
> 已删、刷新挪到工具栏。**列表里点一首歌不再把整个列表拉进队列**，而是把这一首追加到队尾
> 并立即播放，队列里原有的歌都留着。见 [`docs/track-list-toolbar.md`](docs/track-list-toolbar.md)。
> **新增 MV 播放**：曲目行角标、「更多」菜单、播放条三处入口，打开一个与歌词页并列的
> 全窗沉浸 MV 页，带进度、音量、全屏与四种画面比例，并在看 MV 时自动暂停音频、退出恢复。
> 协议与验收见 [`docs/mv.md`](docs/mv.md)。
> 进度见 [`docs/roadmap.md`](docs/roadmap.md)，待办见 [`docs/backlog.md`](docs/backlog.md)。
> **桌面歌词已完成并于 2026-10-05 验收**：悬停背景与图标控制、固定字形逐字高亮、双行对齐、
> 鼠标穿透、锁定、拉伸和屏幕吸附。实现与验证见 [`docs/desktop-lyrics.md`](docs/desktop-lyrics.md)。

## 为什么做这个

官方桌面端缺下载、评论、收藏歌单几块功能，且播放状态不对系统暴露，Lyricify 这类歌词工具读不到。

本项目用 WinUI 3 + libmpv 自己做一个，并把播放状态通过 SMTC 暴露出去。

## 非官方声明

本项目是**个人学习用途的非官方第三方客户端**，与波点音乐官方无任何关联，未获其授权或认可。

- 不打包、不分发官方客户端的任何二进制（含 `libmpv-2.dll`、图标、文案）
- 不含遥测与日志上报
- 音源授权完全由服务端接口裁决，客户端不绕过任何权限
- 请通过官方渠道支持正版

## 目录

| 路径 | 说明 |
| --- | --- |
| `src/Bodian.Core/` | `net10.0`，零 UI 依赖：传输层 / 门面 / DTO / 领域模型 / 分页 / 歌词解码 / 凭据。**可独立单测** |
| `src/Bodian.WinUI/` | `net10.0-windows10.0.26100.0`，WinUI 3。页面 / ViewModel / libmpv 播放引擎 / 导航 |
| `tests/Bodian.Core.Tests/` | xunit.v3，用 `fixtures/` 的真实响应做断言，**零真实网络请求** |
| `docs/` | 逆向勘查记录与设计方案。**开工前先读 [`docs/roadmap.md`](docs/roadmap.md)** |
| `tools/Bodian.Probe/` | P0 协议探针，一次性控制台工具。**不参与 `Bodian.sln`**，但必须保持可独立构建 |
| `libmpv/` | 音频引擎的原生库（115 MB，**不入版本控制**）。来源、校验和与复现步骤见该目录的 README |
| `fixtures/` | 已脱敏的真实响应样本，单测的输入 |
| `apk/` | 逆向用的原始安装包，**不入版本控制**（283 MB 第三方二进制，需自行放置） |

## 文档

| 文档 | 用途 |
| --- | --- |
| [`roadmap.md`](docs/roadmap.md) | **分阶段执行计划**，先读这份 |
| [`transport.md`](docs/transport.md) | **工程骨架与传输层的落地设计稿**：csproj 全文 / 类清单 / DTO 映射 / 测试清单 / 验收命令 |
| [`bodian-api-reference.md`](docs/bodian-api-reference.md) | 接口主文档：传输层 / 签名 / 已验证接口 / 数据模型 / 音质档位 |
| [`audio-quality-audit.md`](docs/audio-quality-audit.md) | 音质接口复核与解密交接：文档误读、APK 调用链、真实样本及未解决项 |
| [`bodian-api-inventory.md`](docs/bodian-api-inventory.md) | 逆向勘查记录，查「这个路径从哪来」时看 |
| [`tech-stack.md`](docs/tech-stack.md) | 技术栈选型：.NET / WinAppSDK / 音频引擎 / SMTC / 工程结构 |
| [`lyrics-ui.md`](docs/lyrics-ui.md) | 歌词界面方案与第三方代码的许可边界 |
| [`search.md`](docs/search.md) | 搜索入口、热榜与历史悬浮面板、综合分段、分类分页及验证记录 |
| [`comments-ui.md`](docs/comments-ui.md) | 评论入口与角标、列表与回复、发布和点赞、主题与图片查看、接口约束及验证 |
| [`list-paging.md`](docs/list-paging.md) | 列表滚到末尾自动续加载：触发机制、补屏行为、失败重试、挂载点与验收 |
| [`collect-follow.md`](docs/collect-follow.md) | 收藏歌单与关注歌手：接口、判定机制、两态按钮与取消确认、验收清单 |
| [`play-queue.md`](docs/play-queue.md) | 播放队列与播放模式：队列的「排列 + 游标」结构、三种模式语义、加入队列入口、右侧抽屉、图标来源与验证 |
| [`fullscreen-lyrics.md`](docs/fullscreen-lyrics.md) | 当前全屏歌词实现、滚动选句与点击跳转、验证及性能待办 |
| [`mv.md`](docs/mv.md) | MV 播放：三处入口、沉浸 MV 页、画面比例、音视频互斥与验收 |
| [`mini-player.md`](docs/mini-player.md) | 小窗（迷你播放器）：透明圆角浮窗、悬停抽屉与传输区、按空间择向的队列面板、贴边收起与验收 |
| [`dev-environment.md`](docs/dev-environment.md) | 开发环境（本机实测状态，换机器时对照） |
| [`backlog.md`](docs/backlog.md) | 未完成事项交接单 |

## 构建与运行

环境要求见 [`docs/dev-environment.md`](docs/dev-environment.md)（.NET 10 SDK + Windows SDK 10.0.26100）。

```bash
# 客户端工程
dotnet build Bodian.sln -c Debug
dotnet test  --project tests/Bodian.Core.Tests/Bodian.Core.Tests.csproj

# 启动（unpackaged + self-contained；「构建通过 ≠ 能跑」）
timeout 8 ./src/Bodian.WinUI/bin/Debug/net10.0-windows10.0.26100.0/win-x64/Bodian.WinUI.exe
# 退出码 124 = 跑满 8 秒被 timeout 杀掉 = 窗口一直开着，这才是正常

# P0 探针（故意不在 sln 里，避免每次构建都被它拖住）
dotnet build tools/Bodian.Probe
dotnet run --project tools/Bodian.Probe -- --help
```

**测试项目依赖 `global.json`**：`dotnet test` 走 Microsoft.Testing.Platform 靠它选择加入，
所以命令要带 `--project`（MTP 模式下不接受位置参数）。详见 [`docs/transport.md`](docs/transport.md) 第 1.4 节。

## 许可

[GPL-3.0](LICENSE)。

移植第三方代码时注意许可边界：**GPL-3.0 的代码可直接移植，AGPL-3.0 的不行**（两者单向兼容）。
完整清单见 [`docs/lyrics-ui.md`](docs/lyrics-ui.md) 的许可边界一节。
