using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bowen.Core.Services.Implementations;

/// <summary>
/// 统一配置文件的加载、防抖写入与原子替换。
/// </summary>
/// <remarks>
/// <para>
/// <b>单一写入口。</b> 所有修改走 <see cref="Update"/> / <see cref="UpdateAndFlush"/>，
/// 内存里只有一份 <see cref="AppSettings"/>，写盘串行化。多个旧 store 各自读写整份文件
/// 会互相覆盖，这正是这次合并要先解决的。
/// </para>
/// <para>
/// <b>先写临时文件再替换。</b> 目标存在时用 <c>File.Replace</c>，顺带留下
/// <c>config.json.bak</c>；目标不存在时用 <c>File.Move</c>。写盘失败只记 warning，
/// 保留脏标记等下一次。
/// </para>
/// <para>
/// <b>读取分两层。</b> 先整份反序列化；失败时按 section 逐个重读，只把坏掉的那一段
/// 回默认，其它 section 保留。主文件读不出来时再试 <c>.bak</c>，都失败才用默认值。
/// </para>
/// </remarks>
public sealed class JsonAppSettingsStore : ISettingsStore, IDisposable
{
    /// <summary>默认防抖窗口。见 <c>docs/settings-storage.md</c> §7.3。</summary>
    public static readonly TimeSpan DefaultSaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly object _gate = new();
    private readonly string _path;
    private readonly ILogger _logger;
    private readonly TimeSpan _saveDelay;
    private readonly Timer _saveTimer;

    private AppSettings _current;
    private bool _dirty;
    private bool _disposed;

