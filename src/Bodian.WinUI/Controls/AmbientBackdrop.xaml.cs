using System.ComponentModel;
using System.Numerics;
using Bodian.Core.Media;
using Bodian.WinUI.Media;
using Bodian.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 氛围背景：三团缓慢漂移的柔和光晕，颜色取自当前封面。
/// </summary>
/// <remarks>
/// 每个 Composition 光晕覆盖完整椭圆，透明外沿随它一起移动。
/// 不能把大半径渐变画在窗口大小的矩形里再平移：矩形边缘仍有颜色，
/// 一旦移入窗口就会显出硬边。窗口裁剪只放在不移动的容器上。
/// 尺寸与圆心相对容器设置，缩放窗口时由合成器同步；漂移也在合成器上运行。
/// </remarks>
public sealed partial class AmbientBackdrop : UserControl
{
    // 三团合起来覆盖窗口；每团只覆盖局部，留出色差与明暗过渡。数值都是窗口比例。
    private static readonly (float CenterX, float CenterY, float RadiusX, float RadiusY)[] BlobGeometry =
    [
        (0.12f, 0.10f, 0.66f, 0.86f),
        (0.92f, 0.32f, 0.68f, 0.90f),
        (0.48f, 0.84f, 0.72f, 0.78f),
    ];

    private static readonly (float FromX, float ToX, int SecondsX, float FromY, float ToY, int SecondsY)[] BlobDrift =
    [
        (-70, 90, 70, -40, 60, 95),
        (80, -70, 88, 50, -60, 76),
        (-60, 70, 110, 60, -40, 104),
    ];

    private static readonly float[] StopOffsets = [0, 0.25f, 0.55f, 0.82f, 1.0f];
    private static readonly byte[] StopAlphas = [0xD0, 0xAC, 0x64, 0x24, 0x00];

    private readonly List<SpriteVisual> _blobs = [];
    private readonly CoverPaletteLoader _loader;

    private IReadOnlyList<RgbColor> _palette = ColorPalette.DefaultAmbient;
    private ContainerVisual? _root;
    private PlayerViewModel? _player;
    private int _paletteRequest;

    public AmbientBackdrop()
    {
        InitializeComponent();

        _loader = new CoverPaletteLoader(
            (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory)
                ?.CreateLogger<CoverPaletteLoader>());

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_root is not null)
        {
            return;
        }

        var compositor = ElementCompositionPreview.GetElementVisual(Host).Compositor;
        _root = compositor.CreateContainerVisual();
        _root.RelativeSizeAdjustment = Vector2.One;
        _root.Clip = compositor.CreateInsetClip();
        ElementCompositionPreview.SetElementChildVisual(Host, _root);

        for (var i = 0; i < BlobGeometry.Length; i++)
        {
            var geometry = BlobGeometry[i];
            var blob = compositor.CreateSpriteVisual();
            blob.AnchorPoint = new Vector2(0.5f, 0.5f);
            blob.RelativeOffsetAdjustment = new Vector3(geometry.CenterX, geometry.CenterY, 0);
            blob.RelativeSizeAdjustment = new Vector2(geometry.RadiusX * 2, geometry.RadiusY * 2);
            blob.Brush = BuildBrush(compositor, _palette[i]);
            _root.Children.InsertAtTop(blob);
            _blobs.Add(blob);

            var drift = BlobDrift[i];
            StartDrift(blob, "Offset.X", drift.FromX, drift.ToX, drift.SecondsX);
            StartDrift(blob, "Offset.Y", drift.FromY, drift.ToY, drift.SecondsY);
        }

        HookPlayer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _paletteRequest++;

        if (_player is not null)
        {
            _player.PropertyChanged -= OnPlayerChanged;
            _player = null;
        }

        if (_root is null)
        {
            return;
        }

        ElementCompositionPreview.SetElementChildVisual(Host, null);
        _root.Children.RemoveAll();

        foreach (var blob in _blobs)
        {
            blob.StopAnimation("Offset.X");
            blob.StopAnimation("Offset.Y");

            if (blob.Brush is CompositionRadialGradientBrush brush)
            {
                blob.Brush = null;
                foreach (var stop in brush.ColorStops)
                {
                    stop.Dispose();
                }

                brush.Dispose();
            }
            blob.Dispose();
        }

        _blobs.Clear();
        _root.Clip.Dispose();
        _root.Dispose();
        _root = null;
    }

    private void HookPlayer()
    {
        if (_player is null && Application.Current.Resources["BodianNowPlaying"] is PlayerViewModel player)
        {
            _player = player;
            _player.PropertyChanged += OnPlayerChanged;
        }

        Refresh();
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.CurrentCoverUri) or nameof(PlayerViewModel.CurrentTrackId))
        {
            Refresh();
        }
    }

    private async void Refresh()
    {
        var player = _player;
        if (player is null)
        {
            return;
        }

        var request = ++_paletteRequest;
        var trackId = player.CurrentTrackId ?? 0;
        var coverUri = player.CurrentCoverUri;
        var palette = await _loader.LoadAsync(trackId, coverUri);

        // 控件卸载、切歌或封面变化后，不再应用此前的异步结果。
        if (request != _paletteRequest
            || _root is null
            || !ReferenceEquals(_player, player)
            || (player.CurrentTrackId ?? 0) != trackId
            || player.CurrentCoverUri != coverUri)
        {
            return;
        }

        ApplyPalette(palette);
    }

    private void ApplyPalette(IReadOnlyList<RgbColor> palette)
    {
        _palette = palette;
        for (var i = 0; i < _blobs.Count; i++)
        {
            var brush = (CompositionRadialGradientBrush)_blobs[i].Brush;
            for (var j = 0; j < StopAlphas.Length; j++)
            {
                brush.ColorStops[j].Color = CoverPaletteLoader.ToColor(palette[i], StopAlphas[j]);
            }
        }
    }

    private static CompositionRadialGradientBrush BuildBrush(Compositor compositor, RgbColor color)
    {
        var brush = compositor.CreateRadialGradientBrush();
        brush.MappingMode = CompositionMappingMode.Relative;
        brush.EllipseCenter = new Vector2(0.5f, 0.5f);
        brush.EllipseRadius = new Vector2(0.5f, 0.5f);
        brush.GradientOriginOffset = Vector2.Zero;
        brush.ExtendMode = CompositionGradientExtendMode.Clamp;

        for (var i = 0; i < StopOffsets.Length; i++)
        {
            brush.ColorStops.Add(compositor.CreateColorGradientStop(
                StopOffsets[i], CoverPaletteLoader.ToColor(color, StopAlphas[i])));
        }

        return brush;
    }

    private static void StartDrift(SpriteVisual blob, string property, float from, float to, int seconds)
    {
        var compositor = blob.Compositor;
        using var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0), new Vector2(0.58f, 1));
        using var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, from);
        animation.InsertKeyFrame(1, to, easing);
        animation.Duration = TimeSpan.FromSeconds(seconds);
        animation.Direction = AnimationDirection.Alternate;
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        blob.StartAnimation(property, animation);
    }
}
