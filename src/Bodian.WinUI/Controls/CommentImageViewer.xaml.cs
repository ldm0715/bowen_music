using System.Numerics;
using Bodian.Core.Media;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Bodian.WinUI.Controls;

/// <summary>评论面板内的原图查看；缩放和平移仅更新合成属性，不经过滚动布局。</summary>
public sealed partial class CommentImageViewer : UserControl
{
    private readonly ImageViewportState _viewport = new();
    private readonly InputSystemCursor _panCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    private BitmapImage? _bitmap;
    private readonly Visual _imageVisual;
    private uint? _panPointerId;
    private Point _panStart;
    private double _panStartX;
    private double _panStartY;
    private bool _renderQueued;
    private int _loadGeneration;

    public CommentImageViewer()
    {
        InitializeComponent();
        ElementCompositionPreview.SetIsTranslationEnabled(FullImage, true);
        _imageVisual = ElementCompositionPreview.GetElementVisual(FullImage);
        _imageVisual.CenterPoint = Vector3.Zero;
        Loaded += (_, _) => UpdateViewport();
        Unloaded += (_, _) => { EndPan(); CancelRender(); };
    }

    public static readonly DependencyProperty ImageUriProperty = DependencyProperty.Register(
        nameof(ImageUri), typeof(Uri), typeof(CommentImageViewer), new PropertyMetadata(null, OnImageUriChanged));

    public Uri? ImageUri
    {
        get => (Uri?)GetValue(ImageUriProperty);
        set => SetValue(ImageUriProperty, value);
    }

    public event EventHandler? CloseRequested;

    private static void OnImageUriChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((CommentImageViewer)sender).LoadImage();

