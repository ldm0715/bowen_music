using System.Diagnostics;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI;

namespace Bodian.WinUI.Controls;

public sealed class AlbumCoverReflection : UserControl
{
    public static readonly DependencyProperty CoverUriProperty = DependencyProperty.Register(
        nameof(CoverUri), typeof(Uri), typeof(AlbumCoverReflection), new PropertyMetadata(null, OnCoverChanged));

    private readonly CanvasControl _canvas;
    private readonly ILogger? _logger;
    private readonly DispatcherQueueTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private CanvasDevice? _device;
    private CanvasBitmap? _bitmap;
    private CanvasLinearGradientBrush? _fade;
    private CanvasLinearGradientBrush? _edgeFade;
    private CanvasRenderTarget? _reflection;
    private Uri? _requestedUri;
    private int _request;
    private bool _loaded;
    private bool _paused;
    private bool _reflectionDirty = true;

    public AlbumCoverReflection()
    {
        IsHitTestVisible = false;
        _canvas = new CanvasControl { ClearColor = Color.FromArgb(0, 0, 0, 0) };
        Content = _canvas;
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(50);
        _timer.Tick += (_, _) => _canvas.Invalidate();
        _logger = (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory)
            ?.CreateLogger<AlbumCoverReflection>();
        _canvas.CreateResources += OnCreateResources;
        _canvas.Draw += OnDraw;
        _canvas.SizeChanged += (_, _) => _reflectionDirty = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public Uri? CoverUri
    {
        get => (Uri?)GetValue(CoverUriProperty);
        set => SetValue(CoverUriProperty, value);
    }

    public bool IsPaused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (_loaded && !value) _timer.Start();
            else _timer.Stop();
        }
    }

    private static void OnCoverChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => _ = ((AlbumCoverReflection)sender).LoadCoverAsync();

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _loaded = true;
        if (!_paused) _timer.Start();
        _canvas.Invalidate();
        _ = LoadCoverAsync();
    }

    private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        _request++;
        ClearResources();
        _requestedUri = null;
        _device = sender.Device;
        _fade = new CanvasLinearGradientBrush(_device, new CanvasGradientStop[]
        {
            new() { Position = 0, Color = Color.FromArgb(255, 255, 255, 255) },
            new() { Position = 0.18f, Color = Color.FromArgb(144, 255, 255, 255) },
            new() { Position = 0.45f, Color = Color.FromArgb(44, 255, 255, 255) },
            new() { Position = 0.9f, Color = Color.FromArgb(0, 255, 255, 255) },
            new() { Position = 1, Color = Color.FromArgb(0, 255, 255, 255) },
        });
        _edgeFade = new CanvasLinearGradientBrush(_device, new CanvasGradientStop[]
        {
            new() { Position = 0, Color = Color.FromArgb(0, 255, 255, 255) },
            new() { Position = 0.04f, Color = Color.FromArgb(255, 255, 255, 255) },
            new() { Position = 0.96f, Color = Color.FromArgb(255, 255, 255, 255) },
            new() { Position = 1, Color = Color.FromArgb(0, 255, 255, 255) },
        });
        _ = LoadCoverAsync();
    }

    private async Task LoadCoverAsync()
    {
        if (!_loaded || _device is not { } device) return;
        var uri = CoverUri;
        if (uri == _requestedUri && _bitmap is not null) return;
        _requestedUri = uri;
        var request = ++_request;
        _bitmap?.Dispose();
        _bitmap = null;
        _reflectionDirty = true;
        if (uri is null) { _canvas.Invalidate(); return; }
        try
        {
            var bitmap = await CanvasBitmap.LoadAsync(device, uri);
            if (!_loaded || request != _request || device != _device)
            {
                bitmap.Dispose();
                return;
            }
            _bitmap = bitmap;
            _reflectionDirty = true;
            _canvas.Invalidate();
        }
        catch (Exception exception)
        {
            if (_loaded && request == _request)
                _logger?.LogWarning(exception, "专辑封面倒影加载失败");
        }
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_bitmap is not { } bitmap || _fade is null || _edgeFade is null || _device is null
            || sender.ActualWidth <= 0 || sender.ActualHeight <= 0) return;
        var side = (float)sender.ActualWidth;
        var height = (float)sender.ActualHeight;
        if (_reflectionDirty || _reflection is null)
        {
            _reflection?.Dispose();
            _reflection = new CanvasRenderTarget(_device, side, height, sender.Dpi);
            var size = bitmap.Size;
            var cropSide = Math.Min(size.Width, size.Height);
            var crop = new Rect((size.Width - cropSide) / 2, (size.Height - cropSide) / 2, cropSide, cropSide);
            _fade.StartPoint = Vector2.Zero;
            _fade.EndPoint = new Vector2(0, height);
            _edgeFade.StartPoint = Vector2.Zero;
            _edgeFade.EndPoint = new Vector2(side, 0);
            using var buffer = _reflection.CreateDrawingSession();
            buffer.Clear(Color.FromArgb(0, 0, 0, 0));
            using (buffer.CreateLayer(_fade))
            using (buffer.CreateLayer(_edgeFade))
            {
                buffer.Transform = Matrix3x2.CreateScale(1, -0.85f) * Matrix3x2.CreateTranslation(0, side * 0.85f);
                buffer.DrawImage(bitmap, new Rect(0, 0, side, side), crop);
            }
            _reflectionDirty = false;
        }
        var session = args.DrawingSession;
        var time = _clock.Elapsed.TotalSeconds;
        for (var top = 0f; top < height; top += 2)
        {
            var progress = top / height;
            var amplitude = 0.3 + 4 * progress * progress;
            var displacement = (float)(amplitude * (Math.Sin(top * 0.15 + time * 1.2)
                + 0.4 * Math.Sin(top * 0.29 - time * 0.7)));
            var stripHeight = Math.Min(2, height - top);
            session.DrawImage(_reflection, new Rect(displacement, top, side, stripHeight),
                new Rect(0, top, side, stripHeight));
        }
    }

    private void ClearResources()
    {
        _bitmap?.Dispose();
        _fade?.Dispose();
        _edgeFade?.Dispose();
        _reflection?.Dispose();
        _bitmap = null;
        _fade = null;
        _edgeFade = null;
        _reflection = null;
        _reflectionDirty = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        _timer.Stop();
        _request++;
        _requestedUri = null;
        _device = null;
        ClearResources();
    }
}
