using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Bodian.Core.Media;
using Bodian.WinUI.Media;
using Bodian.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Bodian.WinUI.Controls;

/// <summary>封面驱动的柔和背景；只在颜色变化时生成一张小图，静止时不刷新整窗光晕。</summary>
public sealed partial class AmbientBackdrop : UserControl
{
    private const int BitmapWidth = 384;
    private const int BitmapHeight = 256;
    private readonly CoverPaletteLoader _loader;
    private IReadOnlyList<RgbColor> _palette = ColorPalette.DefaultAmbient;
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
            (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory)?.CreateLogger<CoverPaletteLoader>());
        Loaded += (_, _) =>
        {
            _loaded = true;
            if (!_resourcesSuspended) Resume();
        };
        Unloaded += (_, _) => { _loaded = false; Release(); };
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
        _ = PaintAsync();
        if (_player is null && Application.Current.Resources["BodianNowPlaying"] is PlayerViewModel player)
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
    }

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
        var palette = await _loader.LoadAsync(trackId, cover);
        if (request != _paletteRequest || !_loaded || _resourcesSuspended
            || !ReferenceEquals(_player, player) || (player.CurrentTrackId ?? 0) != trackId || player.CurrentCoverUri != cover) return;
        if (_bitmap is not null && _palette.SequenceEqual(palette)) return;
        _palette = palette;
        await PaintAsync();
    }

    private async Task PaintAsync()
    {
        var request = ++_paintRequest;
        var palette = _palette.ToArray();
        var pixels = await Task.Run(() => AmbientBackground.Render(palette, BitmapWidth, BitmapHeight));
        if (request != _paintRequest || !_loaded || _resourcesSuspended) return;
        _bitmap ??= new WriteableBitmap(BitmapWidth, BitmapHeight);
        using (var stream = _bitmap.PixelBuffer.AsStream()) stream.Write(pixels);
        _bitmap.Invalidate();
        BackdropImage.Source = _bitmap;
    }
}
