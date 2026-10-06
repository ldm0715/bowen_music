# 系统托盘

托盘菜单、关闭到托盘、单实例。2026-10-06。

相关：[`tech-stack.md`](tech-stack.md) §2（包清单）与 §6（单实例）、[`icons.md`](icons.md)（图标来源纪律）、[`mini-player.md`](mini-player.md)（同为「独立窗口 + 开关」那一族）。

---

## 1. 为什么

官方桌面端缺下载 / 评论 / 收藏歌单，所以自研了这个客户端。托盘的诉求是最小化之后**应用继续跑、播放不中断**，并且不打开主窗口也能控制播放。

功能本身没有新东西 —— 转台、模式切换、小窗、桌面歌词全都是现成的 ViewModel 命令。工作量的全部在**托盘怎么接进来**：本应用是 unpackaged（无 MSIX 身份），托盘只能用 Win32 `Shell_NotifyIcon`，而仓库里此前一行托盘代码都没有。

## 2. 选型

`H.NotifyIcon.WinUI` **2.4.1**（MIT，与 GPL-3.0 兼容）。

| | |
| --- | --- |
| 目标框架 | `net10.0-windows10.0.17763`（本项目 26100，可消费） |
| 依赖 | `H.NotifyIcon 2.4.1`、`Microsoft.WindowsAppSDK >= 1.6.250108002`（本项目 2.5.1，向上统一） |
| 实际进输出目录 | `H.NotifyIcon.WinUI.dll` 176 KB + `H.NotifyIcon.dll` 399 KB |
| 传递依赖 | `H.GeneratedIcons.System.Drawing`、`System.Drawing.Common 10.0.0`（图标生成链带进来的，Windows 上可用） |

**为什么不是 `WinUIEx`**：`tech-stack.md` 原先登记它做「窗口 / 单实例 / 托盘」，但它没有托盘控件，`WindowManager` 也管不到单实例。该登记仍在（留给 P8 的窗口辅助），但托盘与单实例都改由本文这两个办法做。

**为什么不用手写 `Shell_NotifyIcon`**：截图上那颗菜单是 WinUI 风格（深色圆角、每项带矢量图标）。手写的话菜单是系统原生外观，图标还得把 Fluent 路径栅格化成 `HICON` 才画得出来 —— 而那套图标体系正是本项目花过力气统一成矢量路径的（见 `icons.md`）。用库可以直接复用现成资源。

## 3. 托盘控件放在哪（这是这个库最大的坑）

`TaskbarIcon` 是 `FrameworkElement`，必须有**已加载的 XAML 树**和带 `DispatcherQueue` 的 UI 线程。三种放法：

| 放法 | 判定 |
| --- | --- |
| `App.xaml` 资源字典 | ❌ **绝对不行**。资源在任何窗口 / DispatcherQueue 存在之前就被解析 —— 这是该库那批 `Failed to assign to property` 与 COMException 报告的共同来源 |
| 独立隐藏窗口（官方 `Windowless` 样例的形态） | ❌ 不需要。那是给**没有主窗口**的应用用的；本应用主窗口因「关闭到托盘」永不销毁，多一个 XAML 岛只换来不确定性 |
| **`MainWindow.xaml` 根 `Grid` 内** | ✅ 选它。菜单要 `x:Bind` 到 `Player` / `MiniPlayer` / `DesktopLyrics`，而 `MainWindow` 本就持有这几个单例；控件寿命 == 进程寿命；不需要任何新机制 |

控件没有视觉，放进根 `Grid` 时跨三行且 `IsHitTestVisible="False"` —— 后者是必须的，否则这个横跨三行的元素会挡住整个窗口的命中测试。

**真退出时必须在 `MainWindow` 自己的 `Closed` 处理器里 `Tray.Dispose()`**，否则通知区域会留幽灵图标（悬停才消失）。那个 lambda 注册在 App 的 `Closed` lambda 之前，所以顺序天然正确。

### 为什么不为它加一个 Host 服务

`MiniPlayerWindowHost` / `DesktopLyricsWindowHost` 存在的理由是「ViewModel 不能 new 窗口，而窗口必须懒创建」。托盘图标既不由 VM 开关驱动、也不创建窗口 —— 硬套那一层反而要把 XAML 创建的控件反向注入服务。**唯一需要服务化的是单实例**（它在窗口存在之前就要工作，且要在窗口句柄上装子类）。

## 4. 关闭到托盘

### 4.1 用 `AppWindow.Closing`，不是 `Window.Closed`

