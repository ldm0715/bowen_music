# 封面驱动的动态取色

更新日期：2026-10-31。记录当前规则、实测问题与修复方案。
**4.1 与 4.2 已实现。**
**2026-10-31 起，4.3 / 4.4 的「不做」结论被推翻：改为多色提取与保留原色明暗，
灰阶封面不再退回默认配色。方案与实施见 [`shell-background.md`](shell-background.md) §6。**

相关：[`ui-refresh.md`](ui-refresh.md) §8（封面取色与层级分离）、[`performance.md`](performance.md)（取色开销与缓存）。

---

## 1. 结论

现在的动态取色只影响**背景的氛围色**，不改变按钮、强调色或歌词颜色。

它的主色选择偏向大面积浅色：白底封面上的白色桶会赢过小面积彩色图案，主色因此常被算成
`#FEFEFE` / `#FDFDFD`。这类颜色饱和度为 0，`ColorPalette.Ambient` 会直接退回固定默认配色。
结果是很多歌切过去画面完全不变，和「没有封面 / 启动 / 解码失败」长得一模一样。

## 2. 当前规则

### 2.1 作用范围

| 位置 | 说明 |
| --- | --- |
| `MainWindow.xaml` 的 `ShellBackdrop` | 主窗口根背景，跨三行 |
| `LyricsPage.xaml` 的 `AmbientView` | 歌词页背景，`Opacity="0.18"`，变化更淡 |
| `Themes/Theme.xaml` 的 `AmbientOpacity` | 浅色 `0.55`，深色 `0.62` |

不影响强调色、按钮底色与歌词文字颜色。

### 2.2 触发时机

`AmbientBackdrop` 只监听 `PlayerViewModel` 的 `CurrentCoverUri` 与 `CurrentTrackId`。
两者都不变就不重新取色；取到的新调色板与当前完全相同时也不重绘
（`_palette.SequenceEqual(palette)`）。

主题切换是另一条触发路径：缓存的是封面种子，切换明暗主题时用同一批种子重新映射，
不需要重新解码封面。

同一张专辑封面连续播放、没有封面、以及取色失败，都会得到同一个结果：画面不动。

### 2.3 取色管线

```
封面原始地址
  → CoverArtUrl.Jpeg(cover, 96)        改写成 96px JPEG
  → BitmapDecoder（CPU 像素）
  → ColorQuantizer.Palette             多色提取（1–3 个真实封面色）
  → ColorPalette.Ambient(seeds, theme) 在 OKLCH 空间里按主题映射
```

- `CoverArtUrl.Jpeg` 只改写 `*.kuwo.cn` 的 `/star/albumcover/` 与 `/star/starheads/` 路径；
  其它主机或路径原样返回。原始图是 WebP 时，Win10 没有预装编解码器，解码会失败并退回默认配色。
- 采样边长 96px，缓存按 `trackId`，上限 256 条，超了整体清空。
- 无封面、解码失败、取色异常都退回 `ColorPalette.DefaultAmbient`。

### 2.4 主色算法

每通道 4 位分桶，共 4096 个桶；桶内取 RGB 平均值。每个桶的得分是：

```
score = sqrt(count) * suitability

suitability = (0.35 + 0.65 * saturation)
            * (1 - 0.9 * abs(luminance - 0.55) / 0.55)
```

两个关键点：

- **面积开平方**，避免大面积纯色直接取胜。
- **饱和度基数是 0.35**，即使完全灰的颜色也能拿到三分之一的分；亮度惩罚以 0.55 为中心，
  纯白与纯黑各被压到约三成。

多色提取 `Palette` 沿用同一套打分；彩色候选的饱和度基数降到 0.10，避免近灰的大色块
再次压过小面积彩色主体。

### 2.5 配色规则（2026-10-31 起）

`ColorQuantizer.Palette` 先给 1–3 个封面里真实存在的颜色，`ColorPalette.Ambient(seeds, theme)`
再在 OKLCH 空间里按主题映射：

