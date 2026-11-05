using System.Text.Json;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bowen.Core.Services.Implementations;

/// <inheritdoc cref="ILyricPosterSettingsStore" />
/// <remarks>
/// <b>明文 JSON，不加密</b>，与歌词页偏好同一条规矩；文件单独一份，
/// 海报的自定义不会连带重置歌词页的外观。
/// </remarks>
public sealed class JsonLyricPosterSettingsStore : ILyricPosterSettingsStore
{
    private readonly string _path;
    private readonly ILogger<JsonLyricPosterSettingsStore> _logger;

    /// <param name="path">默认 <c>lyrics-poster.json</c>。测试可注入临时路径。</param>
    public JsonLyricPosterSettingsStore(
        string? path = null,
        ILogger<JsonLyricPosterSettingsStore>? logger = null)
    {
        _path = path ?? AppPaths.LyricPosterSettingsFile;
        _logger = logger ?? NullLogger<JsonLyricPosterSettingsStore>.Instance;
    }

    public LyricPosterSettings Load()
    {
        if (!File.Exists(_path))
        {
            return LyricPosterSettings.Default;
        }

        try
        {
            var bytes = File.ReadAllBytes(_path);
            var loaded = JsonSerializer.Deserialize(
                bytes,
                LyricPosterSettingsJsonContext.Default.LyricPosterSettings);

            return (loaded ?? LyricPosterSettings.Default).Normalized();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "歌词海报设置读取失败，按默认继续：{Path}", _path);
            return LyricPosterSettings.Default;
        }
    }

    public void Save(LyricPosterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                settings.Normalized(),
                LyricPosterSettingsJsonContext.Default.LyricPosterSettings);

            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不进去只影响「下次启动记不记得」，本次选择已经生效。
            _logger.LogWarning(ex, "歌词海报设置写入失败：{Path}", _path);
        }
    }
}
