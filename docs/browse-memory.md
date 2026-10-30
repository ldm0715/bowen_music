# 浏览时内存持续增长的排查（2026-10-30）

用户报告：不进歌词页，只是找歌、在专辑 / 歌单 / 歌手页之间来回跳、偶尔看排行榜，内存也会涨到 350 MB 以上，
而且看上去越用越高。此前试过复用 XAML 树、及时释放列表元素、给返回栈设上限，都没有效果。

本文记录这一轮的测量方法、数据、已排除的方向和仍未解决的部分，供以后继续排查时参考。
早期的浏览内存记录见 [`performance.md`](performance.md) 的「发现、乐库与新歌单浏览验证」一节。

## 结论

1. **托管层没有泄漏。** 离开的页面全部被回收，托管堆始终在 7–17 MiB。
   复用 XAML 树、限制返回栈、强制 GC 这几条路都不会有效果，测量已经直接证实。
2. **增长在原生层**，主要是 WinUI 合成引擎（`dwmcorei.dll`）和 XAML 框架（`Microsoft.UI.Xaml.dll`）。
   这些分配在框架自己的线程上进行，调用栈里没有应用代码，应用侧无法直接释放。
3. **大多数路径会封顶。** 只在详情页之间来回跳（根页不变）时，提交内存约 8 轮后停在 270–290 MB。
   用户实测静止播放时稳定在 350 MB 左右，涨上去也会回落。
