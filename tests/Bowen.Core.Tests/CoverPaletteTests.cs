using Bowen.Core.Media;
using Xunit;

namespace Bowen.Core.Tests;

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

    /// <summary>
    /// 大片白底 + 小面积、低饱和的彩色主体，也应该取到主体。
    /// </summary>
    /// <remarks>
    /// <b>这条守的是「先找彩色候选」那段。</b> 只按面积开平方排序时，30% 的白底
    /// （`sqrt(1080) × 0.095 ≈ 3.11`）会赢过 0.8% 的棕色
    /// （`sqrt(30) × 0.50 ≈ 2.74`），主色变成白色后氛围色直接退回默认配色。
    /// </remarks>
    [Fact]
    public void LargeWhiteFieldWithSmallDullPatch_PicksThePatch()
    {
        var white = new (byte, byte, byte)[1080];
        var brown = new (byte, byte, byte)[30];

        for (var i = 0; i < white.Length; i++)
        {
            white[i] = (254, 254, 254);
        }

        for (var i = 0; i < brown.Length; i++)
        {
            brown[i] = (134, 104, 72);
        }

        var dominant = ColorQuantizer.Dominant(Bgra([.. white, .. brown]));

        Assert.NotNull(dominant);
        Assert.True(
            dominant.Value.R > dominant.Value.B + 20,
            $"期望取到棕色主体，实际取到 {dominant.Value}");
    }

    /// <summary>单个饱和像素不能靠「饱和度满分」压过整片白底。</summary>
    [Fact]
    public void SingleSaturatedPixel_DoesNotOverrideLargeWhiteField()
    {
        var white = new (byte, byte, byte)[1000];
        var red = new (byte, byte, byte)[1];

        for (var i = 0; i < white.Length; i++)
        {
            white[i] = (254, 254, 254);
        }

        red[0] = (220, 40, 60);
        var dominant = ColorQuantizer.Dominant(Bgra([.. white, .. red]));

        Assert.NotNull(dominant);
        Assert.True(
            dominant.Value.G > 200 && dominant.Value.B > 200,
            $"期望仍取到白底，实际取到 {dominant.Value}");
    }

    [Fact]
    public void IsDeterministic()
    {
        var bytes = Bgra([.. Enumerable.Repeat(((byte)12, (byte)200, (byte)160), 300)]);

        Assert.Equal(ColorQuantizer.Dominant(bytes), ColorQuantizer.Dominant(bytes));
    }

    // ── ColorQuantizer.Palette ──────────────────────────────────────────────

    [Fact]
    public void Palette_EmptyInput_ReturnsNull()
    {
        Assert.Null(ColorQuantizer.Palette([]));
    }

    [Fact]
    public void Palette_AllTransparent_ReturnsNull()
    {
        Assert.Null(ColorQuantizer.Palette(new byte[4 * 100]));
    }

    /// <summary>两种明显不同的颜色应当都进入种子，而不是只留一种色相。</summary>
    [Fact]
    public void Palette_TwoColoredPatches_ReturnsBothHues()
    {
        var red = new (byte, byte, byte)[400];
        var blue = new (byte, byte, byte)[400];

        for (var i = 0; i < red.Length; i++)
        {
            red[i] = (220, 40, 60);
        }

        for (var i = 0; i < blue.Length; i++)
        {
            blue[i] = (40, 80, 220);
        }

        var extraction = ColorQuantizer.Palette(Bgra([.. red, .. blue]));

        Assert.NotNull(extraction);
        Assert.Equal(2, extraction.Seeds.Count);
        Assert.Contains(extraction.Seeds, color => color.R > color.B + 60);
        Assert.Contains(extraction.Seeds, color => color.B > color.R + 60);
    }

    /// <summary>
    /// 近似色不能同时入选。实测橙红封面上两个 RGB 只差 29 的橙色会同时进入种子，
    /// 第三个颜色变成重复色；通道差阈值必须挡住这种。
    /// </summary>
    [Fact]
    public void Palette_NearDuplicateColors_DoesNotRepeat()
    {
        var pixels = new (byte, byte, byte)[900];
        Array.Fill(pixels, ((byte)232, (byte)104, (byte)37), 0, 500);
        Array.Fill(pixels, ((byte)231, (byte)89, (byte)8), 500, 200);
        Array.Fill(pixels, ((byte)250, (byte)215, (byte)198), 700, 200);

        var extraction = ColorQuantizer.Palette(Bgra(pixels));

        Assert.NotNull(extraction);
        Assert.Equal(extraction.Seeds.Count, extraction.Seeds.Distinct().Count());

        for (var i = 0; i < extraction.Seeds.Count; i++)
        for (var j = i + 1; j < extraction.Seeds.Count; j++)
        {
            var first = extraction.Seeds[i];
            var second = extraction.Seeds[j];
            var distance = Math.Max(
                Math.Abs(first.R - second.R),
                Math.Max(Math.Abs(first.G - second.G), Math.Abs(first.B - second.B)));

            Assert.True(distance >= 32, $"{first} 与 {second} 太接近，不该同时出现");
        }
    }

    /// <summary>白底上的彩色主体应当排在种子第一位，白底只能补位。</summary>
    [Fact]
    public void Palette_LargeWhiteFieldWithSmallColoredPatch_ColoredPatchComesFirst()
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

        var extraction = ColorQuantizer.Palette(Bgra([.. white, .. red]));

        Assert.NotNull(extraction);
        Assert.NotEmpty(extraction.Seeds);
        Assert.True(
            extraction.Seeds[0].R > extraction.Seeds[0].G + 100,
            $"期望第一个种子是红色主体，实际是 {extraction.Seeds[0]}");
    }

    /// <summary>灰阶封面应当返回明暗不同的中性色，而不是空或默认配色。</summary>
    [Fact]
    public void Palette_GrayCover_ReturnsNeutralSeeds()
    {
        var dark = new (byte, byte, byte)[300];
        var middle = new (byte, byte, byte)[600];
        var light = new (byte, byte, byte)[300];

        Array.Fill(dark, ((byte)80, (byte)80, (byte)80));
        Array.Fill(middle, ((byte)128, (byte)128, (byte)128));
        Array.Fill(light, ((byte)176, (byte)176, (byte)176));

        var extraction = ColorQuantizer.Palette(Bgra([.. dark, .. middle, .. light]));

        Assert.NotNull(extraction);
        Assert.Equal(0, extraction.ColoredCandidateCount);
        Assert.True(extraction.UsedFallbackFill);
        Assert.Equal(3, extraction.Seeds.Count);

        Assert.All(extraction.Seeds, color =>
        {
            var maximum = Math.Max(color.R, Math.Max(color.G, color.B));
            var minimum = Math.Min(color.R, Math.Min(color.G, color.B));

            Assert.True(maximum - minimum <= 20, $"{color} 不应带明显色相");
        });

        Assert.NotEqual(ColorPalette.DefaultAmbient, extraction.Seeds);
    }

    /// <summary>
    /// 白底封面上的浅粉条不能被 HSV 饱和度门槛漏掉。
    /// 实测 `#F7DCE3` 的 HSV 饱和度只有 0.11，但 OKLCH 彩度约 0.031，应当进入种子。
    /// </summary>
    [Fact]
    public void Palette_PalePinkAccent_IsSelected()
    {
        var pixels = new (byte, byte, byte)[1000];
        Array.Fill(pixels, ((byte)209, (byte)209, (byte)206), 0, 900);
        Array.Fill(pixels, ((byte)247, (byte)220, (byte)227), 900, 100);

        var extraction = ColorQuantizer.Palette(Bgra(pixels));

        Assert.NotNull(extraction);
        Assert.True(extraction.ColoredCandidateCount >= 1);
        Assert.True(
            extraction.Seeds[0].R > extraction.Seeds[0].B + 10,
            $"浅粉条应当排在种子第一位，实际是 {extraction.Seeds[0]}");
    }

    /// <summary>纯色封面只能取到一个真实颜色，其余两团由明暗变体补足。</summary>
    [Fact]
    public void Palette_SolidColor_ReturnsOneSeedAndAmbientStillHasThree()
    {
        var extraction = ColorQuantizer.Palette(Repeat(500, (30, 90, 200)));

        Assert.NotNull(extraction);
        Assert.Single(extraction.Seeds);
        Assert.Equal(30, extraction.Seeds[0].R);
        Assert.Equal(90, extraction.Seeds[0].G);
        Assert.Equal(200, extraction.Seeds[0].B);

        var ambient = ColorPalette.Ambient(extraction.Seeds, AmbientTheme.Dark);

        Assert.Equal(3, ambient.Count);
        Assert.Equal(3, ambient.Distinct().Count());
    }

    [Fact]
    public void Palette_IsDeterministic()
    {
        var bytes = Bgra(
        [
            .. Enumerable.Repeat(((byte)12, (byte)200, (byte)160), 300),
            .. Enumerable.Repeat(((byte)220, (byte)60, (byte)40), 300),
        ]);

        var first = ColorQuantizer.Palette(bytes);
        var second = ColorQuantizer.Palette(bytes);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Seeds, second.Seeds);
        Assert.Equal(first.ColoredCandidateCount, second.ColoredCandidateCount);
        Assert.Equal(first.UsedFallbackFill, second.UsedFallbackFill);
    }

    // ── ColorPalette ────────────────────────────────────────────────────────

    private static double Luminance(RgbColor color)
        => ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255.0;

    [Fact]
    public void Ambient_ReturnsThreeColors()
    {
        Assert.Equal(3, ColorPalette.Ambient(new RgbColor(200, 60, 90), AmbientTheme.Dark).Count);
    }

    [Fact]
    public void Ambient_ThreeColorsAreDistinct()
    {
        var colors = ColorPalette.Ambient(new RgbColor(60, 120, 220), AmbientTheme.Dark);

        Assert.Equal(3, colors.Distinct().Count());
    }

    /// <summary>灰阶封面在两个主题下都保留自身灰调，不再换成固定默认配色。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(255)]
    public void Ambient_GreySeed_StaysNeutral(byte grey)
    {
        foreach (var theme in new[] { AmbientTheme.Dark, AmbientTheme.Light })
        {
            var colors = ColorPalette.Ambient(new RgbColor(grey, grey, grey), theme);

            Assert.NotEqual(ColorPalette.DefaultAmbient, colors);
            Assert.Equal(3, colors.Distinct().Count());

            Assert.All(colors, color =>
            {
                var maximum = Math.Max(color.R, Math.Max(color.G, color.B));
                var minimum = Math.Min(color.R, Math.Min(color.G, color.B));

                Assert.True(maximum - minimum <= 20, $"{theme} 下 {color} 不应凭空染色");
            });
        }
    }

    /// <summary>
    /// 深色主题下，暗封面也要抬到能读出色相的位置，不能发灰发浑。
    /// </summary>
    [Theory]
    [InlineData(27, 34, 74)]
    [InlineData(51, 85, 138)]
    [InlineData(199, 139, 103)]
    public void Ambient_DarkTheme_LiftsDarkSeedsOutOfMud(byte r, byte g, byte b)
    {
        var colors = ColorPalette.Ambient(new RgbColor(r, g, b), AmbientTheme.Dark);

        Assert.All(colors, color =>
        {
            var brightest = Math.Max(color.R, Math.Max(color.G, color.B));
            var darkest = Math.Min(color.R, Math.Min(color.G, color.B));

            Assert.True(brightest >= 150, $"深色主题下光晕太暗：{color}");
            Assert.True(brightest - darkest >= 40, $"深色主题下光晕接近灰色：{color}");
        });
    }

    /// <summary>同一批种子在浅色主题下必须比深色主题更亮，但色相不变。</summary>
    [Fact]
    public void Ambient_LightTheme_IsLighterThanDarkTheme()
    {
        var seed = new RgbColor(51, 85, 138);

        var dark = ColorPalette.Ambient(seed, AmbientTheme.Dark);
        var light = ColorPalette.Ambient(seed, AmbientTheme.Light);

        for (var i = 0; i < dark.Count; i++)
        {
            Assert.True(
                Luminance(light[i]) > Luminance(dark[i]) + 0.10,
                $"浅色主题应更亮：dark={dark[i]} light={light[i]}");

            Assert.True(light[i].B > light[i].R + 20, $"浅色主题丢了色相：{light[i]}");
            Assert.True(dark[i].B > dark[i].R + 20, $"深色主题丢了色相：{dark[i]}");
        }
    }

    [Fact]
    public void Ambient_MultipleSeeds_PreserveEachHue()
    {
        var red = new RgbColor(220, 40, 60);
        var blue = new RgbColor(40, 80, 220);

        var colors = ColorPalette.Ambient([red, blue], AmbientTheme.Dark);

        Assert.Equal(3, colors.Count);
        Assert.True(colors[0].R > colors[0].B, $"第一个种子应保持红色调：{colors[0]}");
        Assert.True(colors[1].B > colors[1].R, $"第二个种子应保持蓝色调：{colors[1]}");
    }

    [Fact]
    public void Ambient_KeepsSingleSeedHue_SoOverlapsStayClean()
    {
        var colors = ColorPalette.Ambient(new RgbColor(200, 60, 90), AmbientTheme.Dark);

        // 单色封面由明暗变体补足三团，色相仍应一致 —— 都偏红。
        Assert.All(colors, c => Assert.True(c.R > c.B, $"{c} 不在同一色系里"));
    }

    [Fact]
    public void Ambient_IsDeterministic()
    {
        var seed = new RgbColor(90, 140, 210);

        Assert.Equal(
            ColorPalette.Ambient(seed, AmbientTheme.Dark),
            ColorPalette.Ambient(seed, AmbientTheme.Dark));
    }

    // ── ColorPalette.Blend ──────────────────────────────────────────────────

    [Fact]
    public void Blend_EndpointsReturnExactPalettes()
    {
        RgbColor[] from = [new(10, 20, 30), new(40, 50, 60), new(70, 80, 90)];
        RgbColor[] to = [new(200, 210, 220), new(230, 240, 250), new(5, 15, 25)];

        Assert.Equal(from, ColorPalette.Blend(from, to, 0));
        Assert.Equal(to, ColorPalette.Blend(from, to, 1));
    }

    [Fact]
    public void Blend_MidpointAveragesEachChannel()
    {
        RgbColor[] from = [new(0, 100, 200)];
        RgbColor[] to = [new(100, 200, 0)];

        var middle = ColorPalette.Blend(from, to, 0.5);

        Assert.Equal(new RgbColor(50, 150, 100), middle[0]);
    }

    [Fact]
    public void Blend_ClampsAmount()
    {
        RgbColor[] from = [new(0, 0, 0)];
        RgbColor[] to = [new(255, 255, 255)];

        Assert.Equal(from, ColorPalette.Blend(from, to, -1));
        Assert.Equal(to, ColorPalette.Blend(from, to, 2));
    }

    [Fact]
    public void Blend_LengthMismatch_Throws()
    {
        RgbColor[] from = [new(0, 0, 0)];
        RgbColor[] to = [new(0, 0, 0), new(1, 1, 1)];

        Assert.Throws<ArgumentException>(() => ColorPalette.Blend(from, to, 0.5));
    }

    // ── 纯色背景 ────────────────────────────────────────────────────────────

    [Fact]
    public void DarkBackground_KeepsHueAndStaysDark()
    {
        var color = ColorPalette.DarkBackground(new RgbColor(255, 80, 120));
        var luminance = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255;

        Assert.InRange(luminance, 0.03, 0.25);

        // 红色分量的主色仍应偏红，不能把色相丢掉变成灰。
        Assert.True(color.R > color.G);
        Assert.True(color.R > color.B);
    }

    [Fact]
    public void DarkBackground_GraySeedFallsBackToDeepBlue()
    {
        Assert.Equal(new RgbColor(36, 50, 74), ColorPalette.DarkBackground(new RgbColor(128, 128, 128)));
    }
}
