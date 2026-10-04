# 发现页卡片区：不滚动，两侧箭头翻页

2026-10-03。发现页每个模块是一排横向排列的卡片。原来那排是**横向滚动**的，
而 WinUI 的 ScrollViewer 在「不能竖滚、能横滚」时会把鼠标滚轮**吞掉转成横向滚动** ——
指针停在卡片区时页面滚不动，必须先移开。

这次把卡片区的滚动整个去掉：一屏只放「放得下的那些」，两侧的箭头按钮翻页；
顺带按内容语义重做了四种卡片形态，并把发现页从数据层到界面捋了一遍。

## 为什么要去掉横向滚动

两个原因叠在一起：滚轮被劫持（上面那段），以及横向滚动条本身很占视觉重量。

试过的替代方案是「留着 ScrollViewer，用 `ChangeView` 翻页」，**这条路走不通**：
`ScrollMode="Disabled"` 时 `ScrollContentPresenter` 在那一轴用**视口尺寸**当约束去量内容，
于是 `ExtentWidth == ViewportWidth`、`ScrollableWidth == 0`，偏移被钳死在 0，`ChangeView` 是空操作。
要让 `ChangeView` 生效就得把横向开成 `Enabled` —— 那就回到了原问题。

## 零溢出 = 滚轮自己会冒泡

列表只装当前一页放得下的那些，内容宽度永远不超过视口。`ScrollViewer` 只有在**真的滚了**的时候
才把 `PointerWheelChanged` 标成 handled；这里既没有可滚的量、也没有可滚的方向（横向 `Disabled`），
所以滚轮、触摸板横滑、触屏拖动都不会被消费，直接冒泡去滚外层列表。

这是**物理保证**，不是事件拦截。

## 一个模块的语义翻转（type 4/11）

改之前：接口的每个分组各占一个条目，渲染成「小标题 + 3 张单曲卡片」，
点标题才能进那个歌单。结果是 3 首预览看着像主体，真正的歌单反而藏在一个标题后面。

改之后：**一个分组 = 一张歌单卡片**，整个模块一个条目。卡片自己带着 `HomeCard.Ai`
（打开哪个歌单要的两个参数），点整张卡进歌单。`AiPlaylistRef` 因此从 `HomeSection` 挪到了 `HomeCard`。

> `Ai` 的 `index` 用**位置**而不是分组自带的 `id`：type 11 的分组压根没有 id 字段
> （反序列化后是 0），而 type 4 的 id 在实测样本里就是位置。
> `passRecName` 只有 type 11 有，两个模块的 index 都是 0/1/2/3，**只传 index 会串台**。

## 四种卡片形态

判据是**内容语义**，不是来源 type。同一模块里卡片形态一致，所以模板由
`HomeSection.Layout` 一处决定，不必再写按项类型分派的 `DataTemplateSelector`。

| Layout | 模块 | 卡片 |
| --- | --- | --- |
| `PlaylistMosaic` | 个性化歌单（type 4） | 三张封面拼图（左一右二）+ 歌单名 |
| `PlaylistPreview` | 你的主题歌单（type 11） | 左封面 + 右侧歌单名与几行曲目预览 |
| `PlaylistCover` | 宝藏歌单库（type 5） | 单封面 + 左下角播放数角标 + 描述 |
| `TrackColumns` | 偶遇心动单曲 / 心动收藏相似推荐（type 3/10） | 一列几首曲目的白卡片，列与列横向排开 |

**拼图是没办法的办法**：type 4/11 的分组只给一个名字和 3 首预览，
`HomeSection.CoverImage` 一直是 `null` —— 接口**根本没给封面**。
所以卡片顶部的图只能从那几首的专辑封面里取。

单曲行上画**付费标识**（复用 `Formats.PayLabel`，显示 VIP / 付费），
歌单卡片里那几行预览不画。

## 箭头：悬停才出现

鼠标进到这一组才显示箭头，移开就藏；只有一页、或者已经到头的那个方向不显示。