| | `Window.Closed` | `AppWindow.Closing` |
| --- | --- | --- |
| 时机 | 窗口**已销毁之后** | 即将关闭之前 |
| 能否取消 | 不能 | 能（`args.Cancel = true`） |
| 现有用途 | App 的整条清理串 + MainWindow 自己的收尾 | 托盘新增 |

**取消 `AppWindow.Closing` 之后 `Window.Closed` 根本不触发**，所以 App 在 `OnLaunched` 里挂的那条清理串（`TearDownForShutdown` → 两个 WindowHost → SMTC → 播放引擎 → `_host.Dispose()`）自然被跳过 —— 这正是「关到托盘还在放歌」要的，**`App.xaml.cs` 因此一行都没改**。真退出时放行，那条链原样跑完。

隐藏用 `AppWindow.Hide()`，**不是 `Minimize`** —— 后者会在任务栏留一个按钮，那就不是「到托盘」了。

> **不要改成 `AppWindow.Destroy()`**：它绕过 `Closing`，会让关闭到托盘静默失效（表现为「点 ✕ 进程就没了」，而代码看起来完全正常）。

### 4.2 注销 / 关机必须放行

注销走 `WM_QUERYENDSESSION`、**不是 `WM_CLOSE`**，而 `Closing` 只挂在后一条路径上。拿不到这个信号就分不清「用户点了 ✕」和「系统在关机」；万一 WinUI 在关机路上也会关窗口，就会变成「这个应用阻止了关机」。

所以 `MainWindow` 在自己的 HWND 上另装了一个子类（id 4）只为观察 `WM_QUERYENDSESSION` 并置一个位。判定本身抽成了纯函数 `Services/TrayCloseDecision.cs` —— 它是托盘这一套里**唯一能被离线单测覆盖**的部分。

## 5. 菜单

### 5.1 已用元数据核实的事实

下面几条是**查 2.4.1 的实际二进制与 XML 文档**得到的，不是照抄 README（README 在这几处与实现有出入）：

1. **事件在编译期被关掉了。** 文档原文：WinRT 事件默认禁用，「Use the `WinRTEvents = true` option to enable them」。实测二进制里**连 `add_TrayContextMenuOpen` 访问器都不存在**（而 `add_TaskbarCreated`、`add_Click` 都在，说明检索有效），只剩一个 `OnTrayContextMenuOpen` 方法名。
   → **所以拿不到「菜单即将弹出」的钩子。** 状态刷新只能靠 `x:Bind OneWay`：菜单项属性随时是最新的，库在弹出那一刻读到的就是新值。副作用是好事 —— 「主界面改了模式，托盘勾选跟着变」不需要任何接线。
2. **菜单项只认 `Command`，不认 `Click`。** 默认模式把 `MenuFlyout` 转成 Win32 菜单，点击转发挂在项上的命令。所以「退出」「显示主界面」也必须是命令。
3. **`ContextMenuMode` 的三个成员是 `ActiveWindow` / `PopupMenu` / `SecondWindow`**（没有 "Default"），**默认 `PopupMenu`**。
4. **`MenuActivation` 默认 `RightClick`**，不用显式设。
5. **`OnTaskbarCreated` 存在**，文档写明「如果整个任务栏被重建（例如 Explorer 被关闭），则重建托盘图标」—— explorer 重启后图标丢失这条风险由库自己兜住了。
6. `GeneratedIconSource` 的属性是 `Text` / `FontFamily` / `FontSize` / `Foreground` / `Size` / `Background*` / `Border*` / `CornerRadius` / `TextMargin` / `Margin`。

### 5.2 `PathIcon` 的坑

`MenuFlyoutItem.Icon` 收的是 `IconElement`，**塞不进 `Controls/Icon`**（那是 `UserControl`），只能用 `PathIcon`。而 `PathIcon.Data` 收的是 `Geometry` 实例，**不能写 `{StaticResource IconXxx}`**（那是路径**文本**，写上去会在启动时抛 `XamlParseException`、应用根本打不开 —— 见 `icons.md` 与 `Controls/IconGeometry.cs`）。

兜法是 `Formats.IconGeometryFor(key)`：它经 `IconGeometry.From` 把文本转成**归属于该用点的** `Geometry` 实例，与 `Controls/Icon` 内部走的是同一条路。

### 5.3 菜单内容与状态同步

顶部显示 `Player.NowPlayingText`；上一首、播放 / 暂停、下一首三个按钮并排放在一个菜单项里。播放 / 暂停始终是一颗按钮，通过 `Formats.PlayPauseIcon(Player.IsPlaying)` 切换图标。

`Controls/TrayMenuItem` 派生自 `MenuFlyoutItem`，只增加一个 `Body` 属性，用自定义模板中的 `ContentPresenter` 显示任意内容。框架原生模板只显示 `Text`，不能直接承载三个并排按钮。

