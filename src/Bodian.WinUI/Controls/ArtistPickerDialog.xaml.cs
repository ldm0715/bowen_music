using Bodian.WinUI.ViewModels;
using Bodian.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 「查看歌手」对话框的内容：把一位歌手的头像与名字排成一张卡片，点一位回到详情页。
/// </summary>
/// <remarks>
/// <b>只是个内容控件</b>：标题、按钮与主题由调用方用 <see cref="AppDialogs"/> 拼
/// —— 与 <see cref="BatchPlaylistPicker"/> 同一个契约。
/// </remarks>
public sealed partial class ArtistPickerDialog : UserControl
{
    public ArtistPickerDialog() => InitializeComponent();

    /// <summary>用户点了某张头像。外壳据此关窗。</summary>
    public event EventHandler? ChoiceMade;

    /// <summary>
    /// 选中的歌手；没选过时是 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// <b>刻意是 <c>internal</c> 而不是 public。</b> XAML 类型信息生成器会为控件公开的具体类型
    /// 生成 <c>new 该类型()</c>，而 <see cref="ArtistChoice"/> 是定位 record、没有无参构造 ——
    /// 公开它整个项目就编不过（同一个坑见 <see cref="BatchPlaylistPicker.SelectedPlaylist"/>）。
    /// 消费方只有同程序集的行内按钮，收成 internal 就够。
    /// </remarks>
    internal ArtistChoice? Selected { get; private set; }

    /// <summary>
    /// 处理一次「查看歌手」：该直接跳就直接跳，该选人就弹这个框。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>三处共用</b>：曲目行「更多」、播放条第二行的歌手、歌词页「更多」。
    /// 逻辑本身在 <see cref="TrackActionsViewModel.OpenArtistAsync"/> 里，这里只是它那层壳。
    /// </para>
    /// <para>
    /// <b>放在这里而不是各宿主的 code-behind</b>：那样就是三份复制品。
    /// 与「弹窗留在 code-behind」那条约定也不冲突 —— 那条的真实含义是
    /// 「ViewModel / Service 不碰任何 WinUI 类型」，而本类就在 UI 层，
    /// 与 <see cref="AppDialogs"/> 同类同层。
    /// </para>
    /// </remarks>
    /// <param name="viewModel">已经绑定好曲目的菜单状态。</param>
    /// <param name="xamlRoot">宿主页面的 XamlRoot。ViewModel 拿不到它，只能由调用方给。</param>
    /// <param name="theme">代码构造的 ContentDialog 不在可视树里，不显式设主题就永远跟随系统。</param>
    internal static async Task ShowPickerAsync(TrackActionsViewModel viewModel, XamlRoot xamlRoot, ElementTheme theme)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(xamlRoot);

        var artists = await viewModel.OpenArtistAsync();

        // 空 = 已经处理完：要么已直接跳转，要么已提示「这首歌没有歌手信息」。
        if (artists.Count == 0)
        {
            return;
        }

        var picker = new ArtistPickerDialog();
        var dialog = AppDialogs.Create("歌手", xamlRoot, theme, maxWidth: 380);
        dialog.Content = picker;
        dialog.CloseButtonText = "关闭";

        picker.Load(artists);
        picker.ChoiceMade += (_, _) => dialog.Hide();

        await dialog.ShowAsync();

        // 关窗之后再跳：弹窗还挂着时换页会把它的根节点抽掉。
        if (picker.Selected is { } chosen)
        {
            viewModel.OpenArtist(chosen);
        }
    }

    /// <summary>铺开候选人。列表由 ViewModel 算好，这里只负责画。</summary>
    public void Load(IReadOnlyList<ArtistChoice> artists)
    {
        ArgumentNullException.ThrowIfNull(artists);

        ArtistGrid.ItemsSource = artists;
    }

    private void OnArtistClick(object sender, ItemClickEventArgs e)
    {
        // 找不到的那几格容器是禁用的，正常点不到；这里再兜一层（键盘或程序触发）。
        if (e.ClickedItem is not ArtistChoice { IsAvailable: true } artist)
        {
            return;
        }

        CoverTransitionAnimator.PrepareFromClick(ArtistGrid.ContainerFromItem(artist) as FrameworkElement, Formats.ArtistCoverKey(artist.Id));
        Selected = artist;
        ChoiceMade?.Invoke(this, EventArgs.Empty);
    }
}