**踩到的坑**：承载整个卡片区的那个 `Grid` 一开始没设 `Background`，
而**没有背景的元素不参与命中测试** —— `PointerEntered` 根本不触发，箭头永远不出现。
必须写成 `Background="Transparent"`。它只填「没有子元素覆盖」的区域（首尾留白、卡片缝），
列表与卡片仍在它之上，点击照旧落到卡片上。

箭头与列表在同一个 `Grid` 单元格里，按 XAML 顺序叠在列表之上，命中测试优先按钮；
列表左右各留 `SpaceHomePagerMargin`（= 按钮宽 + 两侧的缝），
所以箭头**不压住首尾那张卡片**。也不做「半浮出界」——
控件的 x=0 就是外层列表的视口左边界，再往左会被裁掉。

## 算式与 token

`Controls/CardStripPaging.cs` 不引用任何 WinUI 类型，所以能链进测试项目离屏验
（与 `PagedList` 同一种做法）。宽度为 0、宽度不足一步、换宽度时的锚点、末页夹取全在这里，
用例见 `tests/Bodian.Core.Tests/CardStripPagingTests.cs`。

**步长按 Layout 取**：四种排法的卡片宽度各不相同（`Themes/Tokens.xaml` 里 `SizeHome*` 那一族），
再各自加上 `SpaceHomeCardGap`。**算「一页放得下几张」时还要先减掉左右两段 `SpaceHomePagerMargin`**，
不然最后一页会多算一张、探进按钮那一段留白里。

每页**多装一项**（还有下一页时）：最右那一项被视口裁掉一截，露出下一张的一角 ——
一眼看得出「右边还有」，比干干净净地截断更像一个可以翻页的列表。

## 容器回收时状态必须复位

`HomeSectionView` 活在发现页外层列表的虚拟化容器里，实例会被复用给别的 `Section`。
所以 `OnSectionChanged` 里要完整复位页号与页大小 —— 不复位的话，控件不可见时宽度一直是 0，
下次量到宽度会用上一组的页号当锚点。

滚走再滚回来会回到第一页，与列表自身滚动位置的丢失同性质，可以接受。

宽度变化（拉宽拉窄、侧栏展开收起）时，按「换宽度前当前页的第一项」的下标换算新页号。
另外在 `SizeChanged` 里回填集合会引发重排 → 再触发 `SizeChanged` 的连环，
所以宽度与页大小都没变时直接早退（`ArtistDetailPage` 里踩过同样的坑）。

## 视觉里踩过的四个坑

1. **拼图不能用 `Grid` 的 `*` 列**。在 `Border` 里那两列分不出尺寸，右边两张会被挤成看不见的一条。
   改成三块**各自固定尺寸 + 左/右上/右下对齐**摆放就没有这个问题。
2. **`VerticalAlignment="Stretch"` 会把卡片拉爆**。type 11 的封面本来想「跟着右边文字的高度走」，
   但这张卡片的高度**恰恰由内容决定** —— 解出来是一条上千像素的窄长条，整张卡跟着一起长。
   改成固定尺寸 + 顶部对齐。
3. **卡片底色必须是实色**。页面背景是一层氛围渐变，系统的
   `CardBackgroundFillColorDefaultBrush`（70% 白）会被透掉，卡片看起来根本不存在。
   用自定义的 `HomeCardBackgroundBrush`（三套主题齐全）。
4. **横向 `StackPanel` 不给宽度约束**。里面放「歌名 / 歌手」这种并排文本时不会省略号截断，
   会直接溢出盖住旁边那张卡片。要并排就用 `Grid` 分列，或者干脆上下两行。

## 悬停底：卡片自己画

悬停底是**卡片自己画**的：模板根的第一个子元素是一层 `Opacity=0` 的 `Border`，
指针进出时切它的不透明度（`HomeSectionView.xaml.cs` 的 `OnHoverPointerEntered/Exited`）。
它带一圈负 Margin（`SpaceHomeCardHoverMargin`，-16），四边都溢出卡片 ——
底色于是比卡片四周各大 16，封面与下面的文字都在里面。

