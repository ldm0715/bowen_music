using System.Text.Json;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <inheritdoc cref="IDesktopLyricsSettingsStore" />
/// <remarks>
/// <para>
/// <b>明文 JSON，不加密。</b> 外观偏好不是凭据，也不含任何账号标识。
/// </para>
/// <para>
/// <b>逐项夹越界值，不整份退回默认。</b> 字号写坏了不该把颜色、双行、锁定的选择一起抹掉 ——
/// 那是这个文件与 <c>JsonThemeSettingsStore</c>（只有一个字段，坏值只能整份退回）唯一的差别。
/// </para>
/// <para>
/// <b>读失败一律返回默认，且不删坏文件</b> —— 留着还能人工看一眼出了什么事。
/// </para>
/// </remarks>
public sealed class JsonDesktopLyricsSettingsStore : IDesktopLyricsSettingsStore
{
    private readonly string _path;
    private readonly ILogger<JsonDesktopLyricsSettingsStore> _logger;

    /// <param name="path">默认 <c>desktop-lyrics.json</c>。测试可注入临时路径。</param>
    public JsonDesktopLyricsSettingsStore(
        string? path = null,
        ILogger<JsonDesktopLyricsSettingsStore>? logger = null)
    {
        _path = path ?? AppPaths.DesktopLyricsSettingsFile;
        _logger = logger ?? NullLogger<JsonDesktopLyricsSettingsStore>.Instance;
    }

    public DesktopLyricsSettings Load()
    {
        if (!File.Exists(_path))
        {
            return DesktopLyricsSettings.Default;
        }

        try
        {
            var bytes = File.ReadAllBytes(_path);
            var loaded = JsonSerializer.Deserialize(bytes, DesktopLyricsSettingsJsonContext.Default.DesktopLyricsSettings);

            return loaded?.Normalized() ?? DesktopLyricsSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "桌面歌词设置读取失败，按默认继续：{Path}", _path);

            return DesktopLyricsSettings.Default;
        }
    }

    public void Save(DesktopLyricsSettings settings)
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
                DesktopLyricsSettingsJsonContext.Default.DesktopLyricsSettings);

            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不进去只影响「下次启动记不记得」。本次会话的选择已经生效，不该因此报错。
            _logger.LogWarning(ex, "桌面歌词设置写入失败：{Path}", _path);
        }
    }
}
