using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.UI;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 桌面歌词条的开关与外观偏好。**单例**，播放条那颗按钮与歌词窗共用同一份。
/// </summary>
/// <remarks>
/// <para>
/// <b>开关状态不落盘。</b> 上次退出时开着，不代表这次启动就要在桌面上冒出一个悬浮条 ——
/// 那属于「没让它出现它自己出现了」。字号颜色这些则每次都记住。
/// </para>
/// <para>
/// 设置改了立刻写盘，与 <see cref="ThemeViewModel.Select"/> 同一条规矩：设置项就这几个，
/// 攒着没有收益，反而多一条「进程被杀就丢设置」的路径。
/// </para>
/// <para>
/// <b>这里不碰窗口。</b> 窗口的创建与显隐归 <c>DesktopLyricsWindowHost</c>，
/// 它订阅本对象的 <see cref="IsEnabled"/>。这样这个 ViewModel 能脱离窗口单测。
/// </para>
/// </remarks>
public sealed partial class DesktopLyricsViewModel : ObservableObject
{
    private readonly IDesktopLyricsSettingsStore _store;
    private readonly LyricsViewModel _lyrics;
    private double _fontSize;
    private Color _textColor;
    private bool _loading = true;

    public DesktopLyricsViewModel(IDesktopLyricsSettingsStore store, LyricsViewModel lyrics)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(lyrics);

        _store = store;
        _lyrics = lyrics;

        var settings = store.Load().Normalized();

        // 开关也要恢复：上次退出时开着，这次启动就自动出现。
        IsEnabled = settings.IsEnabled;
        FontSize = settings.FontSize;
        TextColor = ToColor(settings.TextColorArgb);
        DualLine = settings.DualLine;
        Alignment = settings.Alignment;
        Locked = settings.Locked;
        PassThrough = settings.PassThrough;

        // 装载期间不许把刚读出来的值再写回去。
        _loading = false;
    }

    /// <summary>桌面歌词条是否开着。播放条那颗按钮绑的就是它。</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>
    /// 字号，DIP。
    /// </summary>
    /// <remarks>
    /// 手写而不是用 <c>[ObservableProperty]</c>：要在赋值那一刻就把越界值夹住。
    /// 工具箱这个版本不生成 <c>OnXxxChanging(ref T)</c> 那种能改写入参的钩子，
    /// 而「先存进去再改回来」会多播一次变更通知给已经按错值量过一遍的控件。
    /// </remarks>
    public double FontSize
    {
        get => _fontSize;
        set
        {
            var clamped = double.IsFinite(value)
                ? Math.Clamp(value, DesktopLyricsSettings.MinimumFontSize, DesktopLyricsSettings.MaximumFontSize)
                : DesktopLyricsSettings.DefaultFontSize;

            if (SetProperty(ref _fontSize, clamped))
            {
                Persist();
            }
        }
    }

    /// <summary>已唱部分的高亮色；未唱部分保持不透明白色。</summary>
    public Color TextColor
    {
        get => _textColor;
        set
        {
            var normalized = new DesktopLyricsSettings { TextColorArgb = ToArgb(value) }.Normalized();
            if (SetProperty(ref _textColor, ToColor(normalized.TextColorArgb))) Persist();
        }
    }

    /// <summary>是否显示下一句。</summary>
    [ObservableProperty]
    public partial bool DualLine { get; set; }

    /// <summary>双行时的对齐方式。</summary>
    [ObservableProperty]
    public partial DesktopLyricsAlignment Alignment { get; set; }

    /// <summary>是否锁定：锁上后不能拖动，也不能拉伸宽度。</summary>
    [ObservableProperty]
    public partial bool Locked { get; set; }

    /// <summary>透明处是否让鼠标穿透。关掉之后整个窗口都可抓，方便摆位置。</summary>
    [ObservableProperty]
    public partial bool PassThrough { get; set; }

    /// <summary>播放条那颗按钮的命令：开→关，关→开。</summary>
    [RelayCommand]
    private void Toggle() => IsEnabled = !IsEnabled;

    /// <summary>歌词窗上的 ✕ 调它：关掉窗口，同时让播放条那颗按钮回到未激活。</summary>
    [RelayCommand]
    private void Dismiss() => IsEnabled = false;

    /// <summary>恢复默认。</summary>
    [RelayCommand]
    private void Reset()
    {
        var settings = DesktopLyricsSettings.Default;

        FontSize = settings.FontSize;
        TextColor = ToColor(settings.TextColorArgb);
        DualLine = settings.DualLine;
        Alignment = settings.Alignment;
        Locked = settings.Locked;
    }

    /// <remarks>
    /// 除了通知歌词页「桌面歌词开着」，还要落盘 —— 开关是「当前状态」，重启后要还原来那一份。
    /// </remarks>
    partial void OnIsEnabledChanged(bool value)
    {
        _lyrics.IsDesktopLyricsOpen = value;
        Persist();
    }

    partial void OnDualLineChanged(bool value) => Persist();

    partial void OnAlignmentChanged(DesktopLyricsAlignment value) => Persist();

    partial void OnLockedChanged(bool value) => Persist();

    partial void OnPassThroughChanged(bool value) => Persist();

    private void Persist()
    {
        if (_loading)
        {
            return;
        }

        _store.Save(new DesktopLyricsSettings
        {
            IsEnabled = IsEnabled,
            FontSize = FontSize,
            TextColorArgb = ToArgb(TextColor),
            DualLine = DualLine,
            Alignment = Alignment,
            Locked = Locked,
            PassThrough = PassThrough,
        });
    }

    private static Color ToColor(uint argb) => Color.FromArgb(
        (byte)(argb >> 24),
        (byte)(argb >> 16),
        (byte)(argb >> 8),
        (byte)argb);

    private static uint ToArgb(Color color)
        => ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
}
