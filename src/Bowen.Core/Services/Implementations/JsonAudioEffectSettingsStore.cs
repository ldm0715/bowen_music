using System.Text.Json;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bowen.Core.Services.Implementations;

/// <summary>
/// 音频效果偏好存成明文 JSON。写法与 <see cref="JsonVolumeSettingsStore"/> 一致：
/// 先写临时文件再 <c>Move</c> 覆盖，中途断电不会留下半个文件。
/// </summary>
public sealed class JsonAudioEffectSettingsStore(
    string? path = null,
    ILogger<JsonAudioEffectSettingsStore>? logger = null) : IAudioEffectSettingsStore
{
    private readonly string _path = path ?? AppPaths.AudioEffectSettingsFile;
    private readonly ILogger<JsonAudioEffectSettingsStore> _logger =
        logger ?? NullLogger<JsonAudioEffectSettingsStore>.Instance;

    public AudioEffectSettings Load()
    {
        if (!File.Exists(_path))
        {
            return AudioEffectSettings.Default;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize(
                File.ReadAllBytes(_path),
                AudioEffectSettingsJsonContext.Default.AudioEffectSettings);
            return loaded?.Normalized() ?? AudioEffectSettings.Default;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "音效偏好读取失败，按默认值继续：{Path}", _path);
            return AudioEffectSettings.Default;
        }
    }

    public bool Save(AudioEffectSettings settings)
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
                AudioEffectSettingsJsonContext.Default.AudioEffectSettings);
            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "音效偏好写入失败：{Path}", _path);
            return false;
        }
    }
}
