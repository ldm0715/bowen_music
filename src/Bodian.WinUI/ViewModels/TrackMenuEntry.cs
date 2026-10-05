using CommunityToolkit.Mvvm.ComponentModel;

namespace Bodian.WinUI.ViewModels;

/// <summary>「更多」菜单里能做的事。</summary>
public enum TrackMenuAction
{
    Favorite,

    /// <summary>插到播放队列的当前曲目之后。</summary>
    PlayNext,

    /// <summary>加到播放队列的队尾。</summary>
    AddToQueue,

    AddToPlaylist,

    /// <summary>打开这首歌的 MV 页。曲目没有 MV 时这一项<b>不出现</b>。</summary>
    Mv,

    Artist,
    Album,
}

/// <summary>
/// 「更多」菜单用在哪 —— 决定显示哪几项。
/// </summary>
/// <remarks>
/// <b>同一个曲目，放出不同的菜单</b>：曲目行尾那份要管「这一首待会儿怎么办」
/// （喜欢、插队、入队），而正在播放的那一首（歌词页）不需要 ——
/// 喜欢在歌词页左下角本来就有颗带计数的按钮，另两项对已经在放的那一首也没什么意义。
/// 菜单项本身仍由 <c>TrackActionsViewModel</c> 一处构造，这里只做取舍。
/// </remarks>
public enum TrackMenuScope
{
    /// <summary>曲目行尾：完整的一套。</summary>
    Row,

    /// <summary>正在播放的那一首（歌词页底部那颗「更多」）：去掉页面上已有入口的那几项。</summary>
    Player,
}

/// <summary>
/// 「更多」菜单里的一行。
/// </summary>
/// <remarks>
/// 做成可绑定对象、而不是在 XAML 里写死四行：「我喜欢」那一行的图标与文字会随状态翻转
/// （已喜欢 → 实心红心 +「取消喜欢」），写死就得在 code-behind 里按名字摸控件改属性，
/// 也就把这部分逻辑挡在了离线测试之外。
/// </remarks>
public sealed partial class TrackMenuEntry : ObservableObject
{
    public required TrackMenuAction Action { get; init; }

    /// <summary>
    /// 图标在 <c>Themes/Icons.xaml</c> 里的**资源键**（形如 <c>IconHeart</c>），不是路径、不是码位。
    /// XAML 用 <c>Formats.IconPaths</c> 把键换成路径文本。
    /// </summary>
    [ObservableProperty]
    public partial string IconKey { get; set; } = "";

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>
    /// 这一项能不能点。
    /// </summary>
    /// <remarks>
    /// 不可用时<b>置灰而不是隐藏</b>：菜单项忽多忽少比灰着更让人困惑。
    /// 目前只有「查看专辑」会灰 —— 曲目没带专辑 id 时（旧的历史条目就会这样）。
    /// </remarks>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;
}
