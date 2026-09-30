using Bodian.WinUI.Controls;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 主歌词页：左栏当前曲目信息，右栏 Win2D 逐字歌词。
/// </summary>
/// <remarks>
/// <para>
/// <b>它负责把「歌词页是否活跃」告诉 <see cref="LyricsViewModel"/>。</b>
/// 那条信息决定要不要给第三方服务发取词请求 —— 没人看的时候为每一首播过的歌都发一次请求，
/// 属于把别人的服务当压测。所以这个通知由导航服务在固定时机调用，不依赖控件的生命周期事件。
/// </para>
/// <para>
/// <b>只要不在唱歌词页的那几种状态就不渲染</b>：页面切走、窗口不可见（含最小化）。
/// 显式设 <c>Paused</c>，不依赖 Win2D 的隐式省电行为 —— 那个行为没有官方承诺。
/// </para>
/// <para>
/// 滚动与高亮<b>不在这里驱动</b>：Win2D 渲染器按 60fps 的平滑时钟自己算，
/// 而 <see cref="LyricsViewModel.CurrentIndex"/> 是 5Hz 采样的，两套判断同时驱动会打架。
/// </para>
/// </remarks>
public sealed partial class LyricsPage : Page, INavigationAware
{
    private readonly INavigationService _navigation;
    private readonly MainWindow _window;
    private readonly LyricsCanvasView _canvas;

    private bool _windowVisible = true;

    public LyricsPage(
        MainWindow window,
        LyricsCanvasView canvas,
        PlayerViewModel player,
        LyricsViewModel lyrics,
        INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(navigation);

        _window = window;
        _canvas = canvas;
        _navigation = navigation;

        Player = player;
        Lyrics = lyrics;

        InitializeComponent();

        CanvasHost.Content = canvas;
    }

    /// <summary>左栏的信息来源。封面与专辑走它，见 XAML 头部的说明。</summary>
    public PlayerViewModel Player { get; }

    public LyricsViewModel Lyrics { get; }

    public void OnNavigatedTo()
    {
        _window.VisibilityChanged += OnWindowVisibilityChanged;
        Lyrics.IsOpen = true;
        UpdatePause();
    }

    public void OnNavigatedFrom()
    {
        _window.VisibilityChanged -= OnWindowVisibilityChanged;
        Lyrics.IsOpen = false;
        UpdatePause();
    }

    private void OnWindowVisibilityChanged(object? sender, WindowVisibilityChangedEventArgs e)
    {
        _windowVisible = e.Visible;
        UpdatePause();
    }

    private void UpdatePause() => _canvas.IsPaused = !Lyrics.IsOpen || !_windowVisible;

    private void OnBackClick(object sender, RoutedEventArgs e) => _navigation.GoBack();
}
