using Bowen.Core.Models;
using Bowen.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.UI;

namespace Bowen.WinUI.ViewModels;

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
        ShellBackgroundViewModel shellBackground,
        PlayerViewModel player,
        LyricsViewModel lyrics,
        DesktopLyricsViewModel desktopLyrics,
        FontSettingsViewModel font,
        ViewModeService viewMode,
        PlaybackCoordinator coordinator,
        ShortcutSettingsViewModel shortcuts,
        StorageSettingsViewModel storage,
        AboutViewModel about,
        UpdateViewModel update,
        NetworkSettingsViewModel network,
        InputMethodSettingsViewModel inputMethod,
        RestoreQueueSettingsViewModel restoreQueue)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(shellBackground);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(desktopLyrics);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(viewMode);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(about);
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(inputMethod);
        ArgumentNullException.ThrowIfNull(restoreQueue);

        Theme = theme;
        ShellBackground = shellBackground;
        Player = player;
        Lyrics = lyrics;
        DesktopLyrics = desktopLyrics;
        Font = font;
        ViewMode = viewMode;
        Coordinator = coordinator;
        Shortcuts = shortcuts;
        Storage = storage;
        About = about;
        Update = update;
        Network = network;
        InputMethod = inputMethod;
        RestoreQueue = restoreQueue;

        Categories =
        [
            new SettingsCategoryItem("外观", "IconThemeAuto"),
            new SettingsCategoryItem("播放", "IconPlay"),
            new SettingsCategoryItem("歌词", "IconLyrics"),
            new SettingsCategoryItem("网络", "IconGlobe"),
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

        // 顺序必须与 StartupSections 一一对应。默认是「我喜欢的」（StartupSection 的默认值），
        // 不是列表首项。
        StartupPageOptions = ["发现", "排行榜", "乐库", "我喜欢的", "最近播放", "收藏的专辑", "收藏的歌单", "关注的歌手"];

        // 三个字体下拉共用同一份列表：首项固定「默认字体」，其余按字形覆盖分组。
        var fontOptions = new List<FontOption> { FontOption.Default };
        fontOptions.AddRange(Font.Fonts);
        FontOptions = fontOptions;

        SyncFromSources();
    }

    private static readonly AudioQuality[] Qualities =
        [AudioQuality.Standard, AudioQuality.High, AudioQuality.Lossless];

    private static readonly PlayMode[] PlayModes =
        [PlayMode.Sequential, PlayMode.ListLoop, PlayMode.SingleLoop, PlayMode.Shuffle];

    /// <summary>下拉框下标到启动落点的映射，顺序与 <see cref="StartupPageOptions"/> 一致。</summary>
    private static readonly StartupSection[] StartupSections =
    [
        StartupSection.Discover,
        StartupSection.Bangs,
        StartupSection.Library,
        StartupSection.Favorites,
        StartupSection.Recent,
        StartupSection.CollectedAlbums,
        StartupSection.CollectedPlaylists,
        StartupSection.FollowedArtists,
    ];

    /// <summary>左侧分类栏的项。顺序即下标，与下面那六个 <c>IsXxxSelected</c> 一一对应。</summary>
    public IReadOnlyList<SettingsCategoryItem> Categories { get; }

    public IReadOnlyList<string> ThemeOptions { get; }

    public IReadOnlyList<string> QualityOptions { get; }

    public IReadOnlyList<string> PlayModeOptions { get; }

    public IReadOnlyList<string> ColorOptions { get; }

    public IReadOnlyList<string> AlignmentOptions { get; }

    public IReadOnlyList<string> ListDisplayOptions { get; }

    /// <summary>「默认打开的页面」下拉框的选项，顺序与 <see cref="StartupSections"/> 一致。</summary>
    public IReadOnlyList<string> StartupPageOptions { get; }

    /// <summary>界面 / 全屏歌词 / 桌面歌词三个字体下拉共用的选项；首项是「默认字体」。</summary>
    public IReadOnlyList<FontOption> FontOptions { get; }

    public ThemeViewModel Theme { get; }

    /// <summary>主窗口外壳背景。改模式、纯色或跟随封面都立刻生效并落盘。</summary>
    public ShellBackgroundViewModel ShellBackground { get; }

    public PlayerViewModel Player { get; }

    public LyricsViewModel Lyrics { get; }

    public DesktopLyricsViewModel DesktopLyrics { get; }

    /// <summary>界面字体分区的状态源。它的 <c>Select</c> 负责落盘并立刻应用。</summary>
    public FontSettingsViewModel Font { get; }

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

    /// <summary>关于分区里不随更新流程变化的那部分（图标、版本号、声明、两个外链）。</summary>
    public AboutViewModel About { get; }

    /// <summary>
    /// 检查更新的状态与动作。<b>与「关于」分开</b>：它是**全应用唯一一份** ——
    /// 启动时的自动检查、关于页那颗按钮、下载与取消都走它，所以不能是这一页私有的。
    /// </summary>
    public UpdateViewModel Update { get; }

    /// <summary>网络分区：更新通道的代理。</summary>
    public NetworkSettingsViewModel Network { get; }

    public InputMethodSettingsViewModel InputMethod { get; }

    /// <summary>「记住播放列表」开关。薄壳，真值在 <see cref="Coordinator"/> 里。</summary>
    public RestoreQueueSettingsViewModel RestoreQueue { get; }

    /// <remarks>
    /// <b>下标与 <see cref="Categories"/> 的顺序一一对应，改顺序就得改下面那一组 <c>IsXxxSelected</c>。</b>
    /// 2026-10-14 插入「网络」时后面四条的编号整体后移过一次 —— 漏改一个的表现是「点甲显示乙」。
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppearanceSelected))]
    [NotifyPropertyChangedFor(nameof(IsPlaybackSelected))]
    [NotifyPropertyChangedFor(nameof(IsLyricsSelected))]
    [NotifyPropertyChangedFor(nameof(IsNetworkSelected))]
    [NotifyPropertyChangedFor(nameof(IsShortcutsSelected))]
    [NotifyPropertyChangedFor(nameof(IsStorageSelected))]
    [NotifyPropertyChangedFor(nameof(IsAboutSelected))]
    public partial int SelectedCategoryIndex { get; set; }

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial int StartupPageIndex { get; set; }

    [ObservableProperty]
    public partial int QualityIndex { get; set; }

    [ObservableProperty]
    public partial int PlayModeIndex { get; set; }

    [ObservableProperty]
    public partial int DesktopLyricsColorIndex { get; set; }

    [ObservableProperty]
    public partial int DesktopLyricsAlignmentIndex { get; set; }

    [ObservableProperty]
    public partial int AppFontIndex { get; set; }

    [ObservableProperty]
    public partial int LyricsFontIndex { get; set; }

    [ObservableProperty]
    public partial int MiniPlayerLyricsFontIndex { get; set; }

    [ObservableProperty]
    public partial int DesktopLyricsFontIndex { get; set; }

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

    public bool IsNetworkSelected => SelectedCategoryIndex == 3;

    public bool IsShortcutsSelected => SelectedCategoryIndex == 4;

    public bool IsStorageSelected => SelectedCategoryIndex == 5;

    public bool IsAboutSelected => SelectedCategoryIndex == 6;

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
            StartupPageIndex = Math.Max(0, Array.IndexOf(StartupSections, Theme.StartupPage));

            // 读的是「默认音质」这个偏好，不是正在播的那首当前用的档位 —— 两者在切换后会不一致。
            QualityIndex = (int)Coordinator.PreferredQuality;

            // 下标取自数组顺序，不直接转枚举值 —— 枚举的数值顺序不一定要和界面选项顺序一致。
            PlayModeIndex = Math.Max(0, Array.IndexOf(PlayModes, Player.Mode));
            DesktopLyricsColorIndex = PaletteIndexOf(DesktopLyrics.TextColor);
            DesktopLyricsAlignmentIndex = (int)DesktopLyrics.Alignment;

            // 字体下标：首项「默认字体」是 0，往后 1 起对应 Font.Fonts 的下标 + 1。
            AppFontIndex = FontIndexOf(Font.CurrentFontFamily);
            LyricsFontIndex = FontIndexOf(Lyrics.FontFamily);
            MiniPlayerLyricsFontIndex = FontIndexOf(Lyrics.MiniPlayerFontFamily);
            DesktopLyricsFontIndex = FontIndexOf(DesktopLyrics.FontFamily);

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

    partial void OnStartupPageIndexChanged(int value)
    {
        if (_syncing || value < 0 || value >= StartupSections.Length)
        {
            return;
        }

        Theme.StartupPage = StartupSections[value];
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
        if (_syncing || value < 0 || value >= PlayModes.Length)
        {
            return;
        }

        Player.SetPlayModeCommand.Execute(PlayModes[value]);
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

    partial void OnAppFontIndexChanged(int value)
    {
        if (_syncing || value < 0) return;
        if (Font.Select(FontNameAt(value))) return;

        // 保存失败：把下拉恢复到已保存的选择。
        _syncing = true;
        AppFontIndex = FontIndexOf(Font.CurrentFontFamily);
        _syncing = false;
    }

    partial void OnLyricsFontIndexChanged(int value)
    {
        if (_syncing || value < 0) return;
        Lyrics.SetFontFamily(FontNameAt(value));
    }

    partial void OnMiniPlayerLyricsFontIndexChanged(int value)
    {
        if (_syncing || value < 0) return;
        Lyrics.SetMiniPlayerFontFamily(FontNameAt(value));
    }

    partial void OnDesktopLyricsFontIndexChanged(int value)
    {
        if (_syncing || value < 0) return;
        DesktopLyrics.FontFamily = FontNameAt(value);
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

    /// <summary>字体名下标：0 = 默认，往后对应 <see cref="FontOptions"/> 下标 + 1。</summary>
    private int FontIndexOf(string fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily)) return 0;
        for (var i = 1; i < FontOptions.Count; i++)
        {
            var option = FontOptions[i];
            if (string.Equals(option.FamilyName, fontFamily, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option.CanonicalName, fontFamily, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return 0;
    }

    /// <summary>下标换回字体名；越界或 0 一律回空（默认）。</summary>
    private string FontNameAt(int index)
        => index >= 1 && index < FontOptions.Count ? FontOptions[index].FamilyName : string.Empty;

    private static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
