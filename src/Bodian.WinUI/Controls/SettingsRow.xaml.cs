using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 设置页里的一行：左侧可选图标、主标题、可选说明，右侧接任意内容。
/// </summary>
/// <remarks>
/// <para>
/// 属性变化一律在 <see cref="DependencyProperty"/> 的静态回调里**直接改子元素**，
/// 不走 <c>x:Bind</c> —— 与 <see cref="Icon"/> 同一个理由：给 <see cref="UserControl"/>
/// 自己的依赖属性做 <c>x:Bind</c>，是否收到变更通知这件事不值得赌，而回调是确定的。
/// </para>
/// <para>
/// 右侧内容用 <see cref="RightContent"/> 接任意 <see cref="UIElement"/>，所以一个控件就能同时
/// 承载开关、下拉、按钮与只读文本。**不做成 <c>ItemsControl</c> 的 ItemTemplate**：
/// 固定项在页面里一行一项显式写出来，只有条数会变的集合才走 <c>ItemsSource</c>。
/// </para>
/// </remarks>
public sealed partial class SettingsRow : UserControl
{
    private bool _pointerOver;

    public SettingsRow()
    {
        InitializeComponent();

        RowSurface.Tapped += OnRowTapped;
        RowSurface.PointerEntered += OnRowPointerEntered;
        RowSurface.PointerExited += OnRowPointerExited;

        ApplyGlyph();
        ApplyHeader();
        ApplyDescription();
        ApplyRightContent();
        ApplyClickable();
        ApplyRowEnabled();
    }

    /// <summary>整行被点击。只有 <see cref="IsClickable"/> 为真时才会触发。</summary>
    public event EventHandler? Click;

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingsRow), new PropertyMetadata("", OnGlyphChanged));

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsRow), new PropertyMetadata("", OnHeaderChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsRow), new PropertyMetadata("", OnDescriptionChanged));

    public static readonly DependencyProperty RightContentProperty = DependencyProperty.Register(
        nameof(RightContent), typeof(object), typeof(SettingsRow), new PropertyMetadata(null, OnRightContentChanged));

    public static readonly DependencyProperty IsClickableProperty = DependencyProperty.Register(
        nameof(IsClickable), typeof(bool), typeof(SettingsRow), new PropertyMetadata(false, OnClickableChanged));

    public static readonly DependencyProperty IsRowEnabledProperty = DependencyProperty.Register(
        nameof(IsRowEnabled), typeof(bool), typeof(SettingsRow), new PropertyMetadata(true, OnRowEnabledChanged));

    /// <summary>图标资源键（<c>Themes/Icons.xaml</c> 的 <c>x:Key</c>）。空则不画图标列。</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>主标题。</summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>次行说明。空则整行塌掉。</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>右侧内容：<c>ToggleSwitch</c> / <c>ComboBox</c> / <c>Button</c> / <c>TextBlock</c> 都可以。</summary>
    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    /// <summary>整行可点（跳转类）：悬停出底色、右侧出现箭头，点击触发 <see cref="Click"/>。</summary>
    public bool IsClickable
    {
        get => (bool)GetValue(IsClickableProperty);
        set => SetValue(IsClickableProperty, value);
    }

    /// <summary>
    /// 整行是否可用。关掉之后右侧的开关/下拉/滑块都点不动，整行也变淡 ——
    /// 用于「上一级的开关关掉时，它下面那一组设置跟着失效」。
    /// </summary>
    /// <remarks>
    /// 除了 <see cref="Control.IsEnabled"/>（它会让子控件的开关、下拉进各自的 Disabled 视觉状态），
    /// 还要自己压一层不透明度：<c>TextBlock</c> 没有禁用态，光靠 IsEnabled 文字不会变淡。
    /// </remarks>
    public bool IsRowEnabled
    {
        get => (bool)GetValue(IsRowEnabledProperty);
        set => SetValue(IsRowEnabledProperty, value);
    }

    private static void OnGlyphChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((SettingsRow)sender).ApplyGlyph();

    private static void OnHeaderChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((SettingsRow)sender).ApplyHeader();

    private static void OnDescriptionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((SettingsRow)sender).ApplyDescription();

    private static void OnRightContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((SettingsRow)sender).ApplyRightContent();

    private static void OnClickableChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((SettingsRow)sender).ApplyClickable();

    private static void OnRowEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((SettingsRow)sender).ApplyRowEnabled();

    private void ApplyGlyph()
    {
        GlyphIcon.Visibility = string.IsNullOrEmpty(Glyph) ? Visibility.Collapsed : Visibility.Visible;
        GlyphIcon.Data = Formats.IconPaths(Glyph);
    }

    private void ApplyHeader() => HeaderText.Text = Header;

    private void ApplyDescription()
    {
        DescriptionText.Text = Description;
        DescriptionText.Visibility = string.IsNullOrWhiteSpace(Description) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyRightContent() => RightHost.Content = RightContent;

    private void ApplyRowEnabled()
    {
        // 交给基类的 IsEnabled 让右侧的开关、下拉、滑块各自进入 Disabled 视觉状态。
        IsEnabled = IsRowEnabled;

        // 文字没有禁用态，得自己压一层 —— 否则只有控件变灰、标题仍然醒目。
        RowSurface.Opacity = IsRowEnabled ? 1d : 0.4d;

        if (!IsRowEnabled)
        {
            _pointerOver = false;
            UpdateHoverSurface();
        }
    }

    private void ApplyClickable()
    {
        ChevronIcon.Visibility = IsClickable ? Visibility.Visible : Visibility.Collapsed;
        if (!IsClickable)
        {
            _pointerOver = false;
        }

        UpdateHoverSurface();
    }

    private void OnRowTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs args)
    {
        if (!IsClickable)
        {
            return;
        }

        args.Handled = true;
        Click?.Invoke(this, EventArgs.Empty);
    }

    private void OnRowPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        _pointerOver = true;
        UpdateHoverSurface();
    }

    private void OnRowPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        _pointerOver = false;
        UpdateHoverSurface();
    }

    /// <remarks>
    /// 每次都重新查一次画刷，不缓存：主题字典的条目会随主题换对象，缓存下来的那份会过期。
    /// 查不到就不上色 —— 字符串键查不中会在运行期抛异常，而这里是纯装饰，不值得为此崩掉。
    /// </remarks>
    private void UpdateHoverSurface()
    {
        if (!IsClickable || !_pointerOver)
        {
            RowSurface.Background = null;
            return;
        }

        RowSurface.Background = Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var brush)
            ? brush as Brush
            : null;
    }
}
