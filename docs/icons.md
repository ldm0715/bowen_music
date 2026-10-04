# 图标体系替换方案

**写于 2026-10-04。** 本文是「把全应用的图标换成一套矢量图标」的选型与落地定稿。
写于动手之前 —— 定稿后再改代码。

**先读**：`ui-refresh.md` §0（许可边界）、§18.2（播放模式那三个自绘路径的来历）。

---

## 0. 要解决的问题

现在应用里同时跑着**三套**图标：

| 套 | 是什么 | 用在哪 |
| --- | --- | --- |
| Segoe MDL2 Assets | 系统字体的字形，`FontIcon` 渲染 | 全应用约 30 个码位：上一首 / 下一首 / 播放 / 暂停 / 队列 / 收藏 / 分享 / 评论 / 刷新 / 新建 / 删除 / 更多 / 主题 / 全屏 … |
| 自绘路径 | 手写 `PathIcon`，24 单位视框 | 播放模式的三个图标（`PlayerBar`），`MainWindow` 里一处 |
| 汉字当图标 | `FontIcon` + `Microsoft YaHei UI` | 歌词页入口（「词」） |

**症状是两个字：对不齐、调不了大小。**

根子在于 MDL2 是**字体字形**而不是图形：每一颗的墨水大小和上下位置都由字体的基线与字身框决定，彼此不同。于是：

- **想对齐**只能逐颗加偏移。现存补丁：`PlayerBar` 的「词」和 `VolumeButton` 的音量各带 `Margin="0,-4,0,0"`（`TrackStatisticButton` 里那两颗也带了），就是为了把墨水从中心线下面拉回来。
- **想同大小**只能逐颗换算补偿。「词」是汉字、占满字身框，MDL2 只占八成，所以得压到 14 号才能对上 MDL2 的 16 号。

这些补偿都是目测出来的数，换字号、换 DPI、换主题都可能重新失衡。

### 0.1 为什么不能用「WinUI 自带的」

WinUI **不提供图标库**。它只有 `SymbolIcon` / `FontIcon` 两条路，背后的字体就是 Segoe MDL2 Assets —— 也就是我们现在这套，换与不换一回事。

微软新的一代图标字体 **Segoe Fluent Icons** 观感确实统一，但它：

- **只在 Windows 11 随系统装**。本机（Windows 10.0.19045）实测字体目录里只有 `segmdl2.ttf`，没有它 —— 在 Win10 上引用会画成方块。
- **字体文件不允许随应用分发**，也不能打包进去。
- WinUI 3 / Windows App SDK 也**没有**把它捆绑进来。

结论：想统一，只能**由应用自己携带图标资源**。

---

## 1. 选型

**采用 Fluent System Icons（MIT）。**

| | |
| --- | --- |
| 仓库 | `github.com/microsoft/fluentui-system-icons` |
| 许可 | MIT —— 与本项目 GPL-3.0 兼容 |
| 形式 | SVG / 路径数据（**不是**字体），可合法打包进应用 |
| 观感 | 就是 Fluent 设计语言本身，和 WinUI 系统控件同一套笔画与圆角 |

三条理由：

1. **已经开了一半的头。** 播放模式那三个自绘路径本来就是从这个仓库抄的（MIT，`play-queue.md` §7 有记录）。这次只是把剩下的补齐，不是引一套新东西。
2. **和系统控件同源。** 换成 Lucide / Tabler 那类通用现代风，反而要重新和 WinUI 控件打架。
3. **不依赖操作系统。** 路径数据随应用走，Win10 / Win11 一个样。

**明确排除：**

- **图标字体**（不管是 Segoe Fluent Icons 还是自己打包一套）。字体字形正是本文要逃离的东西，换一套字体只是把 MDL2 的毛病原样重来。
- **照抄参照物 LyciaMusic 的图标资源** —— 它是 AGPL-3.0，见 `ui-refresh.md` §0 的硬约束。

---

## 2. 落地形式

### 2.1 一个路径字典

新建 `Themes/Icons.xaml`，每颗图标一条**路径文本**，**全部画在 24 单位视框里**（与现有播放模式路径一致）：

```xml
<x:String x:Key="IconPlay">M5 5.27368C5 3.56682 ...</x:String>
<x:String x:Key="IconPause">M5.74609 3C4.7796 3 ...</x:String>
```

> ★★ **是字符串，不是 `<Geometry>` —— 这一条是拿「应用打不开」换来的，见 §5。**

取 Fluent 仓库里 **24 尺寸**的那一版（它同时提供 16 / 20 / 24 / 28 / 32 / 48 的独立几何，
小尺寸是重新画过的）。先一律用 24 版靠 Viewbox 缩放；某颗在 16 附近显细时，再单独换用它的 20 版。

### 2.2 一个展示控件

新建 `Controls/Icon.xaml(.cs)` 与 `Controls/IconGeometry.cs`，两个属性就够：

```xml
<controls:Icon Data="{StaticResource IconNext}" Size="28" />
```

