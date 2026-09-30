# 波点音乐 WinUI 3 客户端

非官方第三方桌面客户端，目标平台 Windows 10 / 11。

> **当前状态：P0（协议探针）进行中，尚无客户端代码。**
> 协议逆向与选型已完成，`checkRight` 链路已跑通；签名验证、登录、歌词解析等仍待实测。
> 进度与下一步见 [`docs/roadmap.md`](docs/roadmap.md)。

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
| `docs/` | 逆向勘查记录与设计方案。**开工前先读 [`docs/roadmap.md`](docs/roadmap.md)** |
| `tools/Bodian.Probe/` | P0 协议探针，一次性控制台工具，不进主工程 |
| `fixtures/` | 已脱敏的真实响应样本，供 P1 的单元测试使用 |
| `apk/` | 逆向用的原始安装包，**不入版本控制**（283 MB 第三方二进制，需自行放置） |

## 文档

| 文档 | 用途 |
| --- | --- |
| [`roadmap.md`](docs/roadmap.md) | **分阶段执行计划**，先读这份 |
| [`bodian-api-reference.md`](docs/bodian-api-reference.md) | 接口主文档：传输层 / 签名 / 已验证接口 / 数据模型 / 音质档位 |
| [`bodian-api-inventory.md`](docs/bodian-api-inventory.md) | 逆向勘查记录，查「这个路径从哪来」时看 |
| [`tech-stack.md`](docs/tech-stack.md) | 技术栈选型：.NET / WinAppSDK / 音频引擎 / SMTC / 工程结构 |
| [`lyrics-ui.md`](docs/lyrics-ui.md) | 歌词界面方案与第三方代码的许可边界 |
| [`dev-environment.md`](docs/dev-environment.md) | 开发环境（本机实测状态，换机器时对照） |

## 构建与运行

环境要求见 [`docs/dev-environment.md`](docs/dev-environment.md)（.NET 10 SDK + Windows SDK 10.0.26100）。当前只有探针：

```bash
dotnet build tools/Bodian.Probe
dotnet run --project tools/Bodian.Probe -- --help
```

## 许可

[GPL-3.0](LICENSE)。

移植第三方代码时注意许可边界：**GPL-3.0 的代码可直接移植，AGPL-3.0 的不行**（两者单向兼容）。
完整清单见 [`docs/lyrics-ui.md`](docs/lyrics-ui.md) 的许可边界一节。
