# 输入法对照探针

用于复核 Windows 10 / 微信输入法在 WinUI 文本框中没有候选框的现象。
依赖版本直接读取 `src/Directory.Packages.props`，不加入主应用解决方案。

```powershell
dotnet build tools/Bodian.ImeProbe/Bodian.ImeProbe.csproj -c Debug
& ./tools/Bodian.ImeProbe/bin/Debug/net10.0-windows10.0.26100.0/win-x64/Bodian.ImeProbe.exe
```

1. 默认不安装 TSF observer，实验开关也关闭。微信输入法输入 `nihao`，停在拼音组合状态，观察三个 WinUI 输入框。
2. 选择“微软拼音”标记本轮，切换到微软拼音，重复输入。
3. 用“打开原生 EDIT 对照窗口”启动独立 Windows PowerShell / WinForms 进程，使用微信输入法输入同样的拼音。
4. 回到 WinUI 窗口，打开实验开关，重新输入拼音。它通过 `BeginUIElement` 返回允许显示，并在全部 sink 回调结束后尝试对隐藏的 UI element 调用 `Show(true)`；只是待验证的实验，不是已经确认的修复。
5. 如需仅观察 TSF 通知，关闭窗口后以 `Bodian.ImeProbe.exe --observe-tsf` 重复第 1 步。`--no-tsf` 则禁用实验开关，保证整轮不安装 observer。

日志写入 `%LOCALAPPDATA%\Bodian\ime-probe\`。记录输入长度、焦点、组合事件、TSF UI element 的显示状态，以及微信输入法和探针进程的窗口类、可见性、屏幕矩形、owner 与扩展样式。
不记录输入内容、候选词或窗口标题。窗口枚举独立运行，不依赖 `CandidateWindowBoundsChanged` 事件。

原生输入框在独立进程运行，避免 WinUI 初始化对当前线程的输入环境影响原生对照。WinUI 默认不安装观察器。使用 `--observe-tsf` 时观察器不调用 `Activate` / `Deactivate`，不更改 TSF 焦点，也不改写 `pbShow`。关闭实验开关会卸载观察器，恢复默认基线。

判断结果：

| WinUI | 原生 EDIT | 实验开关 | 下一步 |
| --- | --- | --- | --- |
| 正常 | 正常 | 无需开启 | 比较主应用的窗口、焦点与输入处理 |
| 无候选框 | 正常 | 恢复候选框 | 验证 TSF UI 显示协商兼容处理 |
| 无候选框 | 正常 | 无效 | 查候选窗口状态与 WinUI / 微信输入法文本栈交互，评估原生输入控件规避 |
| 无候选框 | 无候选框 | 无效 | 检查进程权限及系统输入法环境，不能直接归因到主应用 |

注意：sink 日志的 `incoming-show` 只是在这个观察器回调时看到的值，不代表所有 sink 的最终决定。
没有 TSF UI element 通知或 XAML 候选窗通知，也不能单独证明微信输入法没有创建窗口。

## 保留 WinUI 的初始化兼容实验

```powershell
& ./tools/Bodian.ImeProbe/bin/Debug/net10.0-windows10.0.26100.0/win-x64/Bodian.ImeProbe.exe --legacy-ime --no-tsf
```

窗口标题为“WinUI 输入法初始化兼容实验（3 个框）”。三个输入框仍全部为 WinUI。
本轮在 XAML 初始化前，仅在探针进程加载的 `Microsoft.ui.xaml.dll` 内存导入表里，
隔离其 `ImmDisableLegacyIME` 调用，不改磁盘上的 DLL，不改系统输入法设置。
使用 PE delay-import 表查找函数，不依赖固定地址；未找到预期导入时记录失败并保留正常初始化。
日志必须包含 `legacy-ime-hook-installed` 和 `legacy-ime-disable-bypassed`，才证明实际拦截到了该调用。

本探针的初始化实验已得到用户确认，最终主应用采用相同的初始化兼容方向，并提供默认关闭的设置开关。
用户已确认最终主应用修好。诊断工具仍独立运行，不会随主应用自动启动。
完整实现、常用启动路径及版本核对见 [docs/ime-candidate-window.md](../../docs/ime-candidate-window.md)。
上面的 TSF 显示实验仅保留用于诊断，不是主应用的最终实现。

`--observe-tsf` 现额外检查 `ITfCandidateListUIElement`、候选数量与选中索引，以及行为接口支持情况。
只记录元数据，不把候选词写入日志。它用于评估由 WinUI 补显示候选框的可行性。
