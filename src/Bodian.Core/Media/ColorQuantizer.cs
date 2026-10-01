namespace Bodian.Core.Media;

/// <summary>
/// 从一张位图的像素里挑出主色。
/// </summary>
/// <remarks>
/// <para>
/// <b>用直方图分桶，不用 k-means。</b> k-means 的迭代次数与初始质心不定，同一张图
/// 可能给出不同结果 —— 那没法写稳定的单测。分桶是纯函数、一趟扫描、结果可精确断言。
/// </para>
/// <para>
/// <b>打分而不是取「出现最多的颜色」。</b> 封面里占比最大的往往是白底或黑边，
/// 直接取胜者会拿到一片死白。所以对每个桶按「<b>√像素数</b> × 饱和度 × 亮度适中度」打分。
/// </para>
/// <para>
/// <b>面积要开平方，不能直接乘。</b> 直接乘的话面积大的一定赢 —— 一片白底占 80%、
/// 彩色图案占 20% 的封面，白底照样胜出，那这套打分就等于没写。开平方把面积优势压平，
/// 让饱和度有机会说话，同时仍然尊重「出现得多才算主色」（一个孤立像素依旧赢不了大片色块）。
/// </para>
/// </remarks>
public static class ColorQuantizer
{
    /// <summary>每通道 4 位，共 16³ = 4096 个桶。</summary>
    private const int BucketShift = 4;

    private const int BucketsPerChannel = 1 << (8 - BucketShift);

    private const int BucketCount = BucketsPerChannel * BucketsPerChannel * BucketsPerChannel;

    /// <summary>完全透明的像素不参与统计。</summary>
    private const byte AlphaThreshold = 128;

    /// <summary>
    /// 挑出主色。
    /// </summary>
    /// <param name="bgra">
    /// 紧密排列的 BGRA 像素（每像素 4 字节，顺序 B、G、R、A）。
    /// <b>顺序是 BGRA 而不是 RGBA</b> —— Win2D 的 <c>GetPixelColors</c> 与 WIC 都是这个顺序。
    /// </param>
    /// <returns>
    /// 主色。输入为空、或全部像素都透明时返回 <c>null</c>，由调用方决定兜底。
    /// </returns>
    public static RgbColor? Dominant(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length < 4)
        {
            return null;
        }

        var counts = new int[BucketCount];
        var sumR = new int[BucketCount];
        var sumG = new int[BucketCount];
        var sumB = new int[BucketCount];

        var pixels = bgra.Length / 4;

        for (var i = 0; i < pixels; i++)
        {
            var offset = i * 4;

            if (bgra[offset + 3] < AlphaThreshold)
            {
                continue;
            }

            byte b = bgra[offset];
            byte g = bgra[offset + 1];
            byte r = bgra[offset + 2];

            var index = ((r >> BucketShift) << (2 * (8 - BucketShift)))
                        | ((g >> BucketShift) << (8 - BucketShift))
                        | (b >> BucketShift);

            counts[index]++;
            sumR[index] += r;
            sumG[index] += g;
            sumB[index] += b;
        }

        var bestWeight = 0.0;
        RgbColor? best = null;

        for (var index = 0; index < BucketCount; index++)
        {
            var count = counts[index];

            if (count == 0)
            {
                continue;
            }

            var r = (byte)(sumR[index] / count);
            var g = (byte)(sumG[index] / count);
            var b = (byte)(sumB[index] / count);

            var weight = Math.Sqrt(count) * Suitability(r, g, b);

            if (weight <= bestWeight)
            {
                continue;
            }

            bestWeight = weight;
            best = new RgbColor(r, g, b);
        }

        return best;
    }

    /// <summary>
    /// 这个颜色有多「适合当氛围背景」。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 系数是调出来的：<b>饱和度权重低（0.35 起步）而不是从 0 起步</b>，
    /// 否则灰阶封面会被压到只剩一点点分数，稍有噪点就被别的桶盖过去。
    /// </para>
    /// <para>
    /// 亮度惩罚以 0.55 为中心 —— 那是「中间调」。纯白（1.0）与纯黑（0.0）
    /// 都会被压到三成以下，避免取出封面的白边或黑边。
    /// </para>
    /// </remarks>
    private static double Suitability(byte r, byte g, byte b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));

        var saturation = max == 0 ? 0.0 : (double)(max - min) / max;

        // 线性化之前先归一化即可 —— 这里只是排序用，不需要严格的光度学精度。
        var luminance = ((0.2126 * r) + (0.7152 * g) + (0.0722 * b)) / 255.0;

        return (0.35 + (0.65 * saturation)) * (1 - (0.9 * Math.Abs(luminance - 0.55) / 0.55));
    }
}
