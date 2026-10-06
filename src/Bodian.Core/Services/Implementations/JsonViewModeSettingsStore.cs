using System.Text.Json;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <inheritdoc cref="IViewModeSettingsStore" />
/// <remarks>
/// <para>
/// <b>明文 JSON，不加密。</b> 显示偏好不是凭据，也不含任何账号标识。
/// </para>
/// <para>
/// <b>读失败一律返回默认，且不删坏文件</b> —— 留着还能人工看一眼出了什么事。
/// </para>
/// <para>
/// <b>先写临时文件再原子替换。</b> 设置项虽小，但直接覆写时进程若在写到一半退出，
/// 留下的是半截 JSON，下次启动就退化成行列表 —— 用户会以为自己那个开关没记住。
/// </para>
/// </remarks>
public sealed class JsonViewModeSettingsStore : IViewModeSettingsStore
{
    private readonly string _path;
    private readonly ILogger<JsonViewModeSettingsStore> _logger;

    /// <param name="path">默认 <see cref="AppPaths.ViewModeFile"/>。测试可注入临时路径。</param>
    public JsonViewModeSettingsStore(
        string? path = null,
        ILogger<JsonViewModeSettingsStore>? logger = null)
    {
        _path = path ?? AppPaths.ViewModeFile;
        _logger = logger ?? NullLogger<JsonViewModeSettingsStore>.Instance;
    }

    public ViewModeSettings Load()
    {
        if (!File.Exists(_path))
        {
            return ViewModeSettings.Default;
        }

        try
        {
            var bytes = File.ReadAllBytes(_path);
            var loaded = JsonSerializer.Deserialize(bytes, ViewModeSettingsJsonContext.Default.ViewModeSettings);
            return loaded?.Normalized() ?? ViewModeSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "显示方式偏好读取失败，按行列表继续：{Path}", _path);
            return ViewModeSettings.Default;
        }
    }

    public void Save(ViewModeSettings settings)
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
                ViewModeSettingsJsonContext.Default.ViewModeSettings);

            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不进去只影响「下次启动记不记得」。本次会话的选择已经生效，不该因此报错。
            _logger.LogWarning(ex, "显示方式偏好写入失败：{Path}", _path);
        }
    }
}
