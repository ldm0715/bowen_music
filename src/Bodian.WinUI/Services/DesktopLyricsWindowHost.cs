using System.ComponentModel;
using Bodian.WinUI.ViewModels;
using Bodian.WinUI.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;

namespace Bodian.WinUI.Services;

/// <summary>
/// 桌面歌词窗的创建、显隐与销毁。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么单独一层。</b> ViewModel 不该自己 new 窗口（那它就再也进不了离线测试），
/// 而窗口又是<b>懒创建</b>的：没人开桌面歌词就不该多出一个 HWND。这一层把
/// 「开关状态」与「窗口生命周期」接在一起，两边各自干净。
/// </para>
/// <para>
/// <b>窗口关闭走这里，不走导航。</b> 歌词窗上的 ✕ 只把
/// <see cref="DesktopLyricsViewModel.IsEnabled"/> 置回 false，本类据此隐藏窗口 ——
/// 播放条那颗按钮因此自动回到未激活，不需要两边互相通知。
/// </para>
/// <para>
/// 窗口隐藏后<b>保留实例</b>：再开时省掉一次创建，也省掉一次「第二次创建窗口踩到
/// WinUI 初始化竞态」的风险。真正销毁只发生在应用退出。
/// </para>
/// </remarks>
public sealed class DesktopLyricsWindowHost : IDisposable
{
    private readonly DesktopLyricsViewModel _settings;
    private readonly Func<DesktopLyricsWindow> _windowFactory;
    private readonly ILogger<DesktopLyricsWindowHost> _logger;

    private DesktopLyricsWindow? _window;
    private bool _disposed;

    public DesktopLyricsWindowHost(
        DesktopLyricsViewModel settings,
        Func<DesktopLyricsWindow> windowFactory,
        ILogger<DesktopLyricsWindowHost>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(windowFactory);

        _settings = settings;
        _windowFactory = windowFactory;
        _logger = logger ?? NullLogger<DesktopLyricsWindowHost>.Instance;

        _settings.PropertyChanged += OnSettingsChanged;
    }

    /// <summary>当前窗口实例；没开过就是 <c>null</c>。给诊断用。</summary>
    public DesktopLyricsWindow? Window => _window;

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DesktopLyricsViewModel.IsEnabled))
        {
            return;
        }

        if (_settings.IsEnabled)
        {
            Show();
        }
        else
        {
            _window?.HideWindow();
        }
    }

    private void Show()
    {
        try
        {
            _window ??= _windowFactory();
            _window.ShowWindow();

            _logger.LogInformation("桌面歌词窗已显示");
        }
        catch (Exception exception)
        {
            // 悬浮窗这套是无官方支持的 hack，创建失败时要留下原因，别静默失效 ——
            // 表现为「点了按钮什么都没发生」是最难查的。
            _logger.LogError(exception, "桌面歌词窗创建或显示失败");

            _window = null;
            _settings.IsEnabled = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.PropertyChanged -= OnSettingsChanged;

        if (_window is { } window)
        {
            _window = null;
            window.Close();
        }
    }
}
