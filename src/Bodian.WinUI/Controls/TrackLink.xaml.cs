using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>行里那一格放的是歌手还是专辑。</summary>
public enum TrackLinkKind
{
    Artist,
    Album,
}

/// <summary>
/// 曲目行里的「歌手名」/「专辑名」：跳得过去时是链接，跳不过去时退成普通文字。
/// </summary>
/// <remarks>
/// <para>
/// <b>与播放条第二行同一个样子</b>：颜色取自 <c>Themes/Theme.xaml</c> 里覆写的那三个键
/// （静止灰、悬停强调色、没有悬停底色）—— 换主题两边一起变。
/// </para>
/// <para>
/// <b>点击不抛事件、直接走服务</b>：控件是 XAML 实例化的，构造函数拿不到 DI 容器，
/// 服务只能从 App 资源取 —— 与 <see cref="TrackMoreButton"/> 同一处例外。
/// 播放条那套「只抛事件、外壳接线」是因为它只有一个宿主；这里有两个，接线就是两份复制品。
/// </para>
/// </remarks>
public sealed partial class TrackLink : UserControl
{
    private TrackActionsService? _service;

    public TrackLink() => InitializeComponent();

    /// <summary>这一行对应的数据。</summary>
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
        nameof(Row),
        typeof(TrackRow),
        typeof(TrackLink),
        new PropertyMetadata(null, OnInputChanged));

    /// <summary>这一格放的是歌手还是专辑。</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind),
        typeof(TrackLinkKind),
        typeof(TrackLink),
        new PropertyMetadata(TrackLinkKind.Artist, OnInputChanged));

    public TrackRow? Row
    {
        get => (TrackRow?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    public TrackLinkKind Kind
    {
        get => (TrackLinkKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TrackLink)d).ApplyKind();

    /// <summary>
    /// 切「显示哪一格」。格子里是链接还是普通文字由模板绑行上的判据决定，这里不掺和。
    /// </summary>
    /// <remarks>
    /// <b>Row 还没赋上时两格一起收起</b>：否则空路径会闪出一对空链接。
    /// 容器回收换曲目时本方法会被再调一次（<c>Row="{x:Bind}"</c> 会重赋），不需要别的清理。
    /// </remarks>
    private void ApplyKind()
    {
        var present = Row is not null;

        ArtistRoot.Visibility = Vis(present && Kind == TrackLinkKind.Artist);
        AlbumRoot.Visibility = Vis(present && Kind == TrackLinkKind.Album);
    }

    private static Visibility Vis(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 点了歌手名。
    /// </summary>
    /// <remarks>
    /// <b>要选人那一层交给 <see cref="ArtistPickerDialog.ShowPickerAsync"/></b>：唯一且有效的歌手直接跳，
    /// 合唱才弹选择框 —— 与曲目菜单「查看歌手」、播放条那一行共用同一份降级逻辑。
    /// <b>先把曲目取出来再 await</b>：弹窗挂着的时候这一行可能已经被回收去装别的曲目了。
    /// </remarks>
    private async void OnArtistClick(object sender, RoutedEventArgs e)
    {
        if (Row is not { } row || Service() is not { } service || XamlRoot is not { } root)
        {
            return;
        }

        // 用控件自己的 XamlRoot 与主题：比外壳那份准，将来多窗口时不会拿错。
        await ArtistPickerDialog.ShowPickerAsync(service.Create(row.Source), root, ActualTheme);
    }

    /// <summary>点了专辑名。曲目没带专辑 id 时这一格本来就是点不动的文字，正常点不到。</summary>
    private void OnAlbumClick(object sender, RoutedEventArgs e)
    {
        if (Row is { } row && Service() is { } service)
        {
            service.Create(row.Source).OpenAlbum();
        }
    }

    /// <remarks>
    /// 服务在外壳构造时就放进 App 资源（<c>BodianTrackActions</c>），早于任何页面，正常一定取得到；
    /// 真取不到就让点击静默失效 —— <b>不学 <see cref="TrackMoreButton"/> 把自己收起来</b>：
    /// 按钮收掉无所谓，这一格收掉就是行里凭空少一段文字。
    /// </remarks>
    private TrackActionsService? Service() =>
        _service ??= Application.Current.Resources["BodianTrackActions"] as TrackActionsService;
}
