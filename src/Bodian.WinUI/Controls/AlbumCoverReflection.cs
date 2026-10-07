using System.Numerics;
using Bodian.Core.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI;

namespace Bodian.WinUI.Controls;

public sealed class AlbumCoverReflection : UserControl
{
    public static readonly DependencyProperty CoverUriProperty = DependencyProperty.Register(
        nameof(CoverUri), typeof(Uri), typeof(AlbumCoverReflection), new PropertyMetadata(null, OnCoverChanged));
    private readonly CompositionCanvasHost _canvas;
    private Uri? _coverUri;

    public AlbumCoverReflection()
    {
        IsHitTestVisible = false;
        var logger = (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory
            ?? NullLoggerFactory.Instance).CreateLogger<AlbumCoverReflection>();
        _canvas = new CompositionCanvasHost("倒影", (device, token) =>
            new ReflectionRenderer(device, () => Volatile.Read(ref _coverUri), token, logger), framesPerSecond: 30);
        Content = _canvas;
    }

    public Uri? CoverUri
    {
        get => (Uri?)GetValue(CoverUriProperty);
        set => SetValue(CoverUriProperty, value);
    }

    public bool IsPaused { get => _canvas.IsPaused; set => _canvas.IsPaused = value; }
    public bool IsResourceSuspended { get => _canvas.IsResourceSuspended; set => _canvas.IsResourceSuspended = value; }

    private static void OnCoverChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (AlbumCoverReflection)sender;
        Volatile.Write(ref view._coverUri, (Uri?)args.NewValue);
        view._canvas.Invalidate();
    }

    private sealed class ReflectionRenderer : ICompositionCanvasRenderer
    {
        private readonly CanvasDevice _device;
        private readonly Func<Uri?> _cover;
        private readonly CancellationToken _stop;
        private readonly ILogger _logger;
        private readonly CanvasLinearGradientBrush _fade;
        private readonly CanvasLinearGradientBrush _edgeFade;
        private CanvasBitmap? _bitmap;
        private CanvasRenderTarget? _reflection;
        private Task<CanvasBitmap>? _loading;
        private CancellationTokenSource? _loadCancellation;
        private Uri? _requested;
        private bool _reflectionDirty = true;

        public ReflectionRenderer(CanvasDevice device, Func<Uri?> cover, CancellationToken stop, ILogger logger)
        {
            _device = device;
            _cover = cover;
            _stop = stop;
            _logger = logger;
            _fade = new CanvasLinearGradientBrush(device, new CanvasGradientStop[]
            {
                new() { Position = 0, Color = Color.FromArgb(255, 255, 255, 255) },
                new() { Position = 0.18f, Color = Color.FromArgb(144, 255, 255, 255) },
                new() { Position = 0.45f, Color = Color.FromArgb(44, 255, 255, 255) },
                new() { Position = 0.9f, Color = Color.FromArgb(0, 255, 255, 255) },
            });
            _edgeFade = new CanvasLinearGradientBrush(device, new CanvasGradientStop[]
            {
                new() { Position = 0, Color = Color.FromArgb(0, 255, 255, 255) },
                new() { Position = 0.12f, Color = Color.FromArgb(255, 255, 255, 255) },
                new() { Position = 0.88f, Color = Color.FromArgb(255, 255, 255, 255) },
                new() { Position = 1, Color = Color.FromArgb(0, 255, 255, 255) },
            });
        }

        public bool Draw(CanvasDrawingSession session, double viewportWidth, double viewportHeight, TimeSpan now)
        {
            var uri = _cover();
            if (uri != _requested)
            {
                CancelLoad();
                _requested = uri;
                _bitmap?.Dispose();
                _bitmap = null;
                _reflectionDirty = true;
                if (uri is not null)
                {
                    _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_stop);
                    _loading = CanvasBitmap.LoadAsync(_device, CoverArtUrl.Jpeg(uri, 512)).AsTask(_loadCancellation.Token);
                }
            }
            if (_loading is { IsCompleted: true } loading)
            {
                _loading = null;
                try { _bitmap = loading.GetAwaiter().GetResult(); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { throw; }
                catch (Exception exception) { _logger.LogWarning(exception, "专辑封面倒影加载失败"); }
            }
            if (_bitmap is not { } bitmap) return _loading is not null;
            var side = (float)viewportWidth;
            var height = (float)viewportHeight;
            if (_reflectionDirty || _reflection is null || _reflection.Size.Width != side || _reflection.Size.Height != height)
            {
                _reflection?.Dispose();
                _reflection = new CanvasRenderTarget(_device, side, height, 96);
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
            var time = now.TotalSeconds;
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
            return true;
        }

        private void CancelLoad()
        {
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = null;
            if (_loading is { } pending)
            {
                _ = pending.ContinueWith(task =>
                {
                    try { if (task.IsCompletedSuccessfully) task.Result.Dispose(); else _ = task.Exception; }
                    catch (Exception exception) { _logger.LogDebug(exception, "释放过期倒影封面失败"); }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                _loading = null;
            }
        }

        public void Dispose()
        {
            CancelLoad();
            _bitmap?.Dispose();
            _reflection?.Dispose();
            _fade.Dispose();
            _edgeFade.Dispose();
        }
    }
}
