# 音效：经典音效 / 增强音效 / 环境音

手机端的三类音效在客户端本地处理后，再交给播放器输出；服务端只下发音效列表，不下发处理好的音频。
本项目的做法是用 **libmpv 的 `af` 属性 + FFmpeg 音频滤镜** 做听感近似，**不移植手机端的原生 DSP**。

## 入口

- 播放条右侧「音效」按钮（三根竖向滑杆图标）。
- 歌词页控制条同一颗按钮。
- 两处共用一个 `AudioEffectsViewModel` 单例，选择与缓存只有一份。

面板分三个页签：

| 页签 | 内容 |
| --- | --- |
| 经典音效 | 8 套预设：清澈人声、重低音、3D环绕、黑胶、LiveHouse、夜晚、开阔宽音、车载 |
| 增强音效 | 7 套预设：3D环绕、超重低音、纯净人声、虚拟现场、均衡器音效、响度均衡、一键HiFi |
| 环境音 | 12 种循环：壁炉、海浪、鸟鸣、人潮、森林、水滴、风铃、流水、秒针、心跳、烟火、雨声 |

## 实现分层

| 层 | 位置 | 职责 |
| --- | --- | --- |
| 设置模型 | `src/Bowen.Core/Models/AudioEffectSettings.cs` | 启停、预设 id、环境音 id、环境音音量、十段 EQ；只做归一化 |
| 目录 | `src/Bowen.Core/Audio/AudioEffectCatalog.cs` | 预设名、描述、滤镜片段、环境音定义 |
| 图构建 | `src/Bowen.Core/Audio/AudioEffectGraphBuilder.cs` | 纯函数：设置 + 环境音路径 → mpv `af` 字符串 |
| 环境音素材 | `src/Bowen.Core/Audio/AmbientSoundGenerator.cs` | 从程序集解压嵌入的 OGG/FLAC 循环到本地缓存 |
| 持久化 | `JsonAudioEffectSettingsStore` | `%LOCALAPPDATA%/audio-effects.json`，临时文件 + Move |
| 播放层 | `IPlaybackService.SetAudioFilter` / `SendAudioFilterCommand` | 只碰 mpv；失败时清空链，不打断播放 |
| 面板 | `AudioEffectsViewModel` + `AudioEffectsPanel.xaml` | 页签、卡片、总开关、音量滑杆、错误提示 |

## `af` 图

只有预设时：

```text
@bowenFx:lavfi=[stereotools=slev=2.1:mlev=0.82,haas=side_gain=0.8]
```

预设 + 环境音同时存在时，合成一张图：

```text
@bowenFx:lavfi=[
  anull[main];
  [main]<预设滤镜链>[fx];
  amovie=C\\:/.../ambient.mp3:loop=0,asetpts=N/SR/TB[amb];
  [fx][amb]amix=inputs=2:weights=1 0.3:normalize=0:duration=shortest
]
```

预设自带纹理（目前只有黑胶底噪）、又叠加了环境音时，混音变成三路：

```text
@bowenFx:lavfi=[
  anull[main];
  [main]<预设滤镜链>[fx];
  amovie=C\\:/.../texture.vinyl.ogg:loop=0,asetpts=N/SR/TB[tx];
  amovie=C\\:/.../ambient.mp3:loop=0,asetpts=N/SR/TB[amb];
  [fx][tx][amb]amix=inputs=3:weights=1 0.5 0.3:normalize=0:duration=shortest
]
```

- **`amix` 必须带 `duration=shortest`。** `amovie` 是无限循环源，`amix` 默认
  `duration=longest` 会让整条链永不 EOF，mpv 等不到 end-file，表现就是「播完不自动
  下一首」。实测加 `duration=shortest` 后，主音频结束即结束混音图，第二首正常加载。
- 输入顺序固定为 主音频 → 纹理 → 环境音，`weights` 必须与个数一致；
  环境音滑杆改权重时也走同一顺序（`MixWeights`），个数对不上会被 mpv 拒绝。
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

## 增益与响度

