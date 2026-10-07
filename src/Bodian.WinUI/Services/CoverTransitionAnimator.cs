using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Bodian.WinUI.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Bodian.WinUI.Services;

/// <summary>共享封面的统一动画层：歌曲落入播放条，封面/头像往返详情页。</summary>
internal sealed class CoverTransitionAnimator : IDisposable
{
    private const string ResourceKey = "BodianCoverTransitionAnimator";
    private readonly FrameworkElement _root;
    private readonly Canvas _overlay;
    private readonly ILogger<CoverTransitionAnimator> _logger;
    private readonly bool _diagnostics = Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") == "1";
    private readonly ConditionalWeakTable<Page, Origin> _origins = new();
    private ExplicitSource? _explicitSource;
    private Action? _cancelFlight;
    private bool _disposed;
    private int _navigationVersion;
    private NavigationCover? _preparedNavigation;
    private Action? _cancelNavigationLoad;
    private Action? _cancelTargetLayout;

    internal sealed record Snapshot(ImageSource Image, Rect Bounds, float Radius, WeakReference<FrameworkElement> Element);
    internal sealed record NavigationCover(Snapshot Source, Page Page, string Key,
        WeakReference<FrameworkElement>? PreferredTarget, bool Backwards, bool IsLyrics = false, bool ToPlayer = false);
    private sealed record Origin(WeakReference<Page> Page, WeakReference<FrameworkElement> Cover, string Key);
    private sealed record ExplicitSource(string Key, Snapshot Cover, WeakReference<FrameworkElement> Element, long Created);

    internal static CoverTransitionAnimator? Current => Application.Current.Resources.TryGetValue(ResourceKey, out var value)
        ? value as CoverTransitionAnimator : null;

    public CoverTransitionAnimator(FrameworkElement root, Canvas overlay)
    {
        _root = root;
        _overlay = overlay;
        _logger = (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory
            ?? NullLoggerFactory.Instance).CreateLogger<CoverTransitionAnimator>();
        root.SizeChanged += OnRootSizeChanged;
        Application.Current.Resources[ResourceKey] = this;
        UpdateClip();
    }

    public static void PrepareFromClick(FrameworkElement? source, string key)
    {
        var animator = Current;
        if (animator is null || string.IsNullOrEmpty(key) || !AppMotion.IsEnabled) return;
        var cover = FindImage(source);
        if (cover is null || animator.Capture(cover) is not { } snapshot) return;
        animator._explicitSource = new ExplicitSource(key, snapshot, new WeakReference<FrameworkElement>(cover), Stopwatch.GetTimestamp());
    }

    internal Snapshot? Capture(FrameworkElement? source)
    {
        var cover = FindImage(source);
        if (cover is null || !Visible(cover)) return null;
        var image = ImageOf(cover);
        if (image is null) return null;
        var bounds = Bounds(cover);
        return new Snapshot(image, bounds, RadiusInRoot(cover, bounds),
            new WeakReference<FrameworkElement>(cover));
    }