模式子菜单同样使用 `TrayMenuItem`，选中背景直接绑定 `Player.IsSequentialMode` / `IsListLoopMode` / `IsShuffleMode`；入口标题绑定 `Player.PlayModeText`。小窗和桌面歌词的选中背景绑定各自的 `IsEnabled`。所有状态都通过 `x:Bind OneWay` 实时同步。

菜单直接定义在 `TaskbarIcon.ContextFlyout` 中。不要把包含这些 `x:Bind` 的菜单项移到懒加载资源字典：绑定初始化时对象尚未创建，会产生空引用并影响主窗口启动。

### 5.4 `CommandParameter` 必须传枚举值

`CommunityToolkit.Mvvm 8.4.2` 的 `RelayCommand<T>` 不做 string→enum 转换，写 `CommandParameter="Sequential"` 会在运行时抛 `ArgumentException`。所以传 `{x:Bind models:PlayMode.Sequential}`。

`PlayerViewModel.SetPlayMode` 直接让队列切换模式；不额外赋值 `Mode`，它仍由队列变化驱动。

### 5.5 渲染模式

当前使用库的 `ContextMenuMode="SecondWindow"`，由库提供独立透明窗口，显示现有 WinUI `MenuFlyout`。菜单窗口生命周期由库管理，应用保留现有托盘接入方式。

早期 `PopupMenu` 模式将标准菜单项转换为 Win32 原生菜单，功能验收记录见 §5.8。当前界面已经使用 `TrayMenuItem.Body` 和并排按钮，不能只改 `ContextMenuMode` 就把这些内容等价转换成原生菜单；若恢复原生模式，需要同时恢复标准菜单项的文本与命令定义。

库会在准备窗口和测量菜单时强制设置顶层项的 `Height = 32`、`Padding = 11,0,11,0`。两份托盘模板自行设置内部留白，避免再次读取被库改写的 `Padding`。

### 5.6 宽度与对齐

- 主菜单项的宽度固定为 `TrayMenuWidth = 160` DIP。外距、弹层边框另计；宽度不由歌名撑大。
- 标题使用受限 `Grid` 的 `*` 列和 `TextTrimming="CharacterEllipsis"`，超长时显示省略号。
- 播放模式入口与其他带图标的行使用相同左边缘：外距 3 + 内距 8 + 图标列 16 + 文字间距 8 = 35 DIP。
- `MenuFlyoutSubItem` 虽然是 sealed，仍可替换 `ControlTemplate`。入口模板保留展开箭头和悬停状态，不沿用原生模板的占位边距和 `NarrowPadding` 动画。
- 展开的三颗模式项使用 `Width="Auto"`。`TrayMenuItem.Loaded` 在实际承载项的 `MenuFlyoutPresenter` 上设置 `MinWidth = 0`，解除默认最小宽度；不要复用主菜单的固定宽度。

### 5.7 点击菜单内保持打开

交互规则：菜单内可以连续操作，点击菜单外才收起；「退出」仍真正结束应用。

共享项模板的根 `Grid` 处理 `PointerPressed` 与 `Tapped`。先阻止 `MenuFlyoutItem` 进入执行后自动关闭的默认指针路径，再拦住 `Tapped` 向托盘库整行点击处理器冒泡；应用检查 `CanExecute` 后执行原项的 `Command` 和 `CommandParameter`。

三个播放按钮仍使用原来的 `Click`，只执行播放命令，不再调用 `Hide`。分隔线设为不可命中，点击留白不会触发库的整行关闭处理。

小窗首次显示会激活新窗口。执行菜单命令前记录托盘窗口；如果命令导致前台窗口变化，同步将托盘窗口带回，继续显示原位置的菜单。「退出」或主窗口已关闭时不恢复窗口。

### 5.8 验收记录

- **2026-10-06，早期 `PopupMenu` 模式，用户实测功能通过**：托盘图标、提示文本、唤起主窗口、播放控制、模式切换、窗口开关、关闭到托盘、真正退出和单实例唤醒。
- **2026-10-06，最终 `SecondWindow` 模式，用户确认菜单交互通过**：播放控制、模式和窗口开关等菜单内操作保持打开，支持连续点击，点击菜单外收起。最终采用固定主菜单宽度、长标题省略号和紧凑子菜单。
- 客户端已从备份旧 `obj` 和输出目录后的完整构建启动；实际加载系统 `.NET 10.0.12`，主窗口响应，本次启动日志没有致命异常。
- `dotnet test --project tests/Bodian.Core.Tests/Bodian.Core.Tests.csproj -c Debug`：**1254 项通过，0 失败**，包含关闭到托盘判定的四种输入组合和重复判定。
- 客户端构建 **0 错误**；已有 `AiPlaylistPage.xaml` 的 WMC1506 绑定警告仍在。

