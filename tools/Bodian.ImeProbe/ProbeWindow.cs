using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.ImeProbe;

internal sealed class ProbeWindow : Window
{
    private readonly ProbeLog _log = Program.Log;
    private readonly NativeWindows _windows;
    private readonly DispatcherQueueTimer _timer;
    private readonly TextBlock _latest = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly CheckBox _force = new() { Content = "实验：请求输入法显示自己的候选界面（默认关闭）" };
    private TsfUiObserver? _tsf;
    private Process? _nativeProcess;
    private bool _closed;

    public ProbeWindow()
    {
        var legacyIme = Environment.GetCommandLineArgs().Contains("--legacy-ime", StringComparer.OrdinalIgnoreCase);
        Title = legacyIme ? "WinUI 输入法初始化兼容实验（3 个框）" : "输入法对照测试 — WinUI（3 个框）";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(860, 850));
        _windows = new NativeWindows(_log);
        _log.LineWritten += line => DispatcherQueue.TryEnqueue(() => _latest.Text = line);
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = legacyIme ? "WinUI 初始化兼容实验" : "微信输入法 / 微软拼音对照", FontSize = 24 });
        panel.Children.Add(new TextBlock { Text = "下面三个输入框全部是 WinUI 控件。", FontSize = 16 });
        if (legacyIme) panel.Children.Add(new TextBlock { Text = "本轮隔离验证 WinUI 初始化时禁用旧输入法的调用是否有关。", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = "先用微信输入法输入 nihao，停在选词状态；再用微软拼音重复。\n原生对照窗口在独立进程中运行。日志只记录长度、事件和窗口状态。",
            TextWrapping = TextWrapping.Wrap
        });
        var scenario = new ComboBox { Header = "当前测试", SelectedIndex = 0 };
        scenario.Items.Add("微信输入法");
        scenario.Items.Add("微软拼音");
        scenario.SelectionChanged += (_, _) => _log.Write($"scenario={scenario.SelectedItem}");
        panel.Children.Add(scenario);

        var textBox = new TextBox { Header = "普通 WinUI TextBox", PlaceholderText = "在这里输入 nihao" };
        textBox.TextChanged += (_, _) => _log.Write($"TextBox text-length={textBox.Text.Length} selection={textBox.SelectionStart}");
        textBox.TextCompositionStarted += (_, _) => _log.Write("TextBox composition-start");
        textBox.TextCompositionChanged += (_, _) => _log.Write("TextBox composition-change");
        textBox.TextCompositionEnded += (_, _) => _log.Write("TextBox composition-end");
        textBox.CandidateWindowBoundsChanged += (_, args) => _log.Write($"TextBox candidate-bounds={args.Bounds}");
        TrackFocus(textBox, "TextBox");
        panel.Children.Add(textBox);

        var suggestions = new AutoSuggestBox { Header = "普通 AutoSuggestBox（无搜索逻辑）", PlaceholderText = "在这里输入 nihao" };
        suggestions.TextChanged += (_, args) => _log.Write($"AutoSuggestBox text-length={suggestions.Text.Length} reason={args.Reason}");
        TrackFocus(suggestions, "AutoSuggestBox");
        panel.Children.Add(suggestions);

        panel.Children.Add(new TextBlock { Text = "普通 RichEditBox" });
        var richEdit = new RichEditBox { Height = 110 };
        richEdit.TextChanged += (_, _) =>
        {
            richEdit.Document.GetText(TextGetOptions.None, out var text);
            _log.Write($"RichEditBox text-length={text.Length}");
        };
        TrackFocus(richEdit, "RichEditBox");
        panel.Children.Add(richEdit);

        var noTsf = Environment.GetCommandLineArgs().Contains("--no-tsf", StringComparer.OrdinalIgnoreCase);
        var observeTsf = !noTsf && Environment.GetCommandLineArgs().Contains("--observe-tsf", StringComparer.OrdinalIgnoreCase);
        _force.IsEnabled = !noTsf;
        _force.Checked += (_, _) => SetForce(true);
        _force.Unchecked += (_, _) => SetForce(false);
        panel.Children.Add(_force);
        panel.Children.Add(new TextBlock
        {
            Text = noTsf ? "本轮未安装 TSF 观察器（--no-tsf），用于检查观察器是否影响现象。"
                : "默认不安装 TSF 观察器。开启实验后，请重新输入拼音；效果尚未验证。",
            FontSize = 12, TextWrapping = TextWrapping.Wrap
        });
        var nativeButton = new Button { Content = "打开原生 EDIT 对照窗口" };
        nativeButton.Click += (_, _) => LaunchNative();
        panel.Children.Add(nativeButton);
        panel.Children.Add(new TextBlock { Text = $"日志：{_log.Path}", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_latest);
        Content = new ScrollViewer { Content = panel };

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => _windows.Capture();
        _timer.Start();
        _log.Write($"scenario=微信输入法 no-tsf={noTsf} observe-tsf={observeTsf}");
        _log.Write($"xaml={typeof(TextBox).Assembly.GetName().Version} virtual-screen={NativeWindows.VirtualScreen}");
        if (observeTsf) _tsf = TsfUiObserver.TryCreate(_log, DispatcherQueue);
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            _tsf?.Dispose();
            _nativeProcess?.Dispose();
            _log.Write("closed");
            _log.Dispose();
        };
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed) textBox.Focus(FocusState.Programmatic);
        });
    }

    private void TrackFocus(Control control, string name)
    {
        control.GotFocus += (_, _) => _log.Write($"focus={name}");
        control.LostFocus += (_, _) => _log.Write($"blur={name}");
    }

    private void SetForce(bool enabled)
    {
        _log.Write($"force-input-method-ui={enabled}");
        if (enabled)
        {
            _tsf ??= TsfUiObserver.TryCreate(_log, DispatcherQueue);
            if (_tsf is not null) _tsf.ForceShow = true;
        }
        else
        {
            _tsf?.Dispose();
            _tsf = null;
        }
    }

    private void LaunchNative()
    {
        try
        {
            if (_nativeProcess is { HasExited: false })
            {
                _log.Write($"native-already-running pid={_nativeProcess.Id}");
                return;
            }
            var start = new ProcessStartInfo
            {
                FileName = System.IO.Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-File",
                System.IO.Path.Combine(AppContext.BaseDirectory, "NativeImeProbe.ps1") }) start.ArgumentList.Add(argument);
            _nativeProcess?.Dispose();
            _nativeProcess = Process.Start(start);
            if (_nativeProcess is not null)
            {
                _nativeProcess.ErrorDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data)) _log.Write($"native-stderr {args.Data}");
                };
                _nativeProcess.BeginErrorReadLine();
                _windows.AddProcess(_nativeProcess.Id);
                _log.Write($"native-start pid={_nativeProcess.Id}");
            }
        }
        catch (Exception exception) { _log.Write($"native-error {exception.GetType().Name}: {exception.Message}"); }
    }
}
