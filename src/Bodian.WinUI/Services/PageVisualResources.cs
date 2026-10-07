using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.Services;

/// <summary>离屏页面保留数据与滚动位置，释放列表实际容器和图片的呈现引用。</summary>
internal static class PageVisualResources
{
    private static readonly ConditionalWeakTable<Page, State> States = new();

    public static void Track(Page page)
    {
        // 媒体页面已有独立的资源释放顺序，不能在播放器解绑前动它的视觉树。
        if (page is Views.LyricsPage or Views.MvPage) return;
        if (States.TryGetValue(page, out _)) return;
        States.Add(page, new State());
        page.Loaded += OnLoaded;
        page.Unloaded += OnUnloaded;
    }

    public static void Reset(Page page)
    {
        if (!States.TryGetValue(page, out var state)) return;
        state.Version++;
        state.Suspended = false;
        state.Lists.Clear();
        state.Images.Clear();
    }

    private static void OnUnloaded(object sender, RoutedEventArgs args)
    {
        var page = (Page)sender;
        var state = States.GetValue(page, static _ => new State());
        if (state.Suspended) return;
        state.Suspended = true;
        state.Version++;
        Release(page, state);
    }

    private static void Release(DependencyObject element, State state)
    {
        if (element is ListViewBase { SelectionMode: ListViewSelectionMode.None } list
            && list.ItemsSource is { } source && source is not IEnumerable<string>)
        {
            var scroll = FindScrollViewer(list);
            state.Lists.Add(new ListState(list, source, scroll?.HorizontalOffset ?? 0, scroll?.VerticalOffset ?? 0));
            // 数据仍由 ViewModel 持有；仅释放原生列表实现的模板、封面与菜单容器。
            list.ItemsSource = null;
            return;
        }
        if (element is Image image && image.Source is { } pixels)
        {
            state.Images.Add((image, pixels));
            image.Source = null;
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            Release(VisualTreeHelper.GetChild(element, index), state);
    }

    private static void OnLoaded(object sender, RoutedEventArgs args)
    {
        var page = (Page)sender;
        if (!States.TryGetValue(page, out var state) || !state.Suspended) return;
        state.Suspended = false;
        var version = ++state.Version;
        var lists = state.Lists.ToArray();
        foreach (var saved in lists)
            if (saved.List.ItemsSource is null) saved.List.ItemsSource = saved.Source;
        foreach (var saved in state.Images)
            if (saved.Image.Source is null) saved.Image.Source = saved.Source;
        state.Lists.Clear();
        state.Images.Clear();
        page.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!page.IsLoaded || state.Version != version) return;
            foreach (var saved in lists)
                FindScrollViewer(saved.List)?.ChangeView(saved.HorizontalOffset, saved.VerticalOffset, null, true);
        });
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer scroll) return scroll;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(element, index)) is { } found) return found;
        return null;
    }

    private sealed class State
    {
        public bool Suspended;
        public int Version;
        public List<ListState> Lists { get; } = [];
        public List<(Image Image, ImageSource Source)> Images { get; } = [];
    }

    private sealed record ListState(ListViewBase List, object Source, double HorizontalOffset, double VerticalOffset);
}
