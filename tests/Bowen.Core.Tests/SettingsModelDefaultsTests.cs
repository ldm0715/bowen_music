using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Bowen.Core.Models;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// 配置模型在缺字段时的回落行为。
/// </summary>
/// <remarks>
/// <para>
/// 源生成器对 <c>init</c> 属性走「有参构造」快路径，缺字段会被塞 CLR 默认值；
/// 只有 <c>get; set;</c> 属性才走无参构造，属性初始化器才会生效。
/// </para>
/// <para>
/// 这组测试把「所有配置模型都用 <c>get; set;</c>」这条约定钉住。
/// 新增配置模型时把它加进 <see cref="SettingsTestJsonContext"/>，并补一条断言。
/// </para>
/// </remarks>
public sealed class SettingsModelDefaultsTests
{
    [Fact]
    public void Theme_UsesInitializers()
    {
        var value = Load(SettingsTestJsonContext.Default.ThemeSettings);

        Assert.Equal(AppTheme.System, value.Theme);
        Assert.True(value.AcrylicEffects);
        Assert.Equal(StartupSection.Favorites, value.StartupPage);
    }

    [Fact]
    public void Playback_UsesInitializers()
    {
        var value = Load(SettingsTestJsonContext.Default.PlaybackSettings);

        Assert.Equal(PlayMode.Sequential, value.Mode);
        Assert.True(value.RestoreQueue);
    }

    [Fact]
    public void Volume_UsesInitializers()
    {
        var value = Load(SettingsTestJsonContext.Default.VolumeSettings);

        Assert.Equal(VolumeSettings.DefaultLevel, value.Level);
    }

    [Fact]
    public void AudioEffects_UsesInitializers()
    {
        var value = Load(SettingsTestJsonContext.Default.AudioEffectSettings);

        Assert.False(value.Enabled);
        Assert.Equal(string.Empty, value.PresetId);
        Assert.Equal(string.Empty, value.AmbientId);
        Assert.Equal(AudioEffectSettings.DefaultAmbientVolume, value.AmbientVolume);
        Assert.Null(value.EqBands);
    }

    [Fact]
    public void Update_UsesInitializers()
    {
        var value = Load(SettingsTestJsonContext.Default.UpdatePreferences);

        Assert.True(value.AutoCheck);
        Assert.Equal(string.Empty, value.SkippedVersion);
    }

    [Fact]
    public void Proxy_UsesInitializers()
    {
        var value = Load(SettingsTestJsonContext.Default.ProxySettings);

        Assert.Equal(ProxyMode.Direct, value.Mode);
        Assert.Equal(string.Empty, value.Address);
    }

    [Fact]
    public void InputMethod_UsesInitializers()
    {
        var value = Load(SettingsTestJsonContext.Default.InputMethodSettings);

        Assert.False(value.CompatibilityEnabled);
    }

    [Fact]
    public void Font_UsesInitializers()
    {
        var value = Load(SettingsTestJsonContext.Default.FontSettings);

        Assert.Equal(string.Empty, value.FontFamily);
    }

    [Fact]
    public void ShellBackground_UsesInitializers()
    {
        var value = Load(SettingsTestJsonContext.Default.ShellBackgroundSettings);

        Assert.Equal(ShellBackgroundMode.CoverAmbient, value.Mode);
        Assert.Equal(ShellBackgroundSettings.DefaultImageDim, value.ImageDim);
        Assert.Equal(ShellBackgroundSettings.DefaultImageBlur, value.ImageBlur);
        Assert.Equal(0.5d, value.ImageFocusX);
        Assert.Equal(0.5d, value.ImageFocusY);
        Assert.Equal(1d, value.ImageZoom);
        Assert.Equal(ShellBackgroundSettings.DefaultSolidColorArgb, value.SolidColorArgb);
    }

    [Fact]
    public void Lyrics_UsesInitializers()
    {
        var value = Load(SettingsTestJsonContext.Default.LyricsSettings);

        Assert.True(value.ShowTranslation);
        Assert.Equal(LyricsSettings.DefaultFontSize, value.FontSize);
        Assert.Equal(LyricsAlignment.Left, value.Alignment);
        Assert.Equal(LyricsSettings.DefaultLineGap, value.LineGap);
        Assert.Equal(LyricsSettings.DefaultHighlightColor, value.HighlightColorArgb);
        Assert.Equal(0.5d, value.BackgroundImageFocusX);
        Assert.Equal(1d, value.BackgroundImageZoom);
    }

    [Fact]
    public void DesktopLyrics_UsesInitializers()
    {
        var value = Load(SettingsTestJsonContext.Default.DesktopLyricsSettings);

        Assert.False(value.IsEnabled);
        Assert.Equal(DesktopLyricsSettings.DefaultFontSize, value.FontSize);
        Assert.Equal(string.Empty, value.FontFamily);
        Assert.Equal(DesktopLyricsAlignment.Center, value.Alignment);
        Assert.False(value.PassThrough);
    }

    [Fact]
    public void LyricPoster_UsesInitializers()
    {
        var value = Load(SettingsTestJsonContext.Default.LyricPosterSettings);

        Assert.Equal(string.Empty, value.FontFamily);
        Assert.Equal(LyricPosterBackgroundSource.Auto, value.BackgroundSource);
        Assert.Equal(LyricPosterSettings.DefaultBackgroundColor, value.BackgroundColorArgb);
        Assert.Equal(LyricPosterSettings.DefaultTextColor, value.TextColorArgb);
    }

    [Fact]
    public void ViewMode_UsesInitializers()
    {
        var value = Load(SettingsTestJsonContext.Default.ViewModeSettings);

        Assert.False(value.UseGrid);
    }

    [Fact]
    public void Shortcuts_UsesInitializers()
    {
        var value = Load(SettingsTestJsonContext.Default.ShortcutSettings);

        Assert.NotNull(value.Bindings);
        Assert.Empty(value.Bindings);
    }

    private static T Load<T>(JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Deserialize("{}", typeInfo)!;
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(ThemeSettings))]
[JsonSerializable(typeof(PlaybackSettings))]
[JsonSerializable(typeof(VolumeSettings))]
[JsonSerializable(typeof(AudioEffectSettings))]
[JsonSerializable(typeof(UpdatePreferences))]
[JsonSerializable(typeof(ProxySettings))]
[JsonSerializable(typeof(InputMethodSettings))]
[JsonSerializable(typeof(FontSettings))]
[JsonSerializable(typeof(ShellBackgroundSettings))]
[JsonSerializable(typeof(LyricsSettings))]
[JsonSerializable(typeof(DesktopLyricsSettings))]
[JsonSerializable(typeof(LyricPosterSettings))]
[JsonSerializable(typeof(ViewModeSettings))]
[JsonSerializable(typeof(ShortcutSettings))]
internal sealed partial class SettingsTestJsonContext : JsonSerializerContext;