    /// <param name="initial">启动时加载好的配置。</param>
    /// <param name="path">默认 <see cref="AppPaths.ConfigFile"/>。测试可注入临时路径。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="saveDelay">防抖窗口；测试可缩短。</param>
    public JsonAppSettingsStore(
        AppSettings initial,
        string? path = null,
        ILogger<JsonAppSettingsStore>? logger = null,
        TimeSpan? saveDelay = null)
    {
        _current = Clone(initial ?? throw new ArgumentNullException(nameof(initial)));
        _path = path ?? AppPaths.ConfigFile;
        _logger = logger ?? NullLogger<JsonAppSettingsStore>.Instance;
        _saveDelay = saveDelay ?? DefaultSaveDelay;
        _saveTimer = new Timer(_ => SaveScheduled(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public AppSettings Current => _current;

    /// <summary>成功写盘的次数。测试用来确认防抖确实合并了写入。</summary>
    internal int WriteCount { get; private set; }

    /// <inheritdoc />
    public void Update(Action<AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            mutate(_current);
            _dirty = true;
            _saveTimer.Change(_saveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <inheritdoc />
    public bool UpdateAndFlush(Action<AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            mutate(_current);
            _dirty = true;
            _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);

            return SaveLocked();
        }
    }

    /// <inheritdoc />
    public bool Flush()
    {
        lock (_gate)
        {
            if (_disposed || !_dirty)
            {
                return true;
            }

            _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return SaveLocked();
        }
    }

    /// <summary>释放前把待写内容落盘。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);

            if (_dirty)
            {
                SaveLocked();
            }

            _disposed = true;
        }

        _saveTimer.Dispose();
    }

    /// <summary>
    /// 读取配置文件；主文件失败时试 <c>.bak</c>，都失败回默认值。
    /// </summary>
    /// <remarks>
    /// 迁移逻辑不在这里：它由 <c>SettingsBootstrap</c> 负责判定主文件、备份与旧文件。
    /// 这一层只负责「把一份 config.json 读成对象」。
    /// </remarks>
    public static AppSettings Load(string? path = null, ILogger? logger = null)
    {
        var file = path ?? AppPaths.ConfigFile;
        var log = logger ?? NullLogger.Instance;

        var primary = TryLoadFile(file, log);
        if (primary is not null)
        {
            return primary;
        }

        var backup = TryLoadFile(file + ".bak", log);
        if (backup is not null)
        {
            log.LogWarning("配置主文件不可用，已改用备份：{Path}", file + ".bak");
            return backup;
        }

        return AppSettings.Default;
    }

    private static AppSettings? TryLoadFile(string file, ILogger logger)
    {
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var settings = Deserialize(File.ReadAllBytes(file), out var usedSectionFallback);
            if (usedSectionFallback)
            {
                logger.LogWarning("配置中至少有一个 section 读取失败，已按默认值处理：{Path}", file);
            }

            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "配置读取失败：{Path}", file);
            return null;
        }
    }

    /// <summary>
    /// 反序列化一份配置。整份失败时按 section 逐个重读，坏哪段只重置哪段。
    /// </summary>
    internal static AppSettings Deserialize(ReadOnlyMemory<byte> utf8Json, out bool usedSectionFallback)
    {
        try
        {
            var full = JsonSerializer.Deserialize(utf8Json.Span, AppSettingsJsonContext.Default.AppSettings);
            if (full is not null)
            {
                usedSectionFallback = false;
                return full.Normalized();
            }
        }
        catch (JsonException)
        {
            // 落到下面按 section 重读。
        }

        using var document = JsonDocument.Parse(utf8Json);
        var root = document.RootElement;

        var settings = new AppSettings
        {
            SchemaVersion = ReadSchemaVersion(root),
            Appearance = ReadSection(root, "appearance", AppSettingsJsonContext.Default.ThemeSettings, new()),
            Fonts = ReadSection(root, "fonts", AppSettingsJsonContext.Default.FontSettings, new()),
            ShellBackground = ReadSection(root, "shellBackground", AppSettingsJsonContext.Default.ShellBackgroundSettings, new()),
            Playback = ReadSection(root, "playback", AppSettingsJsonContext.Default.PlaybackSettings, new()),
            Volume = ReadSection(root, "volume", AppSettingsJsonContext.Default.VolumeSettings, new()),
            AudioQuality = ReadSection(root, "audioQuality", AppSettingsJsonContext.Default.AudioQualityPreferences, new()),
            AudioEffects = ReadSection(root, "audioEffects", AppSettingsJsonContext.Default.AudioEffectSettings, new()),
            Lyrics = ReadSection(root, "lyrics", AppSettingsJsonContext.Default.LyricsSettings, new()),
            DesktopLyrics = ReadSection(root, "desktopLyrics", AppSettingsJsonContext.Default.DesktopLyricsSettings, new()),
            LyricPoster = ReadSection(root, "lyricPoster", AppSettingsJsonContext.Default.LyricPosterSettings, new()),
            ViewMode = ReadSection(root, "viewMode", AppSettingsJsonContext.Default.ViewModeSettings, new()),
            Shortcuts = ReadSection(root, "shortcuts", AppSettingsJsonContext.Default.ShortcutSettings, new()),
            Update = ReadSection(root, "update", AppSettingsJsonContext.Default.UpdatePreferences, new()),
            Proxy = ReadSection(root, "proxy", AppSettingsJsonContext.Default.ProxySettings, new()),
            InputMethod = ReadSection(root, "inputMethod", AppSettingsJsonContext.Default.InputMethodSettings, new()),
            Windows = ReadSection(root, "windows", AppSettingsJsonContext.Default.WindowSettings, new()),
        };

        usedSectionFallback = true;
        return settings.Normalized();
    }

    private static T ReadSection<T>(JsonElement root, string name, JsonTypeInfo<T> typeInfo, T fallback)
    {
        if (!TryGetProperty(root, name, out var element))
        {
            return fallback;
        }

        try
        {
            return element.Deserialize(typeInfo) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
        catch (NotSupportedException)
        {
            return fallback;
        }
    }

    private static int ReadSchemaVersion(JsonElement root) =>
        TryGetProperty(root, "schemaVersion", out var element)
        && element.ValueKind == JsonValueKind.Number
        && element.TryGetInt32(out var version)
        && version > 0
            ? version
            : AppSettings.CurrentSchemaVersion;

    /// <summary>手改过的文件可能把 key 写成 PascalCase，读取时不分大小写。</summary>
    internal static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 深拷贝一份配置，确保 store 持有的是自己的对象。
    /// </summary>
    /// <remarks>
    /// 配置模型的属性是可变的，调用方传进来的对象（尤其是静态 <see cref="AppSettings.Default"/>）
    /// 如果被 store 直接持有，<c>Update</c> 就会改到调用方那份。走一次序列化往返最省事，
    /// 启动时一次，代价可以忽略。
    /// </remarks>
    private static AppSettings Clone(AppSettings settings)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            settings.Normalized(), AppSettingsJsonContext.Default.AppSettings);

        return JsonSerializer.Deserialize(bytes, AppSettingsJsonContext.Default.AppSettings)
            ?? new AppSettings();
    }

    private void SaveScheduled()
    {
        lock (_gate)
        {
            if (!_disposed && _dirty)
            {
                SaveLocked();
            }
        }
    }

    private bool SaveLocked()
    {
        byte[] bytes;
        try
        {
            bytes = JsonSerializer.SerializeToUtf8Bytes(
                _current, AppSettingsJsonContext.Default.AppSettings);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            _logger.LogWarning(ex, "配置序列化失败，保留待写状态：{Path}", _path);
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);

            if (File.Exists(_path))
            {
                try
                {
                    File.Replace(temporary, _path, _path + ".bak");
                }
                catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
                {
                    // 个别文件系统不支持 Replace；退回覆盖写，旧 .bak 保留。
                    File.Move(temporary, _path, overwrite: true);
                }
            }
            else
            {
                File.Move(temporary, _path);
            }

            _dirty = false;
            WriteCount++;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "配置写入失败，下次再试：{Path}", _path);
            return false;
        }
    }
}
