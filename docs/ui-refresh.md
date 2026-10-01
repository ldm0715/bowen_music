# UI 精修：设计 token 与外壳

**写于 2026-10-01。** 本文是「把参照物 `LyciaMusic` 的设计语言迁移到本客户端」的设计定稿与实施记录。

**先读**：`library-sidebar.md`（外壳与侧栏的信息架构，本文不改它）。

---

## 0. 许可证边界（硬约束）

| | |
| --- | --- |
| 参照物 LyciaMusic | **AGPL-3.0** |
| 本项目 Bodian | **GPL-3.0** |

`roadmap.md` §许可 已写明：「**GPL-3.0 可抄，AGPL-3.0 不可**」（两者单向兼容）。

**允许**：迁移设计参数——色值、间距梯度、圆角阶梯、字号层级、布局范式、交互状态公式。
这些是设计思想与事实，色号与像素值本身不受版权保护。

**禁止**：照抄任何 CSS / XAML / TS 源码片段、图标资源、字体文件、品牌名与 logo、逐字文案。
本次**不做任何代码移植**，落地产物全部是独立重写的 XAML。

---

## 1. 设计 token

### 1.1 强调色必须拆成两条轨道

品牌色 `#00F3B0`（薄荷绿）对白底的对比度**只有 1.45:1**，当浅色主题的文字色完全不可读。
参照物的 `#EC4141` 是中深红，恰好一色通吃，绿色不行。

对比度实算（WCAG 相对亮度）：

| 组合 | 比值 | 判定 |
| --- | --- | --- |
| `#00F3B0` on `#FFFFFF` | 1.45:1 | ❌ 不能当文字 |
| `#00F3B0` on `#121212` | 12.89:1 | ✅ |
| `#007A57` on `#FAFAFA` | 5.13:1 | ✅ 过 WCAG AA |
| `#002019` on `#00F3B0` | 11.83:1 | ✅ |

| 用途 | 浅色 | 深色 | 键 |
| --- | --- | --- | --- |
| 填充 / 徽标 / 进度条 / 播放键底 | `#00F3B0` | `#00F3B0` | `AccentFillColorDefaultBrush` |
| 文字 / 图标（浅底） | `#007A57` | `#00F3B0` | `AccentTextFillColorPrimaryBrush` |
| 强调填充之上的前景 | `#002019` | `#002019` | `TextOnAccentFillColorPrimaryBrush` |

> **`TextOnAccentFillColorPrimaryBrush` 必须覆盖。** 系统浅色下它是 `#FFFFFF`，
> 压在 `#00F3B0` 上只有 1.45:1 —— 主播放按钮的图标会直接看不见。

### 1.2 骨架尺寸

| 元素 | 值 | token |
| --- | --- | --- |
| 顶部栏 | 64 | `SizeTopBar` |
| 底部播放栏 | 80 | `SizePlayerBar` |
| 歌曲行高 | 64 | `SizeRowHeight` |
| 列表 / 播放栏封面 | 48，圆角 8 | `SizeCoverRow` / `RadiusMd` |
| 播放按钮 | 44 圆形 | `SizePlayButton` |
| 导航图标 | 16 | `SizeIconSm` |

### 1.3 状态公式

- **选中** = 底 `black/10`（深色 `white/10`）+ 主文字色 + 半粗 + 水平位移 4px
- **hover** = 底 `black/5`（深色 `white/5`）
- **侧栏与内容区之间无可见分割线**，靠背景层次区分

---

## 2. 落地结构

```
src/Bodian.WinUI/Themes/
  Tokens.xaml    主题无关标量：尺寸 / 圆角 / 间距 / 字号 + ControlCornerRadius 覆盖
  Theme.xaml     浅色 / 深色 / 高对比度三套（内联 ThemeDictionaries）
```

`App.xaml` 的合并**顺序是硬要求**——自定义字典必须排在 `XamlControlsResources` **之后**。
合并字典的查找是「后合并的优先」，这是覆盖系统画刷的唯一机制。

---

## 3. 踩过的坑

### 3.1 深色模式下侧栏一片白、导航项看不见

**症状**：切到深色后，`NavigationView` 面板退化成白色，导航项是白字压白底所以整个看不见；
内容区变成灰色。浅色下一切正常。

**根因**：**这个应用从来没有给窗口设过任何背板（`SystemBackdrop`）。**
而 `NavigationView` 的两块底色在设计上都不是实色：

```
NavigationViewDefaultPaneBackground → AcrylicInAppFillColorDefaultBrush   ← 亚克力刷，要采样背后
NavigationViewContentBackground     → LayerFillColorDefaultBrush          ← 半透明层刷
```

半透明层刷浅色是 `#80FFFFFF`、深色是 `#4C3A3A3A`，两者都假定自己叠在 Mica 之上。
没有背板时它们直接与桌面合成：

