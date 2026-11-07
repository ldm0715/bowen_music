using System.ComponentModel;
using Bowen.Core.Models;
using Bowen.WinUI.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 托盘「效率面板」的内容：正在播放、转台、音量、主题、音效/音质/队列、底部动作。
/// </summary>
/// <remarks>
/// <para>
/// <b>尺寸固定</b>：托盘库的 SecondWindow 只在弹出那一刻量一次窗口，所以根元素写死宽高，
/// 三页内容共用同一块固定高区域。改高度时要同步改 <see cref="MainWindow"/> 里
/// <c>TrayPanelItemStyle</c> 的 <c>MinHeight</c>，否则库强写的 <c>Height=32</c> 会裁掉内容。
/// </para>
/// <para>
/// <b>面板内不许再开 Flyout</b>：SecondWindow 是独立窗口，二级弹层会让它失活并被收起。
/// 音效、音质、队列因此全部内联。
/// </para>
/// </remarks>
public sealed partial class TrayControlPanel : UserControl
{
    public TrayControlPanel()
    {
        InitializeComponent();

        // 托盘库在面板这一项上挂了「Tapped 即收起」。子控件（按钮、滑块、列表项）
        // 可能先一步把事件标成 Handled，模板根上的处理器就收不到；这里用
        // handledEventsToo 再兜一层，保证事件不会冒到菜单项那一层。
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPanelPointerPressed), true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPanelPointerPressed), true);
        AddHandler(TappedEvent, new TappedEventHandler(OnPanelTapped), true);

        Loaded += (_, _) =>
        {
            Bindings.Update();
            ApplyHostTheme();
            // 面板可能上次就关在队列页：重新打开时不会走 OnQueueTabClick，
            // 这里补一次队列状态与当前行的定位。
            if (IsQueueTab) ShowQueue();
        };

        // 面板里的主题切换由 x:Bind 改内容；托盘库那个 presenter 的亚克力背景
        // 不在我们的树里，只能按类名找到它再设一次主题。
        ActualThemeChanged += (_, _) => ApplyHostTheme();

        // 面板收起后不再让队列为这块不可见区域重建。
        Unloaded += (_, _) => SetQueueTabActive(false);
    }

    private void OnPanelPointerPressed(object sender, PointerRoutedEventArgs e) =>
        e.Handled = true;

    private void OnPanelTapped(object sender, TappedRoutedEventArgs e) =>
        e.Handled = true;

    public static readonly DependencyProperty PlayerProperty = DependencyProperty.Register(
        nameof(Player),
        typeof(PlayerViewModel),
        typeof(TrayControlPanel),
        new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty EffectsProperty = DependencyProperty.Register(
        nameof(Effects),
        typeof(AudioEffectsViewModel),
        typeof(TrayControlPanel),
        new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty QueueProperty = DependencyProperty.Register(
        nameof(Queue),
        typeof(PlayQueueViewModel),
        typeof(TrayControlPanel),
        new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme),
        typeof(ThemeViewModel),
        typeof(TrayControlPanel),
        new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty MiniPlayerProperty = DependencyProperty.Register(
        nameof(MiniPlayer),
        typeof(MiniPlayerViewModel),
        typeof(TrayControlPanel),
        new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty DesktopLyricsProperty = DependencyProperty.Register(
        nameof(DesktopLyrics),
        typeof(DesktopLyricsViewModel),
        typeof(TrayControlPanel),
        new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty PanelTabProperty = DependencyProperty.Register(
        nameof(PanelTab),
        typeof(int),
        typeof(TrayControlPanel),
        new PropertyMetadata(0, OnInputChanged));

    private static void OnInputChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var panel = (TrayControlPanel)sender;

        if (args.Property == ThemeProperty)
        {
            panel.ObserveTheme(args.NewValue as ThemeViewModel);
        }

        // 这些绑定默认是 OneTime：依赖属性换了实例或页签变了，必须手动重算一次。
        panel.Bindings.Update();
    }

    private ThemeViewModel? _observedTheme;

    private void ObserveTheme(ThemeViewModel? theme)
    {
        if (_observedTheme is not null)
        {
            _observedTheme.PropertyChanged -= OnThemePropertyChanged;
        }

        _observedTheme = theme;

        if (theme is not null)
        {
            theme.PropertyChanged += OnThemePropertyChanged;
        }
    }

    private void OnThemePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeViewModel.ActualTheme))
        {
            // 等 x:Bind 先把内容换完，再处理外面那个 presenter。
            DispatcherQueue.TryEnqueue(ApplyHostTheme);
        }
    }

    /// <summary>
    /// 把托盘库自建的 <see cref="MenuFlyoutPresenter"/> 的主题对齐到外壳实际主题。
    /// </summary>
    /// <remarks>
    /// <b>为什么必须往上找 presenter</b>：SecondWindow 的亚克力底是库自己创建的
    /// <c>MenuFlyout</c> presenter 画的，不在本控件的树里；只给内容设 RequestedTheme
    /// 会让文字/控件变浅色，而背景仍是库按旧主题画的，于是看起来像蒙了一层膜。
    /// </remarks>
    private void ApplyHostTheme()
    {
        if (Theme is not { } theme)
        {
            return;
        }

        for (var node = VisualTreeHelper.GetParent(this);
             node is not null;
             node = VisualTreeHelper.GetParent(node))
        {
            if (node is MenuFlyoutPresenter presenter)
            {
                presenter.RequestedTheme = theme.ActualTheme;
                return;
            }
        }
    }

    public PlayerViewModel? Player
    {
        get => (PlayerViewModel?)GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    public AudioEffectsViewModel? Effects
    {
        get => (AudioEffectsViewModel?)GetValue(EffectsProperty);
        set => SetValue(EffectsProperty, value);
    }

    public PlayQueueViewModel? Queue
    {
        get => (PlayQueueViewModel?)GetValue(QueueProperty);
        set => SetValue(QueueProperty, value);
    }

    public ThemeViewModel? Theme
    {
        get => (ThemeViewModel?)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public MiniPlayerViewModel? MiniPlayer
    {
        get => (MiniPlayerViewModel?)GetValue(MiniPlayerProperty);
        set => SetValue(MiniPlayerProperty, value);
    }

    public DesktopLyricsViewModel? DesktopLyrics
    {
        get => (DesktopLyricsViewModel?)GetValue(DesktopLyricsProperty);
        set => SetValue(DesktopLyricsProperty, value);
    }

    /// <summary>0 音效 / 1 播放列表。</summary>
    public int PanelTab
    {
        get => (int)GetValue(PanelTabProperty);
        set => SetValue(PanelTabProperty, value);
    }

    public bool IsEffectsTab => PanelTab == 0;

    public bool IsQueueTab => PanelTab == 1;

    /// <summary>设置要把主窗口带回前台，由宿主执行现有的 OpenSettings。</summary>
    public event EventHandler? SettingsRequested;

    public event EventHandler? RestartRequested;

    public event EventHandler? ExitRequested;

    private void OnEffectsTabClick(object sender, RoutedEventArgs e)
    {
        SetQueueTabActive(false);
        PanelTab = 0;
    }

    private void OnQueueTabClick(object sender, RoutedEventArgs e)
    {
        PanelTab = 1;
        ShowQueue();
    }

    /// <summary>
    /// 队列页显示出来：同步「面板开着」状态，并把当前播放那一行滚进可视区。
    /// </summary>
    /// <remarks>
    /// 面板没有走主窗口抽屉的 <c>IsOpen</c>，必须单独告知队列「这里也开着」，
    /// 否则 <c>Rows</c> 不会随切歌刷新。切回本页时开关可能已经是 true，再强制补一次。
    /// </remarks>
    private void ShowQueue()
    {
        if (Queue is { } queue)
        {
            queue.IsTrayPanelOpen = true;
            queue.RefreshNow();
        }

        RevealCurrentRow();
    }

    /// <summary>
    /// 把正在播放那一行滚进可视区。
    /// </summary>
    /// <remarks>
    /// 页签刚切过来时列表还是 Collapsed，容器也还没生成，直接 <c>ScrollIntoView</c>
    /// 会落空。排到下一轮布局之后再滚；行对象在 <c>RefreshNow</c> 之后取，避免拿到被重建掉的旧行。
    /// </remarks>
    private void RevealCurrentRow()
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!IsQueueTab || Queue is not { } queue)
            {
                return;
            }

            foreach (var row in queue.Rows)
            {
                if (row.IsCurrent)
                {
                    // 页签刚切换时可视树还没走到这一页，先把布局赶出来，行容器才生成得出来。
                    QueueList.UpdateLayout();
                    QueueList.ScrollIntoView(row, ScrollIntoViewAlignment.Leading);
                    return;
                }
            }
        });
    }

    private void SetQueueTabActive(bool active)
    {
        if (Queue is { } queue)
        {
            queue.IsTrayPanelOpen = active;
        }
    }

    private void OnFavoriteClick(object sender, RoutedEventArgs e) =>
        Player?.ToggleFavoriteCommand.Execute(null);

    private void OnMuteClick(object sender, RoutedEventArgs e) => Player?.ToggleMute();

    private void OnThemeSystemClick(object sender, RoutedEventArgs e) => Theme?.Select(AppTheme.System);

    private void OnThemeLightClick(object sender, RoutedEventArgs e) => Theme?.Select(AppTheme.Light);

    private void OnThemeDarkClick(object sender, RoutedEventArgs e) => Theme?.Select(AppTheme.Dark);

    private void OnEffectsClassicClick(object sender, RoutedEventArgs e) =>
        Effects?.SelectedTabIndex = 0;

    private void OnEffectsEnhancedClick(object sender, RoutedEventArgs e) =>
        Effects?.SelectedTabIndex = 1;

    private void OnPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectedPreset 回写也会走到这里；已经选中的那一项不要再执行一次，
        // SelectPreset 的语义是「再点一次取消」。
        if (e.AddedItems.Count > 0 &&
            e.AddedItems[0] is AudioEffectPresetItem { IsSelected: false } item)
        {
            Effects?.SelectPresetCommand.Execute(item);
        }
    }

    private void OnStandardQualityClick(object sender, RoutedEventArgs e) =>
        _ = Player?.SwitchQualityAsync(AudioQuality.Standard);

    private void OnHighQualityClick(object sender, RoutedEventArgs e) =>
        _ = Player?.SwitchQualityAsync(AudioQuality.High);

    private void OnLosslessQualityClick(object sender, RoutedEventArgs e) =>
        _ = Player?.SwitchQualityAsync(AudioQuality.Lossless);

    private void OnQueueClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlayQueueRow row)
        {
            Queue?.Play(row.Position);
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnRestartClick(object sender, RoutedEventArgs e) =>
        RestartRequested?.Invoke(this, EventArgs.Empty);

    private void OnExitClick(object sender, RoutedEventArgs e) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);
}
