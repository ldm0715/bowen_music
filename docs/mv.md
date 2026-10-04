# MV 播放实现与验收

**2026-10-04。** 从零接入 MV：三处入口 + 一个全窗沉浸的 MV 播放页。
接口协议、实网样本与字段表见 [`../reverse/findings/15-mv.md`](../reverse/findings/15-mv.md)，
本文只讲客户端怎么落地。

**状态**：构建通过、`tests/Bodian.Core.Tests` 全绿（含 6 条 MV 测试）。
界面已在真机跑过并修过一轮（播放与暂停、进度拖动、画面菜单、标题栏间距），
**下面「验收」一节尚未逐条走完**。

---

## 1. 接口

```
GET service/mv/info?musicId={musicId}
```

- `code == 200` → `data.mv`，含 `highUrl`（主视频地址）等 15 个字段
- `code == 20048` → **这首歌没有 MV**，是正常结果不是错误
  （`BodianErrorCode.MvUnavailable`，`GetMvInfoAsync` 返回 `null`）
- 视频是**普通 H.264 MP4**，无需任何额外请求头，支持 Range

**桌面协议的 `service/music/info` 不下放 `mvInfo`** —— 移动端才会顺带带着。
所以取 MV 必须单独打这一趟，不能靠曲目详情省掉。

## 2. 判断「有没有 MV」

`Track.HasMv`，判据是 **`IsMv == 1 || Vid > 0`**。

**不能只看 `isMv`**：列表接口返回的 `isMv` 恒为 0，但 `vid` 是真的。
《晴天》《夜曲》《青花瓷》三首在搜索结果里都是 `isMv=0`，在详情里都是 `isMv=1`。
只用 `isMv` 会让列表里的 MV 全部漏掉。实测见 findings 文档 §4。

这个判据只决定**入口显不显示**，是离线推断；能不能播仍由 `GetMvInfoAsync` 的返回说了算。

## 3. 三处入口

| 位置 | 形态 | 无 MV 时 |
| --- | --- | --- |
| 曲目行标题旁的角标 | `MV` 小标，非入口 | 不显示 |
| 行尾「更多」菜单 | 「播放 MV」菜单项 | **灰着**（不隐藏） |
| 播放条 / 歌词页 | 一颗按钮 | **整颗折叠**（不置灰） |

菜单项与工具条按钮的取舍相反是有意的：菜单是列表语义，项忽多忽少比灰着更让人困惑；
工具条上一颗永远点不动的按钮只是占位置。

列表模板有两份（`TrackListView.xaml`、`SearchPage.xaml`），角标两处都要加。

## 4. MV 页

`Views/MvPage.xaml` + `ViewModels/MvViewModel.cs`，走 `ImmersiveHost`，与歌词页并列。

**渲染用 WinUI 的 `MediaPlayerElement` + `Windows.Media.Playback.MediaPlayer`，不是 mpv。**
音频那条链路（`LibMpvPlaybackService`）是纯音频 headless 且被 `PlaybackCoordinator` 独占，
借它出画面要拆掉「哑引擎」的边界。两条链路各管各的。

`MediaPlayer` 自己的 SMTC 集成**必须关掉**（`CommandManager.IsEnabled = false`），
否则会和 `SmtcManager` 抢系统媒体卡片。

| 操作 | 行为 |
| --- | --- |
| 左上 ↓ | 一路退出沉浸，回到进沉浸之前那个常规页（见下） |
| 右上全屏按钮 / `F11` | 切换无边框全屏；全屏时 `Esc` 先退全屏 |
| 空格 | 播放 / 暂停 |
| 底部进度条 | 拖动定位；拖动期间不回写位置，否则滑条会被弹回 |
| 「听歌」按钮 | 音/视频互切：翻回音频并退出 MV 页 |
| 「适应屏幕」按钮 | 展开四项画面比例，当前项用背景高亮 |

### 4.1 音视频互斥

进 MV 页暂停音频，退出时恢复。有三种情况，代码要分清：

- 进页时音频**在播** → 暂停，记下来
- 进页时音频**本来就是暂停的** → 不去动它，退出时也就没什么可恢复
- 用户点了「听歌」 → 明确要听，即使之前是暂停的也放起来；
  此时要**清掉恢复标记**，否则退出时 `RestoreAudioAfterVideo` 会再按一次把它按停
  （`IsPlaying` 由引擎事件异步送回来，那一刻还是旧值 `false`，守卫会失效）

### 4.2 ↓ 按钮为什么不退一层

