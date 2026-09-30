using CommunityToolkit.Mvvm.ComponentModel;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 歌词面板上的一行。
/// </summary>
/// <remarks>
/// 只带界面要用的两样东西：文本与「是不是当前行」。<b>不给逐字高亮留位置</b> ——
/// 那是 P5 的 Win2D 渲染器的事，这个面板到时会被整个替换掉。
/// </remarks>
public sealed partial class LyricLineView(TimeSpan start, string text) : ObservableObject
{
    /// <summary>这一行的绝对起始时间。点行跳转用。</summary>
    public TimeSpan Start { get; } = start;

    public string Text { get; } = text;

    /// <summary>是不是正在唱的那一行。</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}
