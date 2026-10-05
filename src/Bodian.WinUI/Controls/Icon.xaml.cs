using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.Controls;

/// <summary>画一颗 <c>Themes/Icons.xaml</c> 里的图标。</summary>
/// <remarks>
/// <para>
/// 全应用的图标都走这里，不再用 <c>FontIcon</c> 渲染系统字体字形 ——
/// 字体字形的墨水大小与位置由字体决定、逐颗不同，是「对不齐、调不了大小」的根源。
/// </para>
/// <para>
/// <see cref="Data"/> 收的是**路径文本**（<c>{StaticResource IconX}</c>），不是
/// <see cref="Geometry"/>。资源字典里的 Geometry 赋不进来，见 <see cref="IconGeometry"/>。
/// </para>
/// </remarks>
public sealed partial class Icon : UserControl
{
    private bool _foregroundRefreshPending;
    private DependencyObject? _foregroundSource;
    private DependencyProperty? _sourceForegroundProperty;
    private long _sourceForegroundToken;

    public Icon()
    {
        InitializeComponent();

        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => RefreshForeground());
        ActualThemeChanged += (_, _) => RefreshForeground();
        Loaded += OnLoaded;
        Unloaded += (_, _) => DetachForegroundSource();
        Apply();
        RefreshForeground();
    }

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(string), typeof(Icon), new PropertyMetadata("", OnDisplayChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Icon), new PropertyMetadata(16d, OnDisplayChanged));

    private static void OnDisplayChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((Icon)sender).Apply();

    /// <summary>
    /// 要画的**路径文本**，来自 <c>Themes/Icons.xaml</c>：<c>Data="{StaticResource IconHeart}"</c>。
    /// </summary>
    /// <remarks>
    /// 是字符串而不是 <see cref="Geometry"/> —— 见 <see cref="IconGeometry"/> 上的说明：
    /// 资源字典里的 Geometry 赋不进 Geometry 属性，会直接让应用打不开。
    /// </remarks>
    public string Data
    {
        get => (string)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>视觉边长。路径一律按 24 单位画，这里缩放到这个尺寸。</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        DetachForegroundSource();
        // 按钮的禁用/悬停态在 ContentPresenter 上设置前景，继承到 Icon 时不一定发送属性通知。
        for (var ancestor = VisualTreeHelper.GetParent(this); ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            var property = ancestor switch
            {
                ContentPresenter => ContentPresenter.ForegroundProperty,
                Control => ForegroundProperty,
                _ => null,
            };
            if (property is null) continue;
            _foregroundSource = ancestor;
            _sourceForegroundProperty = property;
            _sourceForegroundToken = ancestor.RegisterPropertyChangedCallback(property, (_, _) => RefreshForeground());
            break;
        }
        RefreshForeground();
    }

    private void DetachForegroundSource()
    {
        if (_foregroundSource is not null && _sourceForegroundProperty is not null)
            _foregroundSource.UnregisterPropertyChangedCallback(_sourceForegroundProperty, _sourceForegroundToken);
        _foregroundSource = null;
        _sourceForegroundProperty = null;
    }

    private void RefreshForeground()
    {
        Glyph.Fill = Foreground;
        if (_foregroundRefreshPending) return;
        // 主题先沿视觉树更新，继承前景随后才传播。末尾再同步一次，无需等待鼠标或下一次状态变化。
        _foregroundRefreshPending = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _foregroundRefreshPending = false;
            Glyph.Fill = Foreground;
        });
    }

    private void Apply()
    {
        Scaler.Width = Scaler.Height = Size;
        Glyph.Data = IconGeometry.From(Data);
    }
}
