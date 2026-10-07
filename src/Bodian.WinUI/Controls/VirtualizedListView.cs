using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace Bodian.WinUI.Controls;

/// <summary>应用列表的共同默认值：可回收面板、有限预取、诊断实际实现容器数量。</summary>
public sealed class VirtualizedListView : ListView
{
    private readonly HashSet<SelectorItem> _realized = [];
    private readonly DispatcherQueueTimer? _diagnosticsTimer;
    private readonly ILogger _logger;
    private bool _loaded;
    private bool _reportedUnconstrained;

    public VirtualizedListView()
    {
        DefaultStyleKey = typeof(ListView);
        ItemsPanel = (ItemsPanelTemplate)Application.Current.Resources["BodianVirtualizedItemsPanel"];
        _logger = (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory
            ?? NullLoggerFactory.Instance).CreateLogger<VirtualizedListView>();
        if (Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") != "1") return;
        _diagnosticsTimer = DispatcherQueue.CreateTimer();
        _diagnosticsTimer.Interval = TimeSpan.FromMilliseconds(500);
        _diagnosticsTimer.IsRepeating = false;
        ContainerContentChanging += OnContainerChanged;
        Loaded += (_, _) => { _loaded = true; _diagnosticsTimer.Tick += OnDiagnosticsTick; ScheduleReport(); };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            _diagnosticsTimer.Stop();
            _diagnosticsTimer.Tick -= OnDiagnosticsTick;
            _realized.Clear();
        };
    }

    private void OnDiagnosticsTick(DispatcherQueueTimer sender, object args) => ReportRealization();

    protected override Size MeasureOverride(Size availableSize)
    {
        // 垂直 StackPanel / 外层 ScrollViewer 提供无限高度时，也不允许把全部数据
        // 都实现成元素。基础控件先限制到窗口视口，并在诊断中指出调用方约束问题。
        if (XamlRoot is { } root && root.Size.Width > 0 && root.Size.Height > 0
            && (!double.IsFinite(availableSize.Width) || !double.IsFinite(availableSize.Height)))
        {
            if (!_reportedUnconstrained && ScrollViewer.GetVerticalScrollMode(this) != ScrollMode.Disabled)
            {
                _reportedUnconstrained = true;
                _logger.LogDebug("列表 {Name} 收到无限布局尺寸，已限制到窗口视口；分组应使用平铺数据", Name);
            }
            availableSize = new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : root.Size.Width,
                double.IsFinite(availableSize.Height) ? availableSize.Height : root.Size.Height);
        }
        return base.MeasureOverride(availableSize);
    }

    private void OnContainerChanged(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) _realized.Remove(args.ItemContainer);
        else _realized.Add(args.ItemContainer);
        ScheduleReport();
    }

    private void ScheduleReport()
    { if (_loaded && _diagnosticsTimer is { IsRunning: false }) _diagnosticsTimer.Start(); }

    private void ReportRealization()
    {
        if (!_loaded || Visibility != Visibility.Visible) return;
        // 重新挂载已有页面时不会再次触发 ContainerContentChanging，直接读取实际面板。
        if (ItemsPanelRoot is Panel panel)
        {
            _realized.Clear();
            foreach (var child in panel.Children.OfType<SelectorItem>()) _realized.Add(child);
        }
        _logger.LogInformation("列表虚拟化 {Name}：{Items} 个条目，{Realized} 个已创建容器，视口 {Width:F0}×{Height:F0}",
            Name, Items.Count, _realized.Count, ActualWidth, ActualHeight);
        if (XamlRoot is { } root && Items.Count > 40 && ActualHeight > root.Size.Height * 2)
            _logger.LogWarning("列表 {Name} 高度超过窗口两倍，检查外层 ScrollViewer 或 StackPanel 是否提供无限高度", Name);
    }
}