4. **轮流点侧栏的路径仍在缓慢上涨。** 每轮 7–22 MB，增量逐轮变小，5 轮内没有到顶。
   最可疑的原因是根页缓存太小，见[下文](#未验证的假设根页缓存)，但还没有验证。

## 指标口径

与 `performance.md`「歌词页与拖动内存」一节相同，三种数值不能混用：

- **私有工作集**：真正驻留的私有页。系统可用内存不足时 Windows 会把页面换出，数值会被拉低，这时不可信。
- **提交**（`PrivateMemorySize64`）：进程提交的私有虚拟内存。本轮主要看它，受换页影响小。
- **托管堆**（`GC.GetTotalMemory`）：只反映 C# 对象。

## 诊断工具

都只在设置环境变量时生效，正常启动没有开销。

### 页面存活诊断

`src/Bowen.WinUI/Services/PageLifetimeDiagnostics.cs`，开关是 `BODIAN_MEMORY_DIAGNOSTICS=1`。

- `NavigationService.ShowCore` 用弱引用登记每个显示过的页面。
- `Report` 输出一行日志：按类型统计的存活页面数、累计已回收数、托管堆、各代 GC 次数、提交与工作集。
- `CollectAsync` 间隔 500 ms 做三次完整回收。只用于判断，不在正式路径上调用。
  CsWinRT 的包装对象要隔开几次 GC 才能收干净，见 microsoft-ui-xaml #6894 里的分析。

### 自动浏览场景

`src/Bowen.WinUI/Services/PerformanceScenarios.cs`。都需要 `BODIAN_LYRICS_DIAGNOSTICS=1`，
并通过 `BODIAN_UI_PERF_SCENARIO` 选择场景。

**`browse-loop`**：以排行榜为根，每轮打开专辑、歌手、歌单再逐层返回，然后进一个榜单详情再返回，
每轮用不同的条目。

| 变量 | 作用 |
| --- | --- |
| `BODIAN_UI_PERF_ROUNDS` | 轮数，默认 20 |
| `BODIAN_UI_PERF_KEYWORD` | 取素材的搜索词，默认「周杰伦」 |
| `BODIAN_NAV_DROP_HISTORY=1` | 详情页一律换根打开，返回栈始终为空 |
| `BODIAN_MEMORY_COLLECT_EACH_ROUND=1` | 每轮结束做一次诊断回收 |
| `BODIAN_UI_PERF_EXTENDED=1` | 多取几页素材；列表滚到底触发续页；歌手页三个 Tab 都切；第一轮静音开播 |

**`daily-loop`**：按用户描述的日常路线，每轮依次走：我喜欢的（第一轮静音开播）→ 歌词页并开关播放列表
→ 排行榜每个榜 → 发现页的歌单卡片 → 收藏的每张专辑及其歌手 → 关注的每位歌手。
每进一个详情页停留 2.5 秒、滚到底再返回，每类最多 20 个。

| 变量 | 作用 |
| --- | --- |
| `BODIAN_UI_PERF_ROUNDS` | 轮数，默认 5 |
| `BODIAN_DAILY_PARTS` | 只跑列出的环节，逗号分隔：`favorites,lyrics,bangs,discover,albums,artists` |
| `BODIAN_DAILY_PLAY=0` | 不播放 |
| `BODIAN_DAILY_LYRICS_REPEAT` | 每轮进出歌词页的次数，默认 1 |

两个场景都只读，不收藏、不改歌单。开播时会把播放器音量设为 0，**这个值会被保存下来**，
测完要把 `%LOCALAPPDATA%\Bowen\volume.json` 恢复原值。播放会替换当前账号的队列，并写入最近播放。

### 运行方式

```powershell
$env:BODIAN_LYRICS_DIAGNOSTICS = '1'
$env:BODIAN_MEMORY_DIAGNOSTICS = '1'
$env:BODIAN_UI_PERF_SCENARIO = 'daily-loop'
$env:BODIAN_UI_PERF_ROUNDS = '4'
& './src/Bowen.WinUI/bin/Release/net10.0-windows10.0.26100.0/win-x64/BowenMusic.exe'
```

结果在 `%LOCALAPPDATA%\Bowen\logs\bowen-*.log` 里，按 `[PID n]` 和「页面存活」过滤。
私有工作集要在外部用 `tools/Measure-PlayerResources.ps1` 采样。

注意事项：

- 应用是单实例，已有实例在跑时测试实例会直接退出。启动前确认没有遗留进程，测完结束测试进程。
- 系统可用内存低于 3 GB 时不要测：换页会拉低工作集，Claude Code 的后台任务也可能被系统中止。
- 测试结束后在正常终端（不带这些变量）里运行应用。
## 测量数据

本机 Release 构建，Windows 10 22H2，Ryzen 5 4600H，GTX 1650 Ti，125% DPI。数值单位为 MB。

### 第一步：页面有没有被回收（`browse-loop`，20 轮，80 个详情页）

| 组 | 存活页面（第 20 轮） | 已回收 | 托管堆 | 提交：初始 → 第 10 轮 → 第 20 轮 |
| --- | --- | ---: | ---: | --- |
| A 默认 | 3 | 79 | 12.7 | 199 → 282 → 275 |
| B 丢弃返回栈 | 3 | 79 | 13.1 | 199 → 277 → 272 |
| C 每轮强制回收 | 2 | 80 | 8.8 | 199 → 268 → 274 |

- 存活的始终是根页加当前页，离开的详情页都被回收了，**排除「页面被引用住」**。
- 5 分钟内跑了 170 多次 Gen2 GC，C 组每轮强制回收，曲线和 B 组几乎一样，**排除「GC 没跑」**。
- A 组和 B 组没有差别，**返回栈上限没有作用**，与用户之前的实验结果一致。
- 私有工作集前 10 轮从 149 涨到约 195，之后持平。

### 第二步：原生内存落在哪里（WPR heap trace，10 轮）

`wpr -start Heap -start VirtualAllocation -filemode`，并对 `BowenMusic.exe` 开启 `HeapTracingConfig`。
需要管理员权限，10 轮的 etl 约 10 GB。**这次 trace 丢了 567 万个事件**，「未释放」的数值偏高，
只能用来比较来源之间的大小，不能当绝对值。

第 1 轮到第 10 轮之间分配、到结束仍未释放的原生堆：

| 来源 | 未释放 | 说明 |
| --- | ---: | --- |
| `dwmcorei.dll` 合成资源 | 约 40 MB | visual、shape、动画，16 万个对象 |
| `Microsoft.UI.Xaml.dll` | 约 24 MB | 框架内部，没有公开符号 |
| `d3d10warp.dll` | 约 22 MB | WinUI 自己用 WARP 做的软件光栅化任务，不是应用的设备 |
| `DWrite.dll` 字形分析 | 约 6 MB | 文字排版缓存 |
| 应用自己的控件 | 约 10 MB | `MarqueeText` 约 6.5 MB / 2.9 万次分配，其次是 `TrackLink`、`Icon` |

关于 WARP：trace 里所有 `D3D11CreateDevice` 都来自 `Microsoft.UI.Xaml.dll` 和 `dwmcorei.dll`，
走的是 NVIDIA 驱动，没有一处来自应用代码。WARP 分配是框架内部行为，**这条线索已排除**。

### 第三步：补上日常操作

`browse-loop` 加 `BODIAN_UI_PERF_EXTENDED=1`（静音播放、列表滚到底、歌手页切三个 Tab），30 轮里跑到第 23 轮：
提交在第 8 轮左右到顶（288–290），之后在 271–285 之间波动，**仍然封顶**。

`daily-loop` 全路线，5 轮，每轮约 67 个详情页，开着播放：

| 轮次 | 提交 | 每轮增量 |
| --- | ---: | ---: |
| 初始 | 173 | |
| 1 | 333 | +160 |
| 2 | 350 | +17 |
| 3 | 363 | +14 |
| 4 | 375 | +12 |
| 5 | 382 | +7 |
| 诊断回收后 | 374 | |

每轮存活的只有 6 个根页。**这条路线持续上涨**，但增量在变小。

### 第四步：逐项排除

| 变体 | 内容 | 提交 | 判断 |
| --- | --- | --- | --- |
| G1 | 只进出歌词页并开关播放列表，每轮 5 次，共 8 轮 | 294 → 307 → 314 → 300 → 314，离开 10 秒后 265 | 封顶，不是它 |
| G2 | 浏览全部环节，开着播放，不进歌词页 | 304 → 321 → 329 → 340 | 持续上涨 |
| G3 | 同 G2，但不播放 | 283 → 305 → 310 → 320 | 持续上涨 |

- 歌词页第一次打开约多 120 MB（GPU 基线，见 `performance.md`），之后进出多少次都不再增加。
- G2 与 G3 涨幅相近，**播放不是原因**。
- 这几组测试时系统可用内存只有 1–2 GB，私有工作集被换页拉低，只看提交。

## 未验证的假设：根页缓存

`browse-loop` 封顶而 `daily-loop` 上涨，两者的关键区别是：

- `browse-loop` 的根页始终是排行榜。
- `daily-loop` 每轮在 5 个根页之间轮换，而 `NavigationService` 的无参根页缓存原先只保留 3 个，
  所以每轮都有排行榜、发现这类最重的页面被丢弃再重建。

诊断回收后存活的页面正好是 3 个，与缓存上限一致。每次重建都会在原生层留下一截，托管侧没有对象存活。

本轮把上限改成了 `MaxCachedRootPages = 8`（侧栏无参根页的总数），**但还没跑验证**。
验证方法：用 G3 的配置（`BODIAN_DAILY_PARTS=favorites,bangs,discover,albums,artists`、
`BODIAN_DAILY_PLAY=0`、4 轮）再跑一次，第 2 到第 4 轮的提交增量合计小于 5 MB 算有效。
如果仍然上涨，说明根页重建不是全部原因。

可以作为辅助的对照：只在 5 个根页之间来回切 30 轮，不进详情页，看提交是否随轮数上涨。

## 本轮的代码改动

| 改动 | 文件 | 状态 |
| --- | --- | --- |
| `MarqueeText` 只在第一次真正滚动时才打开 Translation、取合成视觉；裁剪几何复用一个对象 | `Controls/MarqueeText.xaml.cs` | 构建通过，未单独量化 |
| `TrackLink` 的歌手格、专辑格改为 `x:Load="False"`，只创建用到的一格，每个实例少 2 个 `MarqueeText` | `Controls/TrackLink.xaml(.cs)` | 构建通过，未单独量化 |
| 根页缓存上限 3 → 8 | `Services/NavigationService.cs` | 未验证 |
| 页面存活诊断与两个浏览场景 | `Services/PageLifetimeDiagnostics.cs`、`Services/PerformanceScenarios.cs` | 仅诊断用 |

根页缓存在窗口最小化、隐藏到托盘（`MainWindow.ReleaseBackgroundCaches`）和换账号时会清空，
所以上限调大后常驻的最多是 8 个根页，不会无限增长。

## 已排除的方向

- 复用详情页的 XAML 树、及时释放列表元素：页面本来就能回收，增长不在这里。
  旧的详情页复用还造成过歌曲列表为空的回归，见 `performance.md`。
- 给返回栈设上限，或超出直接丢弃：B 组证明无效。
- 在页面丢弃后补一次 GC：C 组证明无效。
- 应用误建的 WARP 设备：WARP 来自框架内部。
- 字形缓存随内容多样性增长：`daily-loop` 每轮内容基本相同仍在上涨，不是主因。
- 歌词页、播放列表、播放本身：G1、G2、G3 排除。

## 以后可以继续的方向

1. 先验证根页缓存的假设，见上文。
2. 用 Heap Snapshot 对比代替事件流：在第 3 轮和第 10 轮各拍一张快照，直接比较两张快照之间新增的存活分配。
   快照不依赖事件流，不会丢事件，文件也小得多：
   `wpr -snapshotconfig Heap -pid <pid> enable` 后用 `wpr -singlesnapshot Heap <pid>`。
3. 如果确认框架随页面泄漏原生资源，就只能少建页面，也就是复用详情页。
   上次复用失败是因为 `x:Bind` 是 OneTime，换 ViewModel 后列表不刷新。
   可以把关键绑定改成 OneWay，或者换 ViewModel 后调用 `Bindings.Update()`，先用 `daily-loop` 验证功能和内存再决定。
4. 跟进 microsoft-ui-xaml 的修复：#7282（x:Bind 泄漏）已于 2026-09 关闭并合入 #11734，
   升级 Windows App SDK 后值得复测一次。

## 参考

- microsoft-ui-xaml #6894：DataTemplate 事件处理器导致的泄漏（WinAppSDK 1.1 已修），以及多次 GC 才能收干净的分析
- microsoft-ui-xaml #7282：x:Bind 在 Window 上的泄漏，2026-09-04 由 #11734 关闭
- microsoft-ui-xaml #9044：WinUI Gallery 反复导航内存增长，Closed as not planned
