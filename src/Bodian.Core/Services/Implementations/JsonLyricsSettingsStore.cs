using System.Text.Json;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <inheritdoc cref="ILyricsSettingsStore" />
/// <remarks>
/// <para>
/// <b>明文 JSON，不加密。</b> 显示偏好不是凭据，也不含任何账号标识。
/// </para>
/// <para>
/// <b>读失败一律返回默认，且不删坏文件</b> —— 留着还能人工看一眼出了什么事。
/// </para>
/// </remarks>
public sealed class JsonLyricsSettingsStore : ILyricsSettingsStore
{
    private readonly string _path;
    private readonly ILogger<JsonLyricsSettingsStore> _logger;

    /// <param name="path">默认 <c>lyrics.json</c>。测试可注入临时路径。</param>
    public JsonLyricsSettingsStore(
        string? path = null,
        ILogger<JsonLyricsSettingsStore>? logger = null)
    {
        _path = path ?? AppPaths.LyricsSettingsFile;
        _logger = logger ?? NullLogger<JsonLyricsSettingsStore>.Instance;
    }

    public LyricsSettings Load()
    {
        if (!File.Exists(_path))
        {
            return LyricsSettings.Default;
        }

        try
        {
            var bytes = File.ReadAllBytes(_path);
            var loaded = JsonSerializer.Deserialize(bytes, LyricsSettingsJsonContext.Default.LyricsSettings);
            return loaded?.Normalized() ?? LyricsSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "歌词页设置读取失败，按默认继续：{Path}", _path);
            return LyricsSettings.Default;
        }
    }

    public void Save(LyricsSettings settings)
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
                LyricsSettingsJsonContext.Default.LyricsSettings);

            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不进去只影响「下次启动记不记得」。本次会话的选择已经生效，不该因此报错。
            _logger.LogWarning(ex, "歌词页设置写入失败：{Path}", _path);
        }
    }
}
