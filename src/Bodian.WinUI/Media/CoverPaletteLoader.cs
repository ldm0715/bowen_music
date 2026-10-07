using Bodian.Core.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace Bodian.WinUI.Media;

/// <summary>
/// 取当前封面的主色，派生成三团氛围色。
/// </summary>
/// <remarks>
/// <para>
/// <b>必须走 <see cref="CoverArtUrl.Jpeg"/> 改写的地址，不能直接用原始封面地址。</b>
/// 原始地址多半是 WebP，而 <b>Win10 不预装 WebP 编解码器</b>（要另外装商店的扩展）。
/// 解码依赖系统编解码器；改写地址避免依赖可选的 WebP 扩展。
/// 改写成 <c>.jpg</c> 顺带把图缩到 60px，代价可以忽略。
/// </para>
/// <para>
/// <b>只在小图上取色。</b> 60×60 已经足够算主色，解码与统计都在毫秒级。
/// </para>
/// </remarks>
public sealed class CoverPaletteLoader
{
    /// <summary>取色用的缩略图边长。</summary>
    private const int SampleSize = 60;

    /// <summary>缓存上限。超了就整体清空 —— 封面颜色不值得为它做 LRU。</summary>
    private const int CacheLimit = 256;

    private readonly Dictionary<long, IReadOnlyList<RgbColor>> _cache = [];
    private readonly ILogger<CoverPaletteLoader> _logger;

    private CancellationTokenSource? _cts;

    public CoverPaletteLoader(ILogger<CoverPaletteLoader>? logger = null)
        => _logger = logger ?? NullLogger<CoverPaletteLoader>.Instance;

    /// <summary>
    /// 取某一首的颜色。<b>永远不会抛</b> —— 失败时退回默认网格配色。
    /// </summary>
    /// <remarks>
    /// <b>切歌竞态要自己挡。</b> 连着切两首时，前一首的解码可能后完成，
    /// 结果是把旧歌的颜色刷到界面上。每次调用会取消上一次的 CTS，
    /// 返回前再确认一次没被取消。
    /// </remarks>
    public async Task<IReadOnlyList<RgbColor>> LoadAsync(long trackId, Uri? cover)
    {
        _cts?.Cancel();
        if (_cache.TryGetValue(trackId, out var cached)) return cached;
        using var cancellation = new CancellationTokenSource();
        _cts = cancellation;
        try
        {
            var palette = await ExtractAsync(cover, cancellation.Token).ConfigureAwait(true);
            if (cancellation.IsCancellationRequested) return palette;
            if (_cache.Count >= CacheLimit) _cache.Clear();
            _cache[trackId] = palette;
            return palette;
        }
        finally
        {
            if (ReferenceEquals(_cts, cancellation)) _cts = null;
        }
    }

    public void CancelPending() => _cts?.Cancel();

    private async Task<IReadOnlyList<RgbColor>> ExtractAsync(Uri? cover, CancellationToken token)
    {
        var url = CoverArtUrl.Jpeg(cover, SampleSize);

        if (url is null)
        {
            _logger.LogDebug("没有封面地址，氛围色退回默认网格配色");

            return ColorPalette.DefaultAmbient;
        }

        try
        {
            // 小图取色只需 CPU 像素；不为 60×60 的图片创建并常驻 D3D 共享设备。
            using var stream = await RandomAccessStreamReference.CreateFromUri(url).OpenReadAsync()
                .AsTask(token).ConfigureAwait(false);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token).ConfigureAwait(false);
            var scale = Math.Min(1, SampleSize / (double)Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            var width = Math.Max(1, (uint)Math.Round(decoder.PixelWidth * scale));
            var height = Math.Max(1, (uint)Math.Round(decoder.PixelHeight * scale));
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                new BitmapTransform { ScaledWidth = width, ScaledHeight = height },
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage)
                .AsTask(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var bgra = pixels.DetachPixelData();

            var dominant = ColorQuantizer.Dominant(bgra);

            _logger.LogInformation(
                "氛围取色：解码 {Width}×{Height}，主色 {Dominant}，地址 {Url}",
                width,
                height,
                dominant is null ? "（无）" : $"#{dominant.Value.R:X2}{dominant.Value.G:X2}{dominant.Value.B:X2}",
                url);

            return dominant is { } seed
                ? ColorPalette.Ambient(seed)
                : ColorPalette.DefaultAmbient;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 新封面请求取代了本次请求；控件会丢弃过期结果，取消不应进入 async void 异常通道。
            return ColorPalette.DefaultAmbient;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 取不到色只是背景退回默认，不该影响播放。断网、解码失败、地址失效都走这里。
            //
            // ★ 一定要记日志。**静默失败会让这个功能看起来「没生效」而不是「出错了」** ——
            //   实测踩过：win10 上 Win2D 解不了 webp，异常被吞掉，界面只是颜色不变，
            //   完全看不出是解码失败。
            _logger.LogWarning(ex, "氛围取色失败，退回默认网格配色。地址 {Url}", url);

            return ColorPalette.DefaultAmbient;
        }
    }

    /// <summary>把 <see cref="RgbColor"/> 转成 XAML 能用的颜色。</summary>
    /// <remarks>
    /// <c>Color</c> 是 <c>Windows.UI.Color</c> —— <c>Microsoft.UI</c> 下那个同名的
    /// <c>Colors</c> 是预设色的静态表，不是类型。
    /// </remarks>
    public static Color ToColor(RgbColor color, byte alpha = 255) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);
}
