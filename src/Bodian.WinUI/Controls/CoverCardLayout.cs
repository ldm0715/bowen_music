using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation.Collections;

namespace Bodian.WinUI.Controls;

/// <summary>乐库封面保持固定大小，网格行高预留长标题空间，悬浮容器按内容收紧。</summary>
public static class CoverCardLayout
{
    private static readonly ConditionalWeakTable<IObservableVector<object>, GridView> Grids = new();

    // 与 BodianCoverCardItem 的 Padding="4"、Margin="0,0,4,4" 及卡片 RowSpacing="6" 一致。
    private const double Padding = 4;
    private const double Gap = 4;
    private const double RowSpacing = 6;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(CoverCardLayout),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty CoverSizeProperty = DependencyProperty.RegisterAttached(
        "CoverSize",
        typeof(double),
        typeof(CoverCardLayout),
        new PropertyMetadata(150d, OnMetricsChanged));

    public static readonly DependencyProperty TextHeightProperty = DependencyProperty.RegisterAttached(
        "TextHeight",
        typeof(double),
        typeof(CoverCardLayout),
        new PropertyMetadata(40d, OnMetricsChanged));

    public static double GetCoverSize(DependencyObject element) => (double)element.GetValue(CoverSizeProperty);

    public static void SetCoverSize(DependencyObject element, double value) => element.SetValue(CoverSizeProperty, value);

    public static double GetTextHeight(DependencyObject element) => (double)element.GetValue(TextHeightProperty);

    public static void SetTextHeight(DependencyObject element, double value) => element.SetValue(TextHeightProperty, value);

    private static void OnMetricsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is GridView grid && GetIsEnabled(grid))
        {
            UpdateMetrics(grid);
        }
    }

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not GridView grid)
        {
            return;
        }

        if ((bool)args.NewValue)
        {
            grid.Loaded += OnLoaded;
            grid.Unloaded += OnUnloaded;
            if (grid.IsLoaded)
            {
                AttachItems(grid);
            }
        }
        else
        {
            grid.Loaded -= OnLoaded;
            grid.Unloaded -= OnUnloaded;
            grid.Items.VectorChanged -= OnItemsChanged;
            Grids.Remove(grid.Items);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs args) => AttachItems((GridView)sender);

    private static void AttachItems(GridView grid)
    {
        grid.Items.VectorChanged -= OnItemsChanged;
        Grids.Remove(grid.Items);
        Grids.Add(grid.Items, grid);
        grid.Items.VectorChanged += OnItemsChanged;
        UpdateMetrics(grid);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs args)
    {
        var grid = (GridView)sender;
        grid.Items.VectorChanged -= OnItemsChanged;
        Grids.Remove(grid.Items);
    }

    private static void OnItemsChanged(IObservableVector<object> sender, IVectorChangedEventArgs args)
    {
        // 初次加载时空网格尚未创建 ItemsPanelRoot，条目到位后补算尺寸。
        if (Grids.TryGetValue(sender, out var grid))
        {
            UpdateMetrics(grid);
        }
    }

    private static void UpdateMetrics(GridView grid)
    {
        if (grid.ItemsPanelRoot is not ItemsWrapGrid panel)
        {
            return;
        }

        var cover = GetCoverSize(grid);
        var textHeight = GetTextHeight(grid);
        var inset = (2 * Padding) + Gap;
        var cellWidth = cover + inset;
        var cellHeight = cover + RowSpacing + textHeight + inset;

        // 避免条目增加时反复重排，导致末尾容器再次实现并触发多余的自动翻页。
        if (panel.ItemWidth == cellWidth && panel.ItemHeight == cellHeight)
        {
            return;
        }

        panel.ItemWidth = cellWidth;
        panel.ItemHeight = cellHeight;
    }
}