| 条件 | 浅色主题 | 深色主题 |
| --- | --- | --- |
| 彩色种子 | 明度约 `0.78–0.90`，彩度 `0.04–0.10` | 明度约 `0.60–0.74`，彩度 `0.07–0.15` |
| 中性种子（OKLCH 彩度 `< 0.035`） | 明度同上，彩度 `≤ 0.015` | 明度同上，彩度 `≤ 0.015` |
| 没有封面 / 解码失败 / 全透明 | 固定默认配色 | 固定默认配色 |

- 三团分别有 `-0.03 / +0.02 / -0.01` 的明度偏移，叠起来才有层次。
- 目标彩度超出 sRGB 色域时二分降低彩度，保持明度与色相。
- **用 OKLCH 而不是 HSV**：HSV 压低 `V` 会把绝对彩度一起压掉，深色主题就会发灰发浑；
  OKLCH 的明度与彩度解耦，可以单独调整。
- 深色主题的目标明度参考 Material 3 的做法：深色底上的角色色要比浅色主题更亮，
  否则色相读不出来。见 [`shell-background.md`](shell-background.md) §6.2。

## 3. 为什么很多歌不变色

### 3.1 白底封面赢过小面积彩色

本机日志 `%LOCALAPPDATA%\Bowen\logs\bowen-20261023.log` 里，大量记录是：

```
氛围取色：解码 60×60，主色 #FEFEFE
氛围取色：解码 60×60，主色 #FDFDFD
```

拿其中一张封面复现分桶打分（3600 像素）：

| 桶 | 像素数 | 占比 | 平均色 | 饱和度 | 得分 |
| --- | --- | --- | --- | --- | --- |
| 白底 | 1080 | 30.0% | `#FEFEFE` | 0.00 | **3.11** |
| 棕色主体 | 30 | 0.83% | `#866848` | 0.46 | 2.83 |

白色桶面积大，`sqrt(1080) = 32.9`，即使 `suitability` 只有 0.095，仍然赢过
`sqrt(30) = 5.48 × 0.516 = 2.83`。主色被算成白色，饱和度 0，接着必然走默认配色。

这不是「封面没有颜色」：同一张图里明明有棕色主体，只是面积输了。

### 3.2 低饱和门限一刀切

`DesaturatedThreshold = 0.15` 以下不派生，直接返回**同一个固定配色**。
于是所有浅色、灰阶、白底封面都与「没有封面」「解码失败」共用一份颜色，界面上无法区分。

日志里的 `#E6EEF6` 就是例子：它是很浅的蓝，饱和度约 0.07，仍然走默认。

### 3.3 60px 采样太小

白底封面上的彩色主体常常只有几十个像素。60×60 下，0.5% 只有 18 个像素，
分桶平均值和面积判断都容易被压缩、噪点或 JPEG 边缘影响。

### 3.4 派生范围收得窄

饱和度 `0.60–0.88`、明度 `0.78–0.94` 会把不同封面的主色拉向同一个亮度与鲜艳度。
即使色相不同，观感差异也比封面本身小。

### 3.5 其它不变化的情况

| 情况 | 是否符合预期 |
| --- | --- |
| 同一张专辑封面连续播放 | 符合：地址相同，颜色本就应该相同 |
| 没有封面地址 | 符合：只能退回默认 |
| 非 kuwo 域名 / 非已知路径的 WebP | 可能不符：解码失败也应退回默认，需要看日志 |
| 歌词页 | 变化存在，但 `Opacity=0.18`，不容易察觉 |

## 4. 修复方案

### 4.1 主色选择改成两段（已实现）

先找「可用的彩色桶」，找不到才退回原来的主色：