- 浅色：`#80FFFFFF` 叠在浅色桌面上 ≈ 白 → **碰巧正确**
- 深色：`#4C3A3A3A` 叠在浅色桌面上 = 灰；亚克力退回浅色 → **白面板配白字**

**这是既有缺陷，不是本次改动引入的** —— 在此之前客户端没有主题切换入口，深色这条路径从未被走过。

**修法**（`Themes/Theme.xaml` + `MainWindow.xaml`）：

1. 自己铺一层不透明窗口底色 `AppSurfaceBrush`（浅 `#FAFAFA` / 深 `#121212`）
2. 把那四个 `NavigationView` 背景刷覆盖成 `Transparent`，让自铺的底色透上来

> 之所以不用 `SystemBackdrop` 从正面解决：`Mica` 需要 Win11，且本项目的背景层
> 要留给「跟随封面取色的氛围渐变」，不能交给系统。

### 3.2 `RequestedTheme` 必须设在最外层元素上

**症状**：修完 3.1 之后反而更糟 —— 深色下整个窗口一片白。

**根因**：`RequestedTheme` 一开始设在 `NavigationView` 上，而**铺底色的那层是它的兄弟节点**。
兄弟拿不到主题，`{ThemeResource AppSurfaceBrush}` 解析成浅色值。

**修法**：设在最外层的 `Grid` 上，底色层作为它的**子节点**。
不要设在中间层元素上 —— 只有该元素的子树会跟随。

### 3.3 `ThemeDictionaries` 不要用 `Source` 引外部文件

```xml
<!-- 不要这样写 -->
<ResourceDictionary x:Key="Light" Source="ms-appx:///Themes/Colors.Light.xaml" />
```

这种写法**能编译、能启动**，但运行期做主题查找时行为不对。
三套字典一律**内联**在 `Theme.xaml` 的 `ThemeDictionaries` 里。

### 3.4 主题切换时高对比度字典必须存在

只在 `ThemeDictionaries` 里定义、顶层没有兜底的自定义键，在 `HighContrast` 下会解析失败
（运行期抛 `XamlParseException`，表现是启动即崩）。三套必须齐全。

### 3.5 `x:DataType` 的类型必须有公开无参构造，属性不能用 `required` / `init`

XAML 类型信息生成器会为数据类型的**每个公开属性**生成 `new 该类型()` 与 setter。
生成的文件是 `obj/.../XamlTypeInfo.g.cs`，出问题时会给出很直接的报错：

```
error CS8852: 只能在对象初始值设定项中分配 init-only 属性 "TrackRow.Ordinal"
error CS9035: 必须在对象初始值设定项中设置所需的成员 'Track.Id'
```

两条约束：

1. **`x:DataType` 指向的类型（以及它所有公开属性的类型）都要有无参构造。**
   所以 `TrackRow` 不能公开一个 `Track` 属性 —— `Track` 是带 `required` 成员的领域模型，
   不允许空构造。原始曲目只能留成 `internal`，显示字段在 `TrackRow` 上铺开。
   **不要为了迁就生成器去给领域模型加空构造** —— 那会让一个语义上不完整的对象变得可构造。
2. **参与绑定的属性不能用 `required` / `init`。** 生成器要给它生成 setter，`init` 会让那行编不过。

### 3.6 `Duration` 是「时:分:秒」，秒不能 ≥ 60

```xml
<!-- 错：想表达 70 秒，但秒位超范围 -->
<DoubleAnimation Duration="0:0:70" />
<!-- 对 -->
<DoubleAnimation Duration="0:1:10" />
```

**这个错误编译期不报**，只在运行期抛 `XamlParseException`
（`Failed to create a 'Microsoft.UI.Xaml.Duration' from the text '0:0:70'`），
表现是**应用直接打不开**。

> **由此得出一条纪律**：XAML 里凡是「字符串形式的强类型值」（`Duration` / `CornerRadius` / `Thickness` /
> 颜色字面量 / 枚举名），编译期都校验不到。**改完 XAML 必须实际启动一次**，
> 光「构建通过」不足以说明能跑。

---

## 4. 实施进度

