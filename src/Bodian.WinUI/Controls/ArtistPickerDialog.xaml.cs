using Bodian.WinUI.ViewModels;
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

        Selected = artist;
        ChoiceMade?.Invoke(this, EventArgs.Empty);
    }
}