预设听感好不好，一半取决于曲线，一半取决于有没有偷偷掉电平。实测（EBU R128，粉色噪声 + 动态包络测试信号）踩过这三个坑：

| 坑 | 表现 | 处理 |
| --- | --- | --- |
| `aecho` 的 `out_gain` 会连干声一起乘 | `aecho=0.8:0.7:...` 实为 -5.8 dB，混响类的预设整体变小 | 固定 `in_gain=1:out_gain=1`，只调 `decays` |
| `asubboost` | 单独使用掉 7–9 LU，且 `dry` 再削原信号 | 需要次低频时改用 `virtualbass`（实测不改变响度） |
| `acompressor` 阈值是线性值 | `threshold=0.08` = -22 dBFS，整首歌被压掉 6 dB 以上 | 阈值不低于 0.35，并用 `makeup` 补偿 |

抬增益的链条末尾统一加 `alimiter=limit=0.9:level=0`：`limit` 防削波，`level=0` 关掉自动电平，避免限幅器自己再拉一次响度。改完链条后用下面的方法复核：

```bash
ffmpeg -i test.wav -af "<链条>,ebur128=peak=true" -f null -
```

判定标准：整体响度与干声相差不超过 ±2 LU；true peak 低于 -0.5 dBFS。

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

12 个环境音循环加 1 个黑胶底噪作为 `EmbeddedResource` 编译进 `Bowen.Core.dll`，
安装目录里没有散文件。首次选中时解压到 `%LOCALAPPDATA%\Bowen\ambient\v9\`，
之后复用；缓存被删会自动重建。

格式：**OGG Vorbis**（环境音 11 条 + 黑胶底噪 1 条）和 **FLAC**（心跳），保留源素材的原始采样率与声道。
素材不做转码、响度归一或降噪；OGG 用流复制（`-c copy`）截取约 30 秒，音频数据未改动。
秒针源文件只有 16 秒，保持原长。12 个成品合计约 6.8 MB。

| 环境音 | 来源文件 | 上游声明 |
| --- | --- | --- |
| 壁炉 | XMSLEEP `audio/nature/campfire.ogg` | 第三方许可，未逐条标注 |
| 海浪 | XMSLEEP `audio/nature/waves.ogg` | 同上 |
| 鸟鸣 | XMSLEEP `audio/animals/birds.ogg` | 同上 |
| 人潮 | XMSLEEP `audio/urban/crowd.ogg` | 同上 |
| 森林 | XMSLEEP `audio/nature/wind-in-trees.ogg` | 同上 |
| 水滴 | XMSLEEP `audio/nature/droplets.ogg` | 同上 |
| 风铃 | XMSLEEP `audio/things/wind-chimes.ogg` | 同上 |
| 流水 | XMSLEEP `audio/nature/river.ogg` | 同上 |
| 秒针 | XMSLEEP `audio/things/clock.ogg` | 同上 |
| 心跳 | Freesound / Cloud-10，688735 | CC0 |
| 烟火 | XMSLEEP `audio/urban/fireworks.ogg` | 第三方许可，未逐条标注 |
| 雨声 | XMSLEEP `audio/rain/light-rain.ogg` | 同上 |

XMSLEEP 的 README 对声音来源的原文说明：

> **网络声音**：来自 [moodist](https://github.com/remvze/moodist) 项目，遵循 MIT 开源许可协议
>
> **第三方资源**：部分声音来自第三方提供商，遵循相应的许可协议
> - 遵循 **Pixabay Content License** 的声音
> - 遵循 **CC0** 的声音

moodist 的 README 同时说明其音效属于第三方资源，许可为 Pixabay Content License 或 CC0，
但没有逐文件标注。除心跳外的 11 条素材均未逐条确认许可，公开分发前需要自行核实。

成品统一放在 `src/Bowen.Core/Audio/AmbientAssets/`。

### 黑胶底噪

`texture.vinyl.ogg` 是**本项目自己合成的**，不涉及第三方素材与授权：30 秒立体声
44.1 kHz OGG，约 313 KB，整体响度 -31.5 LUFS。由三部分混合：

| 成分 | 做法 |
| --- | --- |
| 炒豆声 | `aeval` 随机脉冲（每声道约 11 次/秒），带通 900 Hz–7.5 kHz |
| 表面嘶声 | 白噪声，带通 1.4 kHz–9 kHz，-30 dB |
| 片基隆隆 | 棕噪声，低通 120 Hz，-26 dB |

选中黑胶预设时解压到与环境音相同的缓存目录，由 `AudioEffectGraphBuilder` 以固定权重
`0.5` 混入（见 `TextureWeight`），不跟环境音滑杆联动。想调底噪大小，改这个常量即可；
想改音色，用下面这条命令重新生成素材：

```bash
ffmpeg -f lavfi -i "anullsrc=r=44100:cl=stereo:d=30" \
       -f lavfi -i "anoisesrc=color=white:amplitude=1:d=30:r=44100" \
       -f lavfi -i "anoisesrc=color=brown:amplitude=1:d=30:r=44100" \
  -filter_complex "\
    [0:a]aeval=exprs='if(lt(random(0),0.00025),(random(1)*2-1),0)',highpass=f=900,lowpass=f=7500,volume=-4.5dB[c];\
    [1:a]highpass=f=1400,lowpass=f=9000,volume=-30dB[h];\
    [2:a]lowpass=f=120,volume=-26dB[r];\
    [c][h][r]amix=inputs=3:normalize=0,alimiter=limit=0.9:level=0" \
  -c:a libvorbis -q:a 4 -ar 44100 -ac 2 texture.vinyl.ogg
