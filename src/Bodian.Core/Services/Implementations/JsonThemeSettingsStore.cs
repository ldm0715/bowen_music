using System.Text.Json;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <inheritdoc cref="IThemeSettingsStore" />
/// <remarks>
/// <para>
/// <b>明文 JSON，不加密。</b> 外观偏好不是凭据，也不含任何账号标识。
/// </para>
/// <para>
/// <b>读失败不是致命错误。</b> 文件损坏、值不合法、权限不足，一律退回
/// <see cref="AppTheme.System"/> 继续跑，<b>并且不删那个坏文件</b> ——
/// 留着还能人工看一眼出了什么事，这与播放历史是同一条规矩。
/// </para>
/// <para>
/// <b>先写临时文件再原子替换。</b> 设置项虽小，但直接覆写时进程若在写到一半退出，
/// 留下的是半截 JSON，下次启动就退化成「跟随系统」——用户会以为设置没保存住。
/// </para>
/// </remarks>
public sealed class JsonThemeSettingsStore : IThemeSettingsStore
{
    private readonly string _path;
    private readonly ILogger<JsonThemeSettingsStore> _logger;

    /// <param name="path">默认 <see cref="AppPaths.SettingsFile"/>。测试可注入临时路径。</param>
    public JsonThemeSettingsStore(
        string? path = null,
        ILogger<JsonThemeSettingsStore>? logger = null)
    {
        _path = path ?? AppPaths.SettingsFile;
        _logger = logger ?? NullLogger<JsonThemeSettingsStore>.Instance;
    }

    public ThemeSettings Load()
    {
        if (!File.Exists(_path))
        {
            return ThemeSettings.Default;
        }

        try
        {
            var bytes = File.ReadAllBytes(_path);
            var loaded = JsonSerializer.Deserialize(bytes, ThemeSettingsJsonContext.Default.ThemeSettings);

            if (loaded is null)
            {
                return ThemeSettings.Default;
            }

            // 字符串枚举走 JsonStringEnumConverter 时，无法识别的名字会抛 JsonException 被下面接住；
            // 但**数字**仍然能反序列化成未定义的枚举值，所以这里还要再挡一道。
            if (!Enum.IsDefined(loaded.Theme))
            {
                _logger.LogWarning("外观设置的值不在枚举范围内（{Value}），按跟随系统继续", loaded.Theme);

                return ThemeSettings.Default;
            }

            return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "外观设置读取失败，按跟随系统继续：{Path}", _path);

            return ThemeSettings.Default;
        }
    }

    public void Save(ThemeSettings settings)
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
                settings,
                ThemeSettingsJsonContext.Default.ThemeSettings);

            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不进去只影响「下次启动是否记得」。本次会话的选择已经生效，不该因此报错。
            _logger.LogWarning(ex, "外观设置写入失败：{Path}", _path);
        }
    }
}
