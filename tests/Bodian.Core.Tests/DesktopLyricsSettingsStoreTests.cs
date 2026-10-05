using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 桌面歌词设置的本地存储。零网络、零真实用户目录。
/// </summary>
/// <remarks>
/// 与 <see cref="ThemeSettingsStoreTests"/> 的差别在一条：这里<b>越界值只夹那一项，
/// 不整份退回默认</b>。字号写坏了不该把颜色、双行、锁定一起抹掉，几组测试守的就是这条。
/// </remarks>
public sealed class DesktopLyricsSettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bodian-desktop-lyrics-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_directory, "desktop-lyrics.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonDesktopLyricsSettingsStore NewStore() => new(Path_);

    private void WriteRaw(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, json);
    }

    // ── 基本读写 ────────────────────────────────────────────────────────────

    [Fact]
    public void NoFile_FallsBackToDefault()
    {
        var loaded = NewStore().Load();

        Assert.Equal(DesktopLyricsSettings.DefaultFontSize, loaded.FontSize);
        Assert.Equal(DesktopLyricsSettings.DefaultTextColor, loaded.TextColorArgb);
        Assert.False(loaded.DualLine);
        Assert.Equal(DesktopLyricsAlignment.Center, loaded.Alignment);
        Assert.False(loaded.Locked);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsEveryField()
    {
        var settings = new DesktopLyricsSettings
        {
            FontSize = 64,
            TextColorArgb = 0xFF1F6FEB,
            DualLine = true,
            Alignment = DesktopLyricsAlignment.Staggered,
            Locked = true,
        };

        NewStore().Save(settings);

        Assert.Equal(settings, NewStore().Load());
    }

    [Theory]
    [InlineData(DesktopLyricsAlignment.Center)]
    [InlineData(DesktopLyricsAlignment.Staggered)]
    public void Alignment_RoundTrips(DesktopLyricsAlignment alignment)
    {
        NewStore().Save(new DesktopLyricsSettings { Alignment = alignment });

        Assert.Equal(alignment, NewStore().Load().Alignment);
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        Assert.False(Directory.Exists(_directory));

        NewStore().Save(DesktopLyricsSettings.Default);

        Assert.True(File.Exists(Path_));
    }

    // ── 枚举写成字符串 ──────────────────────────────────────────────────────

    [Fact]
    public void Save_WritesEnumAsString_NotNumber()
    {
        NewStore().Save(new DesktopLyricsSettings { Alignment = DesktopLyricsAlignment.Staggered });

        var text = File.ReadAllText(Path_);

        Assert.Contains("Staggered", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Alignment\": 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownAlignmentName_FallsBackToDefaultEntirely()
    {
        // 字符串枚举的未知名字是**反序列化期**的失败，整份文档读不出来，
        // 所以这里退回的是全默认值 —— 不是「只把对齐修回居中、其余照读」。
        // 想只夹那一项，得是数字越界那种情况（见下面的 OutOfRangeFontSize）。
        WriteRaw("""{ "FontSize": 60, "Alignment": "Diagonal", "DualLine": true }""");

        Assert.Equal(DesktopLyricsSettings.Default, NewStore().Load());
    }

    [Fact]
    public void SavedFile_IsReadableAgain()
    {
        // 手工写的 JSON 与真实写出来的必须同一套属性名，否则上面几个用例会
        // 因为「压根没绑上」而假通过。这条把两边钉在一起。
        NewStore().Save(new DesktopLyricsSettings { FontSize = 55, DualLine = true });

        var text = File.ReadAllText(Path_);

        Assert.Contains("\"FontSize\"", text, StringComparison.Ordinal);
        Assert.Contains("\"TextColorArgb\"", text, StringComparison.Ordinal);

        var loaded = NewStore().Load();

        Assert.Equal(55, loaded.FontSize);
        Assert.True(loaded.DualLine);
    }

    [Fact]
    public void UndefinedNumericAlignment_FallsBackToCenter()
    {
        // 字符串枚举挡得住未知名字，但数字能反序列化成未定义的枚举值，
        // 所以 Normalized() 里还有一道 Enum.IsDefined。
        WriteRaw("""{ "Alignment": 99 }""");

        Assert.Equal(DesktopLyricsAlignment.Center, NewStore().Load().Alignment);
    }

    // ── 越界值逐项夹 ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, DesktopLyricsSettings.MinimumFontSize)]
    [InlineData(-100, DesktopLyricsSettings.MinimumFontSize)]
    [InlineData(500, DesktopLyricsSettings.MaximumFontSize)]
    public void OutOfRangeFontSize_IsClamped_NotReset(double stored, double expected)
    {
        WriteRaw($$"""{ "FontSize": {{stored}}, "Locked": true }""");

        var loaded = NewStore().Load();

        Assert.Equal(expected, loaded.FontSize);

        // ★ 关键：别的字段不能被一起抹掉。
        Assert.True(loaded.Locked);
    }

    [Fact]
    public void HandWrittenColor_IsRead()
    {
        // 用一个与默认值不同的、本身就不透明的颜色：读回来对得上才说明真的绑上了，
        // 而不是「没绑上 → 用了默认白」这种假通过。
        // 十进制值由插值算出来，不手写 —— 手写十六进制转十进制错了两次，
        // 表现和「属性没绑上」一模一样，很难看出来。
        const uint color = 0xFF1F6FEB;
        WriteRaw($$"""{ "TextColorArgb": {{color}} }""");

        Assert.Equal(color, NewStore().Load().TextColorArgb);
    }

    [Fact]
    public void Normalized_ForcesColorOpaque()
    {
        // 颜色选择器不给透明度，但手改过的文件能写出 alpha=0 —— 那会让歌词彻底看不见。
        var settings = new DesktopLyricsSettings { TextColorArgb = 0x10FF00FF };

        Assert.Equal(0xFFFF00FFu, settings.Normalized().TextColorArgb);
    }

    [Theory]
    [InlineData(0xFFFFFFFFu)]
    [InlineData(0x00FFFFFFu)]
    [InlineData(0x80FFFFFFu)]
    public void Normalized_WhiteHighlightUsesVisibleDefault(uint color)
    {
        var settings = new DesktopLyricsSettings { TextColorArgb = color, DualLine = true,
            Alignment = DesktopLyricsAlignment.Staggered };

        var normalized = settings.Normalized();

        Assert.Equal(DesktopLyricsSettings.DefaultTextColor, normalized.TextColorArgb);
        Assert.True(normalized.DualLine);
        Assert.Equal(DesktopLyricsAlignment.Staggered, normalized.Alignment);
    }

    [Fact]
    public void Load_LegacyWhiteHighlightPreservesOtherPreferences()
    {
        WriteRaw("""{ "FontSize": 36, "TextColorArgb": 4294967295, "DualLine": true, "Alignment": "Staggered" }""");

        var loaded = NewStore().Load();

        Assert.Equal(DesktopLyricsSettings.DefaultTextColor, loaded.TextColorArgb);
        Assert.Equal(36, loaded.FontSize);
        Assert.True(loaded.DualLine);
        Assert.Equal(DesktopLyricsAlignment.Staggered, loaded.Alignment);
    }

    [Fact]
    public void Save_AlsoNormalizes()
    {
        // 内存里被设成越界值时也要收住，否则下次启动读回来的是一个从没写过的值。
        NewStore().Save(new DesktopLyricsSettings { FontSize = 400 });

        Assert.Equal(DesktopLyricsSettings.MaximumFontSize, NewStore().Load().FontSize);
    }

    // ── 坏文件 ──────────────────────────────────────────────────────────────

    [Fact]
    public void CorruptJson_FallsBackToDefault_ButFileIsKept()
    {
        WriteRaw("{ this is not json");

        Assert.Equal(DesktopLyricsSettings.DefaultFontSize, NewStore().Load().FontSize);
        Assert.True(File.Exists(Path_));
    }

    [Fact]
    public void NullJson_FallsBackToDefault()
    {
        WriteRaw("null");

        Assert.Equal(DesktopLyricsSettings.Default, NewStore().Load());
    }

    [Fact]
    public void EmptyFile_FallsBackToDefault()
    {
        WriteRaw(string.Empty);

        Assert.Equal(DesktopLyricsSettings.DefaultFontSize, NewStore().Load().FontSize);
    }

    [Fact]
    public void Save_ToUnwritablePath_DoesNotThrow()
    {
        Directory.CreateDirectory(Path_);

        NewStore().Save(new DesktopLyricsSettings { FontSize = 50 });
    }
}