- `Data`（**string**，路径文本）：`IconGeometry.From` 把它转成**属于这颗图标的** `Geometry`。
- `Size`（double）：套一层 `Viewbox`。

**为什么是字符串而不是 `Geometry`**：资源字典里的 `Geometry` 赋不进 Geometry 属性，
连内置的 `PathIcon.Data` 都会在启动时抛异常。详见 §5 —— 这是本方案唯一一处反直觉的地方，
代价是一次「双击打不开」。

只有**需要由代码切图标**的地方（歌词页全屏两态）才在 code-behind 里查一次资源，
见 `LyricsPage.ResourceIcon`。

**为什么必须有这个控件**：`PathIcon` 不按 `Width`/`Height` 缩放，只按几何的原始尺寸出图
（`ui-refresh.md` §18.2 记录过这个坑，皇冠会被裁掉一半）。所以每处用点都得外套一层
`Viewbox` —— 三十来个用点各写一遍太啰嗦，收进控件里只写一次。

### 2.3 尺寸按角色，一个数决定

不再有「同样是 20 号，字形看起来不一样大」这件事 —— 路径的墨水范围是常量。按角色取：

| 角色 | 尺寸 |
| --- | --- |
| 传输区上一首 / 下一首 | 28 |
| 播放 / 暂停 | 24 |
| 队列 | 22 |
| 列表工具栏、面板标题 | 14 ~ 16 |
| 右区图标按钮、播放条常规 | 16 ~ 20 |

### 2.4 C# 里的动态字形

有 7 处在代码里返回字形字符串或需要在两态间切。别让 ViewModel 去持有 `Geometry`，按情况分两种：

| 情况 | 做法 | 例子 |
| --- | --- | --- |
| 两态、View 能判断 | ViewModel 出一个 `bool`，XAML 里叠两颗 `Icon` 靠 `Visibility` 切 | 音量（`PlayerViewModel.IsMuted`）、播放/暂停 |
| 两态、状态不在 ViewModel | code-behind 查一次资源给 `Icon.Data` | 歌词页全屏（`LyricsPage.ResourceIcon`） |

要改的 7 处：`PlayerViewModel.VolumeGlyph`、`PlayPauseGlyph`、`Formats.CollectGlyph`、
`Formats.FollowGlyph`、`BangListViewModel.IconGlyph`、`ThemeViewModel.CurrentGlyph`、
`LyricsPage.FullscreenIcon`。

---

## 3. 替换清单

按现在的码位列全（30 个）。Fluent 的图标名以官方仓库为准，落地时逐个核对。

| 现在 | 是什么 | 用在哪 | → Fluent |
| --- | --- | --- | --- |
| `E768` / `E769` | 播放 / 暂停 | 播放条、歌词页、专辑页、歌单页、搜索页、曲目行 | `play` / `pause` |
| `E892` / `E893` | 上一首 / 下一首 | 两页传输区；「下一首播放」菜单项 | `previous` / `next` |
| `E142` | 队列 | 播放条；「加入队列」菜单项 | `text_bullet_list_ltr` |
| `E72D` | 分享 | 播放条、歌词页、专辑页、歌手页 | `share` |
| `EB51` / `EB52` | 收藏 空心 / 实心 | 播放条、歌词页、工具栏、菜单 | `heart` / `heart_filled` |
| `E90A` | 评论 | 歌词页 | `comment` |
| `词` | 歌词页入口 | 播放条 | `music_note_2`（或 `lyrics` 类字形） |
| `E740` / `E73F` | 全屏 / 退出全屏 | 歌词页 | `full_screen_maximize` / `full_screen_minimize` |
| `E70D` / `E76C` | 展开 / 收起 | 榜单页、播放队列 | `chevron_down` / `chevron_right` |
| `E711` | 关闭 / 清除 | 评论图片、播放队列、评论面板、工具栏 | `dismiss` |
| `E710` | 新建 | 侧栏、工具栏 | `add` |
| `E712` | 更多 | 曲目行、歌单页 | `more_horizontal` |
| `E72C` | 刷新 | 侧栏、工具栏、主窗口 | `arrow_clockwise` |
| `E74D` | 删除 | 工具栏、歌单页 | `delete` |
| `E72B` | 返回 | 主窗口标题栏、评论面板 | `arrow_left` |
| `E72E` | 移除 | 侧栏歌单、歌单列表 | `subtract` |
| `E8FD` | 专辑 | 工具栏、菜单「查看专辑」 | `album` |
| `E77B` | 歌手 | 菜单「查看歌手」 | `person` |
| `E706` / `E708` | 浅色 / 深色主题 | 标题栏 | `weather_sunny` / `weather_moon` |
| `E770` | 跟随系统主题 | 标题栏 | `desktop` |
| `E74F` / `E767` | 静音 / 音量 | 播放条、歌词页 | `speaker_mute` / `speaker_2` |
| `E738` | 放大 | 评论图片查看 | `zoom_in` |
| `E8CD` | 评论空态 | 评论面板 | `chat` |
| `E762` | 排序 | 曲目工具栏 | `arrow_sort` |
| `E70F` | 编辑 | 歌单页 | `edit` |
| `E76B` | 上一页 | 首页横滑 | `chevron_left` |
| `播放模式 ×3` | 顺序 / 列表循环 / 随机 | 播放条 | 已经是 Fluent 路径，迁进新字典即可 |

