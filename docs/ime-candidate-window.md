# 微信输入法候选框兼容处理

2026-10-06。

**当前状态：用户已确认最终版本修好。** 现有 WinUI 输入控件、绑定和样式保留。
设置中的「输入法兼容模式（实验）」默认关闭，需要时开启，完全退出应用后重新启动生效。

## 1. 验收结果与启动路径

用户原先的复现条件：首次打开后输入，候选框位于输入框下方；输入过一次后关闭窗口到托盘
（没有退出进程），再次打开并输入，候选框跑到屏幕左上角。未输入过就关闭/恢复不一定触发。

最终最小版本 `artifacts/ime-final-minimal/` 修正启动资源后，用户确认「修好了」。
用户随后说明平时从下面的路径启动，之前可能多次启动了旧构建：

```text
F:\My_Project\bodain_winui\src\Bodian.WinUI\bin\Debug\net10.0-windows10.0.26100.0\win-x64\Bodian.WinUI.exe
```

因此，之前各轮负面反馈**不能都当作对应候选构建已经被验证失败**；一些测试的实际版本不明确。
已经确认的结果是最终版本可用，不能进一步认定某一层旧补丁就是位置错乱的唯一原因。

后续开发统一更新上述默认 Debug 输出；临时 `artifacts/` 构建应明确告知路径，验证时检查实际进程路径。
同一会话只能有一个主实例，启动另一个目录的 EXE 可能只唤醒仍在运行的旧实例。
更换构建前应从托盘菜单选择「退出」；窗口的 × 只隐藏，不等于退出。

## 2. 最终实现

- `Program.cs` 在 WinUI `Application.Start` 前读取兼容偏好，复用原生成入口的 STA、COM 包装器和同步上下文步骤。
- 开启兼容模式时，`LegacyImeCompatibility` 查找本进程 `Microsoft.ui.xaml.dll` 的 delay-import，跳过
  `IMM32.dll!ImmDisableLegacyIME`，保留旧输入法回退路径。未找到预期导入时记录失败并正常启动。
- 修改仅发生在本进程内存导入表，退出时恢复；不修改系统 DLL、磁盘运行库或输入法系统设置。
- 默认关闭的偏好保存到 `%LOCALAPPDATA%\Bodian\input-method.json`，使用源生成 JSON。
  坏配置回退关闭、保留原文件；保存失败时开关恢复之前的状态。
- 入口为「设置 → 外观 → 输入法兼容模式（实验）」。开启或关闭都需要完全退出后重新启动。
- 托盘 Hide/Show、非文本焦点和候选窗口定位均交给既有 WinUI 实现；最终应用没有接入额外 IMM 上下文保护、
  手动坐标、原生 caret 或 TSF 文档干预。没有替换原生输入框。

开发时可以使用 `Bodian.WinUI.exe --ime-compatibility` 临时开启本次启动的兼容路径，不写偏好。
最终代码的启动日志带 `final-minimal` 标记，可与早期候选构建区分。

## 3. 排查结论与撤回的判断

实测环境为 Windows 10 19045，微信输入法由 `2.1.3.18` 升级到 `2.1.4.6`。
用户确认升级后仍有问题；微软拼音在原先应用中正常。

| 对照或实验 | 观察与限制 |
| --- | --- |
| 原生 EDIT 单行、多行输入框 | 用户确认候选框正常；仅为对照，没有用于最终应用 |
| 同版本最小 WinUI TextBox / AutoSuggestBox / RichEditBox | 用户确认候选框异常 |
| 请求 TSF 显示 UI、调用 Show(true) | 未获得有效修复；IsShown=1 不等于实际候选框定位正确 |
| 跳过 WinUI 初始化的 ImmDisableLegacyIME | 探针日志确认实际命中；用户确认候选框恢复，形成最终兼容处理 |
| 焦点/上下文保护、手动坐标、TSF 会话修订、最小化到托盘 | 曾尝试但未进入最终实现；部分复测可能使用了旧构建，不作确定的因果结论 |

此前「应用侧无解」的结论已撤回。`CandidateWindowBoundsChanged` 不触发，只能说明框架侧通知缺失，
不能单独证明输入法未创建窗口。构建成功、位置 API 返回成功和自动测试的单轮结果，都不能代替用户验收。

正式源码仅保留最小兼容处理、设置与诊断探针。未采用的实验源码保存在本机
`artifacts/ime-research-archive/`，不进入应用编译或本次提交。

## 4. 独立构建启动失败与修复

曾交付的 `artifacts/ime-final-minimal/` 版本在 `MainWindow.InitializeComponent()` 抛出
`XamlParseException`。只读检查 PRI 发现资源名错误：

- 预期 `Files/MainWindow.xbf`，实际为 `Files/ime-final-minimalMainWindow.xbf`。
- App.xbf 也带有错误目录前缀；主题、控件资源的目录层级丢失。

构建命令的独立输出目录缺少末尾分隔符，造成 XBF 复制及索引路径错误。
保留旧文件，使用带末尾分隔符的输出路径和独立中间目录重新构建后，61 个 XAML 资源全部恢复正确路径。
用户重新启动后确认修好。

```powershell
dotnet build src/Bodian.WinUI/Bodian.WinUI.csproj -c Debug --output artifacts/ime-final-minimal/ -p:IntermediateOutputPath=obj/ime-startup-fixed/
```

一般开发使用默认输出，避免混淆：

```powershell
dotnet build src/Bodian.WinUI/Bodian.WinUI.csproj -c Debug
& ./tools/Verify-WinUIResources.ps1 -OutputDirectory ./src/Bodian.WinUI/bin/Debug/net10.0-windows10.0.26100.0/win-x64
```

资源检查只解析 PRI，不启动应用；核对项目全部 XAML 的相对索引和 XBF 数据。

## 5. 验证与维护限制

- 最终最小版本：用户已确认启动成功、输入法问题修好。
- 相关离屏测试 18 项通过，覆盖兼容偏好、导入表定位和原有托盘关闭判定。
- 主应用构建 0 错误，仅有既有 `AiPlaylistPage.xaml:28` 的 `WMC1506` 警告。
- 交付时核对 61 个 XAML 资源齐全、数据有效；程序集不含已撤掉的 IMM 上下文、候选坐标或 caret 干预。
- 用户指定的默认 Debug 路径已重新构建并通过同样的 61 项资源检查，输出 EXE 时间为 2026-10-06 22:28；本轮没有启动应用。
- 资源检查脚本已用此前错误索引验证，能拒绝 MainWindow.xbf 等资源路径缺失的构建。

该处理依赖当前固定 WinUI 运行时的内部导入，是可选的兼容规避，而非公开 WinUI 配置开关。
升级 WindowsAppSDK 后应重新验证导入定位、微软拼音、微信输入法、非文本控件、评论/弹窗、多窗口与 DPI。
目前的验收不外推为所有版本及输入法都通过。先前上游 PR #11579 只提供相关初始化线索，不能替代本项目验收。

诊断探针见 [tools/Bodian.ImeProbe/README.md](../tools/Bodian.ImeProbe/README.md)。

## 6. 官方接口依据

- [ImmDisableLegacyIME](https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immdisablelegacyime)。
- [WinUI PR #11579](https://github.com/microsoft/microsoft-ui-xaml/pull/11579)：初始化调用的相关兼容性讨论。
- [MakePri 命令](https://learn.microsoft.com/en-us/windows/uwp/app-resources/makepri-exe-command-options)：解析交付 PRI 的实际内容。