MV 常常是从歌词页点进来的，`GoBack()` 一层会落回歌词页 —— 那还是沉浸页，
且和同页的「听歌」做的是同一件事。所以退到**落点不再是沉浸页**为止
（`MainWindow.ExitImmersiveToShell`）。从播放条直接进来的情况自然只退一层。

### 4.3 画面比例

四个模式：适应屏幕（默认）/ 适应宽度 / 适应高度 / 拉伸填满。

**没有直接用 XAML 的 `Stretch`。** `Uniform` / `UniformToFill` 的裁切方向取决于
画面比例与容器比例谁大，给不出「钉死在某一根轴上」的语义。改成按模式显式算画面矩形，
元素统一 `Stretch=Fill` 填那个矩形 —— 除「拉伸填满」外，算出来的矩形本身就带着画面的
宽高比，所以不变形；超出宿主的部分由 `Clip` 裁掉。

比例取自 `PlaybackSession.NaturalVideoWidth/Height`，**解码器报出尺寸后才有值**，
在此之前退回 `Uniform`（否则会拿未知比例铺满）。

> **未验证**：如果片源是变形（anamorphic）的，这里拿到的是存储尺寸、不含像素宽高比，
> 那种片源四个模式都会看着被拉伸。现有样本的 SAR 尚未用 ffprobe 确认。

## 5. 踩过的坑

**在 `OnNavigatedFrom` 里 `Dispose()` `MediaPlayer` 会让应用闪退。**
导航的顺序是「通知离场 → 把本页从 `ContentControl` 摘掉」：离场时元素还挂在树上、
还持着那个播放器，等第二步卸载元素时它去碰已经释放的原生对象，抛的是
`COMException(0x80004004)`，走不到托管 catch。**必须先把元素与播放器解绑**
（`VideoSurface.SetMediaPlayer(null)` 一句）再释放。

**绑定到非可通知属性会让控制条整条禁用。** `HasSource` 一开始写成了
`=> _mv is not null`，绑定只在加载完成前求值一次拿到 `false` 就再也不更新 ——
表现是「进度条拖不动、播放键按不了」。凡是 `x:Bind` 到的状态都得是
`[ObservableProperty]`。

**标题栏右边那颗按钮离系统三颗按钮太远。** 内置 `TitleBar` 给 caption 预留的列用的是
物理像素、没除 DPI 缩放，125% / 150% 的屏上会宽出一截。歌词页与 MV 页都按
`RightInset / RasterizationScale` 重设 `PART_LayoutRoot` 的第 11 列。

**曲目对象不能出现在页面/VM 的公开属性上。** XAML 类型信息生成器会为公开属性里的类型
生成激活代码，而 `Track` 有 `required` 成员 —— 造不出实例，直接编译失败。
`MvPage.Track` 与 `PlayerViewModel.CurrentTrack` 因此都是 `internal`。

## 6. 有意简化

与歌词页不同，这几处是**决定不做**，不是遗漏：

- **不做控制台自动隐藏**：控制条压在画面下沿，藏起来反而让人找不到
- **不做频谱 / 评论面板**
- **不复用 `VolumeButton`**：那个控件绑死在 `PlayerViewModel` 上，而 MV 是另一套引擎，
  音量独立，所以 MV 页用自己的滑条

## 7. 验收

**核心逻辑**（`tests/Bodian.Core.Tests/MvApiTests.cs`，6 条）：

- 有 MV → 取出 `highUrl`；无 MV（20048）→ 返回 `null` 而不是抛
- 搜索列表里 `isMv=0` 但 `vid>0` 的歌 → `HasMv` 为真；`isMv=0 且 vid=0` → 为假
- `playLimitTime > 0` → `IsPreviewOnly`；`= 0` → 不限

**界面**（需人工）：

- [ ] 三处入口：有 MV 的曲子都有入口，没有 MV 的（《三国恋》）角标不显示、菜单项灰着、播放条无按钮
- [ ] 进 MV 页音频暂停；退出恢复；本来就是暂停态时退出不被自动播起来
- [ ] 「听歌」切回音频并退出页面，音频**持续播放**（不是响一声又停）
- [ ] ↓ 按钮从歌词页进的 MV 一步回到进歌词前的页面
- [ ] 全屏按钮与 `F11`；全屏下 `Esc` 先退全屏
- [ ] 四个画面模式生效
- [ ] 看 MV 时系统媒体卡片（SMTC）不跟着跳
- [ ] 非会员的试看限制**未验证**：实测账号 `playLimitTime` 一直是 0

## 8. 未做

- MV 歌词（`service/mv/lyric`，从未实测过）
- 相关 MV / 同歌手 MV 列表
- 画面比例选择不跨会话记忆
- 匿名态能否取到 MV 未测
