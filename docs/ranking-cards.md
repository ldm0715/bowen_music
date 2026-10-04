# 排行榜页：长条列表 → 可折叠的卡片网格

2026-10-04。排行榜页原来是**摊平的长条列表**：分组标题、榜头行、该榜 5 首预览曲目全部塞进
一个虚拟化 `ListView`，靠 `ResultTemplateSelector` 按运行时类型挑模板。一屏看下去只有
榜名和歌名，看不出「这是个榜」。

现在是一个**两列卡片网格**：一个榜一张卡，卡上是「榜名 + 更新时间」「封面 + 前三名」；
分组表头**可以点击收起 / 展开**，滚动时表头钉在顶部，随时看得到自己在哪一组。

改之前先把「参考图上的元素，接口到底给不给」钉死 —— 结论比预想的少，所以这一页
只做了形态与交互，没有编造数据。

## 参考图上有、接口没有的四样东西

样本：`fixtures/home-bangNew.json`（榜单首页）、`fixtures/bang-16-musics.json`（单榜曲目），
接口 `service/home/bangNew`（无参）。

| 参考图上的元素 | 接口现状 |
| --- | --- |
| 更新时间 | 有，但只有榜级的 `pubStr`，形如 `09-30更新`。图上的「刚刚更新」「每周四更新」「每30分钟更新」「更新11首」**接口不返回**，是官方客户端自己拼的文案 |
| 「新」绿色角标 | 曲目上有 `isNew`（int），但两份样本连同其余 fixture 共 **279 条记录全是 0**，没有见过 1 |
| ▲红升 / ▼绿降 / — 持平 | **任何榜单响应里都没有这个字段**。现有的名次是「位置」推出来的（`RankedTrack(Rank, Track)`），不是响应字段 |
| 图里那 6 个榜里的 3 个 | **原创榜、数字专辑畅销榜、歌曲畅销指数榜**在接口和逆向文档里都查不到。`bangNew` 原始回 5 组 22 条，其中「H5榜单」那组 2 条没有 `id`（是外部 H5 链接）被过滤掉，所以页面实际是 **4 组 20 个榜**，与图上只有 3 个重合 |

于是这一页的四条决定：

1. **保留接口返回的全部分组**，每组一段，组内排卡片 —— 数据一个不丢
2. **不做**升降标记（没有数据，本地快照对比会算出假的涨跌）
3. **不做**「新」角标（服务端从不给 1）
4. 更新时间直接显示 `pubStr` 原文，**不模仿**官方那一族文案

## 卡片与表头规格

**卡片**：底色 `HomeCardBackgroundBrush`、圆角 `RadiusMd`、`Padding=16`，与发现页卡片同源。
第一行「榜名（`BodyStrongTextBlockStyle`）+ 更新时间（`CaptionTextBlockStyle` / Tertiary，右对齐）」，
第二行「封面 96 方 + 前三名」。尺寸令牌在 `Themes/Tokens.xaml`：
`SizeBangCard`(380) / `SizeBangCardHeight`(160) / `SizeBangCardCover`(96) / `SpaceBangCardGap`。

宽度 380 是按**默认窗口**算的：默认内容区约 825 逻辑像素，`ItemsWrapGrid` 格子取 400 时
两张正好 800，右边不留大片空白。曾经是 440 —— 那样默认窗口只放得下一张，右边空出将近一张卡的宽度。

**表头**：整行一颗 `Button`（`BangSectionHeaderButton`，套用 `BodianCardPagerButton` 那套
「透明底 + VSM 换底色」的写法），内容是「折叠箭头（次级色）+ 组名（`BodianModuleTitle`）+
这一组有几个榜（Tertiary 小字）」。点击收起 / 展开。

**交互只有一条**：点卡片任意位置 → 进该榜详情页（压栈，侧栏继续高亮「排行榜」）。
**卡片上的曲目行不接点击** —— 曾经点一行就播那一首，已按使用者要求删掉：
卡片上只有「进详情」一个动作，想听就先进榜详情页，那边的曲目列表才是能播的地方。
（正在播放的那一首在卡片上仍显示起伏条，那是状态不是入口。）

