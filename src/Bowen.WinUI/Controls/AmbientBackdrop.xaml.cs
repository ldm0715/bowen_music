using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Bowen.Core.Media;
using Bowen.WinUI.Media;
using Bowen.WinUI.Services;
using Bowen.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Bowen.WinUI.Controls;

/// <summary>封面驱动的柔和背景；颜色变化时缓慢过渡，静止时不刷新整窗光晕。</summary>
public sealed partial class AmbientBackdrop : UserControl
{
    private const int BitmapWidth = 384;
    private const int BitmapHeight = 256;
    private const int TransitionDurationMs = 650;
    private static readonly TimeSpan TransitionFrameInterval = TimeSpan.FromMilliseconds(40);

    private readonly CoverPaletteLoader _loader;

    /// <summary>逐帧渲染的共享缓冲区；避免颜色过渡时反复分配 384 KB 的大数组。</summary>
    private byte[]? _pixels;

    /// <summary>渲染与写回必须串行：切歌时旧帧可能还没画完。</summary>
    private readonly SemaphoreSlim _renderGate = new(1, 1);

    private IReadOnlyList<RgbColor> _palette = ColorPalette.DefaultAmbient;
    private IReadOnlyList<RgbColor> _seeds = [];
    private IReadOnlyList<RgbColor>? _transitionTarget;
    private PlayerViewModel? _player;
    private WriteableBitmap? _bitmap;
    private int _paletteRequest;
    private int _paintRequest;
    private bool _loaded;
    private bool _resourcesSuspended;

    public AmbientBackdrop()
    {
        InitializeComponent();
        _loader = new CoverPaletteLoader(
            (Application.Current.Resources["BowenLoggerFactory"] as ILoggerFactory)?.CreateLogger<CoverPaletteLoader>());
        Loaded += (_, _) =>
        {
            _loaded = true;
            if (!_resourcesSuspended) Resume();
        };
        Unloaded += (_, _) => { _loaded = false; Release(); };
        ActualThemeChanged += (_, _) => _ = RefreshThemeAsync();
    }

    public bool IsResourceSuspended
    {
        get => _resourcesSuspended;
        set
        {
            if (_resourcesSuspended == value) return;
            _resourcesSuspended = value;
            if (value) Release();
            else if (_loaded) Resume();
        }
    }

    private void Resume()
    {
        _ = PaintAsync(++_paintRequest, _palette);
        if (_player is null && Application.Current.Resources["BowenNowPlaying"] is PlayerViewModel player)
        {
            _player = player;
            player.PropertyChanged += OnPlayerChanged;
        }
        _ = RefreshAsync();
    }

    private void Release()
    {
        _paletteRequest++;
        _paintRequest++;
        _loader.CancelPending();
        if (_player is not null) _player.PropertyChanged -= OnPlayerChanged;
        _player = null;
        BackdropImage.Source = null;
        _bitmap = null;
        _pixels = null;
        _seeds = [];
        _transitionTarget = null;
    }

