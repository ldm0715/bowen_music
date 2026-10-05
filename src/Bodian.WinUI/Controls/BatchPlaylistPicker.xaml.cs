using Bodian.Core.Models;
using Bodian.Core.Services;
using Bodian.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 「批量加入歌单」对话框的内容：列出全部自建歌单，选一个，就地写入并显示进度。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是对话框而不是浮动面板</b>：行尾「更多」用的是 Flyout，因为它挂在会被回收的行容器里
/// （二级弹层在那种位置定位不可靠，见 <see cref="TrackMoreButton"/>）。工具栏在页面顶层，没有这个约束；
/// 而「选一个目标 + 看进度 + 看失败原因」是一屏级的决策，对话框能给它一个稳定的宿主。
/// </para>
/// <para>
/// <b>写入过程中不关窗</b>：失败原因与「已取消，成功 N 首」这类结果要留在面板里。
/// 关闭由外壳的按钮驱动，写入时点「取消」是取消写入而不是关窗，见
/// <see cref="CancelWrite"/> 与 <c>TrackListToolbar</c> 里的 Closing 处理。
/// </para>
/// </remarks>
public sealed partial class BatchPlaylistPicker : UserControl
{
    private ITrackBatchActions? _actions;
    private IReadOnlyList<Track> _tracks = [];
    private CancellationTokenSource? _write;

    public BatchPlaylistPicker() => InitializeComponent();

    /// <summary>选中的歌单变了。外壳据此决定主按钮可不可点。</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>正在写入。<b>外壳据此把关闭按钮改成「取消写入」</b>。</summary>
    public bool IsWriting => _write is not null;

    /// <summary>
    /// 当前选中的歌单；还没选时是 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// <b>刻意是 <c>internal</c> 而不是 public。</b> XAML 类型信息生成器会为控件公开的每个具体类型
    /// 生成 <c>new 该类型()</c>，而 <see cref="Playlist"/> 带 <c>required</c> 成员、不允许空构造 ——
    /// 公开它整个项目就编不过（同一个坑见 <see cref="ViewModels.TrackRow"/> 的类型说明）。
    /// 消费方只有同程序集的工具栏，收成 internal 就够。
    /// </remarks>
    internal Playlist? SelectedPlaylist => PlaylistList.SelectedItem as Playlist;

    /// <summary>拉一次自建歌单并铺开列表。</summary>
    public async Task LoadAsync(ITrackBatchActions actions, IReadOnlyList<Track> tracks)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(tracks);

        _actions = actions;
        _tracks = tracks;

        ShowLoading();

        try
        {
            var playlists = await actions.GetCreatedPlaylistsAsync().ConfigureAwait(true);

            if (playlists.Count == 0)
            {
                ShowMessage("还没有自建歌单。");
                return;
            }

            PlaylistList.ItemsSource = playlists;
            PlaylistList.Visibility = Visibility.Visible;
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
        catch (InvalidOperationException)
        {
            // 未登录。没发请求就没有别的失败可能，所以单独认它。
            ShowMessage("登录后可以添加到歌单。");
        }
        catch (Exception)
        {
            ShowMessage("歌单读取失败，请稍后再试。");
        }
    }

    /// <summary>
    /// 把选中的歌单写进去。
    /// </summary>
    /// <returns>全部成功返回 <c>true</c>，外壳据此关窗；其余情况返回 <c>false</c>，错误留在面板里。</returns>
    public async Task<bool> CommitAsync()
    {
        if (_actions is not { } actions || SelectedPlaylist is not { } playlist || _tracks.Count == 0)
        {
            return false;
        }

        using var cts = new CancellationTokenSource();
        _write = cts;
        SelectionChanged?.Invoke(this, EventArgs.Empty);

        ShowWriting();

        try
        {
            var progress = new Progress<BatchProgress>(ShowProgress);
            var result = await actions.AddToPlaylistAsync(playlist.Id, _tracks, progress, cts.Token)
                .ConfigureAwait(true);

            WritePanel.Visibility = Visibility.Collapsed;

            if (result.Canceled)
            {
                ShowMessage($"已取消，成功加入 {result.Succeeded} 首。");
                return false;
            }

            if (result.Failed > 0)
            {
                ShowMessage($"成功加入 {result.Succeeded} 首，{result.Failed} 首失败。");
                return false;
            }

            actions.ShowNotice($"已加入「{playlist.Name}」{result.Succeeded} 首", NoticeSeverity.Success);
            return true;
        }
        catch (InvalidOperationException)
        {
            WritePanel.Visibility = Visibility.Collapsed;
            ShowMessage("登录后可以添加到歌单。");
            return false;
        }
        catch (Exception)
        {
            WritePanel.Visibility = Visibility.Collapsed;
            ShowMessage("加入歌单失败，请稍后再试。");
            return false;
        }
        finally
        {
            _write = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>叫停正在进行的写入。外壳在写入期间收到关窗请求时调它。</summary>
    public void CancelWrite() => _write?.Cancel();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        => SelectionChanged?.Invoke(this, EventArgs.Empty);

    private void ShowLoading()
    {
        MessageText.Visibility = Visibility.Collapsed;
        LoadingRing.Visibility = Visibility.Visible;
        LoadingRing.IsActive = true;
    }

    private void ShowMessage(string message)
    {
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
    }

    private void ShowWriting()
    {
        MessageText.Visibility = Visibility.Collapsed;
        WriteBar.Value = 0;
        WriteText.Text = "正在加入…";
        WritePanel.Visibility = Visibility.Visible;
    }

    private void ShowProgress(BatchProgress progress)
    {
        // Value 用绝对数、Maximum 用总数：进度条自己算比例，这里不必再除一次。
        WriteBar.Maximum = progress.Total;
        WriteBar.Value = progress.Done;
        WriteText.Text = $"正在加入 {progress.Done}/{progress.Total} 首…";
    }
}
