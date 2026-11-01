# 音效：银河音效 / 蝰蛇音效 / 环境音

手机端的三类音效在客户端本地处理后，再交给播放器输出；服务端只下发音效列表，不下发处理好的音频。
本项目的做法是用 **libmpv 的 `af` 属性 + FFmpeg 音频滤镜** 做听感近似，**不移植手机端的原生 DSP**。

## 入口

- 播放条右侧「音效」按钮（三根竖向滑杆图标）。
- 歌词页控制条同一颗按钮。
- 两处共用一个 `AudioEffectsViewModel` 单例，选择与缓存只有一份。

面板分三个页签：

| 页签 | 内容 |
| --- | --- |
| 银河音效 | 8 套预设：清澈人声、重低音、3D环绕、黑胶、LiveHouse、夜晚、开阔宽音、车载 |
| 蝰蛇音效 | 7 套预设：3D环绕、超重低音、纯净人声、虚拟现场、均衡器音效、AI智能音效、一键HiFi |
| 环境音 | 12 种循环：壁炉、海浪、鸟鸣、人潮、森林、水滴、风铃、流水、秒针、心跳、烟火、雨声 |

## 实现分层

| 层 | 位置 | 职责 |
| --- | --- | --- |
| 设置模型 | `src/Bowen.Core/Models/AudioEffectSettings.cs` | 启停、预设 id、环境音 id、环境音音量、十段 EQ；只做归一化 |
| 目录 | `src/Bowen.Core/Audio/AudioEffectCatalog.cs` | 预设名、描述、滤镜片段、环境音定义 |
| 图构建 | `src/Bowen.Core/Audio/AudioEffectGraphBuilder.cs` | 纯函数：设置 + 环境音路径 → mpv `af` 字符串 |
| 环境音素材 | `src/Bowen.Core/Audio/AmbientSoundGenerator.cs` | 从程序集解压嵌入的 12 秒 MP3 循环到本地缓存 |
| 持久化 | `JsonAudioEffectSettingsStore` | `%LOCALAPPDATA%/audio-effects.json`，临时文件 + Move |
| 播放层 | `IPlaybackService.SetAudioFilter` / `SendAudioFilterCommand` | 只碰 mpv；失败时清空链，不打断播放 |
| 面板 | `AudioEffectsViewModel` + `AudioEffectsPanel.xaml` | 页签、卡片、总开关、音量滑杆、错误提示 |

## `af` 图

只有预设时：

```text
@bowenFx:lavfi=[stereotools=slev=1.6:mlev=0.92,haas=side_gain=0.18]
```

预设 + 环境音同时存在时，合成一张图：

```text
@bowenFx:lavfi=[
  anull[main];
  [main]<预设滤镜链>[fx];
  amovie=C\\:/.../ambient.mp3:loop=0,asetpts=N/SR/TB[amb];
  [fx][amb]amix=inputs=2:weights=1 0.3:normalize=0
]
```

- `anull[main]` 是主音频的唯一未连接输入 pad；`amovie` 是 source，不是 input pad。
  这正是 mpv `af=lavfi=[...]` 能接受的形状。
- `amovie` 在 mpv **解析**滤镜图时就会打开文件；文件不存在会让整条 `af` 被拒绝，
  EQ 和预设一起消失。所以调用方必须先生成好文件再写 `af`。
- 环境音滑杆不重建图，走：

```text
af-command bowenFx weights "1 0.3" amix
```

- 路径转义：盘符 `:` 写两个反斜杠，`[ ] , ;` 写一个，单引号写三个。
  这是滤镜图解析器 + 滤镜选项解析器两层吃反斜杠的结果，见 `AudioEffectGraphBuilder.EscapeFilterPath`。

## libmpv 构建

当前 `libmpv` 的精简构建只放行了少量滤镜。音效需要重编并把这些加进
`tools/mpv/build_audio_mpv.py` 的 `FILTERS`：

```text
highpass,lowpass,bass,treble,virtualbass,asubboost,acompressor,
adynamicequalizer,dynaudnorm,stereotools,stereowiden,haas,crossfeed,
aecho,afir,amix,asplit,amovie
```