| 步 | 内容 | 状态 |
| --- | --- | --- |
| 1 | Token 层 + 强调色覆盖 + `ControlCornerRadius=6` | ✅ |
| 2 | 主题切换（`AppTheme` 持久化 + 顶栏三选一） | ✅ |
| 2.5 | **修深色模式**（见 §3.1 / §3.2） | ✅ |
| 3a | 侧栏重样式（透明面板 / 去竖条 / 胶囊选中 / 状态色） | ✅ |
| 3b | 侧栏 4px 位移 + 选中加粗（可选，需自写模板） | ⬜ |
| 3c | 外壳布局：播放条通栏、账号入口上顶栏、侧栏 200px（见 §5） | ✅ |
| 4 | 自定义标题栏 | ⬜ |
| 4.5 | 窗口默认尺寸 1200×800 + 居中 + 记住上次 | ✅ |
| 5 | 氛围渐变背景 | ✅ |
| 5.5 | 层级分离（见 §8.3：**侧栏与顶栏已回退**，只留播放条那一层） | ✅ |
| 6 | 封面取色（见 §8） | ✅ |
| 7 | 歌曲列表重做（64px 行 + 序号列 + 正在播放高亮） | ✅ |
| 7.5 | 榜单两页并入共享行模板（见 §7） | ✅ |
| 8 | 底部播放栏精修（48 封面 + 44 圆按钮 + 细进度条） | ✅ |
| 9 | 其余页面骨架 Style 收敛（见 §6） | ✅ |
| 10 | 播放条布局常量同源化（见 §9） | ✅ |

---

## 5. 外壳布局（2026-10-01 按用户要求改）

三处改动，**推翻了 `library-sidebar.md` 里「侧栏从顶到底、模仿官方 PC 端」那条决策**：

1. **播放条通栏** —— 从 `NavigationView.Content` 里挪到根 `Grid` 的第 1 行，横跨整个窗口宽度。
   代价是侧栏不再到窗口底部。底色层与氛围渐变层都加了 `Grid.RowSpan="2"`，
   否则播放条那一带会露出纯色，与上面的渐变之间出现一条硬边。
2. **账号入口上顶栏** —— 侧栏底部的 `PaneFooter` 账号卡片整个删掉。
   顶栏现在是 `[搜索框] [主题按钮] [头像按钮]`；点头像弹 `Flyout`，里面有头像、昵称、
   VIP 徽标与退出登录。属性必须是 `OneWay`（顶栏在主窗口构造时就建好了，那时还没有会话）。
3. **侧栏 200px** —— `OpenPaneLength`（默认 320）。

## 6. 页面骨架收敛

十三个页面各自写了一遍 `<Grid Padding="24,16" RowSpacing="…">` + 16px 的 `ProgressRing`，
行间距有三种值（12 / 10 / 8）。现在收敛到 `Themes/Styles/Pages.xaml`：
`BodianPageRoot` / `BodianPageTitle` / `BodianPageStatus` / `BodianPageBusyRing` / `BodianPageFooter`。

顺带归一化了圆角（10 / 8 / 6 / 4 混用 → token 阶梯）与封面尺寸（散落 13 档 → 对到 token）。

**`LyricsPage` 不在本次范围内**（歌词页暂不改）。

> **一条已验证的编译器事实**：WinUI 的 `Style` **可以**作用于 `Grid` 这类非 `Control` 的
> `FrameworkElement`（`Style.TargetType="Grid"` + `Padding`/`RowSpacing` setter 编译通过）。
> 先用单个页面验证过才推广。

---

## 7. 榜单页并入共享行模板

`BangDetailPage` 与 `BangListPage` 原先**各写了一份曲目行模板**（`BangDetailPage` 的注释自己都承认
"与榜列表页同一套行样式"）—— 样式改一处漏一处，而且两页都没有「正在播放」指示。

现在两页都用 `TrackRow`：

- `BangDetailPage` 的行模板整个换成 `TrackListView`（它现在认得 `RankedTrack` 数据源：
  用条目自带的 `Rank` 当序号、**不补零** —— 「第 1 名」写成 `01` 是错的，曲目列表才补零）。
- `BangListPage` 的 5 首预览**不能**换成 `TrackListView`（内含 `ListView`，嵌在分组里会和外层滚动打架），
  所以保留 `ItemsControl`，但数据源换成 `TrackRow`，行模板复用同一套「名次 / 波普条 / 播放键」三态。

顺带删掉了 `BangVisuals.cs`（只有 `RankBrush` 一个成员，改用 `{ThemeResource}` 后没有消费方了）。
它原来的写法有个隐蔽的错：

```csharp
// 取的是**应用级**主题（跟随系统），不是元素级 RequestedTheme
Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
```

深色模式下会拿到浅色主题的值，前三名的名次压在深色底上看不见。
**凡是要跟随主题的颜色，一律写 `{ThemeResource}`，不要在代码里读 `Application.Current.Resources`。**

---

## 8. 封面取色与层级分离

### 8.1 背景为什么曾经「脏」

三团色是**手挑的三个色相**（薄荷绿 → 蓝 → 紫罗兰），跨度大。
三个半透明色团叠在一起，重叠区在 sRGB 里往灰走 —— 表现就是中间那块浑浊发脏。

**根治办法是让颜色有来源。** 同一张封面上取出来的色天生同族，不会出现三个互不相干的色相交叠。

