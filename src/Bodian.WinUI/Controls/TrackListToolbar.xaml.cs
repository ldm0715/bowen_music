using System.Windows.Input;
using Bodian.Core.Models;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 曲目列表右上角的操作条：全部加入播放列表 / 多选 / 刷新，以及多选态的批量动作。
/// </summary>
/// <remarks>
/// <para>
/// <b>选择状态不在这里，在 <see cref="Target"/> 上。</b> 工具栏只是个遥控器 ——
/// 它读 <see cref="TrackListView.SelectionCount"/> 显示计数，调它的方法做全选与清空。
/// 校验一下状态归属：选择要和「哪些行勾上了」严格一致，而只有列表知道行的事，
/// 工具栏再存一份就是两个真值来源。
/// </para>
/// <para>
/// <b>服务走 App 资源</b>：像本控件这样由 XAML 实例化的控件构造函数必须无参、拿不到 DI 容器，
/// 与 <see cref="TrackMoreButton"/> 是同一处例外。取不到就把批量按钮整块收起来 ——
/// 留一颗点不动的按钮比没有它更糟。
/// </para>
/// <para>
/// <b>页面只需绑两样</b>：<see cref="Target"/>（那一页的 <c>TrackListView</c>）与
/// <see cref="ReloadCommand"/>（那一页自己的重载命令）。
/// </para>
/// </remarks>
public sealed partial class TrackListToolbar : UserControl
{
    /// <summary>要操作哪个列表。必填 —— 不设就只剩一颗刷新按钮是活的。</summary>
    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.Register(
            nameof(Target),
            typeof(TrackListView),
            typeof(TrackListToolbar),
            new PropertyMetadata(null, OnTargetChanged));

    /// <summary>「刷新」按下去要执行什么。各页给自己的重载命令。</summary>
    public static readonly DependencyProperty ReloadCommandProperty =
        DependencyProperty.Register(
            nameof(ReloadCommand),
            typeof(ICommand),
            typeof(TrackListToolbar),
            new PropertyMetadata(null));

    /// <summary>
    /// 可以从哪个自建歌单里移出曲目。<c>0</c>（默认）表示这一页没有「移出」这个动作。
    /// </summary>
    /// <remarks>
    /// <b>只给自建歌单填</b>：收藏来的歌单不是用户的，移不了。「我喜欢的」走
    /// <see cref="IsLikedPlaylist"/> 那条路，因为它要顺带同步喜欢状态的缓存。
    /// </remarks>
    public static readonly DependencyProperty RemovablePlaylistIdProperty =
        DependencyProperty.Register(
            nameof(RemovablePlaylistId),
            typeof(long),
            typeof(TrackListToolbar),
            new PropertyMetadata(0L, OnRemoveTargetChanged));

    /// <summary>这一页是不是「我喜欢的」。为真时「移出」走取消喜欢那条路。</summary>
    public static readonly DependencyProperty IsLikedPlaylistProperty =
        DependencyProperty.Register(
            nameof(IsLikedPlaylist),
            typeof(bool),
            typeof(TrackListToolbar),
            new PropertyMetadata(false, OnRemoveTargetChanged));

    private ITrackBatchActions? _actions;
    private CancellationTokenSource? _batch;

    public TrackListToolbar()
    {
        InitializeComponent();

        // x:Bind 在 InitializeComponent 里就设过一轮了，这里兜住「页面两个都没给」的默认状态。
        SyncRemoveAction();

        // 服务要等 Loaded 才取得出来，所以「移出」那面也得等它之后再定一次。
        Loaded += (_, _) =>
        {
            EnsureService();
            SyncRemoveAction();
        };

        Unloaded += (_, _) => CancelBatch();
    }

    public long RemovablePlaylistId
    {
        get => (long)GetValue(RemovablePlaylistIdProperty);
        set => SetValue(RemovablePlaylistIdProperty, value);
    }

    public bool IsLikedPlaylist
    {
        get => (bool)GetValue(IsLikedPlaylistProperty);
        set => SetValue(IsLikedPlaylistProperty, value);
    }

