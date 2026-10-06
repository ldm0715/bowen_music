using System.Text.Json;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

public sealed class JsonInputMethodSettingsStore(
    string? path = null,
    ILogger<JsonInputMethodSettingsStore>? logger = null) : IInputMethodSettingsStore
{
    private readonly string _path = path ?? AppPaths.InputMethodSettingsFile;
    private readonly ILogger<JsonInputMethodSettingsStore> _logger = logger ?? NullLogger<JsonInputMethodSettingsStore>.Instance;

    public InputMethodSettings Load()
    {
        if (!File.Exists(_path)) return InputMethodSettings.Default;
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllBytes(_path),
                InputMethodSettingsJsonContext.Default.InputMethodSettings) ?? InputMethodSettings.Default;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "输入法兼容设置读取失败，按关闭继续：{Path}", _path);
            return InputMethodSettings.Default;
        }
    }

    public bool Save(InputMethodSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, InputMethodSettingsJsonContext.Default.InputMethodSettings);
            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "输入法兼容设置写入失败：{Path}", _path);
            return false;
        }
    }
}
