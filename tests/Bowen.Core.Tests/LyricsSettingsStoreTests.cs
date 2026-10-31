using Bowen.Core.Models;
using Bowen.Core.Services.Implementations;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// 歌词页偏好的本地存储。零网络、零真实用户目录。
/// </summary>
/// <remarks>
/// 与 <see cref="DesktopLyricsSettingsStoreTests"/> 同一套形状：读写、原子替换、
/// 坏文件回默认且不删。默认值这条是本文件独有的 —— <b>默认显示译文</b>，
/// 因为它本来就在歌词内容里，这个开关只是给用户一个关掉的办法。
/// </remarks>
public sealed class LyricsSettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bowen-lyrics-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_directory, "lyrics.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonLyricsSettingsStore NewStore() => new(Path_);

    private void WriteRaw(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, json);
    }

    [Fact]
    public void NoFile_FallsBackToShowingTranslation()
    {
        var loaded = NewStore().Load();

        Assert.True(loaded.ShowTranslation);
        Assert.Equal(string.Empty, loaded.MiniPlayerFontFamily);
        Assert.Equal(LyricsSettings.DefaultFontSize, loaded.FontSize);
        Assert.Equal(LyricsAlignment.Left, loaded.Alignment);
        Assert.Equal(LyricsSettings.DefaultHighlightColor, loaded.HighlightColorArgb);
        Assert.Equal(LyricsSettings.DefaultLineGap, loaded.LineGap);
        Assert.True(loaded.SyncTranslationHighlight);
        Assert.Equal(LyricsSettings.DefaultTranslationFontScale, loaded.TranslationFontScale);
        Assert.Equal(LyricsSettings.DefaultTranslationLineGap, loaded.TranslationLineGap);
        Assert.Equal(LyricsSettings.DefaultEdgeBlurRatio, loaded.EdgeBlurRatio);
        Assert.Equal(LyricsBackgroundMode.CoverBlur, loaded.BackgroundMode);
        Assert.Equal(string.Empty, loaded.BackgroundImageFile);
        Assert.Equal(LyricsSettings.DefaultBackgroundDim, loaded.BackgroundImageDim);
        Assert.Equal(LyricsSettings.DefaultBackgroundBlur, loaded.BackgroundImageBlur);
        Assert.Equal(0.5d, loaded.BackgroundImageFocusX);
        Assert.Equal(0.5d, loaded.BackgroundImageFocusY);
        Assert.Equal(1d, loaded.BackgroundImageZoom);
        Assert.Equal(LyricsSettings.DefaultBackgroundColor, loaded.BackgroundColorArgb);
        Assert.Equal(LyricsSettings.DefaultBackgroundColor, loaded.BackgroundCustomColorArgb);
        Assert.False(loaded.BackgroundColorAuto);
        Assert.Equal(LyricsSettings.DefaultOffsetPercent, loaded.HorizontalOffsetPercent);
        Assert.Equal(LyricsSettings.DefaultOffsetPercent, loaded.VerticalOffsetPercent);
    }

    [Fact]
    public void RoundTripsTheChoice()
    {
        NewStore().Save(new LyricsSettings
        {
            ShowTranslation = false,
            FontFamily = "Microsoft YaHei UI",
            MiniPlayerFontFamily = "SimSun",
            FontSize = 56,
            Alignment = LyricsAlignment.Center,
            HighlightColorArgb = 0xFF4D9CFF,
            LineGap = 0,
            SyncTranslationHighlight = false,
            TranslationFontScale = 0.9,
            TranslationLineGap = 12,
            EdgeBlurRatio = 0.35,
            BackgroundMode = LyricsBackgroundMode.Mica,
            BackgroundImageFile = "lyrics-bg-test.png",
            BackgroundImageDim = 0.45,
            BackgroundImageBlur = 12,
            BackgroundImageFocusX = 0.2,
            BackgroundImageFocusY = 0.8,
            BackgroundImageZoom = 1.6,
            BackgroundColorArgb = 0xFF112233,
            BackgroundCustomColorArgb = 0xFF445566,
            BackgroundColorAuto = true,
            HorizontalOffsetPercent = 15,
            VerticalOffsetPercent = -10,
        });

        var loaded = NewStore().Load();
        Assert.False(loaded.ShowTranslation);
        Assert.Equal("Microsoft YaHei UI", loaded.FontFamily);
        Assert.Equal("SimSun", loaded.MiniPlayerFontFamily);
        Assert.Equal(56d, loaded.FontSize);
        Assert.Equal(LyricsAlignment.Center, loaded.Alignment);
        Assert.Equal(0xFF4D9CFFu, loaded.HighlightColorArgb);
        Assert.Equal(0d, loaded.LineGap);
        Assert.False(loaded.SyncTranslationHighlight);
        Assert.Equal(0.9d, loaded.TranslationFontScale);
        Assert.Equal(12d, loaded.TranslationLineGap);
        Assert.Equal(0.35d, loaded.EdgeBlurRatio);
        Assert.Equal(LyricsBackgroundMode.Mica, loaded.BackgroundMode);
        Assert.Equal("lyrics-bg-test.png", loaded.BackgroundImageFile);
        Assert.Equal(0.45d, loaded.BackgroundImageDim);
        Assert.Equal(12d, loaded.BackgroundImageBlur);
        Assert.Equal(0.2d, loaded.BackgroundImageFocusX);
        Assert.Equal(0.8d, loaded.BackgroundImageFocusY);
        Assert.Equal(1.6d, loaded.BackgroundImageZoom);
        Assert.Equal(0xFF112233u, loaded.BackgroundColorArgb);
        Assert.Equal(0xFF445566u, loaded.BackgroundCustomColorArgb);
        Assert.True(loaded.BackgroundColorAuto);
        Assert.Equal(15d, loaded.HorizontalOffsetPercent);
        Assert.Equal(-10d, loaded.VerticalOffsetPercent);
    }

    /// <summary>写盘的字段名与其它设置一致：属性名原样，不转驼峰。</summary>
    [Fact]
    public void Save_WritesTheFieldNameTheOtherSettingsUse()
    {
        NewStore().Save(new LyricsSettings { ShowTranslation = false });

        var json = File.ReadAllText(Path_);
        Assert.Contains("\"ShowTranslation\"", json, StringComparison.Ordinal);
        Assert.Contains("\"MiniPlayerFontFamily\"", json, StringComparison.Ordinal);
        Assert.Contains("\"FontSize\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Alignment\"", json, StringComparison.Ordinal);
        Assert.Contains("\"HighlightColorArgb\"", json, StringComparison.Ordinal);
        Assert.Contains("\"LineGap\"", json, StringComparison.Ordinal);
        Assert.Contains("\"SyncTranslationHighlight\"", json, StringComparison.Ordinal);
        Assert.Contains("\"TranslationFontScale\"", json, StringComparison.Ordinal);
        Assert.Contains("\"TranslationLineGap\"", json, StringComparison.Ordinal);
        Assert.Contains("\"EdgeBlurRatio\"", json, StringComparison.Ordinal);
        Assert.Contains("\"BackgroundMode\"", json, StringComparison.Ordinal);
        Assert.Contains("\"BackgroundImageFile\"", json, StringComparison.Ordinal);
        Assert.Contains("\"BackgroundImageDim\"", json, StringComparison.Ordinal);
        Assert.Contains("\"BackgroundImageBlur\"", json, StringComparison.Ordinal);
        Assert.Contains("\"BackgroundImageFocusX\"", json, StringComparison.Ordinal);
        Assert.Contains("\"BackgroundImageFocusY\"", json, StringComparison.Ordinal);
        Assert.Contains("\"BackgroundImageZoom\"", json, StringComparison.Ordinal);
        Assert.Contains("\"BackgroundColorArgb\"", json, StringComparison.Ordinal);
        Assert.Contains("\"BackgroundCustomColorArgb\"", json, StringComparison.Ordinal);
        Assert.Contains("\"BackgroundColorAuto\"", json, StringComparison.Ordinal);
        Assert.Contains("\"HorizontalOffsetPercent\"", json, StringComparison.Ordinal);
        Assert.Contains("\"VerticalOffsetPercent\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// 旧版 lyrics.json 没有外观字段。缺字段时源生成的构造会塞 CLR 默认值，
    /// 行距 0 恰好也是合法值，必须在 store 里按“文件里有没有这个属性”补默认。
    /// </summary>
    [Fact]
    public void LegacyFile_WithoutAppearanceFields_FallsBackToDefaults()
    {
        WriteRaw("""
            {
              "ShowTranslation": true,
              "FontFamily": "Microsoft YaHei UI",
              "MiniPlayerFontFamily": ""
            }
            """);

        var loaded = NewStore().Load();

        Assert.Equal(LyricsSettings.DefaultFontSize, loaded.FontSize);
        Assert.Equal(LyricsAlignment.Left, loaded.Alignment);
        Assert.Equal(LyricsSettings.DefaultHighlightColor, loaded.HighlightColorArgb);
        Assert.Equal(LyricsSettings.DefaultLineGap, loaded.LineGap);
        Assert.True(loaded.SyncTranslationHighlight);
        Assert.Equal(LyricsSettings.DefaultTranslationFontScale, loaded.TranslationFontScale);
        Assert.Equal(LyricsSettings.DefaultTranslationLineGap, loaded.TranslationLineGap);
        Assert.Equal(LyricsSettings.DefaultEdgeBlurRatio, loaded.EdgeBlurRatio);
        Assert.Equal(LyricsBackgroundMode.CoverBlur, loaded.BackgroundMode);
        Assert.Equal(string.Empty, loaded.BackgroundImageFile);
        Assert.Equal(LyricsSettings.DefaultBackgroundDim, loaded.BackgroundImageDim);
        Assert.Equal(LyricsSettings.DefaultBackgroundBlur, loaded.BackgroundImageBlur);
        Assert.Equal(0.5d, loaded.BackgroundImageFocusX);
        Assert.Equal(0.5d, loaded.BackgroundImageFocusY);
        Assert.Equal(1d, loaded.BackgroundImageZoom);
        Assert.Equal(LyricsSettings.DefaultBackgroundColor, loaded.BackgroundColorArgb);
        Assert.Equal(LyricsSettings.DefaultBackgroundColor, loaded.BackgroundCustomColorArgb);
        Assert.False(loaded.BackgroundColorAuto);
        Assert.Equal(LyricsSettings.DefaultOffsetPercent, loaded.HorizontalOffsetPercent);
        Assert.Equal(LyricsSettings.DefaultOffsetPercent, loaded.VerticalOffsetPercent);
    }

    [Theory]
    [InlineData(1, LyricsSettings.MinimumFontSize)]
    [InlineData(200, LyricsSettings.MaximumFontSize)]
    public void FontSize_IsClamped(double stored, double expected)
    {
        NewStore().Save(new LyricsSettings { FontSize = stored });

        Assert.Equal(expected, NewStore().Load().FontSize);
    }

    [Theory]
    [InlineData(-5, LyricsSettings.MinimumLineGap)]
    [InlineData(999, LyricsSettings.MaximumLineGap)]
    public void LineGap_IsClamped(double stored, double expected)
    {
        NewStore().Save(new LyricsSettings { LineGap = stored });

        Assert.Equal(expected, NewStore().Load().LineGap);
    }

    [Theory]
    [InlineData(0.1, LyricsSettings.MinimumTranslationFontScale)]
    [InlineData(2, LyricsSettings.MaximumTranslationFontScale)]
    public void TranslationFontScale_IsClamped(double stored, double expected)
    {
        NewStore().Save(new LyricsSettings { TranslationFontScale = stored });

        Assert.Equal(expected, NewStore().Load().TranslationFontScale);
    }

    [Theory]
    [InlineData(-5, LyricsSettings.MinimumTranslationLineGap)]
    [InlineData(99, LyricsSettings.MaximumTranslationLineGap)]
    public void TranslationLineGap_IsClamped(double stored, double expected)
    {
        NewStore().Save(new LyricsSettings { TranslationLineGap = stored });

        Assert.Equal(expected, NewStore().Load().TranslationLineGap);
    }

    [Theory]
    [InlineData(-1, LyricsSettings.MinimumEdgeBlurRatio)]
    [InlineData(0.9, LyricsSettings.MaximumEdgeBlurRatio)]
    public void EdgeBlurRatio_IsClamped(double stored, double expected)
    {
        NewStore().Save(new LyricsSettings { EdgeBlurRatio = stored });

        Assert.Equal(expected, NewStore().Load().EdgeBlurRatio);
    }

    [Theory]
    [InlineData(-1, LyricsSettings.MinimumBackgroundDim)]
    [InlineData(2, LyricsSettings.MaximumBackgroundDim)]
    public void BackgroundImageDim_IsClamped(double stored, double expected)
    {
        NewStore().Save(new LyricsSettings { BackgroundImageDim = stored });

        Assert.Equal(expected, NewStore().Load().BackgroundImageDim);
    }

    [Theory]
    [InlineData(-5, LyricsSettings.MinimumBackgroundBlur)]
    [InlineData(99, LyricsSettings.MaximumBackgroundBlur)]
    public void BackgroundImageBlur_IsClamped(double stored, double expected)
    {
        NewStore().Save(new LyricsSettings { BackgroundImageBlur = stored });

        Assert.Equal(expected, NewStore().Load().BackgroundImageBlur);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    public void BackgroundImageFocus_IsClamped(double stored, double expected)
    {
        NewStore().Save(new LyricsSettings
        {
            BackgroundImageFocusX = stored,
            BackgroundImageFocusY = stored,
        });

        var loaded = NewStore().Load();
        Assert.Equal(expected, loaded.BackgroundImageFocusX);
        Assert.Equal(expected, loaded.BackgroundImageFocusY);
    }

    [Theory]
    [InlineData(0.2, 1)]
    [InlineData(5, 3)]
    public void BackgroundImageZoom_IsClamped(double stored, double expected)
    {
        NewStore().Save(new LyricsSettings { BackgroundImageZoom = stored });

        Assert.Equal(expected, NewStore().Load().BackgroundImageZoom);
    }

    /// <summary>背景图只存文件名，带路径的旧值要剥成文件名。</summary>
    [Fact]
    public void BackgroundImageFile_IsStrippedToFileName()
    {
        NewStore().Save(new LyricsSettings { BackgroundImageFile = @"C:\somewhere\photo.png" });

        Assert.Equal("photo.png", NewStore().Load().BackgroundImageFile);
    }

    [Fact]
    public void BackgroundColor_ZeroFallsBackToDefaultAndIsForcedOpaque()
    {
        NewStore().Save(new LyricsSettings { BackgroundColorArgb = 0 });
        Assert.Equal(LyricsSettings.DefaultBackgroundColor, NewStore().Load().BackgroundColorArgb);

        NewStore().Save(new LyricsSettings { BackgroundColorArgb = 0x10FF00FF });
        Assert.Equal(0xFFFF00FFu, NewStore().Load().BackgroundColorArgb);
    }

    [Theory]
    [InlineData(-50, LyricsSettings.MinimumHorizontalOffsetPercent)]
    [InlineData(50, LyricsSettings.MaximumHorizontalOffsetPercent)]
    public void HorizontalOffset_IsClamped(double stored, double expected)
    {
        NewStore().Save(new LyricsSettings { HorizontalOffsetPercent = stored });

        Assert.Equal(expected, NewStore().Load().HorizontalOffsetPercent);
    }

    [Theory]
    [InlineData(-40, LyricsSettings.MinimumVerticalOffsetPercent)]
    [InlineData(40, LyricsSettings.MaximumVerticalOffsetPercent)]
    public void VerticalOffset_IsClamped(double stored, double expected)
    {
        NewStore().Save(new LyricsSettings { VerticalOffsetPercent = stored });

        Assert.Equal(expected, NewStore().Load().VerticalOffsetPercent);
    }

    [Fact]
    public void HighlightColor_IsForcedOpaque()
    {
        NewStore().Save(new LyricsSettings { HighlightColorArgb = 0x10FF00FF });

        Assert.Equal(0xFFFF00FFu, NewStore().Load().HighlightColorArgb);
    }

    [Fact]
    public void BrokenFile_FallsBackToDefaultAndKeepsTheFile()
    {
        WriteRaw("{ 这不是 JSON");

        Assert.True(NewStore().Load().ShowTranslation);

        // 坏文件留着，方便人工看一眼出了什么事。
        Assert.True(File.Exists(Path_));
    }

    /// <summary>
    /// 缺字段时落到 <b>CLR 默认值</b>（<c>false</c>），不是属性上写的那个初始值。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 源生成器给 record 生成的是「有参构造」那条快路径
    /// （<c>ObjectWithParameterizedConstructorCreator</c>，见
    /// <c>obj/.../LyricsSettingsJsonContext.LyricsSettings.g.cs</c>），
    /// 缺的字段会被塞 <c>default</c>，属性初始值根本不参与。
    /// </para>
    /// <para>
    /// <b>这对本项目每一份设置 record 都成立</b>，不是这份独有的 ——
    /// <c>LyricsSettings.ShowTranslation</c> 声明成默认开，同样会被 <c>{}</c> 关掉。
    /// 实际踩不到：<c>Save</c> 每次写完整的一份，缺字段只可能来自手工改文件。
    /// </para>
    /// <para>
    /// 这条测试是把行为钉住，省得以后有人看见 <c>{}</c> 读成 false 就去「修」属性初始值 ——
    /// 修不动。真要改，得动的是这份 record 的形状，那是全项目一起的事。
    /// </para>
    /// </remarks>
    [Fact]
    public void MissingField_FallsBackToTheClrDefault_NotTheInitializer()
    {
        WriteRaw("{}");

        Assert.False(NewStore().Load().ShowTranslation);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFile()
    {
        NewStore().Save(new LyricsSettings { ShowTranslation = false });

        Assert.False(File.Exists(Path_ + ".tmp"));
    }

    [Fact]
    public void Save_CreatesTheDirectory()
    {
        Assert.False(Directory.Exists(_directory));

        NewStore().Save(new LyricsSettings());

        Assert.True(File.Exists(Path_));
    }
}