    internal NavigationCover? PrepareNavigation(Page? current, Page next, bool backwards)
    {
        _navigationVersion++;
        _cancelNavigationLoad?.Invoke();
        _preparedNavigation = null;
        CancelFlight();
        var explicitSource = _explicitSource;
        _explicitSource = null;
        if (_disposed || current is null || !AppMotion.IsEnabled) return null;
        // MV 没有作为交接来源的封面；退回普通页或歌词页时只做页面过渡。
        if (current is Views.MvPage) return null;
        if (current is Views.LyricsPage && next is not Views.LyricsPage and not Views.MvPage
            && current.FindName("DetailCover") is FrameworkElement lyricsCover
            && Capture(lyricsCover) is { } albumCover)
        {
            var trackKey = Motion.GetSharedCoverKey(lyricsCover);
            return _preparedNavigation = new NavigationCover(albumCover, next, trackKey, null,
                true, IsLyrics: true, ToPlayer: true);
        }
        // 返回要先找原来的卡片；父页可能本身也有自己的页头封面（例如歌手页的专辑列表）。
        if (backwards && current.FindName("DetailCover") is FrameworkElement outgoing
            && _origins.TryGetValue(current, out var previousOrigin)
            && previousOrigin.Page.TryGetTarget(out var previousPage) && ReferenceEquals(previousPage, next)
            && Capture(outgoing) is { } returningCover)
            return _preparedNavigation = new NavigationCover(returningCover, next, previousOrigin.Key, previousOrigin.Cover, true);
        if (next.FindName("DetailCover") is FrameworkElement detail)
        {
            // 首次进入时 XAML 的 Loading 事件尚未触发，不能读取目标元素的 x:Bind 值。
            // 页面模型在构造时已经可用，用它识别封面，目标布局仍等 Loaded 后再测量。
            var key = DetailKey(next);
            if (string.IsNullOrEmpty(key)) return null;
            Snapshot? source = null;
            FrameworkElement? sourceElement = null;
            if (explicitSource?.Key == key && Stopwatch.GetElapsedTime(explicitSource.Created) < TimeSpan.FromSeconds(5))
            {
                source = explicitSource.Cover;
                explicitSource.Element.TryGetTarget(out sourceElement);
            }
            else
            {
                sourceElement = FindVisibleCover(current.Content as DependencyObject, key)
                    ?? FindVisibleCover(_root, key);
                source = Capture(sourceElement);
            }
            if (source is null)
            {
                if (_diagnostics) _logger.LogInformation("共享封面回退：{Page}，未找到可见来源 {Key}", next.GetType().Name, key);
                return null;
            }
            if (_diagnostics) _logger.LogInformation("共享封面准备：{Page}，{Key}，来源 {Bounds}", next.GetType().Name, key, source.Bounds);
            if (sourceElement is not null)
            {
                _origins.Remove(next);
                _origins.Add(next, new Origin(new WeakReference<Page>(current), new WeakReference<FrameworkElement>(sourceElement), key));
            }
            return _preparedNavigation = new NavigationCover(source, next, key, null, backwards,
                IsLyrics: next is Views.LyricsPage);
        }
        return null;
    }

    private static string DetailKey(Page page) => page switch
    {
        Views.LyricsPage lyrics => Formats.TrackCoverKey(lyrics.Player.CurrentTrackId),
        Views.ArtistDetailPage artist => Formats.ArtistCoverKey(artist.ViewModel.Artist.Id),
        Views.PlaylistDetailPage playlist => Formats.PlaylistCoverKey(playlist.ViewModel.Playlist.Id),
        Views.AlbumDetailPage album => Formats.AlbumCoverKey(album.ViewModel.Album.Id),
        _ => ""
    };

    internal bool HasLyricsEntrance(Page page) => _preparedNavigation is { IsLyrics: true, ToPlayer: false } cover
        && ReferenceEquals(cover.Page, page);

    internal Task<bool> PlayLyricsDepartureAsync(Page next)
    {
        if (_preparedNavigation is not { ToPlayer: true } navigation || !ReferenceEquals(navigation.Page, next))
            return Task.FromResult(false);
        var target = FindVisibleCover(_root, navigation.Key);
        return target is null ? Task.FromResult(false)
            : PlayAsync(navigation.Source, target, falling: false, TimeSpan.FromMilliseconds(420));
    }

    internal async Task PlayNavigationAsync(NavigationCover navigation)
    {
        if (navigation.ToPlayer) return;
        var version = _navigationVersion;
        var page = navigation.Page;
        if (!page.IsLoaded)
        {
            var loaded = new TaskCompletionSource<bool>();
            RoutedEventHandler? handler = null;
            void FinishWaiting(bool ready)
            {
                page.Loaded -= handler;
                _cancelNavigationLoad = null;
                loaded.TrySetResult(ready);
            }
            handler = (_, _) => FinishWaiting(true);
            _cancelNavigationLoad = () => FinishWaiting(false);
            page.Loaded += handler;
            if (!await loaded.Task) return;
        }
        if (_disposed || version != _navigationVersion) return;
        FrameworkElement? target = null;
        if (navigation.PreferredTarget?.TryGetTarget(out var preferred) == true && Visible(preferred))
        {
            // 弹窗关闭后的头像、已被回收复用的列表项，都不能作为返回落点。
            if (IsWithin(preferred, page) && Motion.GetSharedCoverKey(preferred) == navigation.Key) target = preferred;
        }
        target ??= FindVisibleCover(page.Content as DependencyObject, navigation.Key);
        if (target is null)
        {
            // 原卡片已离屏或不存在时，保留内容淡入，不强行滚动用户的列表。
            if (_diagnostics) _logger.LogInformation("共享封面回退：{Page}，未找到可见目标 {Key}", page.GetType().Name, navigation.Key);
            return;
        }
        await PlayAsync(navigation.Source, target, falling: false,
            navigation.IsLyrics ? TimeSpan.FromMilliseconds(520)
                : navigation.Backwards ? TimeSpan.FromMilliseconds(340) : TimeSpan.FromMilliseconds(420));
        if (version == _navigationVersion) _preparedNavigation = null;
    }