界面交互由用户手动验证，不将编译通过或单元测试通过当作托盘界面验收。此前报告的主题跟随问题没有在本次点击交互修复中修改，不计为已验证解决。

## 6. 单实例

### 6.1 接入点在 `App` 构造函数

项目**没有 `Program.cs`**（XAML 编译器生成的 `Main` 只有四行）。`App` 的构造函数是最早的、我们控制得到的点，且在 `Host.CreateApplicationBuilder()` 之前 —— 第二个实例不该建 DI 容器、建窗口、装开始菜单快捷方式。

判定放在 `InitializeComponent()` **之后**：再往前挪就要跳过 App.xaml 的资源加载，那是一条平时永不执行、只在这个分支上走的初始化路径 —— 为省一次资源字典合并去踩一个没人验证过的启动路径不划算。真正贵的东西都在下面，这里已经全跳过了。

判定失败就置 `_isSecondaryInstance` 并返回，由 `OnLaunched` 走 `Exit()`。那个实例没有 Host，所以 `OnLaunched` 第一步必须退出。

### 6.2 机制

`Local\<AUMID>.SingleInstance` 命名互斥量 + `RegisterWindowMessage` + `PostMessage(HWND_BROADCAST)`。

- 互斥量名从 `AppIdentity.AppUserModelId` 派生，不另写字面量（与 `AppIdentity` 上「这些值必须一致」同一条纪律）。用 `Local\` 前缀（按会话），同机两个用户各跑一份是合理的。
- **互斥量句柄必须存在静态字段里** —— 被 GC 回收就等于命名对象消失，下一个实例会误判自己是主实例。`Dispose` 里**刻意不释放它**（它的寿命必须等于进程寿命）。
- Win32 文档明确 `HWND_BROADCAST` **能投递到隐藏的顶层窗口**，所以主窗口藏在托盘里照样收得到唤醒。同进程里别的顶层窗没装子类，收到后走 `DefSubclassProc`，无副作用。
- 唤醒前先 `AllowSetForegroundWindow(ASFW_ANY)` 让出前台权限，否则接收方的 `SetForegroundWindow` 会被系统拒绝（症状：「双击了没反应」）。同族经验见 `MiniPlayerWindow.OnReturnToMainClick` 的注释。
- 子类 id 用 **3**（主窗口上 1 是 `WindowRenderActivity`、4 是会话结束监视）。**id 撞了会把先装的那个静默顶掉**，所以每次新增都要先查一遍主窗口上已用掉的号。

## 7. 托盘图标

用户尚未定正式 `.ico`，当前是**占位**：库自带的动态图标生成，画一个 CJK 字符「波」（CJK 字形在 Win10 上必然存在；Emoji 要另挂字体，16×16 缩下去会糊）。

**正式图标到位后只改 `MainWindow.xaml` 里 `<tb:TaskbarIcon.IconSource>` 这一个元素**：换成 `IconSource="/Assets/Tray.ico"`，再给 csproj 加一条 `Content` + 复制到输出。本项目目前没有 `Assets/` 目录，也没有任何图片资源，所以那是第一次引入。

## 8. 测试

能离屏测的只有 `Services/TrayCloseDecision.cs`（两个 bool 进、一个 bool 出），测试项目用 `<Compile Include>` 链过去，见 `TrayCloseDecisionTests.cs`。

**测不到的**（要真窗口真进程，只能手动验）：托盘控件本身、`AppWindow.Closing`、命名互斥量、窗口消息。互斥量**尤其不要写单测** —— 名字是会话全局的，测试并行跑会互相干扰，而且「第二个实例」本来就是另一个进程。


## 输入法兼容处理与托盘验收（2026-10-06）

最终版本保留既有 `AppWindow.Hide()` / `AppWindow.Show()`、还原最小化与激活流程。
早期试过焦点重建、手动定位、TSF 会话修订和最小化隐藏任务栏，但这些均不进入正式实现。
最终只保留启动前的可选初始化兼容处理，用户已确认输入及托盘恢复后候选框正常。

用户通常从默认 Debug 输出启动，之前可能使用了旧构建。更换验证版本前必须从托盘选择「退出」，
不能只点窗口 ×；启动其他目录的 EXE 可能只唤醒旧进程。
常用路径与资源检查见 [`ime-candidate-window.md`](ime-candidate-window.md)。