## 四处容易翻车的地方

### 1. 表头下面那条线：`GridViewHeaderItem` 自带的，得换 `HeaderContainerStyle`

分组表头下面有一条 1px 全宽分隔线，压着页面的氛围渐变很难看。
它**不在 `HeaderTemplate` 里** —— GridView 给每个表头自动生成一个 `GridViewHeaderItem`，
默认模板里有一个 `Rectangle`，那就是这条线。`HeaderTemplate` 管的是内容，管不到容器。

改 `GroupStyle.HeaderContainerStyle`（目标类型必须是 **`GridViewHeaderItem`**；
用 ListView 时才是 `ListViewHeaderItem`，写错静默不生效），模板里只留一个 `ContentPresenter`。

> 走过弯路：一开始改的是 `GroupStyle.ContainerStyle`（目标 `GroupItem`），没有任何效果 ——
> 那条根本不是分组容器画的。用像素采样定位到线在卡片区域之外（x=1000 处也有）才确认它是全宽的。

### 2. 悬停底必须由卡片自己画，不能借容器那层

`GridViewItem` 自带 pointer-over 底色，但它铺在**整格**上、而卡片底色是不透明的 ——
两者一叠，容器那层永远看不见。这是本项目里反复出现的「悬浮背景不生效」。

所以：容器 `Padding=0`，把它彻底让到卡片后面去；悬停由卡片模板里**最后一层 `Border`** 负责 ——
位置在内容**之上**（压得住不透明卡片底）、卡片边界**之内**（不可能被父容器或滚动视口裁掉），
`IsHitTestVisible="False"` 让它对指针透明。不用负 margin 的外溢光晕：
那种做法会被滚动视口裁掉，也会和卡片自己的底色打架。

状态挂在 `BangItemViewModel.IsPointerOver` 上（`HoverOpacity` 出 0/1），不在容器上。

### 3. 卡片里不能再套集合控件

`ViewPerformanceContractTests` 明确禁止 `BangListPage` / `SearchPage` / `DiscoverPage` 三个页面
出现 `ItemsControl` —— 它会一次性实现整组内容，一页 22 张卡再各套一个就是嵌套的整组渲染。

卡片本来就只显示前三名，所以改成**三个具名槽位**（`BangItemViewModel.Rank1/2/3` +
`Rank2Visibility/Rank3Visibility`），用三个 `ContentControl` 套同一个行模板。
卡高固定也因此有了结构上的保证。

### 4. 长歌名要显式 `TextWrapping="NoWrap"`

曲目行的歌名与歌手写在同一个 `TextBlock` 的三个 `Run` 里，整行一起截断
（拆成两个 `TextBlock` 的话，歌名超长时后面的「 - 歌手」会留在原地被切掉半截）。
另外显式写死 `NoWrap`：默认值虽然是它，但一旦被谁改掉，长歌名折成两行就会把定高的卡片撑破。

## 收起 / 展开怎么实现

`BangSectionViewModel` **本身就是那一组的集合**（继承 `ObservableCollection<BangItemViewModel>`，
分组要求如此）。收起就是 `Clear()`，展开就是从 `_all` 装回来 —— 不另做可见性标记。

成立的前提是 **`GroupStyle.HidesIfEmpty` 默认 false**：组空了不会被隐藏，表头留在原地，
否则收起的组连点回去的地方都没有。

`IsExpanded` 自己转发 `PropertyChanged` 而不用 `[ObservableProperty]`：那个源生成器要求基类是
`ObservableObject`，而这里必须继承 `ObservableCollection<T>`。好在这个基类本来就有 `OnPropertyChanged`。

表头还挂了 `AutomationProperties.Name`（「置顶位，已展开，点击收起」）：表头内容是箭头 + 文字 + 数量，
读屏念不出「点了会怎样」。

### 表头被钉住时不许收起

钉住的那一组，卡片正显示在下面；这时一收，面板立刻重排，钉的位置会在
「钉这一组」和「钉下一组」之间来回切，看起来就是一阵抽搐。所以 `OnSectionClick` 里先问一句
`IsHeaderPinned`，是就什么都不做 —— 想收这一组，先滚一下让它离开钉住的位置。