### 8.2 取色：取一个主色，再派生，而不是硬挑三个

```
封面 → CoverArtUrl.Jpeg(60px) → Win2D 解码 → 像素摊平成 BGRA
     → ColorQuantizer.Dominant（直方图分桶）
     → ColorPalette.Ambient（按色相 ±40° 派生三团）
```

**为什么不直接从封面里挑三个色**：挑出来的三个可能彼此毫不相干（封面上一块青、一块橙、一块灰），
三团叠起来照样脏。按色相派生则保证三个色同族 —— **「和谐」是构造出来的，不是碰运气**。

**打分函数里的两个关键决定**：

1. **面积开平方，不直接乘。** 直接乘的话面积大的一定赢 —— 一片白底占 80%、彩色图案占 20%
   的封面，白底照样胜出，那这套打分等于没写。开平方把面积优势压平，让饱和度有机会说话。
   **测试里有专门一条守它**（大片白底 + 小块饱和色，必须取到彩色）。
2. **亮度惩罚以 0.55 为中心。** 纯白（1.0）与纯黑（0.0）都被压到三成以下，避免取出封面的白边黑边。

**灰阶封面**没有可用色相，退回品牌色相（163.5°）—— 硬把饱和度拉起来会凭空冒出一个红。

**取色必须走 `.jpg` 改写地址**：原始地址多半是 WebP，而 **Win10 不预装 WebP 编解码器**，
Win2D 走 WIC 在干净的 Win10 上会**静默失败**（开发机装过扩展，所以看不出来）。

**切歌竞态**：解码是异步的，连着切两首时前一首可能后完成。完成后再比对一次曲目 id，对不上就丢弃。

### 8.3 层级分离：**试过，已回退**

**结论：侧栏与顶栏一律全透明，渐变连续流过；只有播放条留了一层很淡的底 + 顶部细线。**

曾经给侧栏与顶栏各叠一层极淡的底做「分层」，结果是**背景被切成三块矩形、中间是硬边**。
两个层面都错了：

1. **实现上是同一个坑踩了第二次**：侧栏那层写成了
   `<StaticResource x:Key="NavigationViewDefaultPaneBackground" ResourceKey="…"/>` ——
   主题字典里 `StaticResource` 的解析时机不可靠（与本文件 §3.x 里 `AccentButton` 那条同源）。
   别名没生效，面板退回了系统的**亚克力刷**，整块侧栏变成不透明的灰板，把渐变挡在外面。
   **数值要直接写字面量，不要用别名。**
2. **理念上不成立**：参考项目那种毛玻璃分层能成立，是因为它**真的采样背后的模糊**。
   这里没有背板可采样，叠上去只是一块平色 —— **在连续渐变上叠平色，边界一定看得出来**。

保留的两个 token：`SurfaceRaisedBrush`（播放条，浅色 40% / 深色 18%）、
`SurfaceStrokeBrush`（播放条顶部细线）。播放条是通栏的常驻控制条，与页面之间确实需要一条界线。

> **一个具体的坑**：播放条的底色一开始写成了 `#E6FFFFFF`（90% 不透明）——
> 那基本就是实心白，深色下是实心黑，把渐变全盖住了。**分层靠的是「比周围亮一点」，
> 不是「盖住周围」。** 上限别超过 40%。
>
> 这一条来自一份外部建议（"给侧栏与播放条各一层底色"）。它同时主张把背景换成单色暗角
> （`#1E1E2E → #11111B`），**没有采纳** —— 那会删掉「跟随封面变色」这个设计，
> 而且整套色值只考虑深色，浅色模式会直接坏掉。

---

## 9. 播放条布局常量同源化

播放条的信息块宽度与徽标间距，原先在两处各写了一份：XAML 里是 `Width="220"` / `Spacing="6"`，
而 `Formats.PlayerTitleMaxWidth` 里是 `const double block = 220` / `const double gap = 4`。

**这两处漂了，而且是静默的**：算式里的徽标间距写死 4，XAML 里实际是 6。
两个徽标（试听 + 付费）同时出现时，算出来的曲名最大宽度多了 2px ——
曲名够长时第二个徽标会被顶出信息块。界面上只是「徽标怎么偏出去了」，看不出是算错的。

现在由 `Formats` 当唯一来源，XAML 反过来绑它：

- `Formats.PlayerInfoBlockWidth` ← `PlayerBar.xaml` 的 `Width`
- `Formats.PlayerBadgeSpacing` ← `PlayerBar.xaml` 的 `Spacing`

> 这类「同一个值在 XAML 与 C# 各写一份」的漂移，用 `{x:Bind local:Formats.X}` 绑静态属性就能根治 ——
> 函数绑定本来就在用同一个命名空间，静态属性没有额外成本。
