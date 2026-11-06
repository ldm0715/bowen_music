using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;

namespace Bowen.Core.Services.Implementations;

/// <summary>
/// 旧设置接口到统一 <see cref="ISettingsStore"/> 的适配器。
/// </summary>
/// <remarks>
/// <para>
/// <b>迁移期用，不是最终形态。</b> 现有 18 个 <c>I*SettingsStore</c> 接口和它们的调用点
/// 一行不用改，DI 换成这些适配器，读写就落到同一份 <c>config.json</c> 上。
/// 调用点逐步改为直接注入 <see cref="ISettingsStore"/> 后，这些类可以删掉。
/// </para>
/// <para>
/// <b>Load 返回副本。</b> 配置对象属性是可变的；把 <c>Current</c> 里的对象直接交出去，
/// 调用方一改就会绕过 <c>Update</c>，既不落盘也与别人不同步。
/// 返回 <c>with { }</c> 的浅拷贝，数组项（快捷键）单独复制一份。
/// </para>
/// </remarks>
public sealed class UnifiedThemeSettingsStore(ISettingsStore store) : IThemeSettingsStore
{
    public ThemeSettings Load() => store.Current.Appearance with { };

    public void Save(ThemeSettings settings) =>
        store.UpdateAndFlush(current => current.Appearance = settings.Normalized());
}

public sealed class UnifiedInputMethodSettingsStore(ISettingsStore store) : IInputMethodSettingsStore
{
    public InputMethodSettings Load() => store.Current.InputMethod with { };

    public bool Save(InputMethodSettings settings) =>
        store.UpdateAndFlush(current => current.InputMethod = settings);
}

public sealed class UnifiedShellBackgroundSettingsStore(ISettingsStore store) : IShellBackgroundSettingsStore
{
    public ShellBackgroundSettings Load() => store.Current.ShellBackground with { };

    public void Save(ShellBackgroundSettings settings) =>
        store.Update(current => current.ShellBackground = settings.Normalized());
}

public sealed class UnifiedWindowPlacementStore(ISettingsStore store) : IWindowPlacementStore
{
    public WindowPlacement? Load() => Copy(store.Current.Windows.Main);

    public void Save(WindowPlacement placement) =>
        store.UpdateAndFlush(current => current.Windows.Main = placement with { });

    private static WindowPlacement? Copy(WindowPlacement? placement) =>
        placement is null ? null : placement with { };
}

public sealed class UnifiedDesktopLyricsPlacementStore(ISettingsStore store) : IDesktopLyricsPlacementStore
{
    public WindowPlacement? Load() => Copy(store.Current.Windows.DesktopLyrics);

    public void Save(WindowPlacement placement) =>
        store.UpdateAndFlush(current => current.Windows.DesktopLyrics = placement with { });

    private static WindowPlacement? Copy(WindowPlacement? placement) =>
        placement is null ? null : placement with { };
}

public sealed class UnifiedMiniPlayerPlacementStore(ISettingsStore store) : IMiniPlayerPlacementStore
{
    public WindowPlacement? Load() => Copy(store.Current.Windows.MiniPlayer);

    public void Save(WindowPlacement placement) =>
        store.UpdateAndFlush(current => current.Windows.MiniPlayer = placement with { });

    private static WindowPlacement? Copy(WindowPlacement? placement) =>
        placement is null ? null : placement with { };
}

public sealed class UnifiedAudioQualitySettingsStore(ISettingsStore store) : IAudioQualitySettingsStore
{
    public AudioQuality Load() => store.Current.AudioQuality.Preferred;

    public void Save(AudioQuality quality)
    {
        if (!Enum.IsDefined(quality))
        {
            throw new ArgumentOutOfRangeException(nameof(quality));
        }

        store.UpdateAndFlush(current => current.AudioQuality.Preferred = quality);
    }
}

public sealed class UnifiedPlaybackSettingsStore(ISettingsStore store) : IPlaybackSettingsStore
{
    public PlaybackSettings Load() => store.Current.Playback with { };

    public void Save(PlaybackSettings settings) =>
        store.UpdateAndFlush(current => current.Playback = settings.Normalized());
}

public sealed class UnifiedVolumeSettingsStore(ISettingsStore store) : IVolumeSettingsStore
{
    public VolumeSettings Load() => store.Current.Volume with { };

    public bool Save(VolumeSettings settings) =>
        store.UpdateAndFlush(current => current.Volume = settings.Normalized());
}

public sealed class UnifiedAudioEffectSettingsStore(ISettingsStore store) : IAudioEffectSettingsStore
{
    public AudioEffectSettings Load()
    {
        var settings = store.Current.AudioEffects;
        return settings with
        {
            EqBands = settings.EqBands is null ? null : [.. settings.EqBands],
        };
    }

    public bool Save(AudioEffectSettings settings) =>
        store.UpdateAndFlush(current => current.AudioEffects = settings.Normalized());
}

public sealed class UnifiedLyricsSettingsStore(ISettingsStore store) : ILyricsSettingsStore
{
    public LyricsSettings Load() => store.Current.Lyrics with { };

    public void Save(LyricsSettings settings) =>
        store.Update(current => current.Lyrics = settings.Normalized());
}

public sealed class UnifiedDesktopLyricsSettingsStore(ISettingsStore store) : IDesktopLyricsSettingsStore
{
    public DesktopLyricsSettings Load() => store.Current.DesktopLyrics with { };

    public void Save(DesktopLyricsSettings settings) =>
        store.Update(current => current.DesktopLyrics = settings.Normalized());
}

public sealed class UnifiedLyricPosterSettingsStore(ISettingsStore store) : ILyricPosterSettingsStore
{
    public LyricPosterSettings Load() => store.Current.LyricPoster with { };

    public void Save(LyricPosterSettings settings) =>
        store.Update(current => current.LyricPoster = settings.Normalized());
}

public sealed class UnifiedFontSettingsStore(ISettingsStore store) : IFontSettingsStore
{
    public FontSettings Load() => store.Current.Fonts with { };

    public bool Save(FontSettings settings) =>
        store.UpdateAndFlush(current => current.Fonts = settings.Normalized());
}

public sealed class UnifiedViewModeSettingsStore(ISettingsStore store) : IViewModeSettingsStore
{
    public ViewModeSettings Load() => store.Current.ViewMode with { };

    public void Save(ViewModeSettings settings) =>
        store.UpdateAndFlush(current => current.ViewMode = settings.Normalized());
}

public sealed class UnifiedShortcutSettingsStore(ISettingsStore store) : IShortcutSettingsStore
{
    public ShortcutSettings Load()
    {
        var settings = store.Current.Shortcuts;
        return settings with { Bindings = [.. settings.Bindings] };
    }

    public void Save(ShortcutSettings settings) =>
        store.UpdateAndFlush(current => current.Shortcuts = settings.Normalized());
}

public sealed class UnifiedUpdatePreferencesStore(ISettingsStore store) : IUpdatePreferencesStore
{
    public UpdatePreferences Load() => store.Current.Update with { };

    public bool Save(UpdatePreferences preferences) =>
        store.UpdateAndFlush(current => current.Update = preferences with { });
}

public sealed class UnifiedProxySettingsStore(ISettingsStore store) : IProxySettingsStore
{
    public ProxySettings Load() => store.Current.Proxy with { };

    public bool Save(ProxySettings settings) =>
        store.UpdateAndFlush(current => current.Proxy = settings.WithKnownMode());
}
