using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 小窗（迷你播放器）的开关。**单例**，标题栏那颗按钮与小窗共用同一份。
/// </summary>
/// <remarks>
/// <para>
/// <b>开关状态不落盘。</b> 上次退出时开着，不代表这次启动就该在屏幕角上冒出一个浮窗 ——
/// 那属于「没让它出现它自己出现了」。位置则每次记住（那是用户摆的）。
/// </para>
/// <para>
/// <b>这里不碰窗口。</b> 窗口的创建与显隐归 <c>MiniPlayerWindowHost</c>，
/// 它订阅本对象的 <see cref="IsEnabled"/>。这样这个 ViewModel 能脱离窗口单测，
/// 也不会与 <c>MainWindow</c> 形成依赖环。
/// </para>
/// <para>
/// 播放状态、收藏、音量一概不在这里 —— 小窗直接注入那几个现成的单例 ViewModel。
/// </para>
/// </remarks>
public sealed partial class MiniPlayerViewModel : ObservableObject
{
    private readonly LyricsViewModel _lyrics;

    public MiniPlayerViewModel(LyricsViewModel lyrics)
    {
        ArgumentNullException.ThrowIfNull(lyrics);

        _lyrics = lyrics;
    }

    /// <summary>小窗是否开着。标题栏那颗按钮绑的就是它。</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>标题栏那颗按钮的命令：开→关，关→开。</summary>
    [RelayCommand]
    private void Toggle() => IsEnabled = !IsEnabled;

    /// <summary>小窗上的 ✕ 与 ▢ 都调它：关掉窗口，同时让标题栏那颗按钮回到未激活。</summary>
    [RelayCommand]
    private void Dismiss() => IsEnabled = false;

    /// <summary>
    /// 小窗也是歌词的消费者：它在播放时那一行位置显示当前歌词，所以打开时要触发取词。
    /// </summary>
    /// <remarks>
    /// 与 <c>DesktopLyricsViewModel</c> 同一手法 —— <c>LyricsViewModel</c> 把
    /// <c>IsMiniPlayerOpen</c> 作为与歌词页、桌面歌词并列的第三个取词来源。
    /// </remarks>
    partial void OnIsEnabledChanged(bool value) => _lyrics.IsMiniPlayerOpen = value;
}
