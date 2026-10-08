# 文档索引

面向开发者的设计文档与逆向勘查记录。**对外说明看仓库根的 [`README.md`](../README.md)**，这里是从前挂在它下面的那份索引。

## 开始前

**先读 [`roadmap.md`](roadmap.md)** —— 分阶段执行计划；未完成事项看 [`backlog.md`](backlog.md)。

## 目录结构

| 路径 | 说明 |
| --- | --- |
| `src/Bodian.Core/` | `net10.0`，零 UI 依赖：传输层 / 门面 / DTO / 领域模型 / 分页 / 歌词解码 / 凭据。**可独立单测** |
| `src/Bodian.WinUI/` | `net10.0-windows10.0.26100.0`，WinUI 3。页面 / ViewModel / libmpv 播放引擎 / 导航 |
| `tests/Bodian.Core.Tests/` | xunit.v3，用 `fixtures/` 的真实响应做断言，**零真实网络请求** |
| `docs/` | 逆向勘查记录与设计方案 |
| `tools/Bodian.Probe/` | P0 协议探针，一次性控制台工具。**不参与 `Bodian.sln`**，但必须保持可独立构建 |
| `tools/mpv/` | libmpv 音频精简构建的工具链锁定与构建脚本 |
| `libmpv/` | 音频精简版原生库（7.97 MiB）。**已随仓库提供**，克隆后无需额外下载 |
| `installer/` | NSIS 安装脚本。设计与构建方式见 [`release.md`](release.md) |
| `fixtures/` | 已脱敏的真实响应样本，单测的输入 |
| `apk/` | 逆向用的原始安装包，**不入版本控制**（283 MB 第三方二进制，需自行放置） |
| `artifacts/` | 本机可再生的中间产物，**不入版本控制** |

## 按主题

### 计划与工程

| 文档 | 用途 |
| --- | --- |
| [`roadmap.md`](roadmap.md) | 分阶段执行计划，先读这份 |
| [`backlog.md`](backlog.md) | 未完成事项交接单 |
| [`tech-stack.md`](tech-stack.md) | 技术栈选型：.NET / WinAppSDK / 音频引擎 / SMTC / 工程结构 |
| [`transport.md`](transport.md) | 工程骨架与传输层的落地设计稿：csproj 全文 / 类清单 / DTO 映射 / 测试清单 / 验收命令 |
| [`dev-environment.md`](dev-environment.md) | 开发环境（本机实测状态，换机器时对照） |
| [`release.md`](release.md) | **打包与发布**：三个产物的差别、NSIS 安装器设计、本地复现、首次发版准备 |
| [`size-optimization.md`](size-optimization.md) | **编译产物体积优化主文档**：AI/ML 组件剔除、语言资源裁剪、PDB 开关、自包含发布的取舍 |

### 接口与协议

> 接口主文档、逆向勘查记录与音质复核这三份**已不再公开**，见下面[不公开的部分](#不公开的部分)。

| 文档 | 用途 |
| --- | --- |
| [`collect-follow.md`](collect-follow.md) | 收藏歌单与关注歌手：接口、判定机制、两态按钮与取消确认、验收清单 |
| [`comments-ui.md`](comments-ui.md) | 评论入口与角标、列表与回复、发布和点赞、主题与图片查看、接口约束及验证 |
| [`mv.md`](mv.md) | MV 播放：三处入口、沉浸 MV 页、画面比例、音视频互斥与验收 |
| [`list-paging.md`](list-paging.md) | 列表滚到末尾自动续加载：触发机制、补屏行为、失败重试、挂载点与验收 |

### 界面与交互

| 文档 | 用途 |
| --- | --- |
| [`ui-refresh.md`](ui-refresh.md) | UI 设计与实施记录：主题、渐变与外壳；§10 底部播放器，§11 标题栏与登录，§12 侧栏与列表间距 |
| [`icons.md`](icons.md) | 图标来源与许可（Fluent System Icons） |
| [`animations.md`](animations.md) | 页面 / Tab / 卡片切换、共享封面、歌词展开回位、主题资源保护与验证 |
| [`lyrics-ui.md`](lyrics-ui.md) | 歌词界面方案与第三方代码的许可边界 |
| [`fullscreen-lyrics.md`](fullscreen-lyrics.md) | 全屏歌词实现、滚动选句与点击跳转、验证及性能待办 |
| [`desktop-lyrics.md`](desktop-lyrics.md) | 桌面歌词：悬停控制、固定字形扫色、穿透、拉伸、吸附与验收 |
| [`search.md`](search.md) | 搜索入口、热榜与历史悬浮面板、综合分段、分类分页及验证记录 |
| [`play-queue.md`](play-queue.md) | 播放队列与播放模式：队列结构、三种模式语义、加入队列入口、右侧抽屉 |
| [`playlist-detail.md`](playlist-detail.md) | 歌单详情页头部、收藏按钮的显示条件、实现与验收清单 |
| [`track-list-toolbar.md`](track-list-toolbar.md) | 列表工具栏、多选与批量操作；「点行 = 追加一首」的语义变更 |
| [`mini-player.md`](mini-player.md) | 小窗：透明圆角浮窗、悬停抽屉与传输区、贴边收起与验收 |
| [`tray.md`](tray.md) | 系统托盘、关闭到托盘与单实例 |
| [`settings.md`](settings.md) | 设置页：入口与页内布局、三种值的分野、应用内快捷键、封面缓存与存储清理 |
| [`create-playlist.md`](create-playlist.md) | 侧栏新建歌单与刷新：接口实测结论、零歌单门控、对话框与验收 |
| [`edit-playlist.md`](edit-playlist.md) | 歌单编辑 |
| [`like-share.md`](like-share.md) | 收藏与分享入口 |
| [`library-sidebar.md`](library-sidebar.md) | 曲库侧栏 |
| [`discover-card-strip.md`](discover-card-strip.md) | 发现页的横向卡片 |
| [`ranking-cards.md`](ranking-cards.md) | 榜单页 |
| [`track-link.md`](track-link.md) | 曲目行内的歌手 / 专辑跳转；末尾记着行内链接光标不稳这一**未解决**问题 |
| [`ime-candidate-window.md`](ime-candidate-window.md) | 第三方输入法候选框不显示的排查记录与最终结论 |
| [`multi-account.md`](multi-account.md) | 多账号下的本地数据作用域：设备级 / 个人级 / 账号级三分与旧布局迁移 |
| [`account-switch.md`](account-switch.md) | 账号快捷切换：记住的账号清单（DPAPI）、切号不重新认证、登出语义 |
| [`anonymous-browse.md`](anonymous-browse.md) | 匿名浏览：未登录也能进外壳、匿名可用的能力（含实测表） |
| [`phone-login.md`](phone-login.md) | 手机号登录 |
| [`performance.md`](performance.md) | 120 fps 目标的优化范围与验证限制 |
| [`libmpv-audio-build.md`](libmpv-audio-build.md) | **libmpv 精简构建维护主文档**：115.22 → 7.97 MiB、裁剪范围、构建、验证、替换、回退与排障 |

## 归档

[`archive/`](archive/) 放已经完成、不再需要跟进的分阶段记录。