    public TrackListView? Target
    {
        get => (TrackListView?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    public ICommand? ReloadCommand
    {
        get => (ICommand?)GetValue(ReloadCommandProperty);
        set => SetValue(ReloadCommandProperty, value);
    }

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var toolbar = (TrackListToolbar)d;

        if (e.OldValue is TrackListView previous)
        {
            previous.SelectionChanged -= toolbar.OnTargetSelectionChanged;
        }

        if (e.NewValue is TrackListView current)
        {
            current.SelectionChanged += toolbar.OnTargetSelectionChanged;
        }

        toolbar.SyncFromTarget();
    }

    private void OnTargetSelectionChanged(object? sender, EventArgs e) => SyncFromTarget();

    private static void OnRemoveTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TrackListToolbar)d).SyncRemoveAction();

    /// <summary>决定「移出」这颗按钮出不出现、叫什么。</summary>
    private void SyncRemoveAction()
    {
        // 服务拿不到时整个批量区都是死的，别单独留一颗点得动的「移出」。
        var available = _actions is not null && (IsLikedPlaylist || RemovablePlaylistId > 0);

        BatchRemoveButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        BatchRemoveText.Text = IsLikedPlaylist ? "移出喜欢" : "移出歌单";
    }

    private ITrackBatchActions? EnsureService()
    {
        _actions ??= Application.Current.Resources["BodianTrackActions"] as ITrackBatchActions;

        // 拿不到服务就只收起批量动作，刷新还能用 —— 它不依赖服务。
        if (_actions is null)
        {
            BatchQueueButton.Visibility = Visibility.Collapsed;
            BatchLikeButton.Visibility = Visibility.Collapsed;
            BatchPlaylistButton.Visibility = Visibility.Collapsed;
            SelectAllButton.Visibility = Visibility.Collapsed;
            AddAllButton.IsEnabled = false;
            MultiSelectButton.IsEnabled = false;
        }

        return _actions;
    }

    /// <summary>把列表的选择状态刷到工具栏上。列表是唯一真值来源，这里只做投影。</summary>
    private void SyncFromTarget()
    {
        var target = Target;

        if (target is null)
        {
            return;
        }

        var selecting = target.IsSelectionMode;

        NormalBar.Visibility = selecting ? Visibility.Collapsed : Visibility.Visible;
        SelectionBar.Visibility = selecting ? Visibility.Visible : Visibility.Collapsed;

        // 批量进行中时计数让位给进度文案，别互相覆盖。
        if (_batch is null)
        {
            CountText.Text = $"已选 {target.SelectionCount} 首";
        }

        SelectAllButton.Content = target.IsAllSelected ? "取消全选" : "全选";

        var hasSelection = target.SelectionCount > 0;

        BatchQueueButton.IsEnabled = hasSelection && _batch is null;
        BatchLikeButton.IsEnabled = hasSelection && _batch is null;
        BatchPlaylistButton.IsEnabled = hasSelection && _batch is null;
        BatchRemoveButton.IsEnabled = hasSelection && _batch is null;
        SelectAllButton.IsEnabled = target.Rows.Count > 0 && _batch is null;
    }

    // ── 普通态 ──────────────────────────────────────────────────────────────

    /// <summary>把列表里**已经加载的**全部加到播放队列。</summary>
    private async void OnAddAllToQueueClick(object sender, RoutedEventArgs e)
    {
        if (Target is not { } target || _batch is not null)
        {
            return;
        }

        await AddToQueueAsync(target.SourceTracks);
    }

    /// <summary>批量加入选中的那些。与「全部加入」共用一处提示逻辑。</summary>
    private async void OnBatchAddToQueueClick(object sender, RoutedEventArgs e)
    {
        if (Target is not { } target || _batch is not null || target.SelectionCount == 0)
        {
            return;
        }

        await AddToQueueAsync(target.GetSelectedTracks());
    }

    /// <remarks>
    /// <b>只加传进来的那些</b>，不回头翻页拉全 —— 按钮因此是瞬时的。
    /// 提示里带上真实条数，免得用户以为整个列表都进去了。
    /// </remarks>
    private async Task AddToQueueAsync(IReadOnlyList<Track> tracks)
    {
        if (_actions is not { } actions || tracks.Count == 0)
        {
            return;
        }

        var added = await actions.AddToQueueAsync(tracks);

        actions.ShowNotice(
            added > 0
                ? $"已加入 {added} 首到播放队列"
                : "这些歌都已在播放队列里",
            added > 0 ? NoticeSeverity.Success : NoticeSeverity.Informational);
    }

    private void OnEnterSelectionClick(object sender, RoutedEventArgs e)
    {
        if (Target is { } target)
        {
            target.IsSelectionMode = true;
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        // 刷新会重建全部行，选择集合里存的引用随之失效 —— 先退出多选再刷，
        // 否则「已选 5 首」会挂在一个已经不存在的选择上。
        ExitSelection();
        RefreshList();
    }

    // ── 多选态 ──────────────────────────────────────────────────────────────

    private void OnExitSelectionClick(object sender, RoutedEventArgs e) => ExitSelection();

    /// <summary>退出多选。置 false 会连带清空选择，见 <c>TrackListView.ApplySelectionMode</c>。</summary>
    private void ExitSelection()
    {
        if (Target is { } target)
        {
            target.IsSelectionMode = false;
        }
    }

    private void OnToggleSelectAllClick(object sender, RoutedEventArgs e)
    {
        if (Target is not { } target)
        {
            return;
        }

        if (target.IsAllSelected)
        {
            target.ClearSelection();
        }
        else
        {
            target.SelectAll();
        }
    }

    private async void OnBatchLikeClick(object sender, RoutedEventArgs e)
    {
        if (_actions is not { } actions || Target is not { } target || _batch is not null || target.SelectionCount == 0)
        {
            return;
        }

        var tracks = target.GetSelectedTracks();
        using var cts = BeginBatch();

        LikedSongsBatchOutcome result;

        try
        {
            var progress = new Progress<BatchProgress>(p => CountText.Text = $"正在加入 {p.Done}/{p.Total} 首…");
            result = await actions.SetLikedManyAsync(tracks, liked: true, progress, cts.Token);
        }
        finally
        {
            // 异常路径也要把按钮放回可用，否则一次失败就把工具栏永久锁死。
            EndBatch();
        }

        switch (result.Outcome)
        {
            case LikedSongsOutcome.NotAuthenticated:
                actions.ShowNotice("登录后可以喜欢。", NoticeSeverity.Error);
                break;
            case LikedSongsOutcome.NoLikedPlaylist:
                actions.ShowNotice("账号还没有「我喜欢的」歌单，暂时无法喜欢。", NoticeSeverity.Error);
                break;
            case LikedSongsOutcome.AlreadyPending:
                actions.ShowNotice("这批歌正在处理中，请稍候。");
                break;
            case LikedSongsOutcome.Succeeded when result.Canceled:
                actions.ShowNotice($"已取消，成功喜欢 {result.Succeeded} 首");
                break;
            case LikedSongsOutcome.Succeeded when result.Failed > 0:
                actions.ShowNotice($"成功喜欢 {result.Succeeded} 首，{result.Failed} 首失败", NoticeSeverity.Error);
                break;
            case LikedSongsOutcome.Succeeded:
                actions.ShowNotice($"已喜欢 {result.Succeeded} 首", NoticeSeverity.Success);
                break;
            default:
                actions.ShowNotice("操作失败，请稍后再试", NoticeSeverity.Error);
                break;
        }
    }

    private async void OnBatchPlaylistClick(object sender, RoutedEventArgs e)
    {
        if (_actions is not { } actions || Target is not { } target || target.SelectionCount == 0)
        {
            return;
        }

        var picker = new BatchPlaylistPicker();
        var dialog = AppDialogs.Create("添加到歌单", XamlRoot, ActualTheme, maxWidth: 340);

        dialog.Content = picker;
        dialog.PrimaryButtonText = "加入";
        dialog.CloseButtonText = "取消";
        dialog.IsPrimaryButtonEnabled = false;

        picker.SelectionChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = picker.SelectedPlaylist is not null && !picker.IsWriting;

            // 写入中点「取消」是叫停写入，不是关窗 —— 关掉的话进度和失败原因就看不见了。
            dialog.CloseButtonText = picker.IsWriting ? "取消写入" : "取消";
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();

            try
            {
                args.Cancel = !await picker.CommitAsync();
            }
            finally
            {
                deferral.Complete();
            }
        };

        dialog.Closing += (_, args) =>
        {
            if (picker.IsWriting)
            {
                args.Cancel = true;
                picker.CancelWrite();
            }
        };

        await picker.LoadAsync(actions, target.GetSelectedTracks());
        await dialog.ShowAsync();
    }

    /// <remarks>
    /// <b>先确认再动手。</b> 批量移除误点一下就是几十首没了 —— 虽然能手动加回来，
    /// 但那比多弹一次框贵得多。默认按钮也让它落在「取消」上，回车不会误触。
    /// </remarks>
    private async void OnBatchRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_actions is not { } actions || Target is not { } target
            || _batch is not null || target.SelectionCount == 0
            || XamlRoot is not { } root)
        {
            return;
        }

        var tracks = target.GetSelectedTracks();
        var liked = IsLikedPlaylist;

        var dialog = AppDialogs.Create(liked ? "移出喜欢" : "移出歌单", root, ActualTheme);
        dialog.Content = new TextBlock
        {
            Text = liked
                ? $"确定把选中的 {tracks.Count} 首移出「我喜欢的」吗？之后还可以再点喜欢加回来。"
                : $"确定把选中的 {tracks.Count} 首从这个歌单移出吗？",
            TextWrapping = TextWrapping.Wrap,
        };
        dialog.PrimaryButtonText = "移出";
        dialog.CloseButtonText = "取消";
        dialog.DefaultButton = ContentDialogButton.Close;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var removed = 0;
        var failed = 0;
        var canceled = false;

        using var cts = BeginBatch();

        try
        {
            var progress = new Progress<BatchProgress>(p => CountText.Text = $"正在移出 {p.Done}/{p.Total} 首…");

            if (liked)
            {
                var outcome = await actions.SetLikedManyAsync(tracks, liked: false, progress, cts.Token);

                if (outcome.Outcome is LikedSongsOutcome.NotAuthenticated)
                {
                    actions.ShowNotice("登录后可以修改「我喜欢的」。", NoticeSeverity.Error);
                    return;
                }

                if (outcome.Outcome is LikedSongsOutcome.NoLikedPlaylist)
                {
                    actions.ShowNotice("账号还没有「我喜欢的」歌单。", NoticeSeverity.Error);
                    return;
                }

                removed = outcome.Succeeded;
                failed = outcome.Failed;
                canceled = outcome.Canceled;
            }
            else
            {
                var result = await actions.RemoveFromPlaylistAsync(RemovablePlaylistId, tracks, progress, cts.Token);
                removed = result.Succeeded;
                failed = result.Failed;
                canceled = result.Canceled;
            }
        }
        catch (InvalidOperationException)
        {
            actions.ShowNotice("登录后可以修改歌单。", NoticeSeverity.Error);
            return;
        }
        finally
        {
            EndBatch();
        }

        // 一句话里可能既有成功又有失败，取更重的那一档：有失败就是红的，
        // 否则有成功是绿的，全被取消（removed / failed 都是 0）才是中性。
        actions.ShowNotice(
            Describe(removed, failed, canceled, liked),
            failed > 0 ? NoticeSeverity.Error
                : removed > 0 ? NoticeSeverity.Success
                : NoticeSeverity.Informational);

        // 移出过的那些歌还挂在列表上，重取一次才对得上。刷新前先退出多选 ——
        // 列表会整表重建，留着选择计数就是错的（与刷新按钮同一条规矩）。
        if (removed > 0 || failed > 0)
        {
            ExitSelection();
            RefreshList();
        }
    }

    private static string Describe(int removed, int failed, bool canceled, bool liked)
    {
        var what = liked ? "移出喜欢" : "移出";

        if (canceled)
        {
            return $"已取消，{what} {removed} 首";
        }

        return failed > 0
            ? $"{what} {removed} 首，{failed} 首失败"
            : $"已{what} {removed} 首";
    }

    /// <summary>重取当前列表。</summary>
    private void RefreshList()
    {
        if (ReloadCommand?.CanExecute(null) == true)
        {
            ReloadCommand.Execute(null);
        }
    }

    // ── 批量进行中的状态 ────────────────────────────────────────────────────

    /// <summary>进入「正在批量处理」：禁用按钮，并把计数位置让给进度文案。</summary>
    private CancellationTokenSource BeginBatch()
    {
        var cts = new CancellationTokenSource();
        _batch = cts;

        BatchLikeButton.IsEnabled = false;
        BatchPlaylistButton.IsEnabled = false;
        SelectAllButton.IsEnabled = false;

        return cts;
    }

    private void EndBatch()
    {
        _batch = null;
        SyncFromTarget();
    }

    private void CancelBatch()
    {
        _batch?.Cancel();
        _batch = null;
    }
}
