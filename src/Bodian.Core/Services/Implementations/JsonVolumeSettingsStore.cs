using System.Text.Json;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <summary>
/// 音量偏好存成明文 JSON。写法与 <see cref="JsonInputMethodSettingsStore"/> 一致：
/// 先写临时文件再 <c>Move</c> 覆盖，中途断电不会留下半个文件。
/// </summary>
public sealed class JsonVolumeSettingsStore(
    string? path = null,
    ILogger<JsonVolumeSettingsStore>? logger = null) : IVolumeSettingsStore
{
    private readonly string _path = path ?? AppPaths.VolumeFile;
    private readonly ILogger<JsonVolumeSettingsStore> _logger = logger ?? NullLogger<JsonVolumeSettingsStore>.Instance;

    public VolumeSettings Load()
    {
        if (!File.Exists(_path)) return VolumeSettings.Default;
        try
        {
            var loaded = JsonSerializer.Deserialize(File.ReadAllBytes(_path),
                VolumeSettingsJsonContext.Default.VolumeSettings);
            return loaded?.Normalized() ?? VolumeSettings.Default;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "音量偏好读取失败，按默认音量继续：{Path}", _path);
            return VolumeSettings.Default;
        }
    }

    public bool Save(VolumeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, VolumeSettingsJsonContext.Default.VolumeSettings);
            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "音量偏好写入失败：{Path}", _path);
            return false;
        }
    }
}
