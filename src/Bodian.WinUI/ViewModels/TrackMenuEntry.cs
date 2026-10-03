using CommunityToolkit.Mvvm.ComponentModel;

namespace Bodian.WinUI.ViewModels;

/// <summary>「更多」菜单里能做的四件事。</summary>
public enum TrackMenuAction
{
    Favorite,
    AddToPlaylist,
    Artist,
    Album,
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

    [ObservableProperty]
    public partial string Glyph { get; set; } = "";

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