    internal async Task<bool> PlayDropAsync(Snapshot source, FrameworkElement target)
    {
        CancelFlight();
        // 首播会首次显示曲目信息块，Started 通知早于封面第一次布局。
        // 保留点击时的快照，等有效落点出现；播放本身不等待这个视觉任务。
        if (!Visible(target) && !await WaitForTargetLayoutAsync(target)) return false;
        return await PlayAsync(source, target, falling: true, TimeSpan.FromMilliseconds(620));
    }

    private Task<bool> WaitForTargetLayoutAsync(FrameworkElement target)
    {
        if (_disposed || !AppMotion.IsEnabled) return Task.FromResult(false);
        var ready = new TaskCompletionSource<bool>();
        var timeout = _root.DispatcherQueue.CreateTimer();
        timeout.Interval = TimeSpan.FromSeconds(2);
        timeout.IsRepeating = false;
        RoutedEventHandler? loaded = null;
        SizeChangedEventHandler? sized = null;
        EventHandler<object>? layout = null;
        void Finish(bool visible)
        {
            if (ready.Task.IsCompleted) return;
            _cancelTargetLayout = null;
            timeout.Stop();
            target.Loaded -= loaded;
            target.SizeChanged -= sized;
            _root.LayoutUpdated -= layout;
            ready.TrySetResult(visible);
        }
        void Check()
        {
            if (ready.Task.IsCompleted) return;
            if (Visible(target) && ImageOf(target) is not null) Finish(true);
        }
        loaded = (_, _) => Check();
        sized = (_, _) => Check();
        layout = (_, _) => Check();
        target.Loaded += loaded;
        target.SizeChanged += sized;
        _root.LayoutUpdated += layout;
        _cancelTargetLayout = () => Finish(false);
        timeout.Tick += (_, _) => Finish(false);
        timeout.Start();
        target.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, Check);
        if (_diagnostics) _logger.LogInformation("共享封面等待首播目标布局");
        return ready.Task;
    }

    private Task<bool> PlayAsync(Snapshot source, FrameworkElement target, bool falling, TimeSpan duration)
    {
        CancelFlight();
        if (_disposed || !AppMotion.IsEnabled || !Visible(target)) return Task.FromResult(false);
        var targetBounds = Bounds(target);
        var bounds = source.Bounds;
        if (_diagnostics) _logger.LogInformation("共享封面动画启动：{Kind}，{From} -> {To}",
            falling ? "drop" : "connected", bounds, targetBounds);
        var displacement = new Vector2(
            (float)(targetBounds.X + targetBounds.Width / 2 - bounds.X - bounds.Width / 2),
            (float)(targetBounds.Y + targetBounds.Height / 2 - bounds.Y - bounds.Height / 2));
        var targetScale = new Vector2((float)(targetBounds.Width / bounds.Width), (float)(targetBounds.Height / bounds.Height));
        var ghost = new Border
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Child = new Image { Source = source.Image, Stretch = Stretch.UniformToFill },
            IsHitTestVisible = false
        };
        Canvas.SetLeft(ghost, bounds.X);
        Canvas.SetTop(ghost, bounds.Y);
        _overlay.Children.Add(ghost);
        ElementCompositionPreview.SetIsTranslationEnabled(ghost, true);
        var visual = ElementCompositionPreview.GetElementVisual(ghost);
        visual.CenterPoint = new Vector3((float)bounds.Width / 2, (float)bounds.Height / 2, 0);
        visual.RotationAxis = Vector3.UnitZ;
        var compositor = visual.Compositor;
        using var translation = compositor.CreateVector3KeyFrameAnimation();
        using var scale = compositor.CreateVector3KeyFrameAnimation();
        using var rotation = compositor.CreateScalarKeyFrameAnimation();
        using var corners = compositor.CreateVector2KeyFrameAnimation();
        using var linear = compositor.CreateLinearEasingFunction();
        var geometry = compositor.CreateRoundedRectangleGeometry();
        geometry.Size = new Vector2((float)bounds.Width, (float)bounds.Height);
        geometry.CornerRadius = new Vector2(source.Radius);
        var clip = compositor.CreateGeometricClip(geometry);
        visual.Clip = clip;
        var endRadius = RadiusInRoot(target, targetBounds);
        var endCorners = new Vector2(endRadius / targetScale.X, endRadius / targetScale.Y);
        for (var index = 0; index <= 24; index++)
        {
            var progress = index / 24f;
            var eased = 1 - MathF.Pow(1 - progress, 3);
            var position = falling ? CoverDropPath.Translation(displacement, progress) : displacement * eased;
            translation.InsertKeyFrame(progress, new Vector3(position, 0), linear);
            scale.InsertKeyFrame(progress, falling ? CoverDropPath.Scale(targetScale, progress)
                : new Vector3(Vector2.Lerp(Vector2.One, targetScale, eased), 1), linear);
            rotation.InsertKeyFrame(progress, falling ? MathF.Sin(progress * MathF.PI) * MathF.Sign(displacement.X) * 0.09f : 0, linear);
            corners.InsertKeyFrame(progress, Vector2.Lerp(new Vector2(source.Radius), endCorners, eased), linear);
        }
        translation.Duration = scale.Duration = rotation.Duration = corners.Duration = duration;
        // 交接期间隐藏原封面，避免一张留在原处、一张同时飞行的重复画面。
        FrameworkElement? sourceElement = null;
        DependencyProperty? sourceProperty = null;
        long sourceCallback = 0;
        RoutedEventHandler? sourceUnloaded = null;
        void RestoreSource()
        {
            if (sourceElement is not { } original) return;
            sourceElement = null;
            original.Unloaded -= sourceUnloaded;
            if (sourceProperty is not null) original.UnregisterPropertyChangedCallback(sourceProperty, sourceCallback);
            ElementCompositionPreview.GetElementVisual(original).Opacity = 1;
        }
        if (source.Element.TryGetTarget(out var sourceCover) && !ReferenceEquals(sourceCover, target)
            && Visible(sourceCover) && ReferenceEquals(ImageOf(sourceCover), source.Image))
        {
            sourceElement = sourceCover;
            AppMotion.Reset(sourceCover);
            ElementCompositionPreview.GetElementVisual(sourceCover).Opacity = 0;
            sourceUnloaded = (_, _) => RestoreSource();
            sourceCover.Unloaded += sourceUnloaded;
            sourceProperty = sourceCover is Image ? Image.SourceProperty : PersonPicture.ProfilePictureProperty;
            sourceCallback = sourceCover.RegisterPropertyChangedCallback(sourceProperty, (_, _) => RestoreSource());
        }
        var completion = new TaskCompletionSource<bool>();
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        Windows.Foundation.TypedEventHandler<object, CompositionBatchCompletedEventArgs>? completed = null;
        RoutedEventHandler? unloaded = null;
        void Finish(bool finished)
        {
            _cancelFlight = null;
            batch.Completed -= completed;
            target.Unloaded -= unloaded;
            batch.Dispose();
            _overlay.Children.Remove(ghost);
            clip.Dispose();
            geometry.Dispose();
            RestoreSource();
            ElementCompositionPreview.GetElementVisual(target).Opacity = 1;
            if (finished && falling && Visible(target))
                _ = AppMotion.PlayAsync(target, Vector3.Zero, Vector3.Zero, 1, 1,
                    new Vector3(0.92f, 0.92f, 1), Vector3.One, AppMotion.Quick);
            if (_diagnostics) _logger.LogInformation("共享封面动画结束：{Finished}", finished);
            completion.TrySetResult(finished);
        }
        completed = (_, _) => Finish(true);
        unloaded = (_, _) => Finish(false);
        _cancelFlight = () => Finish(false);
        batch.Completed += completed;
        target.Unloaded += unloaded;
        AppMotion.Reset(target);
        ElementCompositionPreview.GetElementVisual(target).Opacity = 0;
        visual.StartAnimation("Translation", translation);
        visual.StartAnimation("Scale", scale);
        visual.StartAnimation("RotationAngle", rotation);
        geometry.StartAnimation("CornerRadius", corners);
        batch.End();
        return completion.Task;
    }

    internal void CancelFlight()
    {
        _cancelTargetLayout?.Invoke();
        _cancelFlight?.Invoke();
    }

    private Rect Bounds(FrameworkElement element) => element.TransformToVisual(_root).TransformBounds(
        new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private bool Visible(FrameworkElement element)
    {
        if (!element.IsLoaded || element.XamlRoot != _root.XamlRoot || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        var bounds = Bounds(element);
        if (bounds.Right <= 0 || bounds.Bottom <= 0 || bounds.Left >= _root.ActualWidth || bounds.Top >= _root.ActualHeight) return false;
        DependencyObject? ancestor = element;
        while (ancestor is not null)
        {
            if (ancestor is UIElement { Visibility: Visibility.Collapsed }) return false;
            if (ancestor is ScrollViewer scroll)
            {
                var viewport = Bounds(scroll);
                var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                if (!viewport.Contains(center)) return false;
            }
            ancestor = VisualTreeHelper.GetParent(ancestor);
        }
        return true;
    }

    private FrameworkElement? FindVisibleCover(DependencyObject? root, string key)
    {
        if (root is null) return null;
        if (root is FrameworkElement element && Motion.GetSharedCoverKey(root) == key && Visible(element)) return element;
        if (root is UIElement { Visibility: Visibility.Collapsed }) return null;
        // 只读取已实现的视觉树，不调用 ScrollIntoView，不强制实现离屏容器。
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindVisibleCover(VisualTreeHelper.GetChild(root, index), key) is { } cover) return cover;
        }
        return null;
    }

    private static bool IsWithin(DependencyObject element, DependencyObject parent)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (ReferenceEquals(current, parent)) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private static ImageSource? ImageOf(FrameworkElement element) => element switch
    {
        Image image => image.Source,
        PersonPicture person => person.ProfilePicture,
        _ => null
    };

    private static FrameworkElement? FindImage(DependencyObject? root)
    {
        if (root is null) return null;
        if (root is FrameworkElement element && ImageOf(element) is not null) return element;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindImage(VisualTreeHelper.GetChild(root, index)) is { } cover) return cover;
        }
        return null;
    }

    private static float RadiusInRoot(FrameworkElement element, Rect bounds)
    {
        var scale = Math.Min(bounds.Width / element.ActualWidth, bounds.Height / element.ActualHeight);
        return Math.Min((float)(RadiusOf(element) * scale), (float)Math.Min(bounds.Width, bounds.Height) / 2);
    }

    private static float RadiusOf(FrameworkElement element)
    {
        if (element is PersonPicture) return (float)Math.Min(element.ActualWidth, element.ActualHeight) / 2;
        DependencyObject? ancestor = element;
        while (ancestor is not null && ancestor is not Page)
        {
            if (ancestor is ContentPresenter or ListViewItem or GridViewItem or UserControl) break;
            if (ancestor is Border border) return (float)border.CornerRadius.TopLeft;
            ancestor = VisualTreeHelper.GetParent(ancestor);
        }
        return 0;
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs args)
    {
        _explicitSource = null;
        CancelFlight();
        UpdateClip();
    }

    private void UpdateClip() => _overlay.Clip = new RectangleGeometry
    {
        Rect = new Rect(0, 0, Math.Max(0, _root.ActualWidth), Math.Max(0, _root.ActualHeight))
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _navigationVersion++;
        _cancelNavigationLoad?.Invoke();
        _preparedNavigation = null;
        _explicitSource = null;
        CancelFlight();
        _root.SizeChanged -= OnRootSizeChanged;
        if (ReferenceEquals(Current, this)) Application.Current.Resources.Remove(ResourceKey);
    }
}
