using System.Text.Json;
using System.Text.Json.Serialization;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <summary>
/// 播放偏好存成明文 JSON。写法与 <see cref="JsonAudioQualitySettingsStore"/> 一致：
/// 先写临时文件再 <c>Move</c> 覆盖，中途断电不会留下半个文件。
/// </summary>
public sealed class JsonPlaybackSettingsStore(string? path = null, ILogger<JsonPlaybackSettingsStore>? logger = null)
    : IPlaybackSettingsStore
{
    private readonly string _path = path ?? AppPaths.PlaybackSettingsFile;
    private readonly ILogger _logger = logger ?? NullLogger<JsonPlaybackSettingsStore>.Instance;

    public PlayMode Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var value = JsonSerializer.Deserialize(File.ReadAllBytes(_path), PlaybackSettingsJsonContext.Default.PlayMode);
                if (Enum.IsDefined(value))
                {
                    return value;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning("读取播放偏好失败，使用顺序播放");
        }

        return PlayMode.Sequential;
    }

    public void Save(PlayMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = _path + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(mode, PlaybackSettingsJsonContext.Default.PlayMode));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("保存播放偏好失败");
        }
    }
}

[JsonSerializable(typeof(PlayMode))]
internal partial class PlaybackSettingsJsonContext : JsonSerializerContext;
