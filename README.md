# 波点音乐 WinUI 3 客户端

非官方第三方桌面客户端，目标平台 Windows 10 / 11。

> **当前状态（2026-10-02）：P0–P5、P7 与 P8 评论主流程完成，677 个离线测试通过。**
> 已支持登录、综合与分类搜索、热榜与本地搜索历史、播放、SMTC、曲库与歌单、评论与回复；
> 搜索默认分段显示综合结果，“更多”进入分类 tab，入口面板与主题浮窗使用相同的半透明背景。
> 搜索框左侧统一提供返回按钮（间距 8 DIP），支持逐级返回；歌词页保留原来的 ↓ 收起按钮。
> 搜索说明见 [`docs/search.md`](docs/search.md)。歌词界面已替换为参考 LyciaMusic 的全窗口沉浸设计，
> 包含透明封面背景、逐字渐变、弹簧滚动、长音效果、滚动浏览和点击歌词跳转，
> 并加入通栏进度条、真实音频频谱、水面封面倒影，以及普通窗口和全屏统一的操作栏自动收起。
> 窗口、动态绘制、导航和图片加载已按 120 fps 目标优化，验证范围与实际限制见
> [`docs/performance.md`](docs/performance.md)；歌词交互说明见 [`docs/fullscreen-lyrics.md`](docs/fullscreen-lyrics.md)。
> 评论界面支持主题、数量角标、面板内图片缩放／拖动、文字发布和点赞，见 [`docs/comments-ui.md`](docs/comments-ui.md)。
> 进度见 [`docs/roadmap.md`](docs/roadmap.md)，待办见 [`docs/backlog.md`](docs/backlog.md)。
> P6 桌面悬浮歌词及其 P1.5 透明窗口验证仍未完成。

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
| [`bodian-api-inventory.md`](docs/bodian-api-inventory.md) | 逆向勘查记录，查「这个路径从哪来」时看 |
| [`tech-stack.md`](docs/tech-stack.md) | 技术栈选型：.NET / WinAppSDK / 音频引擎 / SMTC / 工程结构 |
| [`lyrics-ui.md`](docs/lyrics-ui.md) | 歌词界面方案与第三方代码的许可边界 |
| [`search.md`](docs/search.md) | 搜索入口、热榜与历史悬浮面板、综合分段、分类分页及验证记录 |
| [`comments-ui.md`](docs/comments-ui.md) | 评论入口与角标、列表与回复、发布和点赞、主题与图片查看、接口约束及验证 |
| [`fullscreen-lyrics.md`](docs/fullscreen-lyrics.md) | 当前全屏歌词实现、滚动选句与点击跳转、验证及性能待办 |
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
