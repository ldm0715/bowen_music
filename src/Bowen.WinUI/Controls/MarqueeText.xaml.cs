using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 单行文字，放不下时鼠标悬停横向滚动。
/// </summary>
/// <remarks>
/// <para>
/// <b>静止态仍显示省略号。</b> 文字层始终按自然宽度排布，裁剪窗口在右侧留出
/// <see cref="EllipsisReserve"/> 画一个「…」；悬停时裁剪窗口吃满整行、藏起省略号再滚动。
/// </para>
/// <para>
/// <b>不显示空白。</b> 位移范围只取「文字自然宽度 − 视口宽度」，滚到末尾时最后一个字正好贴住右边缘；
/// 不会出现「文字滚走、后面一段空」。
/// </para>
/// <para>
/// <b>宽度上限由外部的 <see cref="TitleClamp"/> 刷。</b> 本控件按文字自然宽度测量，
/// 上限 = 所在星号列宽（减角标占位），所以角标仍紧贴歌名。
/// 没有上限时横向 <see cref="StackPanel"/> 会按无限宽度排布，文字又会溢出。
/// </para>
/// <para>
/// 悬停时滚到末尾、停一会儿、再滚回起点，循环；移开时从当前位置快速回到起点。
/// 行被回收（<c>Unloaded</c>）或换文字时同样复位，避免容器复用时停在上一次的偏移上。
/// </para>
/// </remarks>
public sealed partial class MarqueeText : UserControl
{
    /// <summary>悬停滚动速度，DIP / 秒。慢到能看清、快到不用等太久。</summary>
    private const double ScrollSpeed = 60;

    /// <summary>移开 / 回程速度，DIP / 秒。</summary>
    private const double ReturnSpeed = 360;

    /// <summary>静止态给省略号留的宽度，DIP。够放下三种字号下的「…」。</summary>
    private const double EllipsisReserve = 14;

    private static readonly TimeSpan EndHold = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan CycleGap = TimeSpan.FromMilliseconds(450);

    /// <summary>每次开始 / 取消都加一，旧循环看到它变了就退出。</summary>
    private int _run;

    private bool _hovered;

    /// <summary>当前水平位移（≤ 0）。取消时要从这里接着往回滚，不能回到 0 再重来。</summary>
    private double _offset;

    /// <summary>
    /// 文字层的合成视觉。<b>第一次真正需要滚动时才取。</b>
    /// </summary>
    /// <remarks>
    /// 取元素的合成视觉会让该元素永久转入合成层交接，每个实例都要额外建视觉与属性集。
    /// 每行歌曲有多个本控件，而会被悬停滚动的只有指针下那一行，所以不在构造或换文字时取。
    /// </remarks>
    private Visual? _trackVisual;

    private RectangleGeometry? _clip;

    public MarqueeText()
    {
        InitializeComponent();

        // 容器回收时事件会重新挂到新曲目上，偏移必须归零，否则新行一露面就停在半截。
        Unloaded += (_, _) => Reset();
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(MarqueeText),
        new PropertyMetadata("", OnTextChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MarqueeText)d).Reset();

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_hovered)
        {
            ApplyRestingLayout();
        }
    }

    private void OnClipHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 复用同一个几何对象，尺寸变化只改矩形。
        _clip ??= ClipHost.Clip = new RectangleGeometry();
        _clip.Rect = new Rect(0, 0, Math.Max(0, e.NewSize.Width), Math.Max(0, e.NewSize.Height));
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hovered = true;

        // 吃满整行：省略号和它的留白都让给完整文字。
        ClipHost.Margin = new Thickness(0);
        EllipsisLine.Visibility = Visibility.Collapsed;

        var run = ++_run;
        _ = RunAsync(run);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hovered = false;

        // 用新的代次从当前位置往回滚；旧的滚动循环看到代次变了就停。
        var run = ++_run;
        _ = ReturnToStartAsync(run);
    }

    private async Task ReturnToStartAsync(int run)
    {
        await ScrollToAsync(0, run, ReturnSpeed);

        // 回到起点后才恢复省略号；中途再次进入则交给新的悬停循环。
        if (run == _run)
        {
            ApplyRestingLayout();
        }
    }

    private async Task RunAsync(int run)
    {
        while (run == _run && _hovered)
        {
            var overflow = TextLine.ActualWidth - Viewport.ActualWidth;

            // 没有截断就什么都不做 —— 短文字不该凭空动。
            if (overflow <= 0.5)
            {
                return;
            }

            await ScrollToAsync(-overflow, run, ScrollSpeed);
            if (run != _run) return;

            await Task.Delay(EndHold);
            if (run != _run) return;

            await ScrollToAsync(0, run, ReturnSpeed);
            if (run != _run) return;

            await Task.Delay(CycleGap);
        }
    }

    /// <summary>按恒定速度把水平位移推到 <paramref name="target"/>。</summary>
    private async Task ScrollToAsync(double target, int run, double speed)
    {
        var start = _offset;
        var remaining = target - start;

        if (Math.Abs(remaining) <= 0.5)
        {
            return;
        }

        var started = Stopwatch.GetTimestamp();

        while (run == _run)
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds;
            var travelled = Math.Min(Math.Abs(remaining), speed * elapsed);
            _offset = start + Math.Sign(remaining) * travelled;
            ApplyOffset(_offset);

            if (travelled >= Math.Abs(remaining) - 0.5)
            {
                _offset = target;
                ApplyOffset(_offset);
                return;
            }

            await Task.Delay(16);
        }
    }

    /// <summary>回到静止态：裁剪窗口右侧留出省略号，文字还没超宽时不留。</summary>
    private void ApplyRestingLayout()
    {
        var truncated = TextLine.ActualWidth > Viewport.ActualWidth + 0.5;
        ClipHost.Margin = truncated ? new Thickness(0, 0, EllipsisReserve, 0) : new Thickness(0);
        EllipsisLine.Visibility = truncated ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyOffset(double value)
    {
        if (_trackVisual is null)
        {
            // 从未滚动过时位移本来就是 0，归零不需要建视觉。
            if (value == 0) return;

            // Translation 必须显式打开，否则写进 visual 的偏移不生效（与 AppMotion 同一做法）。
            ElementCompositionPreview.SetIsTranslationEnabled(Track, true);
            _trackVisual = ElementCompositionPreview.GetElementVisual(Track);
        }

        _trackVisual.Properties.InsertVector3("Translation", new Vector3((float)value, 0, 0));
    }

    /// <summary>取消当前滚动并回到起点（不在树里、换文字时走这里）。</summary>
    private void Reset()
    {
        _run++;
        _hovered = false;
        _offset = 0;
        ApplyOffset(0);
        ApplyRestingLayout();
    }
}