```

## 已知限制

- **不是 1:1 复刻，也不再沿用手机端名字。** 手机端两组预设分别是 SuperSound 3
  （`libSuperSound3.so` + AEP）与 `libmusic3d.so` 的 ViPER 链，DTS 是授权 SDK；
  它们都是 ARM64 Android 原生库，Windows 无法加载。桌面端的「经典」「增强」两页
  按效果方向自行调整，曲线和命名都归本项目，与手机端不再一一对应。
- **DTS:X、臻品全景声不做。** 「响度均衡」（原 AI智能音效）用 `dynaudnorm` 做真正的动态响度
  归一（按 500 ms 帧把 RMS 拉到 `targetrms=0.35`，约 -9 dBFS RMS），再抬 2.5 kHz 中频；
  **不是模型**，名字只描述功能。目标值默认取 0.35：0.25 会把 -8 LUFS
  的现代热母带压掉 3 dB 以上，听感就是「开了反而更小」。`maxgain=3` 限制安静段的
  最大抬升（实测 4 会把纯底噪抬 7.6 dB），`threshold=0.05` 让低于 -26 dBFS 的帧不参与
  放大，两者一起保住底噪。
- **`afir` 在当前 libmpv 构建里不可用。** 滤镜已编入，但实测无论 IR 是 WAV 还是 FLAC、
  路径绝对还是相对、是否统一采样率和格式，输出都是静音（-68 dB）。3D环绕因此改用
  `aecho` 多抽头长反射加 `haas` 展宽；要换回卷积混响，先在真实 libmpv 里验证 afir 能出声。
- **切换预设会重建 `af`。** 有一段很短的音频链重初始化，通常听不出来；环境音音量滑杆不重建。
- **高解析音质叠加音效** 仍允许。手机端在部分母带档位会禁用音效；桌面端没有做这个门控，
  需要的话在音质切换处加提示。
- **黑胶底噪是合成素材**，不是真实唱片的录音。它只提供炒豆声、嘶声和低频隆隆三类成分，
  没有唱片磨损带来的音色随机性；循环周期 30 秒，长时间单曲循环听可能察觉重复。
- **ViperDSP 不随包分发。** 它逆向自 ViPER4Android，没有开源许可，仅限个人使用；
  公开 GPL 发行版不能链接它。

## 验收清单

- [ ] 播放条「音效」按钮能打开面板；歌词页同一颗按钮打开同一份选择。
- [ ] 经典/增强页签切换后卡片列表正确；选中卡片有强调色描边和对勾。
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
