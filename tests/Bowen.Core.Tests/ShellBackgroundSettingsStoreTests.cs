using Bowen.Core.Models;
using Bowen.Core.Services.Implementations;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// 主窗口外壳背景偏好的本地存储。零网络、零真实用户目录。
/// </summary>
/// <remarks>
/// 与主题设置同一套形状：读写、坏文件回默认且不删、枚举与颜色夹取。
/// </remarks>
public sealed class ShellBackgroundSettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bowen-shell-background-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_directory, "shell-background.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonShellBackgroundSettingsStore NewStore() => new(Path_);

    private void WriteRaw(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, json);
    }

    [Fact]
    public void NoFile_FallsBackToCoverAmbient()
    {
        var loaded = NewStore().Load();

        Assert.Equal(ShellBackgroundMode.CoverAmbient, loaded.Mode);
        Assert.Equal(ShellBackgroundSettings.DefaultSolidColorArgb, loaded.SolidColorArgb);
        Assert.Equal(ShellBackgroundSettings.DefaultLightSolidColorArgb, loaded.LightSolidColorArgb);
        Assert.False(loaded.SolidColorAuto);
        Assert.Equal(string.Empty, loaded.ImageFile);
        Assert.Equal(ShellBackgroundSettings.DefaultImageDim, loaded.ImageDim);
        Assert.Equal(ShellBackgroundSettings.DefaultImageBlur, loaded.ImageBlur);
        Assert.Equal(0.5d, loaded.ImageFocusX);
        Assert.Equal(0.5d, loaded.ImageFocusY);
        Assert.Equal(1d, loaded.ImageZoom);
    }

    [Fact]
    public void RoundTripsTheChoice()
    {
        NewStore().Save(new ShellBackgroundSettings
        {
            Mode = ShellBackgroundMode.SolidColor,
            SolidColorArgb = 0xFF2C2542,
            LightSolidColorArgb = 0xFFF0EBF7,
            SolidColorAuto = true,
            ImageFile = "shell-bg-test.png",
            ImageDim = 0.3,
            ImageBlur = 24,
            ImageFocusX = 0.25,
            ImageFocusY = 0.75,
            ImageZoom = 2,
        });

        var loaded = NewStore().Load();

        Assert.Equal(ShellBackgroundMode.SolidColor, loaded.Mode);
        Assert.Equal(0xFF2C2542u, loaded.SolidColorArgb);
        Assert.Equal(0xFFF0EBF7u, loaded.LightSolidColorArgb);
        Assert.True(loaded.SolidColorAuto);
        Assert.Equal("shell-bg-test.png", loaded.ImageFile);
        Assert.Equal(0.3d, loaded.ImageDim);
        Assert.Equal(24d, loaded.ImageBlur);
        Assert.Equal(0.25d, loaded.ImageFocusX);
        Assert.Equal(0.75d, loaded.ImageFocusY);
        Assert.Equal(2d, loaded.ImageZoom);
    }

    /// <summary>数字枚举能反序列化成未定义值，必须被夹回封面取色。</summary>
    [Fact]
    public void UndefinedMode_FallsBackToCoverAmbient()
    {
        WriteRaw("""{ "Mode": 99, "SolidColorArgb": 4282002972, "SolidColorAuto": true }""");

        var loaded = NewStore().Load();

        Assert.Equal(ShellBackgroundMode.CoverAmbient, loaded.Mode);
        Assert.True(loaded.SolidColorAuto);

        // 0xAARRGGBB 的十进制，用来确认颜色本身没被连带重置。
        Assert.Equal(0xFF3A2E1Cu, loaded.SolidColorArgb);
        Assert.Equal(ShellBackgroundSettings.DefaultLightSolidColorArgb, loaded.LightSolidColorArgb);
    }

    [Fact]
    public void ZeroColor_FallsBackToDefaultAndForcesOpaque()
    {
        WriteRaw("""{ "Mode": "SolidColor", "SolidColorArgb": 0, "LightSolidColorArgb": 0, "SolidColorAuto": false }""");

        var loaded = NewStore().Load();

        Assert.Equal(ShellBackgroundSettings.DefaultSolidColorArgb, loaded.SolidColorArgb);
        Assert.Equal(ShellBackgroundSettings.DefaultLightSolidColorArgb, loaded.LightSolidColorArgb);

        WriteRaw("""{ "Mode": "SolidColor", "SolidColorArgb": 2372170, "LightSolidColorArgb": 15922681, "SolidColorAuto": false }""");

        var opaque = NewStore().Load();

        // 0x0024324A / 0x00F2F5F9 没有 alpha，读回时必须顶成不透明。
        Assert.Equal(0xFF24324Au, opaque.SolidColorArgb);
        Assert.Equal(0xFFF2F5F9u, opaque.LightSolidColorArgb);
    }

    [Fact]
    public void NormalizesImageSettings()
    {
        var loaded = new ShellBackgroundSettings
        {
            ImageFile = @"..\evil.png",
            ImageDim = 2,
            ImageBlur = 99,
            ImageFocusX = -1,
            ImageFocusY = 3,
            ImageZoom = 9,
        }.Normalized();

        Assert.Equal("evil.png", loaded.ImageFile);
        Assert.Equal(ShellBackgroundSettings.MaximumImageDim, loaded.ImageDim);
        Assert.Equal(ShellBackgroundSettings.MaximumImageBlur, loaded.ImageBlur);
        Assert.Equal(0d, loaded.ImageFocusX);
        Assert.Equal(1d, loaded.ImageFocusY);
        Assert.Equal(ShellBackgroundSettings.MaximumImageZoom, loaded.ImageZoom);
    }

    /// <summary>两套预设必须各自适合自己的主题：深色全暗、浅色全亮。</summary>
    [Fact]
    public void SolidPresets_MatchTheirTheme()
    {
        foreach (var (argb, name) in ShellBackgroundSettings.DarkSolidPresets)
        {
            Assert.True(Luminance(argb) < 0.30, $"{name} 对深色主题太亮");
        }

        foreach (var (argb, name) in ShellBackgroundSettings.LightSolidPresets)
        {
            Assert.True(Luminance(argb) > 0.85, $"{name} 对浅色主题太暗");
        }
    }

    private static double Luminance(uint argb)
        => ((0.2126 * ((argb >> 16) & 0xFF))
            + (0.7152 * ((argb >> 8) & 0xFF))
            + (0.0722 * (argb & 0xFF))) / 255.0;

    [Fact]
    public void CorruptFile_FallsBackToDefaultAndKeepsTheFile()
    {
        WriteRaw("{ not json");

        var loaded = NewStore().Load();

        Assert.Equal(ShellBackgroundSettings.Default, loaded);
        Assert.True(File.Exists(Path_));
    }
}
