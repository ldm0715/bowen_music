using System.Diagnostics;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace Bodian.WinUI.Services;

/// <summary>独立遮罩柔和退场；不动画、修改或替换共用主题画刷，也不改变整窗透明度。</summary>
internal sealed class ThemeTransitionAnimator : IDisposable
{
    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(650);
    private readonly FrameworkElement _root;
    private readonly Border _veil;
    private readonly Func<bool> _canAnimate;
    private readonly SpriteVisual _surface;
    private readonly CompositionColorBrush _veilBrush;
    private readonly ILogger<ThemeTransitionAnimator> _logger;
    private readonly bool _diagnostics = Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") == "1";
    private Action? _cancelTransition;
    private bool _disposed;

    public ThemeTransitionAnimator(FrameworkElement root, Border veil, Func<bool> canAnimate)
    {
        _root = root;
        _veil = veil;
        _canAnimate = canAnimate;
        // 独立原生合成图层，不修改 XAML 的透明度，也不引用主题资源中的画刷。
        var compositor = ElementCompositionPreview.GetElementVisual(veil).Compositor;
        _veilBrush = compositor.CreateColorBrush(Colors.White);
        _surface = compositor.CreateSpriteVisual();
        _surface.RelativeSizeAdjustment = Vector2.One;
        _surface.Brush = _veilBrush;
        _surface.Opacity = 0;
        ElementCompositionPreview.SetElementChildVisual(veil, _surface);
        _logger = (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory
            ?? NullLoggerFactory.Instance).CreateLogger<ThemeTransitionAnimator>();
        root.Unloaded += OnUnloaded;
    }

    public Task ChangeAsync(Action apply)
    {
        _cancelTransition?.Invoke();
        var previous = _root.ActualTheme;
        var canAnimate = !_disposed && AppMotion.IsEnabled && _root.IsLoaded && _veil.IsLoaded && _canAnimate();
        var started = Stopwatch.GetTimestamp();
        // 正常 ThemeResource 解析负责全部样式，过渡不介入控件的 Background / Foreground。
        apply();
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (!canAnimate || previous == _root.ActualTheme) return Task.CompletedTask;

        _veilBrush.Color = previous == ElementTheme.Dark ? Color.FromArgb(255, 24, 24, 24) : Colors.White;
        var visual = _surface;
        var compositor = visual.Compositor;
        using var fade = compositor.CreateScalarKeyFrameAnimation();
        using var linear = compositor.CreateLinearEasingFunction();
        // 长一些的连续退场，不使用整窗闪暗/闪亮的两段透明度脉冲。
        for (var frame = 0; frame <= 24; frame++)
        {
            var progress = frame / 24f;
            var opacity = ThemeColorMotion.Sample(new Vector4(0.18f), Vector4.Zero, progress).X;
            fade.InsertKeyFrame(progress, opacity, linear);
        }
        fade.Duration = TransitionDuration;
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var completion = new TaskCompletionSource<bool>();
        var animationStarted = Stopwatch.GetTimestamp();
        Windows.Foundation.TypedEventHandler<object, CompositionBatchCompletedEventArgs>? completed = null;
        void Finish(bool finished)
        {
            _cancelTransition = null;
            batch.Completed -= completed;
            batch.Dispose();
            visual.StopAnimation("Opacity");
            visual.Opacity = 0;
            if (_diagnostics) _logger.LogInformation("主题遮罩过渡结束：{Finished}，实际 {Elapsed:F2} ms",
                finished, Stopwatch.GetElapsedTime(animationStarted).TotalMilliseconds);
            completion.TrySetResult(finished);
        }
        completed = (_, _) => Finish(true);
        _cancelTransition = () => Finish(false);
        batch.Completed += completed;
        // 终值先作为底值再启动；启动后写 Opacity 会提前中止显式动画。
        // 只操作独立 SpriteVisual，XAML 布局不会覆盖它的 Opacity。
        visual.Opacity = 0;
        visual.StartAnimation("Opacity", fade);
        batch.End();
        if (_diagnostics) _logger.LogInformation("主题遮罩过渡启动：{Duration} ms，应用主题 {Elapsed:F2} ms",
            TransitionDuration.TotalMilliseconds, elapsed);
        return completion.Task;
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => _cancelTransition?.Invoke();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _root.Unloaded -= OnUnloaded;
        _cancelTransition?.Invoke();
        ElementCompositionPreview.SetElementChildVisual(_veil, null);
        _surface.Brush = null;
        _veilBrush.Dispose();
        _surface.Dispose();
    }
}