**为什么不借容器那层底色**（歌手页的专辑卡是那样做的）：横向的 `ItemsStackPanel` 里，
容器的高度跟着内容走、横向宽度却不跟 —— 试过把卡片让出的空白写在容器 `Padding` 上、
也写在卡片自己的 `Margin` 上，两次都是**只有纵向变高、横向纹丝不动**。
自己画一层，尺寸才是说了算的。

> 卡片让出的空白（`SpaceHomeCardMargin`，12）留着：它决定卡片之间的间隔
> （12 + 容器间隔 4 + 12 = 28），也是悬停底溢出的余量 —— 16 < 28，两张卡的底不会碰到一起。
> 它也进了分页算式：「一步」= 卡片宽 + 2 × 空白 + 容器间隔（见 `HomeSectionView.Step`）。
> 卡片左边离列表边缘多了 12，发现页的模块标题跟着从 28 挪到 40 对齐。

容器是圆的（`CornerRadius` = `RadiusMd`），单曲行那一列的悬停还是行自己画的
（底色用侧栏 Tab 那个键 `NavigationViewItemBackgroundPointerOver`，与卡片同一档）。

## 刷新：两个单曲推荐模块（2026-10-04）

**偶遇心动单曲**（type 3）与**心动收藏相似推荐**（type 10）的标题右侧各加了一颗刷新按钮，
点了就地重取这个模块。歌单类的三个模块（4 / 5 / 11）**不加**。

### 刷新就是重调同一个接口

`service/home/module?moduleId=N` **没有「换一批」参数，也没有独立的刷新接口** ——
重调一次就是刷新。逆向产物里这个请求的 `data` 只有一个 `moduleId` 键：

```
disc_block_base.dart:1370   "moduleId"
               :1400   "service/home/module"
               :1407   const [0, 0x4, 0x4, 0x2, data, 0x2, encrypt, 0x3, null]
```

同族的 `service/finds/module` 形状完全一致（`discovery_default_body_view.dart:3615-3648`、
`discovery_artist_widget.dart:535-568`）。全仓 grep 也没有 `refresh` / `seed` / `cache` / `ts`
这类业务参数。路径相对 `reverse/android-5.2.5/blutter/asm/allin/discovery/`。

所以「刷新」在客户端这一侧等价于把那一批 `Rows` 换掉，**不换接口**。

### 按钮挂在哪

`ListSectionHeader` 多了一个可空的 `RefreshCommand` 与一个 `RefreshVisibility` ——
前者为 `null` 的标题不显示按钮。命令只有 type 3 / 10 会挂。

模块与它当前的内容**不存在标题对象上**，而是放在 `DiscoverViewModel` 的
`Dictionary<ListSectionHeader, LoadedModule> _loaded` 里。原因是 XAML 的类型信息生成器
会为数据类型的每个公开属性生成 `new 该类型()`，而 `HomeModule` 是只有位置构造函数的 record，
公开它整个项目编不过（与 `TrackRow.Source` 同一处坑）。

`ReplaceModule` 按 `header` 在 `Rows` 里的位置删掉它后面连续的 `HomeSection`，
再插回新的一组；`header` 对象本身不换 —— 命令挂在它身上。刷新失败只写状态文案，
**不清空原有卡片**：手上有数据，一次网络抖动不该把它清掉。

### 闪退：换组不能带着上一组的项

加了刷新之后，section 第一次会**换实例**，于是踩到 `HomeSectionView` 里一个潜伏已久的 bug。

`OnSectionChanged` 原来的顺序是「先换模板、再填页」：

```csharp
view._items = view.BuildItems();
view.ApplyItemTemplate();          // 模板换成按新排法编译的那种
view.SyncPage(resetToFirst: true); // ← 可能在这里早退
```

