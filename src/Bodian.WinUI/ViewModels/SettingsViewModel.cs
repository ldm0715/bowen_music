using Bodian.Core.Models;
using Bodian.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.UI;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 设置页。
/// </summary>
/// <remarks>
/// <para>
/// 页面左侧是分类栏、右侧是当前分类的内容。分类切换**走页内状态而不是导航栈**：
/// <c>MainWindow.SyncSelection</c> 按根页类型反查侧栏高亮，压栈会让返回键变成
/// 「回上一个分类」，而分类本来是视图状态。见 <c>docs/settings.md</c>。
/// </para>
/// <para>
/// <b>本类不持有任何设置的真实状态</b>：主题、播放、歌词、桌面歌词各有一个单例
/// ViewModel，这里只把它们的值折成下拉框要的下标，写回也走它们各自的入口
/// （<see cref="ThemeViewModel.Select"/>、<see cref="PlayerViewModel.SwitchQualityAsync"/> 等），
/// 因此标题栏那颗主题按钮、播放条的音质菜单、桌面歌词窗口内的面板与这一页永远一致。
/// </para>
/// <para>
/// <b>为什么这些是下标的转发属性而不是直接双向绑枚举</b>：<c>ComboBox</c> 只认
/// <c>SelectedIndex</c>，而主题与播放模式在各自 ViewModel 里都是只读的
/// （写入口是有副作用的命令或方法，直接写属性不会真的切歌/切模式）。
/// </para>
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>正在把各单例的值同步进下标属性。同步期间不写回，否则会自激。</summary>
    private bool _syncing;

    public SettingsViewModel(
        ThemeViewModel theme,
        PlayerViewModel player,
        LyricsViewModel lyrics,
        DesktopLyricsViewModel desktopLyrics,
        ViewModeService viewMode,
        PlaybackCoordinator coordinator,
        ShortcutSettingsViewModel shortcuts,
        StorageSettingsViewModel storage,
        AboutViewModel about)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(desktopLyrics);
        ArgumentNullException.ThrowIfNull(viewMode);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(about);

        Theme = theme;
        Player = player;
        Lyrics = lyrics;
        DesktopLyrics = desktopLyrics;
        ViewMode = viewMode;
        Coordinator = coordinator;
        Shortcuts = shortcuts;
        Storage = storage;
        About = about;

        Categories =
        [
            new SettingsCategoryItem("外观", "IconThemeAuto"),
            new SettingsCategoryItem("播放", "IconPlay"),
            new SettingsCategoryItem("歌词", "IconLyrics"),
            new SettingsCategoryItem("快捷键", "IconKeyboard"),
            new SettingsCategoryItem("存储", "IconStorage"),
            new SettingsCategoryItem("关于", "IconInfo"),
        ];

        // 选项文案一律取自各自的枚举，不在 XAML 里再抄一份 —— 抄了就会和托盘菜单、
        // 播放条音质菜单的措辞慢慢走偏。
        ThemeOptions = ["跟随系统", "浅色", "深色"];
        QualityOptions = [.. Qualities.Select(AudioQualityTable.DisplayName)];
        PlayModeOptions = [.. PlayModes.Select(mode => mode.DisplayName())];
        ColorOptions = [.. DesktopLyricsSettings.Palette.Select(entry => entry.Name)];
        AlignmentOptions = ["两句都居中", "上行居左 · 下行居右"];

        // 选项本身按「读起来就是当前状态」写，不再用「列表显示方式」这种带默认暗示的开关文案。
        ListDisplayOptions = ["行列表", "封面卡片"];

        SyncFromSources();
    }

    private static readonly AudioQuality[] Qualities =
        [AudioQuality.Standard, AudioQuality.High, AudioQuality.Lossless];

    private static readonly PlayMode[] PlayModes =
        [PlayMode.Sequential, PlayMode.ListLoop, PlayMode.Shuffle];

    /// <summary>左侧分类栏的项。顺序即下标，与下面那六个 <c>IsXxxSelected</c> 一一对应。</summary>
    public IReadOnlyList<SettingsCategoryItem> Categories { get; }

    public IReadOnlyList<string> ThemeOptions { get; }

    public IReadOnlyList<string> QualityOptions { get; }

    public IReadOnlyList<string> PlayModeOptions { get; }

    public IReadOnlyList<string> ColorOptions { get; }

    public IReadOnlyList<string> AlignmentOptions { get; }

    public IReadOnlyList<string> ListDisplayOptions { get; }

    public ThemeViewModel Theme { get; }

    public PlayerViewModel Player { get; }

    public LyricsViewModel Lyrics { get; }

    public DesktopLyricsViewModel DesktopLyrics { get; }

    /// <summary>
    /// 「用封面卡片还是行列表」这一个的状态源。它的 setter 自己落盘并发通知。
    /// </summary>
    public ViewModeService ViewMode { get; }

    /// <summary>
    /// 音质偏好（<c>PreferredQuality</c>）的持有者。**设置页读写的这个，不是当前播放那首的档位。**
    /// </summary>
    public PlaybackCoordinator Coordinator { get; }

    /// <summary>快捷键分区。键位表在它持有的单例服务里。</summary>
    public ShortcutSettingsViewModel Shortcuts { get; }

    /// <summary>存储分区。</summary>
    public StorageSettingsViewModel Storage { get; }

    /// <summary>关于分区。</summary>
    public AboutViewModel About { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppearanceSelected))]
    [NotifyPropertyChangedFor(nameof(IsPlaybackSelected))]
    [NotifyPropertyChangedFor(nameof(IsLyricsSelected))]
    [NotifyPropertyChangedFor(nameof(IsShortcutsSelected))]
    [NotifyPropertyChangedFor(nameof(IsStorageSelected))]
    [NotifyPropertyChangedFor(nameof(IsAboutSelected))]
    public partial int SelectedCategoryIndex { get; set; }

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial int QualityIndex { get; set; }

    [ObservableProperty]
    public partial int PlayModeIndex { get; set; }

    [ObservableProperty]
    public partial int DesktopLyricsColorIndex { get; set; }

    [ObservableProperty]
    public partial int DesktopLyricsAlignmentIndex { get; set; }

    /// <summary>
    /// 列表 / 卡片，给下拉框用的下标。<b>0 = 行列表，1 = 封面卡片</b>（枚举顺序即下标）。
    /// </summary>
    /// <remarks>
    /// 手写而不是 <c>[ObservableProperty]</c>：真实状态在 <see cref="ViewModeService"/> 里，
    /// 这里只是一个读它、写它的窗口，另存一份字段就会多出一处要同步的东西。
    /// </remarks>
    public int ListDisplayIndex
    {
        get => ViewMode.DefaultUseGrid ? 1 : 0;
        set
        {
            if (_syncing)
            {
                return;
            }

            ViewMode.DefaultUseGrid = value == 1;
        }
    }

    /// <summary>
    /// 歌词译文默认开关。<b>读写的是默认值</b>（<see cref="LyricsViewModel.DefaultShowTranslation"/>），
    /// 不是歌词页当前这一次显示不显示。
    /// </summary>
    public bool DefaultShowTranslation
    {
        get => Lyrics.DefaultShowTranslation;
        set
        {
            if (_syncing)
            {
                return;
            }

            Lyrics.SetDefaultShowTranslation(value);
        }
    }

    /// <summary>桌面歌词那一组设置项是否可用：总开关关掉时整组置灰。</summary>
    public bool DesktopLyricsRowsEnabled => DesktopLyrics.IsEnabled;

    /// <summary>双行对齐是否可用：没开双行时这一项没有意义。</summary>
    public bool DesktopLyricsAlignmentEnabled => DesktopLyrics.IsEnabled && DesktopLyrics.DualLine;

    public bool IsAppearanceSelected => SelectedCategoryIndex == 0;

    public bool IsPlaybackSelected => SelectedCategoryIndex == 1;

    public bool IsLyricsSelected => SelectedCategoryIndex == 2;

    public bool IsShortcutsSelected => SelectedCategoryIndex == 3;

    public bool IsStorageSelected => SelectedCategoryIndex == 4;

    public bool IsAboutSelected => SelectedCategoryIndex == 5;

    /// <summary>
    /// 把各个单例的当前值同步进下标属性。页面进入时调一次，各单例在外面被改动时也调
    /// （见 <c>SettingsPage</c> 里那组订阅）。
    /// </summary>
    public void SyncFromSources()
    {
        _syncing = true;
        try
        {
            ThemeIndex = (int)Theme.Current;

            // 读的是「默认音质」这个偏好，不是正在播的那首当前用的档位 —— 两者在切换后会不一致。
            QualityIndex = (int)Coordinator.PreferredQuality;

            PlayModeIndex = (int)Player.Mode;
            DesktopLyricsColorIndex = PaletteIndexOf(DesktopLyrics.TextColor);
            DesktopLyricsAlignmentIndex = (int)DesktopLyrics.Alignment;

            // 这几个是从别的对象算出来的，不跟着上面那批走，得自己喊一声。
            OnPropertyChanged(nameof(ListDisplayIndex));
            OnPropertyChanged(nameof(DefaultShowTranslation));
            OnPropertyChanged(nameof(DesktopLyricsRowsEnabled));
            OnPropertyChanged(nameof(DesktopLyricsAlignmentEnabled));
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// 分类栏是否已收成图标轨。由页面按自身宽度判定后推送 —— 阈值是布局量，不是业务状态。
    /// </summary>
    public void SetRailCompact(bool compact)
    {
        foreach (var category in Categories)
        {
            category.ShowLabel = !compact;
        }
    }

    /// <summary>页面每次进入时调一次：重新对齐各单例的当前值。</summary>
    /// <remarks>
    /// 根页实例被 <see cref="Services.NavigationService"/> 缓存，重新进入时不一定重建实例，
    /// 所以带副作用的初始化不能只放在构造函数里。
    /// </remarks>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        SyncFromSources();

        // 键位可能在别处被改过（恢复默认、上一次进设置页录的），每次进来重新对一遍文案。
        Shortcuts.Refresh();

        // 存储占用每次进来重量一次：用过一段时间之后那几项是会长的。
        await Storage.MeasureAsync(cancellationToken).ConfigureAwait(true);
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_syncing || value is < 0 or > 2)
        {
            return;
        }

        Theme.Select((AppTheme)value);
    }

    partial void OnQualityIndexChanged(int value)
    {
        if (_syncing || value is < 0 or > 2)
        {
            return;
        }

        // 只改「默认音质」这个偏好，不动正在播的那首 —— 设置页给的是默认值，
        // 不是「立刻切档位」那个动作（后者是播放条那颗音质菜单）。
        Coordinator.SetPreferredQuality((AudioQuality)value);
    }

    partial void OnPlayModeIndexChanged(int value)
    {
        if (_syncing || value is < 0 or > 2)
        {
            return;
        }

        Player.SetPlayModeCommand.Execute((PlayMode)value);
    }

    partial void OnDesktopLyricsColorIndexChanged(int value)
    {
        if (_syncing || value < 0 || value >= DesktopLyricsSettings.Palette.Length)
        {
            return;
        }

        DesktopLyrics.TextColor = ToColor(DesktopLyricsSettings.Palette[value].Argb);
    }

    partial void OnDesktopLyricsAlignmentIndexChanged(int value)
    {
        if (_syncing || value is < 0 or > 1)
        {
            return;
        }

        DesktopLyrics.Alignment = (DesktopLyricsAlignment)value;
    }

    private static int PaletteIndexOf(Color color)
    {
        var argb = (uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B);
        for (var index = 0; index < DesktopLyricsSettings.Palette.Length; index++)
        {
            if (DesktopLyricsSettings.Palette[index].Argb == argb)
            {
                return index;
            }
        }

        return -1;
    }

    private static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
