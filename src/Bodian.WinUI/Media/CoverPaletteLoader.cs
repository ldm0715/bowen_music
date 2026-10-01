using Bodian.Core.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace Bodian.WinUI.Media;

/// <summary>
/// 取当前封面的主色，派生成三团氛围色。
/// </summary>
/// <remarks>
/// <para>
/// <b>必须走 <see cref="CoverArtUrl.Jpeg"/> 改写的地址，不能直接用原始封面地址。</b>
/// 原始地址多半是 WebP，而 <b>Win10 不预装 WebP 编解码器</b>（要另外装商店的扩展）。
/// Win2D 走 WIC，在干净的 Win10 上会<b>静默失败</b> —— 开发机装过扩展，所以看不出来。
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

    /// <summary>没有封面时的种子色。是灰的，所以 <see cref="ColorPalette.Ambient"/> 会退回品牌色相。</summary>
    private static readonly RgbColor FallbackSeed = new(128, 128, 128);

    private readonly Dictionary<long, IReadOnlyList<RgbColor>> _cache = [];
    private readonly ILogger<CoverPaletteLoader> _logger;

    private CancellationTokenSource? _cts;

    public CoverPaletteLoader(ILogger<CoverPaletteLoader>? logger = null)
        => _logger = logger ?? NullLogger<CoverPaletteLoader>.Instance;

    /// <summary>
    /// 取某一首的颜色。<b>永远不会抛</b> —— 失败时退回品牌色相的默认配色。
    /// </summary>
    /// <remarks>
    /// <b>切歌竞态要自己挡。</b> 连着切两首时，前一首的解码可能后完成，
    /// 结果是把旧歌的颜色刷到界面上。每次调用会取消上一次的 CTS，
    /// 返回前再确认一次没被取消。
    /// </remarks>
    public async Task<IReadOnlyList<RgbColor>> LoadAsync(long trackId, Uri? cover)
    {
        if (_cache.TryGetValue(trackId, out var cached))
        {
            return cached;
        }

        var previous = _cts;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        previous?.Cancel();

        var palette = await ExtractAsync(cover, token).ConfigureAwait(true);

        if (token.IsCancellationRequested)
        {
            // 已经被更新的一次调用取代，结果作废（但仍然返回，调用方自己会比对曲目 id）。
            return palette;
        }

        if (_cache.Count >= CacheLimit)
        {
            _cache.Clear();
        }

        _cache[trackId] = palette;

        return palette;
    }

    private async Task<IReadOnlyList<RgbColor>> ExtractAsync(Uri? cover, CancellationToken token)
    {
        var url = CoverArtUrl.Jpeg(cover, SampleSize);

        if (url is null)
        {
            _logger.LogDebug("没有封面地址，氛围色退回品牌色相");

            return ColorPalette.Ambient(FallbackSeed);
        }

        try
        {
            using var bitmap = await CanvasBitmap.LoadAsync(CanvasDevice.GetSharedDevice(), url);

            token.ThrowIfCancellationRequested();

            var pixels = bitmap.GetPixelColors();

            // Win2D 给的是 BGRA —— 与 ColorQuantizer 约定的顺序一致，逐字节摊平即可。
            var bgra = new byte[pixels.Length * 4];

            for (var i = 0; i < pixels.Length; i++)
            {
                var color = pixels[i];

                bgra[(i * 4) + 0] = color.B;
                bgra[(i * 4) + 1] = color.G;
                bgra[(i * 4) + 2] = color.R;
                bgra[(i * 4) + 3] = color.A;
            }

            var dominant = ColorQuantizer.Dominant(bgra);

            _logger.LogInformation(
                "氛围取色：解码 {Width}×{Height}，主色 {Dominant}，地址 {Url}",
                bitmap.SizeInPixels.Width,
                bitmap.SizeInPixels.Height,
                dominant is null ? "（无）" : $"#{dominant.Value.R:X2}{dominant.Value.G:X2}{dominant.Value.B:X2}",
                url);

            return dominant is { } seed
                ? ColorPalette.Ambient(seed)
                : ColorPalette.Ambient(FallbackSeed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 取不到色只是背景退回默认，不该影响播放。断网、解码失败、地址失效都走这里。
            //
            // ★ 一定要记日志。**静默失败会让这个功能看起来「没生效」而不是「出错了」** ——
            //   实测踩过：win10 上 Win2D 解不了 webp，异常被吞掉，界面只是颜色不变，
            //   完全看不出是解码失败。
            _logger.LogWarning(ex, "氛围取色失败，退回品牌色相。地址 {Url}", url);

            return ColorPalette.Ambient(FallbackSeed);
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