`equalizer`、`volume`、`pan`、`alimiter`、`aresample`、`asetpts` 原本就有。
构建与替换步骤见 [`libmpv-audio-build.md`](libmpv-audio-build.md)。

### 当前构建

- 2026-11-01 通过 GitHub Actions `build-libmpv.yml`（run `41313665015`）构建
- 产物：8,457,728 字节（8.07 MiB）
- SHA256：`ef27c3f0700bf29a09adf5a04343f4d5a9a3a5e7e64b2303ab196a1652be3c9f`
- 外部导入只有 Windows 系统 DLL 和 UCRT，没有额外的 `libc++.dll` 等运行时
- `libmpv/libmpv-2.dll` 已替换为该产物，旧版备份在
  `artifacts/native-mpv/backup/5c9005.../`（本机，不入 Git）

**写不存在的滤镜会让 mpv 拒绝整串 `af`**，不是只丢掉那一个滤镜。因此：
`LibMpvPlaybackService.ApplyAudioFilter` 在失败时会清空 `af` 并记日志，保证主音频还能播。

## 环境音素材

12 个循环录音作为 `EmbeddedResource` 编译进 `Bowen.Core.dll`，安装目录里没有散文件。
首次选中时解压到 `%LOCALAPPDATA%\Bowen\ambient\v2\`，之后复用；缓存被删会自动重建。

统一规格：**12 秒、44.1 kHz、立体声、160 kbps MP3**。只从原始录音截取 12 秒，
应用固定峰值增益和 10 ms 首尾淡化；**不做降噪、EQ、压缩、响度归一或交叉淡化**。
全部素材为 **CC0 1.0**，可随 GPL 客户端分发：

| 环境音 | 来源 | 许可 |
| --- | --- | --- |
| 壁炉 | [Ambie](https://github.com/jenius-apps/ambie) / 16FThumaF，freesound 499032 | CC0 |
| 海浪 | [Ambie](https://github.com/jenius-apps/ambie) / jordir，freesound 360578 | CC0 |
| 鸟鸣 | Ambie / hargissssound，freesound 345851 | CC0 |
| 人潮 | Ambie / o_ciz，freesound 475513 | CC0 |
| 森林 | Leminno · nature-forest-day（Sadiquecat，freesound 799439） | CC0 |
| 水滴 | Freesound / Lydmakeren，511488 | CC0 |
| 风铃 | Freesound / Anthousai，398496 | CC0 |
| 流水 | Ambie / hargissssound，freesound 324591 | CC0 |
| 秒针 | Freesound / giddster，434841（截取 46–58 秒，保持 1 秒一响） | CC0 |
| 心跳 | Freesound / Cloud-10，688735 | CC0 |
| 烟火 | Freesound / Posples，861406 | CC0 |
| 雨声 | Ambie / jmbphilmes，freesound 200270 | CC0 |

素材处理脚本是一次性的，不入仓库；成品统一放在
`src/Bowen.Core/Audio/AmbientAssets/`。

## 已知限制

- **不是 1:1 复刻。** 手机端的银河音效是 SuperSound 3（`libSuperSound3.so` + AEP 预设），
  蝰蛇是 `libmusic3d.so` 的 ViPER 链，DTS 是授权 SDK；它们都是 ARM64 Android 原生库，
  Windows 无法加载。这里只做常规 EQ / 低音 / 声场 / 混响的听感近似。
- **AI智能音效、DTS:X、臻品全景声不做。** 「AI智能音效」是动态响度 + 中频增强的近似命名，
  不是模型。
- **切换预设会重建 `af`。** 有一段很短的音频链重初始化，通常听不出来；环境音音量滑杆不重建。
- **高解析音质叠加音效** 仍允许。手机端在部分母带档位会禁用音效；桌面端没有做这个门控，
  需要的话在音质切换处加提示。
- **ViperDSP 不随包分发。** 它逆向自 ViPER4Android，没有开源许可，仅限个人使用；
  公开 GPL 发行版不能链接它。

## 验收清单

- [ ] 播放条「音效」按钮能打开面板；歌词页同一颗按钮打开同一份选择。
- [ ] 银河/蝰蛇页签切换后卡片列表正确；选中卡片有强调色描边和对勾。
- [ ] 总开关关闭后立即恢复无音效；再打开恢复上次选择。
- [ ] 12 种环境音首次选中能解压并混入音乐；滑杆实时改变音量，不重启当前曲。
- [ ] 同时选预设和环境音时两者都生效；关闭环境音后预设仍在。
- [ ] 关闭客户端再启动，预设、环境音、音量都恢复。
- [ ] 切歌、拖动进度、暂停/恢复后音效仍在。
- [ ] 当前 libmpv 缺滤镜时主音频仍能播放，日志有明确警告。

## 参考实现与许可

音效本身参考过的项目：

| 项目 | 用途 | 许可 | 使用方式 |
| --- | --- | --- | --- |
| [SimpMusic](https://github.com/maxrave-dev/core) | mpv `af` 滤镜链结构、EQ/回声/混响分层写法 | GPL-3.0 | 参考结构，C# 重新实现 |
| [E-cells-Music](https://github.com/e-cells/E-cells-Music) | 预设命名与档案划分 | 无 LICENSE | 仅看思路，未复制代码 |
| [audioeffect-qm](https://github.com/AllenHeartcore/audioeffect-qm) | QQ 音乐 QMAEP 格式与预设参数研究 | GPL-3.0 | 未复制代码、未打包 QQ 预设数据 |
| [ViPERDSP](https://github.com/likelikeslike/ViPERDSP) / ViPER4Windows / ViPER4Host | 评估过的 ViPER 引擎 | 无开源许可，仅个人使用 | **未使用、未分发** |
| kugou-dsp / 酷狗 `dsp.dll` | 酷狗 ViPER 预设参考 | 专有 DLL | 未使用、未分发 |
| [libspatialaudio](https://github.com/videolan/libspatialaudio) | HRTF / 空间音频备选 | LGPL-2.1+ | 当前未使用 |
| [mpv-sofa](https://github.com/kalexis1994/mpv-sofa) | mpv HRTF 滤镜参考 | GPL-2.0+ / LGPL-2.1+ | 当前未使用 |

此前歌词 / 协议阶段参考过的项目，一并记在这里，避免散落在 README：

| 项目 | 用途 | 许可 | 使用方式 |
| --- | --- | --- | --- |
| [BetterLyrics](https://github.com/jayfunc/BetterLyrics) / [HyPlayer](https://github.com/HyPlayer/HyPlayer) | 歌词动效参数与渲染架构 | GPL-3.0 | 参数/架构参考；BetterLyrics 为按公式自行实现 |
| [AMLL](https://github.com/amll-dev/amll-player) / 本地参考项目 LyciaMusic | 弹簧参数、滚动曲线、视觉参考 | AGPL-3.0 | 只取参数/视觉，未复制代码 |
| pushkine | 解析式弹簧求解器 | MIT | `LyricSpring` 的求解器来源 |
| [tomakino/LyricProvider](https://github.com/tomakino/LyricProvider) | 歌词解析行为参考 | Apache-2.0 | 仅参考行为，未复制代码 |
| [Mintcolour/bodian-music-api](https://github.com/Mintcolour/bodian-music-api)、[folia-major](https://github.com/Mintcolour/folia-major) | 波点协议事实 | AGPL-3.0 | 只采用协议事实，未复制源码 |

`libmpv-2.dll` 内部静态链接了 FFmpeg、libass、libplacebo、FreeType、FriBidi、HarfBuzz；
固定版本见 `tools/mpv/sources.lock.json`，许可文本见各自上游。

`amovie` 循环选项：[FFmpeg amovie](https://ext-ffmpeg.com/filters/sources/amovie/)、
[循环混音示例](https://stackoverflow.com/questions/42122723/repeat-input-audios-while-mixing/42129123)。

> AGPL-3.0 与 GPL-3.0 不兼容。上表中的 AMLL、LyciaMusic、Mintcolour 项目只作参考；
> 如后续发现逐行代码移植，必须重写或移除，不能靠保留注释解决。