    private AmbientTheme CurrentTheme
        => ActualTheme == ElementTheme.Dark ? AmbientTheme.Dark : AmbientTheme.Light;

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PlayerViewModel.CurrentCoverUri) or nameof(PlayerViewModel.CurrentTrackId))
            _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_player is not { } player) return;
        var request = ++_paletteRequest;
        var trackId = player.CurrentTrackId ?? 0;
        var cover = player.CurrentCoverUri;
        var seeds = await _loader.LoadAsync(trackId, cover);
        if (request != _paletteRequest || !_loaded || _resourcesSuspended
            || !ReferenceEquals(_player, player) || (player.CurrentTrackId ?? 0) != trackId || player.CurrentCoverUri != cover) return;
        _seeds = seeds;
        var palette = ColorPalette.Ambient(seeds, CurrentTheme);
        if (_bitmap is not null && _palette.SequenceEqual(palette)) return;
        if (_transitionTarget is not null && _transitionTarget.SequenceEqual(palette)) return;
        await TransitionAsync(palette);
    }

    /// <summary>
    /// 主题切换后按同一批封面种子重新映射明度与彩度。
    /// </summary>
    /// <remarks>
    /// 暗色与浅色的目标明度不同，但色相与种子不变；重算只涉及三个颜色，成本可以忽略。
    /// 没有封面时默认配色与主题无关，不重绘。
    /// </remarks>
    private async Task RefreshThemeAsync()
    {
        if (!_loaded || _resourcesSuspended || _seeds.Count == 0) return;

        var palette = ColorPalette.Ambient(_seeds, CurrentTheme);

        if (_transitionTarget is not null && _transitionTarget.SequenceEqual(palette)) return;

        await TransitionAsync(palette);
    }

    /// <summary>
    /// 从当前颜色缓慢过渡到目标调色板。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 没有采用两张半透明位图交叉淡入：氛围图本身带 alpha，两层叠在一起时中间透明度会变化，
    /// 看起来像闪一下。这里改为逐帧插值三个主色再重绘，最终颜色准确，过渡也连续。
    /// </para>
    /// <para>
    /// 切歌时新的过渡会把 <c>_paintRequest</c> 推高，旧循环在下一帧自行退出。
    /// 起点取 <c>_palette</c>（上一帧真正画到界面上的颜色），所以连续切歌不会跳回旧颜色。
    /// </para>
    /// </remarks>
    private async Task TransitionAsync(IReadOnlyList<RgbColor> target)
    {
        var request = ++_paintRequest;
        var from = _palette.ToArray();
        var to = target.ToArray();

        if (from.SequenceEqual(to))
        {
            if (_bitmap is null && await PaintAsync(request, to)) _palette = to;
            return;
        }

        _transitionTarget = to;
        try
        {
            if (!AppMotion.IsEnabled)
            {
                if (await PaintAsync(request, to)) _palette = to;
                return;
            }

            var started = Stopwatch.GetTimestamp();
            var duration = TransitionDurationMs / 1000.0;

            while (true)
            {
                var elapsed = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
                var progress = Math.Clamp(elapsed / duration, 0, 1);

                if (progress > 0)
                {
                    // Smoothstep：两端慢、中间快，颜色不会像线性插值那样突然起步或停住。
                    var eased = progress * progress * (3 - (2 * progress));
                    var frame = ColorPalette.Blend(from, to, eased);

                    if (!await PaintAsync(request, frame)) return;
                    _palette = frame;
                }

                if (progress >= 1) return;
                await Task.Delay(TransitionFrameInterval);
            }
        }
        finally
        {
            // 只有这次请求仍是当前请求时才清；被新切歌接管时目标已经换成新的了。
            if (request == _paintRequest) _transitionTarget = null;
        }
    }

    /// <summary>渲染并写回一帧；返回假表示这次请求已被新的切歌或卸载取消。</summary>
    private async Task<bool> PaintAsync(int request, IReadOnlyList<RgbColor> palette)
    {
        await _renderGate.WaitAsync();
        try
        {
            if (request != _paintRequest || !_loaded || _resourcesSuspended) return false;

            var pixels = _pixels ??= new byte[BitmapWidth * BitmapHeight * 4];
            await Task.Run(() => AmbientBackground.Render(palette, BitmapWidth, BitmapHeight, pixels));

            if (request != _paintRequest || !_loaded || _resourcesSuspended) return false;

            _bitmap ??= new WriteableBitmap(BitmapWidth, BitmapHeight);
            using (var stream = _bitmap.PixelBuffer.AsStream()) stream.Write(pixels, 0, pixels.Length);
            _bitmap.Invalidate();
            BackdropImage.Source = _bitmap;
            return true;
        }
        finally
        {
            _renderGate.Release();
        }
    }
}