1. 候选桶必须有足够面积：`count >= max(18, pixels * 0.005)`，即至少 0.5%。
2. 候选桶的 OKLCH 彩度 `>= 0.025`，亮度在 `0.10–0.92` 之间，排除白边、黑边与近灰。
   不用 HSV 饱和度：浅粉 `#F7DCE3` 的饱和度只有 0.11，但彩度约 0.031，应当算彩色。
   `Dominant` 仍沿用原来的饱和度 0.18 门槛，只有多色 `Palette` 改判据。
3. 候选桶之间用 `sqrt(count) * suitability` 排序，`suitability` 的饱和度基数从 0.35 降到 0.10；
   面积下限挡住单个噪点，避免「一个饱和像素胜出」。
4. 没有候选桶时，再走原来的 `Dominant` 结果与灰阶兜底。

按这套规则复算前面两张白底封面，会分别取到 `#866848` 与 `#785746`，不再取白。

实现落在 `ColorQuantizer.Dominant`：先算所有桶的兜底得分，再单独筛彩色候选；
两套得分只在饱和度基数上不同。没有彩色候选时返回兜底主色，行为与改动前一致。

### 4.2 采样边长 60 → 96（已实现）

96×96 有 9216 个像素，彩色主体的占比更稳定；取色仍在小图上做，解码与统计开销可以忽略。
`CoverPaletteLoader.SampleSize` 已改为 96。

### 4.3 低饱和但有色相的封面走淡色分支（2026-10-31 已推翻）

原决定：真正灰阶的封面保持默认配色，不为它凭空染色。

新决定：**灰阶封面用封面自身的灰调生成低饱和光晕**，不再换成青绿蓝紫。
真正灰的封面得到灰调背景，浅蓝、米色封面保留自身色温。默认配色只留给无封面与解码失败。
依据是 issue #3 的反馈：用户看不出背景与封面的关系，而灰阶封面跳默认色是最直接的断裂。

### 4.4 放宽派生范围（2026-10-31 已推翻）

原决定：先不改 `ColorPalette.Ambient` 的饱和度与明度钳制，留到真机验证后再评估。

新决定：不再统一钳到高明度，改为在 OKLCH 空间里按主题映射目标明度与彩度，
保留封面之间的色相差异，同时避免深色发浑、浅色刺眼。
实现与调参记录见 [`shell-background.md`](shell-background.md) §6。

### 4.5 调色板过渡（已实现）

切歌时不再直接替换 `WriteableBitmap`，而是把三团颜色从旧调色板插值到新调色板，
每 40ms 重绘一帧，650ms 走完；用 smoothstep 缓动，两端慢、中间快。

没有采用两张半透明位图交叉淡入：氛围图本身带 alpha，两层叠在一起时中间透明度会变化，
看起来像闪一下。逐帧插值最终颜色准确，过渡也连续。

连续切歌时新的过渡会把渲染请求序号推高，旧循环在下一帧退出；起点取上一帧真正画到界面上的颜色，
所以不会跳回更早的颜色。系统关闭动画时直接切到目标色。

渲染复用同一块 384×256 的像素缓冲区，并用信号量串行化渲染与写回，避免切歌时旧帧还没画完
就覆盖同一块内存。

### 4.6 诊断日志（2026-10-31 已实现）

每取色记录：彩色候选数量、是否使用补位色、最终种子颜色列表、封面地址。
不再只有一行主色，排查灰阶分支与多色选择时可以直接对照。

## 5. 测试与验收

### 5.1 离线单测

给 `ColorQuantizer` 与 `ColorPalette` 补合成像素用例，当前覆盖：

