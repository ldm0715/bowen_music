using System.Text.Json;
using System.Text.Json.Serialization;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

public sealed class JsonAudioQualitySettingsStore(string? path = null, ILogger<JsonAudioQualitySettingsStore>? logger = null)
    : IAudioQualitySettingsStore
{
    private readonly string _path = path ?? Path.Combine(AppPaths.LocalAppData, "audio-quality.json");
    private readonly ILogger _logger = logger ?? NullLogger<JsonAudioQualitySettingsStore>.Instance;

    public AudioQuality Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var value = JsonSerializer.Deserialize(File.ReadAllBytes(_path), AudioQualitySettingsJsonContext.Default.AudioQuality);
                if (Enum.IsDefined(value)) { return value; }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning("读取音质偏好失败，使用无损档位");
        }
        return AudioQuality.Lossless;
    }

    public void Save(AudioQuality quality)
    {
        if (!Enum.IsDefined(quality)) { throw new ArgumentOutOfRangeException(nameof(quality)); }
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) { Directory.CreateDirectory(directory); }
            var temp = _path + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(quality, AudioQualitySettingsJsonContext.Default.AudioQuality));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("保存音质偏好失败");
        }
    }
}

[JsonSerializable(typeof(AudioQuality))]
internal partial class AudioQualitySettingsJsonContext : JsonSerializerContext;