    private void LoadImage()
    {
        EndPan();
        CancelRender();
        var generation = ++_loadGeneration;
        FullImage.Source = null;
        _bitmap = null;
        _viewport.Reset();
        UpdateAppearance();
        ImageProgress.IsActive = ImageUri is not null;
        LoadingState.Visibility = ImageUri is null ? Visibility.Collapsed : Visibility.Visible;
        ErrorState.Visibility = Visibility.Collapsed;
        if (ImageUri is not { } uri) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (generation != _loadGeneration || ImageUri != uri) return;
            // 请求仍为原图；解码尺寸覆盖当前视口的最高倍率，避免拖动超大纹理。
            var pixels = Math.Clamp((int)Math.Ceiling(Math.Max(400, ImageViewport.ActualWidth)
                * (XamlRoot?.RasterizationScale ?? 1) * ImageViewportState.MaxZoom), 1024, 4096);
            _bitmap = new BitmapImage { DecodePixelWidth = pixels, UriSource = uri };
            FullImage.Source = _bitmap;
            ImageCloseButton.Focus(FocusState.Keyboard);
        });
    }

    private void OnImageOpened(object sender, RoutedEventArgs args)
    {
        if (ImageUri is null || _bitmap is not { PixelWidth: > 0, PixelHeight: > 0 }) return;
        ImageProgress.IsActive = false;
        LoadingState.Visibility = Visibility.Collapsed;
        _viewport.Resize(ImageViewport.ActualWidth, ImageViewport.ActualHeight);
        _viewport.SetImageSize(_bitmap.PixelWidth, _bitmap.PixelHeight);
        UpdateImageSize();
        UpdateAppearance();
        QueueRender();
    }

    private void OnImageFailed(object sender, ExceptionRoutedEventArgs args)
    {
        if (ImageUri is null) return;
        ImageProgress.IsActive = false;
        LoadingState.Visibility = Visibility.Collapsed;
        ErrorState.Visibility = Visibility.Visible;
    }

    private void FitImage()
    {
        EndPan();
        _viewport.Fit();
        UpdateImageSize();
        UpdateAppearance();
        QueueRender();
    }

    private void SetZoom(double factor, Point? anchor = null)
    {
        EndPan();
        var center = anchor ?? new Point(_viewport.ViewportWidth / 2, _viewport.ViewportHeight / 2);
        _viewport.ZoomTo(factor, center.X, center.Y);
        UpdateAppearance();
        QueueRender();
    }

    private void OnViewportPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse || !_viewport.CanPan
            || !args.GetCurrentPoint(ImageViewport).Properties.IsLeftButtonPressed || _panPointerId is not null) return;
        if (!ImageViewport.CapturePointer(args.Pointer)) return;
        _panStart = args.GetCurrentPoint(ImageViewport).Position;
        _panStartX = _viewport.OffsetX;
        _panStartY = _viewport.OffsetY;
        _panPointerId = args.Pointer.PointerId;
        args.Handled = true;
    }

    private void OnViewportPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_panPointerId != args.Pointer.PointerId) return;
        var point = args.GetCurrentPoint(ImageViewport);
        if (!point.Properties.IsLeftButtonPressed) { EndPan(); return; }
        _viewport.PanTo(_panStartX + point.Position.X - _panStart.X, _panStartY + point.Position.Y - _panStart.Y);
        // 高频鼠标事件只保留当前位移；每个显示帧最多写一次合成属性。
        QueueRender();
        args.Handled = true;
    }

    private void OnViewportPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_panPointerId != args.Pointer.PointerId) return;
        EndPan();
        args.Handled = true;
    }

    private void OnViewportPointerCaptureLost(object sender, PointerRoutedEventArgs args)
    {
        if (_panPointerId == args.Pointer.PointerId) _panPointerId = null;
    }

    private void OnViewportPointerWheelChanged(object sender, PointerRoutedEventArgs args)
    {
        if (_bitmap is null) return;
        var point = args.GetCurrentPoint(ImageViewport);
        SetZoom(_viewport.Zoom * Math.Pow(1.2, point.Properties.MouseWheelDelta / 120d), point.Position);
        args.Handled = true;
    }

    private void OnViewportManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs args)
    {
        if (_bitmap is null) return;
        _viewport.PanBy(args.Delta.Translation.X, args.Delta.Translation.Y);
        if (Math.Abs(args.Delta.Scale - 1) > 0.0001)
        {
            _viewport.ZoomTo(_viewport.Zoom * args.Delta.Scale, args.Position.X, args.Position.Y);
            UpdateAppearance();
        }
        QueueRender();
        args.Handled = true;
    }

    private void EndPan()
    {
        var wasPanning = _panPointerId is not null;
        _panPointerId = null;
        if (wasPanning) ImageViewport.ReleasePointerCaptures();
    }

    private void UpdateAppearance()
    {
        ProtectedCursor = _viewport.CanPan ? _panCursor : null;
        var hint = _viewport.CanPan ? "按住鼠标左键拖动，双击缩放" : "双击或点击 + 放大图片";
        if (PanHintText.Text != hint) PanHintText.Text = hint;
        var label = $"{_viewport.Zoom:P0}";
        if (ZoomText.Text != label) ZoomText.Text = label;
    }

    private void UpdateViewport()
    {
        var width = Math.Max(0, ImageViewport.ActualWidth);
        var height = Math.Max(0, ImageViewport.ActualHeight);
        ImageViewportClip.Rect = new Rect(0, 0, width, height);
        _viewport.Resize(width, height);
        UpdateImageSize();
        UpdateAppearance();
        QueueRender();
    }

    private void UpdateImageSize()
    {
        // 图片大小仅在加载、适应面板或视口尺寸变化时更新；拖动和缩放不改布局尺寸。
        FullImage.Width = _viewport.FittedWidth;
        FullImage.Height = _viewport.FittedHeight;
    }

    private void QueueRender()
    {
        if (_renderQueued || !IsLoaded || Visibility != Visibility.Visible) return;
        _renderQueued = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, object args)
    {
        CancelRender();
        _imageVisual.Scale = new Vector3((float)_viewport.Zoom, (float)_viewport.Zoom, 1);
        _imageVisual.Properties.InsertVector3("Translation", new Vector3((float)_viewport.OffsetX, (float)_viewport.OffsetY, 0));
    }

    private void CancelRender()
    {
        if (!_renderQueued) return;
        CompositionTarget.Rendering -= OnRendering;
        _renderQueued = false;
    }

    private void OnZoomIn(object sender, RoutedEventArgs args) => SetZoom(_viewport.Zoom * 1.5);
    private void OnZoomOut(object sender, RoutedEventArgs args) => SetZoom(_viewport.Zoom / 1.5);
    private void OnFitClick(object sender, RoutedEventArgs args) => FitImage();
    private void OnImageDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    { SetZoom(_viewport.Zoom > 1.1 ? 1 : 2, args.GetPosition(ImageViewport)); args.Handled = true; }
    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs args) => UpdateViewport();
    private void OnRetryClick(object sender, RoutedEventArgs args) => LoadImage();
    private void OnCloseClick(object sender, RoutedEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
