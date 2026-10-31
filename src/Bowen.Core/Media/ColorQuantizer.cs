namespace Bowen.Core.Media;

/// <summary>
/// 从一张位图的像素里挑出主色与多色配色。
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
    /// 彩色候选桶的最小面积占比。
    /// </summary>
    /// <remarks>
    /// 白底封面上的彩色主体常常只占几十个像素。没有面积下限时，单个饱和噪点也能靠
    /// 「饱和度满分」胜出；0.5% 在 96×96 上约 46 个像素，足够挡住噪点，又不会漏掉小主体。
    /// </remarks>
    private const double MinimumCandidateFraction = 0.005;

    /// <summary>彩色候选桶的最小像素数；小图上按比例算出来可能只有几个像素。</summary>
    private const int MinimumCandidatePixels = 18;

    /// <summary>
    /// 低于这个 OKLCH 彩度就不算「彩色候选」。
    /// </summary>
    /// <remarks>
    /// <b>用绝对彩度而不是 HSV 饱和度。</b> HSV 的饱和度是相对值，浅粉 `#F7DCE3` 的
    /// 饱和度只有 0.11，看起来却明显是粉色；用 0.18 的门槛会把它排除，
    /// 白底封面上的彩色条就消失了。OKLCH 彩度约 0.031，能正确识别这类浅色。
    /// 真正的灰（`#888886` 彩度约 0.002、暖灰约 0.02）仍在门槛之下。
    /// </remarks>
    private const double MinimumCandidateChroma = 0.025;

    /// <summary>亮度低于这个值按黑边处理，高于上限按白边处理。</summary>
    private const double CandidateMinimumLuminance = 0.10;

    private const double CandidateMaximumLuminance = 0.92;

    /// <summary>彩色候选里饱和度的权重；比兜底那条高，避免大面积的近灰再次取胜。</summary>
    private const double CandidateSaturationWeight = 0.10;

    /// <summary>没有任何彩色候选时，沿用原来的低饱和度基数，让灰阶封面仍能稳定选出主色。</summary>
    private const double FallbackSaturationWeight = 0.35;

    /// <summary>多色提取最多返回的颜色数，与氛围背景的三团光晕一致。</summary>
    public const int MaximumPaletteColors = 3;

    /// <summary>两个彩色种子之间至少差这么多色相，才算「另一种颜色」。</summary>
    private const double MinimumHueDistance = 24;

    /// <summary>凑不满时放宽的色相距离。</summary>
    private const double RelaxedHueDistance = 15;

    /// <summary>
    /// 两个种子之间至少差这么多通道值，避免选到几乎一样的颜色。
    /// </summary>
    /// <remarks>
    /// 24 太松：实测橙红封面上两个 RGB 只差 29 的橙色会同时入选，第三个种子变成重复色。
    /// 32 能挡住这类近似色，又不会把灰阶封面的暗部与中间调拆开。
    /// </remarks>
    private const int MinimumRgbDistance = 32;

    /// <summary>中性色补位时希望拉开的明暗差。</summary>
    private const double MinimumLuminanceDistance = 0.12;

    /// <summary>中性色补位优先避开纯白与纯黑，最后才放开。</summary>
    private const double NeutralMinimumLuminance = 0.15;

    private const double NeutralMaximumLuminance = 0.85;

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
    /// <remarks>
    /// <b>先找彩色候选，找不到才退回面积主色。</b> 只按「面积开平方 × 适合度」排序时，
    /// 白底封面上的白色桶经常赢过小面积彩色主体（30% 白底可以压过 0.8% 的棕色），
    /// 主色变成白色后，氛围色会失去色相。
    /// 两段式先把近白、近黑和低饱和的桶排除，再在剩下的桶里按面积与饱和度排序。
    /// </remarks>
    public static RgbColor? Dominant(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length < 4)
        {
            return null;
        }

        var buckets = BuildBuckets(bgra, out var visiblePixels);

        if (visiblePixels == 0)
        {
            return null;
        }

        var minimumCandidateCount = MinimumCandidateCount(visiblePixels);

        var bestCandidateWeight = 0.0;
        RgbColor? bestCandidate = null;

        var bestFallbackWeight = 0.0;
        RgbColor? bestFallback = null;

        foreach (var bucket in buckets)
        {
            if (bucket.Count == 0)
            {
                continue;
            }

            var fallbackWeight = Math.Sqrt(bucket.Count)
                * Suitability(bucket.Saturation, bucket.Luminance, FallbackSaturationWeight);

            if (fallbackWeight > bestFallbackWeight)
            {
                bestFallbackWeight = fallbackWeight;
                bestFallback = bucket.Color;
            }

            if (!IsColoredCandidate(bucket, minimumCandidateCount))
            {
                continue;
            }

            var candidateWeight = Math.Sqrt(bucket.Count)
                * Suitability(bucket.Saturation, bucket.Luminance, CandidateSaturationWeight);

            if (candidateWeight <= bestCandidateWeight)
            {
                continue;
            }

            bestCandidateWeight = candidateWeight;
            bestCandidate = bucket.Color;
        }

        return bestCandidate ?? bestFallback;
    }

    /// <summary>
    /// 提取 1–3 个封面里真实存在的颜色，供氛围背景使用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>不再由单个主色派生同族色。</b> 单主色加 ±40° 偏移只能得到一种色相，
    /// 整页看起来像一套固定渐变，与封面的色彩构成无关。这里先按得分取最高的一色，
    /// 再挑选与已选颜色色相差足够大的候选，得到真正来自封面的多个色相。
    /// </para>
    /// <para>
    /// <b>灰阶封面不再返回空或默认配色。</b> 没有彩色候选时挑选明暗不同的中性色，
    /// 让背景保持封面本身的灰调；真正的默认配色只留给没有封面和解码失败。
    /// </para>
    /// </remarks>
    /// <param name="bgra">BGRA 像素，顺序同 <see cref="Dominant"/>。</param>
    /// <param name="count">期望的颜色数，1–<see cref="MaximumPaletteColors"/>。</param>
    /// <returns>提取结果；输入为空或全部透明时返回 <c>null</c>。</returns>
    public static ExtractedPalette? Palette(ReadOnlySpan<byte> bgra, int count = MaximumPaletteColors)
    {
        if (bgra.Length < 4)
        {
            return null;
        }

        count = Math.Clamp(count, 1, MaximumPaletteColors);

        var buckets = BuildBuckets(bgra, out var visiblePixels);

        if (visiblePixels == 0)
        {
            return null;
        }

        var minimumCandidateCount = MinimumCandidateCount(visiblePixels);

        var colored = new List<ScoredBucket>();
        var fallback = new List<ScoredBucket>();

        foreach (var bucket in buckets)
        {
            if (bucket.Count == 0)
            {
                continue;
            }

            fallback.Add(new ScoredBucket(
                bucket,
                Math.Sqrt(bucket.Count) * Suitability(bucket.Saturation, bucket.Luminance, FallbackSaturationWeight)));

            if (!IsColoredCandidate(bucket, minimumCandidateCount))
            {
                continue;
            }

            colored.Add(new ScoredBucket(
                bucket,
                Math.Sqrt(bucket.Count) * Suitability(bucket.Saturation, bucket.Luminance, CandidateSaturationWeight)));
        }

        colored.Sort(static (left, right) => right.Weight.CompareTo(left.Weight));
        fallback.Sort(static (left, right) => right.Weight.CompareTo(left.Weight));

        var seeds = new List<RgbColor>(count);

        // 先按色相拉开，凑不满再放宽；同一色相的两个桶只当作一种颜色。
        AddColoredSeeds(seeds, colored, count, MinimumHueDistance);
        if (seeds.Count < count)
        {
            AddColoredSeeds(seeds, colored, count, RelaxedHueDistance);
        }

        if (seeds.Count < count)
        {
            AddColoredSeeds(seeds, colored, count, 0);
        }

        var neutralCount = seeds.Count;

        // 彩色不足时用封面里真实存在的中性色补位，而不是凭空派生色相。
        // 先避开纯白和纯黑，最后才放开，否则白底封面会补出一团白。
        AddNeutralSeeds(seeds, fallback, count, NeutralMinimumLuminance, NeutralMaximumLuminance, MinimumLuminanceDistance);
        if (seeds.Count < count)
        {
            AddNeutralSeeds(seeds, fallback, count, NeutralMinimumLuminance, NeutralMaximumLuminance, MinimumLuminanceDistance / 2);
        }

        if (seeds.Count < count)
        {
            AddNeutralSeeds(seeds, fallback, count, 0, 1, 0);
        }

        if (seeds.Count == 0)
        {
            return null;
        }

        return new ExtractedPalette(
            seeds,
            colored.Count,
            UsedFallbackFill: seeds.Count > neutralCount || colored.Count == 0);
    }

    private static int MinimumCandidateCount(int visiblePixels)
        => Math.Max(MinimumCandidatePixels, (int)Math.Ceiling(visiblePixels * MinimumCandidateFraction));

    private static bool IsColoredCandidate(Bucket bucket, int minimumCandidateCount)
        => bucket.Count >= minimumCandidateCount
            && bucket.Chroma >= MinimumCandidateChroma
            && bucket.Luminance >= CandidateMinimumLuminance
            && bucket.Luminance <= CandidateMaximumLuminance;

    private static void AddColoredSeeds(
        List<RgbColor> seeds,
        List<ScoredBucket> candidates,
        int count,
        double hueThreshold)
    {
        foreach (var (bucket, _) in candidates)
        {
            if (seeds.Count >= count)
            {
                return;
            }

            if (seeds.Any(seed => ColorMath.RgbDistance(seed, bucket.Color) < MinimumRgbDistance))
            {
                continue;
            }

            if (seeds.Any(seed => HueGap(seed, bucket.Color) < hueThreshold))
            {
                continue;
            }

            seeds.Add(bucket.Color);
        }
    }

    private static void AddNeutralSeeds(
        List<RgbColor> seeds,
        List<ScoredBucket> candidates,
        int count,
        double minimumLuminance,
        double maximumLuminance,
        double luminanceThreshold)
    {
        foreach (var (bucket, _) in candidates)
        {
            if (seeds.Count >= count)
            {
                return;
            }

            if (bucket.Luminance < minimumLuminance || bucket.Luminance > maximumLuminance)
            {
                continue;
            }

            if (seeds.Any(seed => ColorMath.RgbDistance(seed, bucket.Color) < MinimumRgbDistance))
            {
                continue;
            }

            if (luminanceThreshold > 0
                && seeds.Any(seed => Math.Abs(ColorMath.Luminance(seed) - bucket.Luminance) < luminanceThreshold))
            {
                continue;
            }

            seeds.Add(bucket.Color);
        }
    }

    /// <summary>两个颜色的色相距离；任一颜色接近灰时按「离得很远」处理。</summary>
    private static double HueGap(RgbColor first, RgbColor second)
    {
        var firstOklch = ColorMath.ToOklch(first);
        var secondOklch = ColorMath.ToOklch(second);

        if (firstOklch.Chroma < MinimumCandidateChroma || secondOklch.Chroma < MinimumCandidateChroma)
        {
            return 180;
        }

        return ColorMath.HueDistance(firstOklch.Hue, secondOklch.Hue);
    }

    /// <summary>一趟扫描把像素分进 4096 个桶，并算好桶内平均色与色度。</summary>
    private static Bucket[] BuildBuckets(ReadOnlySpan<byte> bgra, out int visiblePixels)
    {
        var counts = new int[BucketCount];
        var sumR = new int[BucketCount];
        var sumG = new int[BucketCount];
        var sumB = new int[BucketCount];

        visiblePixels = 0;
        var pixels = bgra.Length / 4;

        for (var i = 0; i < pixels; i++)
        {
            var offset = i * 4;

            if (bgra[offset + 3] < AlphaThreshold)
            {
                continue;
            }

            visiblePixels++;

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

        var buckets = new Bucket[BucketCount];

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
            var color = new RgbColor(r, g, b);
            var (_, saturation, _) = ColorMath.ToHsv(color);
            var (_, chroma, _) = ColorMath.ToOklch(color);

            buckets[index] = new Bucket(count, color, saturation, chroma, ColorMath.Luminance(color));
        }

        return buckets;
    }

    /// <summary>
    /// 这个颜色有多「适合当氛围背景」。
    /// </summary>
    /// <param name="saturation">0..1。</param>
    /// <param name="luminance">0..1。</param>
    /// <param name="saturationWeight">
    /// 饱和度为零时的基础分。彩色候选用低值（0.10），让近灰大色块不容易再赢；
    /// 没有任何彩色候选时用 0.35，保证灰阶封面仍能稳定选出主色。
    /// </param>
    /// <remarks>
    /// 亮度惩罚以 0.55 为中心 —— 那是「中间调」。纯白（1.0）与纯黑（0.0）
    /// 都会被压到三成以下，避免取出封面的白边或黑边。
    /// </remarks>
    private static double Suitability(double saturation, double luminance, double saturationWeight)
        => ((saturationWeight + ((1 - saturationWeight) * saturation))
            * (1 - (0.9 * Math.Abs(luminance - 0.55) / 0.55)));

    private readonly struct Bucket(int count, RgbColor color, double saturation, double chroma, double luminance)
    {
        public int Count { get; } = count;

        public RgbColor Color { get; } = color;

        public double Saturation { get; } = saturation;

        public double Chroma { get; } = chroma;

        public double Luminance { get; } = luminance;
    }

    private readonly record struct ScoredBucket(Bucket Bucket, double Weight)
    {
        public RgbColor Color => Bucket.Color;

        public double Luminance => Bucket.Luminance;
    }
}

/// <summary>多色提取的结果。</summary>
/// <param name="Seeds">按得分排序的封面颜色，1–3 个。</param>
/// <param name="ColoredCandidateCount">通过饱和度与亮度筛选的彩色候选桶数量。</param>
/// <param name="UsedFallbackFill">是否因为彩色候选不足而使用了其他封面颜色补位，或走了灰阶分支。</param>
public sealed record ExtractedPalette(
    IReadOnlyList<RgbColor> Seeds,
    int ColoredCandidateCount,
    bool UsedFallbackFill);