判据是两个条件的**与**：列表确实滚过（`ScrollViewer.VerticalOffset > 0`），
且表头贴在视口顶端（相对 GridView 的 y 落在 2px 容差内）。
少了前一条，滚到最顶上时的第一个表头也会被判成钉住 —— 那时收起明明没有任何问题。

表头的位置由面板负责移动（钉住就是把它挪到顶部），所以取它当下的 `TransformToVisual` 即可，
不需要额外的状态位。找祖先 `GridViewHeaderItem` 与后代 `ScrollViewer` 的两个泛型辅助写在页面里，
仓库原有的同类代码是「按名字找模板部件」，这里要按类型找，所以没有复用。

## 分组：这一页是仓库里第一处用分组

`GridView` 要显示「表头占满整行 + 底下换列」必须走 `CollectionViewSource`：
`BangListViewModel.GroupedSections`（`IsSourceGrouped = true`）。

**给 `ItemsSource` 的必须是 `CollectionViewSource.View`，不能是 `CollectionViewSource` 本身。**
UWP 时代两者都行，WinUI 3 不行：把 CVS 直接赋给 `ItemsSource` 会抛
`ArgumentException: Value does not fall within the expected range`，
而且是在 XAML 绑定阶段抛 —— **症状就是「一点排行榜就闪退」**，堆栈落在
`BangListPage.g.cs` 的 `Update_ViewModel_GroupedSections` 里，看不出是资源有问题。

源在 ViewModel 构造期就挂上：`x:Bind` 是加载时求值一次，等加载完再挂的话
`View` 还是 `null`，第一次绑定拿到的是空源，页面会空着。

分组还带来一个白拿的好处：**表头滚动时会钉在顶部**，随时看得到自己在哪一组。
代价是表头背景是透明的，钉住时卡片会从它下面滑过；这一版先这样，觉得晃眼再单独处理。

## 验收

Core 层没动，`dotnet test --project tests/Bodian.Core.Tests/Bodian.Core.Tests.csproj`
仍是 **1062 通过 / 0 失败**（含上面那条性能契约测试）。

已用 UI Automation 核实（读元素树与坐标，不截图）：

- 同一 Y 上并排出现两个榜名（`热歌榜` x=687 / `新歌榜` x=1187，同为 y=459）→ 两列成立
- 表头右侧显示组内榜数（`置顶位` … `2 个榜`）
- 点表头 → 该组卡片消失、下一组上移；再点 → 全部装回
- 长歌名（`Sold Out - Hawk Nelson&Jonathan Steingard`）不换行
- 卡片上的曲目行不再可点，整张卡只有「进详情」一个动作

仍需人工目视确认：

1. 表头下面那条分隔线**没了**（这是本次改 `GridViewHeaderItem` 的目标）
2. 表头字号与页面其他标题（「排行榜」、发现页模块标题）是一套
3. 两列卡片右边几乎不留白；窗口拉宽会自动换三列
4. 悬停表头 / 悬停卡片都有 5% 底色，相邻卡片不会被一起点亮
5. 浅色 / 深色 / 高对比度三套主题下表头与悬停底色都正常
6. 点卡片进详情、返回键能退回
7. **表头钉在顶部时点它没有反应**（不抽搐）；滚回顶部或滚过这一组之后能正常收起

## 相关文件

| 文件 | 作用 |
| --- | --- |
| `src/Bodian.WinUI/Views/BangListPage.xaml` / `.xaml.cs` | 卡片网格、表头样式与折叠、事件 |
| `src/Bodian.WinUI/ViewModels/BangListViewModel.cs` | `BangItemViewModel` / `BangSectionViewModel` / 分组源 |
| `src/Bodian.WinUI/Themes/Tokens.xaml` | 卡片尺寸令牌 |
| `src/Bodian.WinUI/Views/BangDetailPage.xaml` | 单榜详情（未改动，唯一能点播的地方） |
| `tests/Bodian.Core.Tests/ViewPerformanceContractTests.cs` | 挡 `ItemsControl` 的那条契约 |
