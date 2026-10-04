using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 曲目行尾的「更多」按钮。行模板只给它一个 <see cref="Row"/>，其余自洽。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么要做成控件</b>：行模板有三份（共享列表、搜索页综合结果、榜单预览），
/// 把按钮与菜单写进每一份就是三份要同步的复制品。
/// </para>
/// <para>
/// <b>服务走 App 资源</b>：控件是 XAML 实例化的，构造函数拿不到 DI 容器 ——
/// 与 <c>TrackListView.NowPlaying</c> 同一处例外。
/// </para>
/// <para>
/// <b>已知取舍</b>：按钮只在悬停时出现，键盘用户 Tab 到行上看不到它，也就无法用键盘打开菜单。
/// 这是按用户要求做的取舍，不是遗漏。
/// </para>
/// </remarks>
public sealed partial class TrackMoreButton : UserControl
{
    private TrackActionsService? _service;

    public TrackMoreButton()
    {
        InitializeComponent();
        Loaded += (_, _) => EnsureService();
    }

    /// <summary>这一行对应的数据。</summary>
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
        nameof(Row),
        typeof(TrackRow),
        typeof(TrackMoreButton),
        new PropertyMetadata(null, OnRowChanged));

    /// <summary>菜单的状态与动作，<see cref="Row"/> 变化时现造一个。</summary>
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(TrackActionsViewModel),
        typeof(TrackMoreButton),
        new PropertyMetadata(null));

    public TrackRow? Row
    {
        get => (TrackRow?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    public TrackActionsViewModel? ViewModel
    {
        get => (TrackActionsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnRowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var button = (TrackMoreButton)d;

        // 容器被回收去装别的曲目时，旧那一行的菜单必须关掉并清掉「开着」——
        // 否则菜单会挂在新曲目上，或者旧行的按钮再也不消失。
        if (e.OldValue is TrackRow previous)
        {
            previous.IsMenuOpen = false;
        }

        button.MoreFlyout.Hide();

        button.ViewModel = e.NewValue is TrackRow row && button.EnsureService() is { } service
            ? service.Create(row.Source)
            : null;
    }

    private TrackActionsService? EnsureService()
    {
        if (_service is not null)
        {
            return _service;
        }

        _service = Application.Current.Resources["BodianTrackActions"] as TrackActionsService;

        // 拿不到服务就整个收起来：留一个点不动的按钮比没有它更糟。
        if (_service is null)
        {
            Visibility = Visibility.Collapsed;
        }

        return _service;
    }

    private void OnFlyoutOpening(object? sender, object e)
    {
        if (Row is { } row)
        {
            row.IsMenuOpen = true;
        }

        if (ViewModel is { } viewModel)
        {
            _ = viewModel.InitializeAsync();
        }
    }

    private void OnFlyoutClosed(object? sender, object e)
    {
        if (Row is { } row)
        {
            row.IsMenuOpen = false;
        }

        // 回到第一面：下次打开不残留上一首的歌单加载态与错误。
        ViewModel?.ResetPlaylistPicker();
    }

    private async void OnMenuEntryClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not TrackMenuEntry entry || ViewModel is not { } viewModel)
        {
            return;
        }

        switch (entry.Action)
        {
            case TrackMenuAction.Favorite:
                MoreFlyout.Hide();
                await viewModel.ToggleFavoriteAsync();
                break;

            case TrackMenuAction.PlayNext:
                MoreFlyout.Hide();
                await viewModel.PlayNextAsync();
                break;

            case TrackMenuAction.AddToQueue:
                MoreFlyout.Hide();
                await viewModel.AddToQueueAsync();
                break;

            case TrackMenuAction.AddToPlaylist:
                // 这一项不关菜单，改成在同一个弹层里选出目标歌单。
                await viewModel.LoadPlaylistsAsync();
                break;

            case TrackMenuAction.Mv:
                MoreFlyout.Hide();
                viewModel.OpenMv();
                break;

            case TrackMenuAction.Artist:
                MoreFlyout.Hide();
                await viewModel.OpenArtistAsync();
                break;

            case TrackMenuAction.Album:
                MoreFlyout.Hide();
                viewModel.OpenAlbum();
                break;
        }
    }

    private async void OnPlaylistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not Playlist playlist || ViewModel is not { } viewModel)
        {
            return;
        }

        // 失败时留着菜单：错误就显示在这个面板里，关掉用户就看不见了。
        if (await viewModel.AddToPlaylistAsync(playlist))
        {
            MoreFlyout.Hide();
        }
    }

    private void OnBackToMenuClick(object sender, RoutedEventArgs e) => ViewModel?.ResetPlaylistPicker();
}
