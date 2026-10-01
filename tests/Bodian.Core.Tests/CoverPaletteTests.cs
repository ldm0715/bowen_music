using Bodian.Core.Media;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 封面取色与氛围配色。**零 UI、零文件** —— 直接喂构造出来的像素数组。
/// </summary>
public sealed class CoverPaletteTests
{
    /// <summary>把若干 RGB 像素拼成 Win2D / WIC 那个顺序的 BGRA 字节流。</summary>
    private static byte[] Bgra(params (byte R, byte G, byte B)[] pixels)
    {
        var bytes = new byte[pixels.Length * 4];

        for (var i = 0; i < pixels.Length; i++)
        {
            bytes[(i * 4) + 0] = pixels[i].B;
            bytes[(i * 4) + 1] = pixels[i].G;
            bytes[(i * 4) + 2] = pixels[i].R;
            bytes[(i * 4) + 3] = 255;
        }

        return bytes;
    }

    private static byte[] Repeat(int count, (byte R, byte G, byte B) color)
    {
        var pixels = new (byte, byte, byte)[count];

        for (var i = 0; i < count; i++)
        {
            pixels[i] = color;
        }

        return Bgra(pixels);
    }

    // ── ColorQuantizer ──────────────────────────────────────────────────────

    [Fact]
    public void EmptyInput_ReturnsNull()
    {
        Assert.Null(ColorQuantizer.Dominant([]));
    }

    [Fact]
    public void AllTransparent_ReturnsNull()
    {
        // 四个字节都写 0，alpha 就是 0。
        Assert.Null(ColorQuantizer.Dominant(new byte[4 * 100]));
    }

    [Fact]
    public void SolidImage_ReturnsThatColor()
    {
        var dominant = ColorQuantizer.Dominant(Repeat(500, (30, 90, 200)));

        Assert.NotNull(dominant);
        Assert.Equal(30, dominant.Value.R);
        Assert.Equal(90, dominant.Value.G);
        Assert.Equal(200, dominant.Value.B);
    }

    /// <summary>
    /// 大片白底 + 小块饱和色，应该取**那块彩色**，而不是白底。
    /// </summary>
    /// <remarks>
    /// <b>这条守的是「面积开平方」那个设计。</b> 若改回直接乘像素数，白底会以 8:1 的面积
    /// 压过彩色块，这个用例就会挂 —— 那正是它存在的意义。
    /// </remarks>
    [Fact]
    public void LargeWhiteFieldWithSmallSaturatedPatch_PicksThePatch()
    {
        var white = new (byte, byte, byte)[1000];
        var red = new (byte, byte, byte)[120];

        for (var i = 0; i < white.Length; i++)
        {
            white[i] = (255, 255, 255);
        }

        for (var i = 0; i < red.Length; i++)
        {
            red[i] = (220, 40, 60);
        }

        var dominant = ColorQuantizer.Dominant(Bgra([.. white, .. red]));

        Assert.NotNull(dominant);

        // 红色分量明显高于绿蓝，说明取到的是那块彩色而不是白。
        Assert.True(
            dominant.Value.R > dominant.Value.G + 100,
            $"期望取到饱和的红色块，实际取到 {dominant.Value}");
    }

    [Fact]
    public void IsDeterministic()
    {
        var bytes = Bgra([.. Enumerable.Repeat(((byte)12, (byte)200, (byte)160), 300)]);

        Assert.Equal(ColorQuantizer.Dominant(bytes), ColorQuantizer.Dominant(bytes));
    }

    // ── ColorPalette ────────────────────────────────────────────────────────

    [Fact]
    public void Ambient_ReturnsThreeColors()
    {
        Assert.Equal(3, ColorPalette.Ambient(new RgbColor(200, 60, 90)).Count);
    }

    [Fact]
    public void Ambient_ThreeColorsAreDistinct()
    {
        var colors = ColorPalette.Ambient(new RgbColor(60, 120, 220));

        Assert.Equal(3, colors.Distinct().Count());
    }

    /// <summary>灰阶封面没有色相，使用明确的默认配色，不再生成灰青色背景。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(255)]
    public void Ambient_GreySeed_FallsBackToDefaultPalette(byte grey)
    {
        var colors = ColorPalette.Ambient(new RgbColor(grey, grey, grey));

        Assert.Equal(ColorPalette.DefaultAmbient, colors);
        Assert.Equal(3, colors.Distinct().Count());
    }

    /// <summary>暗色和低饱和封面也应产生可辨认的彩色光晕。</summary>
    [Theory]
    [InlineData(8, 16, 24)]
    [InlineData(150, 170, 185)]
    [InlineData(30, 10, 12)]
    public void Ambient_DarkAndMutedSeeds_PreserveVisibleChroma(byte r, byte g, byte b)
    {
        var colors = ColorPalette.Ambient(new RgbColor(r, g, b));

        Assert.All(colors, color =>
        {
            var brightest = Math.Max(color.R, Math.Max(color.G, color.B));
            var darkest = Math.Min(color.R, Math.Min(color.G, color.B));

            Assert.True(brightest >= 160, $"光晕原始色太暗：{color}");
            Assert.True(brightest - darkest >= 80, $"光晕原始色接近灰色：{color}");
        });
    }

    [Fact]
    public void Ambient_KeepsHueFamily_SoOverlapsStayClean()
    {
        var colors = ColorPalette.Ambient(new RgbColor(200, 60, 90));

        // 三团是同一个色的 ±40° 派生，所以彼此的通道大小关系应当一致 ——
        // 都偏红。若派生逻辑坏了（例如某个色相算错跑到对面去），这条会挂。
        Assert.All(colors, c => Assert.True(c.R > c.B, $"{c} 不在同一色系里"));
    }

    [Fact]
    public void Ambient_IsDeterministic()
    {
        var seed = new RgbColor(90, 140, 210);

        Assert.Equal(ColorPalette.Ambient(seed), ColorPalette.Ambient(seed));
    }
}