---

## 4. 实施顺序

每步一个提交，可独立验收。

1. **基建** —— 新建 `Themes/Icons.xaml` + `Controls/Icon`；把现有的三个播放模式路径迁进来（观感不变，
   纯粹换存放位置）；`App.xaml` 注册字典。
2. **播放条 + 歌词页** —— 用户最先看到、也是现在补丁最密的地方（「词」和音量那两条 `Margin` 在这个
   阶段删掉）。这一步做完，「对不齐 / 调不了大小」应当肉眼可见地消失。
3. **曲目列表 + 工具栏 + 评论面板**。
4. **侧栏 + 标题栏 + 账号菜单 + 其余页面**。

---

## 5. 风险与边界

- **许可**：Fluent System Icons 是 MIT。要在 `NOTICE` 或 `README` 里记一笔（项目已有 VipBadge 与
  播放模式路径两处先例）。**不许**从参照物、APK 或任何闭源客户端里取图标。
- **笔画粗细**：Fluent 的 24 版缩到 14 会偏细。清单里 14 号那几个（工具栏、面板标题）先按 24 版缩放看，
  显细就换该图标自己的 20 版几何。
- ★★ **`Geometry` 不能当资源用 `{StaticResource}` 赋给 Geometry 属性**（实施时踩到，付出一次
  「双击打不开」的代价）。资源字典里写 `<Geometry x:Key="IconHeart">M ...</Geometry>`，
  用点写 `Data="{StaticResource IconHeart}"` —— 编译期 **0 错误**，启动时**必崩**：

  ```
  XamlParseException: Failed to assign to property 'Bodian.WinUI.Controls.TrackStatisticButton.Data'
  Failed to assign to property 'Microsoft.UI.Xaml.Controls.PathIcon.Data'   ← 连内置控件也一样
  ```

  定位过程：先把失败点从自定义控件换成内置 `PathIcon`，两边都炸 —— 于是排除了自定义控件与
  自定义枚举，锁定到「Geometry 来自资源字典」这一件事。**这属于运行期**（解析 XBF）**才暴露的问题，
  构建通过完全不能作为「能跑」的证据。**

  **解法**：字典里存路径**文本**（`<x:String>`），控件内部逐个转成各自的 `Geometry` 实例
  （`Controls/IconGeometry.From`）。文本到 Geometry 走的是 XAML 自己的类型转换器，
  与 `<Geometry>M ...</Geometry>` 是同一套解析。
- **XAML 编译器会拿上一次的元数据**（实施时踩到的第二坑）：改完 `Icon.Data` 的类型后直接构建，
  生成的 `*.g.cs` 还在按旧类型转换，报一堆 `CS1503 无法从 string 转换为 Geometry`，
  夹杂 `WMC9999 未将对象引用设置到对象的实例`。**清掉 `obj/` 重建**才恢复
  （清 `obj` 会连 `project.assets.json` 一起删掉，要重新还原一次包）。
- **路径文本的写法**：值里不能出现 `<` `>` `&` `"`，Fluent 的路径只含 `M L C A Z` 与数字、逗号、空格，
  安全。写成 `<PathGeometry Figures="M ..."/>` 则编译不过 —— `PathFigureCollection` 没有路径简写的
  类型转换器，报 `WMC0055`。
- **路径方向**：SVG 的隐式分隔必须规范化成 XAML 认的写法，这个坑 `play-queue.md` §7 已经踩过一次。
- **高对比度**：路径图标一律走 `Foreground` 继承，不额外写死颜色，高对比度下自动跟系统配色。
- **本次不动布局**：只换图标来源，各按钮的尺寸、间距、位置保持不变。有任何观感调整另开一轮。

---

## 6. 验证

```powershell
dotnet build src/Bodian.WinUI/Bodian.WinUI.csproj --no-restore --verbosity minimal
```

界面部分逐页过一遍（自动检查覆盖不到）：

| 页 | 看什么 |
| --- | --- |
| 播放条 | 传输区图标同高同重心；「词」和音量不再需要 `Margin` 补偿就和音质标签对齐 |
| 歌词页 | 传输区、评论、音量、顶部 ↓ / ⛶ |
| 曲目列表 | 行内播放键、悬停「更多」、工具栏 |
| 评论面板 | 关闭、发送、点赞、空态 |
| 侧栏 / 标题栏 | 新建、刷新、删除、返回、主题三态 |
| 深浅两个主题 + 高对比度 | 图标颜色跟随，不出现黑色底上的黑图标 |
