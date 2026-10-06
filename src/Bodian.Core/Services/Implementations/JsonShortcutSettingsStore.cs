using System.Text.Json;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <inheritdoc cref="IShortcutSettingsStore" />
/// <remarks>
/// <para>
/// <b>明文 JSON，不加密。</b> 键位不是凭据。
/// </para>
/// <para>
/// <b>读失败一律返回默认，且不删坏文件</b> —— 留着还能人工看一眼出了什么事。
/// 读出来的内容还会过一遍 <see cref="ShortcutSettings.Normalized"/>：
/// 手工改坏一个键位不该让整套快捷键失灵。
/// </para>
/// <para>
/// <b>先写临时文件再原子替换。</b> 与其余几份设置同一条规矩，理由也一样：
/// 覆写到一半退出会留下半截 JSON，下次启动整份键位退回默认，
/// 而用户只觉得「我改的键又变回去了」。
/// </para>
/// </remarks>
public sealed class JsonShortcutSettingsStore : IShortcutSettingsStore
{
    private readonly string _path;
    private readonly ILogger<JsonShortcutSettingsStore> _logger;

    /// <param name="path">默认 <see cref="AppPaths.ShortcutFile"/>。测试可注入临时路径。</param>
    public JsonShortcutSettingsStore(
        string? path = null,
        ILogger<JsonShortcutSettingsStore>? logger = null)
    {
        _path = path ?? AppPaths.ShortcutFile;
        _logger = logger ?? NullLogger<JsonShortcutSettingsStore>.Instance;
    }

    public ShortcutSettings Load()
    {
        if (!File.Exists(_path))
        {
            return ShortcutSettings.Default;
        }

        try
        {
            var bytes = File.ReadAllBytes(_path);
            var loaded = JsonSerializer.Deserialize(bytes, ShortcutSettingsJsonContext.Default.ShortcutSettings);
            return loaded?.Normalized() ?? ShortcutSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "快捷键键位读取失败，按默认键位继续：{Path}", _path);
            return ShortcutSettings.Default;
        }
    }

    public void Save(ShortcutSettings settings)
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
                ShortcutSettingsJsonContext.Default.ShortcutSettings);

            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不进去只影响「下次启动记不记得」。本次会话的键位已经生效，不该因此报错。
            _logger.LogWarning(ex, "快捷键键位写入失败：{Path}", _path);
        }
    }
}
