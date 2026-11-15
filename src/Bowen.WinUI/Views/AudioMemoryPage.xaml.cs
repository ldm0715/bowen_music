using System.ComponentModel;
using System.Globalization;
using Bowen.Core.Api;
using Bowen.Core.Models.AudioMemory;
using Bowen.WinUI.Controls;
using Bowen.WinUI.Services;
using Bowen.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bowen.WinUI.Views;

/// <summary>
/// 「音波回忆」根页：本周最爱 / 听歌月报两个页签。
/// </summary>
/// <remarks>
/// 两个页签的数据都跟账号绑定，换号后整页丢弃重拉。
/// 写操作（新建月度歌单）的确认框放在这里，因为 <c>XamlRoot</c> 拿不到 ViewModel 里。
/// </remarks>
public sealed partial class AudioMemoryPage : Page, INavigationAware, IAccountScopedView
{
    private readonly IBodianLogin _login;
    private readonly IClipboardService _clipboard;
    private readonly INoticeSink _notices;
    private readonly IWindowHandleProvider _windowHandles;
    private readonly FontSettingsViewModel _fontSettings;
    private readonly ILogger<AudioMemoryPage> _logger;

    /// <summary>上一次显示的页签，用来决定进入动画的方向。</summary>
    private int _lastTab;

    public AudioMemoryPage(
        AudioMemoryViewModel viewModel,
        IBodianLogin login,
        IClipboardService clipboard,
        INoticeSink notices,
        IWindowHandleProvider windowHandles,
        FontSettingsViewModel fontSettings,
        ILogger<AudioMemoryPage> logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(windowHandles);
        ArgumentNullException.ThrowIfNull(fontSettings);
        ArgumentNullException.ThrowIfNull(logger);

        ViewModel = viewModel;
        _login = login;
        _clipboard = clipboard;
        _notices = notices;
        _windowHandles = windowHandles;
        _fontSettings = fontSettings;
        _logger = logger;

        InitializeComponent();

        _lastTab = viewModel.SelectedTab;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public AudioMemoryViewModel ViewModel { get; }

    public void OnNavigatedTo() => _ = ViewModel.EnsureLoadedAsync();

    public Task OnAccountSwitchedAsync()
    {
        ViewModel.ResetForAccountChange();
        return ViewModel.EnsureLoadedAsync();
    }

    public void OnNavigatedFrom()
    {
    }

    /// <summary>
    /// 页签切换动画。
    /// </summary>
    /// <remarks>
    /// <see cref="Controls.PillTabBar"/> 自带的 <c>Motion.AnimateTabs</c> 依赖可见性更新的时序；
    /// 这里先把新内容真正放出来，再同步触发进入动画，避免动画在 Collapsed 元素上跑完。
    /// </remarks>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AudioMemoryViewModel.SelectedTab))
        {
            return;
        }

        var tab = ViewModel.SelectedTab;
        var direction = Math.Sign(tab - _lastTab);
        _lastTab = tab;

        var weekly = tab == 0;
        WeeklyContent.Visibility = weekly ? Visibility.Visible : Visibility.Collapsed;
        ReportContent.Visibility = weekly ? Visibility.Collapsed : Visibility.Visible;

        // 动画挂在始终可见的 TabHost 上：刚由 Collapsed 转 Visible 的子元素
        // 直接在它自己身上跑合成动画时不一定能渲染出来。
        _ = AppMotion.EnterAsync(TabHost, direction * 40, 0, TimeSpan.FromMilliseconds(400));
    }

    private void OnWeeklyPlayClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PlayRankingRow row })
        {
            ViewModel.Weekly.PlayCommand.Execute(row);
        }
    }

    private void OnReportTrackPlayClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ReportTrackRow row })
        {
            ViewModel.Report.PlayTrackCommand.Execute(row);
        }
    }

    /// <summary>
    /// 新建月度歌单前的确认。
    /// </summary>
    /// <remarks>
    /// 这是写操作，会在账号里真的建一个歌单；失败原因不在客户端展开，只给一条结果提示。
    /// </remarks>
    private async void OnCreatePlaylistClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Report.Report is not { } report)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,

            // 代码构造的 ContentDialog 不在可视树里，不显式给主题会永远跟随系统。
            RequestedTheme = ActualTheme,
            Title = "新建月度歌单？",
            Content = $"会在你的账号里新建一个「{report.Year}年{report.Month}月」的歌单。",
            PrimaryButtonText = "新建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.Report.CreatePlaylistAsync();
        }
    }

    private async void OnExportOverviewClick(object sender, RoutedEventArgs e)
        => await ShowPosterAsync(MonthPosterKind.Overview);

    private async void OnExportTracksClick(object sender, RoutedEventArgs e)
        => await ShowPosterAsync(MonthPosterKind.Tracks);

    private async Task ShowPosterAsync(MonthPosterKind kind)
    {
        if (ViewModel.Report.Report is not { } report)
        {
            return;
        }

        await MonthPosterDialog.ShowAsync(
            BuildPosterContent(report),
            kind,
            XamlRoot,
            ActualTheme,
            _windowHandles.WindowHandle,
            _clipboard,
            _notices,
            _logger);
    }

    private MonthPosterContent BuildPosterContent(MonthReport report)
    {
        var top = report.TopTracks.FirstOrDefault();
        var tracks = report.TopTracks
            .Select((track, index) => new MonthPosterTrack(
                index + 1,
                track.Title,
                track.Artist,
                track.Cover,
                track.PlayCount ?? 0))
            .ToArray();

        // 万一专辑封面列表为空，至少把榜首封面放进封面墙。
        var albumCovers = report.AlbumCovers.ToList();
        if (top?.Cover is { } topCover && !albumCovers.Contains(topCover))
        {
            albumCovers.Insert(0, topCover);
        }

        return new MonthPosterContent(
            _login.Nickname,
            _login.Account?.Avatar,
            _fontSettings.CurrentFontFamily,
            report.Year,
            report.Month,
            top?.Title ?? "—",
            top?.Artist ?? "",
            top?.Cover,
            report.SongCount,
            report.PlayCount,
            report.TimeRank,
            FormatListenTime(report.ListenTotalMinutes),
            albumCovers,
            tracks);
    }

    /// <summary>把服务端的分钟数字串成海报用的 <c>2h 55m</c>。</summary>
    private static string FormatListenTime(string? raw)
    {
        if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || minutes <= 0)
        {
            return "—";
        }

        var hours = minutes / 60;
        var rest = minutes % 60;

        return hours > 0 ? $"{hours}h {rest:00}m" : $"{rest}m";
    }
}
