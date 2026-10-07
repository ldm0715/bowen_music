using System.Diagnostics;
using System.Numerics;
using Bodian.Core.Performance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;

namespace Bodian.WinUI.Controls;

/// <summary>交互缩放使用合成预览；可在帧预算内完成的页面实时重排，昂贵页面松手后提交最终布局。</summary>
public sealed class ResponsiveViewport : Panel
{
    private readonly ResizeLayoutBudget _budget = new(TimeProvider.System);
    private readonly DispatcherQueueTimer _timer;
    private readonly ILogger _logger;
    private readonly bool _diagnostics = Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") == "1";
    private Size _layoutSize;
    private bool _interactive, _commit = true, _layingOut;
    private double _layoutMilliseconds;

    public ResponsiveViewport()
    {
        _logger = (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory
            ?? NullLoggerFactory.Instance).CreateLogger<ResponsiveViewport>();
        _timer = DispatcherQueue.CreateTimer();
        _timer.IsRepeating = false;
        Loaded += (_, _) =>
        {
            _timer.Tick += OnCommitTimer;
            var visual = ElementCompositionPreview.GetElementVisual(this);
            visual.Clip = visual.Compositor.CreateInsetClip();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _timer.Tick -= OnCommitTimer;
            _budget.Reset();
            _commit = true;
        };
    }

    private void OnCommitTimer(DispatcherQueueTimer sender, object args) { _commit = true; InvalidateMeasure(); }

    public bool IsInteractive
    {
        get => _interactive;
        set
        {
            if (_interactive == value) return;
            _interactive = value;
            _timer.Stop();
            if (!value) { _budget.Reset(); _commit = true; }
            else _commit = _layoutSize.Width <= 0 || _layoutSize.Height <= 0;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!double.IsFinite(availableSize.Width) || !double.IsFinite(availableSize.Height))
            return MeasureUnconstrained(availableSize);
        var changed = _layoutSize != availableSize;
        if (!_interactive || _commit || _layoutSize.Width <= 0 || _layoutSize.Height <= 0
            || (changed && _budget.CanReflowInteractively && _budget.Remaining == TimeSpan.Zero))
        {
            _layoutSize = availableSize;
            _commit = false;
            _layingOut = true;
            _layoutMilliseconds = 0;
        }
        else if (changed && _budget.CanReflowInteractively && !_timer.IsRunning)
        {
            _timer.Interval = TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerMillisecond, _budget.Remaining.Ticks));
            _timer.Start();
        }
        if (_layingOut)
        {
            var measured = Stopwatch.GetTimestamp();
            foreach (var child in Children) child.Measure(_layoutSize);
            _layoutMilliseconds += Stopwatch.GetElapsedTime(measured).TotalMilliseconds;
        }
        return availableSize;
    }

    private Size MeasureUnconstrained(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return new Size(Children.Count == 0 ? 0 : Children.Max(child => child.DesiredSize.Width),
            Children.Count == 0 ? 0 : Children.Max(child => child.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var layout = _interactive && _layoutSize.Width > 0 && _layoutSize.Height > 0 ? _layoutSize : finalSize;
        var arranged = Stopwatch.GetTimestamp();
        foreach (var child in Children)
        {
            if (!_interactive || _layingOut) child.Arrange(new Rect(0, 0, layout.Width, layout.Height));
            // 只改变合成变换，不让尺寸请求再次递归测量所有子节点。
            ElementCompositionPreview.GetElementVisual(child).Scale = new Vector3(
                layout.Width > 0 ? (float)(finalSize.Width / layout.Width) : 1,
                layout.Height > 0 ? (float)(finalSize.Height / layout.Height) : 1, 1);
        }
        if (_layingOut)
        {
            _layingOut = false;
            var elapsed = TimeSpan.FromMilliseconds(_layoutMilliseconds + Stopwatch.GetElapsedTime(arranged).TotalMilliseconds);
            _budget.RecordLayout(elapsed);
            if (_diagnostics && _interactive && elapsed.TotalMilliseconds > ResizeLayoutBudget.FrameBudget.TotalMilliseconds)
                _logger.LogInformation("视口布局 {Name}：{Elapsed:F2} ms，下次重排间隔 {Interval:F2} ms", Name, elapsed.TotalMilliseconds, _budget.Interval.TotalMilliseconds);
        }
        return finalSize;
    }
}