| 用例 | 期望 | 测试 |
| --- | --- | --- |
| 1080 白底 + 30 棕色 | 取到棕色，不取白 | `LargeWhiteFieldWithSmallDullPatch_PicksThePatch` |
| 1000 白底 + 1 个饱和红像素 | 仍取白底，不让单像素胜出 | `SingleSaturatedPixel_DoesNotOverrideLargeWhiteField` |
| 红蓝两块 | 两个色相都进入种子 | `Palette_TwoColoredPatches_ReturnsBothHues` |
| 灰阶三段 | 返回明暗不同的中性色，不走默认配色 | `Palette_GrayCover_ReturnsNeutralSeeds` |
| 纯色封面 | 只取到一个真实色，氛围色仍补足三团 | `Palette_SolidColor_ReturnsOneSeedAndAmbientStillHasThree` |
| 灰阶种子 | 光晕保持中性，不凭空染色 | `Ambient_GreySeed_StaysNeutral` |
| 浅粉彩色条 | 低饱和但有色，仍进入种子 | `Palette_PalePinkAccent_IsSelected` |
| 深色主题暗封面 | 抬到可读出色相，不发灰 | `Ambient_DarkTheme_LiftsDarkSeedsOutOfMud` |
| 浅色与深色对比 | 浅色更亮，色相不变 | `Ambient_LightTheme_IsLighterThanDarkTheme` |
| 多个种子 | 各自色相保留 | `Ambient_MultipleSeeds_PreserveEachHue` |

### 5.2 真机验收

准备 10 张封面：白底彩色主体、黑底彩色主体、浅色、深色、灰阶、双色、同专辑连续播放。
逐首播放，确认：

- 白底彩色主体不再固定成默认配色；
- 灰阶封面得到灰调背景，不再出现青绿蓝紫；
- 深色主题下暗封面不发灰发浑，浅色主题下颜色不刺眼；
- 切歌时颜色变化可察觉，歌词页也不至于完全看不见；
- 日志里的候选数、补位与种子列表与界面一致。

## 6. 已定范围

1. **只改背景氛围色。** 按钮、强调色与歌词颜色不跟随封面；那需要覆盖 `SystemAccentColor`
   一类的主题键并处理前景对比度，属于另一套方案。
2. **灰阶封面保留自身灰调。** 不为灰阶封面凭空染色，也不退回固定默认配色；
   默认配色只留给无封面与解码失败。

## 7. 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/Bowen.Core/Media/ColorQuantizer.cs` | `Dominant` 改成两段式；新增 `Palette` 多色提取；彩色候选改用 OKLCH 彩度判据；`ExtractedPalette` 诊断信息 |
| `src/Bowen.Core/Media/ColorMath.cs` | 新增：HSV / OKLCH 转换、色域裁切、亮度、色相与通道距离，供取色与配色共用 |
| `src/Bowen.Core/Media/AmbientTheme.cs` | 新增：浅色 / 深色主题枚举 |
| `src/Bowen.Core/Media/ColorPalette.cs` | `Ambient` 改为接收多个种子与主题；OKLCH 主题映射；灰阶走中性分支；新增 `ColorCount` |
| `src/Bowen.WinUI/Media/CoverPaletteLoader.cs` | 采样边长 60 → 96；缓存封面种子；改用 `Palette` 与新的诊断日志 |
| `src/Bowen.WinUI/Controls/AmbientBackdrop.xaml.cs` | 主题切换时按同一批种子重算调色板 |
| `src/Bowen.Core/Media/AmbientBackground.cs` | 新增写入调用方缓冲区的 `Render` 重载，过渡时复用像素数组 |
| `src/Bowen.WinUI/Controls/AmbientBackdrop.xaml.cs` | 650ms 调色板过渡，40ms 一帧；渲染请求序号与信号量处理连续切歌 |
| `tests/Bowen.Core.Tests/CoverPaletteTests.cs` | 多色提取、灰阶分支、纯色封面、保留色相，以及原有白底/单像素/`Blend` 用例 |
| `tests/Bowen.Core.Tests/AmbientBackgroundTests.cs` | 复用缓冲区渲染与缓冲区过小 |

验证：`Bowen.Core.Tests` 1753 项通过，0 失败；WinUI 构建 0 警告 0 错误。