而 `SyncPage` 开头是 `if (width <= 0) return;`（`HomeSectionView.xaml.cs:118-119`）——
容器刚从回收池取出来、`StripHost` 还没量到宽度时就早退，**不填页**。
于是 ItemsSource 里留着上一组的项，模板却已经是新排法的：

- 一个先前展示单曲列（`_items` 是 `HomeTrackColumn`）的容器被回收给别的排法的模块
- 模板换成按 `HomeCard` 编译的那种，ItemsSource 里还是 `HomeTrackColumn`
- `x:Bind` 的 `SetDataRoot` 强转 → `ArgumentException` → 进程崩

日志里的原话：

```
The source object type ('Bodian.WinUI.ViewModels.HomeTrackColumn')
being cast to type 'Bodian.Core.Models.Home.HomeCard' is not a projected type
   at HomeSectionView.HomeSectionView_obj19_Bindings.SetDataRoot(...)
```

进程表现为 `STATUS_STOWED_EXCEPTION (0xC000027B)`、故障模块 `Microsoft.UI.Xaml.dll`；
不打日志就只能看到一句「模块 combase.dll、异常码 E_INVALIDARG」，够不着原因。

**此前不发作**：section 从不换实例，`OnSectionChanged` 只在「null → 第一组」时跑过，
早退时 ItemsSource 本来就是空的，空列表没东西可渲染。

**修法**：`ApplyItemTemplate()` **之前**先 `view._pageItems.Clear()`。早退仍然允许
（等 `SizeChanged` 再填），但不能带着上一组的项早退。宽度已量到时清空后会被
`ApplyPage` 在同一次回调里立刻填满，不会闪。

## 手动验收

| 项 | 验证重点 |
| --- | --- |
| 滚轮停在卡片区 | **第一验收项**：页面必须滚；不能出现「指针一进卡片区页面就不动」 |
| 触摸板横滑/竖滑 | 卡片区纹丝不动，页面照滚 |
| 鼠标进入卡片区 | 箭头出现；移到箭头上箭头不消失、点得到；移出这一组才消失 |
| 翻页箭头 | 首页只剩右箭头、末页只剩左箭头；个性化歌单只有一页、两颗都不该出现 |
| 个性化歌单点整卡 | 打开对应歌单（0/1/2/3 各点一次，标题要与卡片上的名字对得上） |
| 你的主题歌单点整卡 | 同上，且**不能**打开个性化歌单的同号歌单（index 串台就是这个症状） |
| 卡片悬停 | 划过**整张卡**（含封面与下面的文字）时，底色四边都往外一大圈（各 16），相邻两张卡的底不碰到一起 |
| 单曲那一列 | 点哪一**行**放哪一首；鼠标划过有底色；付费曲目有 VIP/付费标识且**紧贴歌名** |
| 宝藏歌单库 | 点进歌单详情，曲目列表非空（source 填错的表现是空歌单）；返回键可点，退回发现页，且侧栏仍高亮「发现」（用了 `NavigateRoot` 的表现是返回键变灰） |
| 长名字 | 歌名/歌手/歌单名一律省略号截断，不撑破卡片 |
| 窗口拉宽拉窄 / 侧栏收起 | 每页张数跟着变，原来在眼前的那张卡还在，不来回抖 |
| 主题三态 | 浅/深/高对比度下箭头与卡片底色都读得出来 |
| 键盘 | Tab 能聚焦箭头、Enter 能翻页，焦点框可见 |
| 刷新按钮的范围 | **只有**「偶遇心动单曲」「心动收藏相似推荐」两个标题右侧有；其余模块没有 |
| 点刷新 | 该模块卡片区就地换一批；页面不重排、滚动位置不跳、标题不变 |
| 连点刷新 | 刷新期间按钮是灰的、点不动，完成后恢复 |
| 刷新失败 | 断网后点刷新 → **原有卡片不被清空**，只多一条状态文案 |
| 刷新后滚动 | **第一验收项**：刷新完再上下滚几屏，不闪退（`HomeSectionView` 那个强转崩溃的复现路径） |
