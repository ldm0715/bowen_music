using System.Collections.ObjectModel;
using Bodian.Core.Models;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 曲目「更多」菜单的内容：各项动作与「选歌单」两个互斥面板。
/// </summary>
/// <remarks>
/// <b>只是个内容控件</b>：按钮与 Flyout 由宿主自己声明 —— 行尾那颗是悬停出现的方钮，
/// 歌词页那颗是常显的圆钮，形状与显隐来源都不同，没有共用的余地。
/// 宿主有：<see cref="TrackMoreButton"/>（曲目行）与歌词页。
/// </remarks>
public sealed partial class TrackActionsMenu : UserControl
{
    public TrackActionsMenu()
    {
        InitializeComponent();
        EntriesList.ItemsSource = _entries;
    }

    /// <summary>菜单的状态与动作。切歌时宿主换一个新的。</summary>
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(TrackActionsViewModel),
        typeof(TrackActionsMenu),
        new PropertyMetadata(null, OnEntriesInputChanged));

    /// <summary>菜单用在哪。<see cref="TrackMenuScope"/> 里写了两种场景的差别。</summary>
    public static readonly DependencyProperty ScopeProperty = DependencyProperty.Register(
        nameof(Scope),
        typeof(TrackMenuScope),
        typeof(TrackActionsMenu),
        new PropertyMetadata(TrackMenuScope.Row, OnEntriesInputChanged));

    /// <summary>
    /// 某个动作要求关掉外层 Flyout。
    /// </summary>
    /// <remarks>
    /// <b>只能往上抛</b>：本控件拿不到外面那个 <see cref="Flyout"/> —— `UserControl` 没有
    /// 「我的 Flyout」这种 API。与 <c>PlayQueuePanel</c>、<c>SongCommentsPanel</c> 同一契约。
    /// </remarks>
    public event EventHandler? CloseRequested;

    public TrackActionsViewModel? ViewModel
    {
        get => (TrackActionsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public TrackMenuScope Scope
    {
        get => (TrackMenuScope)GetValue(ScopeProperty);
        set => SetValue(ScopeProperty, value);
    }

    /// <summary>
    /// 这一份作用域下真正要显示的那几行。清空重填即可，反正菜单一开一关就作废。
    /// </summary>
    /// <remarks>
    /// <b>私有字段，ItemsSource 在 code-behind 里设</b>：公开一个
    /// <c>ObservableCollection&lt;TrackMenuEntry&gt;</c> 属性会让 XAML 类型信息生成器
    /// 去给元素类型生成 <c>new TrackMenuEntry()</c> + 属性赋值，而它带 <c>required</c> 成员，
    /// 整个项目就编不过（同一个坑见 `Theme.xaml` 的「踩过的坑」一节）。
    /// </remarks>
    private readonly ObservableCollection<TrackMenuEntry> _entries = [];

    private static void OnEntriesInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TrackActionsMenu)d).RebuildEntries();

    private void RebuildEntries()
    {
        _entries.Clear();

        if (ViewModel is not { } viewModel)
        {
            return;
        }

        foreach (var entry in viewModel.MenuEntries)
        {
            if (Shows(Scope, entry.Action))
            {
                _entries.Add(entry);
            }
        }
    }

    /// <summary>这一项在这个作用域下显不显示。</summary>
    /// <remarks>
    /// <b>只在播放那一侧做减法</b>：喜欢、下一首播放、加入播放队列在歌词页是多余的 ——
    /// 左下角就有带计数的收藏按钮，另两项对一首已经在放的歌也没什么用。
    /// 「添加到歌单」「播放 MV」「查看歌手」「查看专辑」留下。
    /// </remarks>
    private static bool Shows(TrackMenuScope scope, TrackMenuAction action) => scope switch
    {
        TrackMenuScope.Player => action is TrackMenuAction.AddToPlaylist
            or TrackMenuAction.Mv
            or TrackMenuAction.Artist
            or TrackMenuAction.Album,
        _ => true,
    };

    /// <summary>Flyout 打开时调：拉一次喜欢状态。</summary>
    public void Initialize()
    {
        if (ViewModel is { } viewModel)
        {
            _ = viewModel.InitializeAsync();
        }
    }

    /// <summary>Flyout 关上时调：回到第一面，下次打开不残留上一首的歌单加载态与错误。</summary>
    public void Reset() => ViewModel?.ResetPlaylistPicker();

    private async void OnMenuEntryClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not TrackMenuEntry entry || ViewModel is not { } viewModel)
        {
            return;
        }

        switch (entry.Action)
        {
            case TrackMenuAction.Favorite:
                Close();
                await viewModel.ToggleFavoriteAsync();
                break;

            case TrackMenuAction.PlayNext:
                Close();
                await viewModel.PlayNextAsync();
                break;

            case TrackMenuAction.AddToQueue:
                Close();
                await viewModel.AddToQueueAsync();
                break;

            case TrackMenuAction.AddToPlaylist:
                // 这一项不关菜单，改成在同一个弹层里选出目标歌单。
                await viewModel.LoadPlaylistsAsync();
                break;

            case TrackMenuAction.Mv:
                Close();
                viewModel.OpenMv();
                break;

            case TrackMenuAction.Artist:
                Close();

                // 唯一且有效的歌手由 ViewModel 直接跳走；合唱的那几位交给弹窗。
                if (XamlRoot is { } root)
                {
                    await ArtistPickerDialog.ShowPickerAsync(viewModel, root, ActualTheme);
                }

                break;

            case TrackMenuAction.Album:
                Close();
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
            Close();
        }
    }

    private void OnBackToMenuClick(object sender, RoutedEventArgs e) => ViewModel?.ResetPlaylistPicker();

    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
