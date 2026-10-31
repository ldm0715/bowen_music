using System.Text.Json;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bowen.Core.Services.Implementations;

/// <inheritdoc cref="IShellBackgroundSettingsStore" />
/// <remarks>
/// <para>
/// <b>明文 JSON，不加密。</b> 背景偏好不是凭据，也不含任何账号标识。
/// </para>
/// <para>
/// <b>读失败一律返回默认，且不删坏文件</b> —— 留着还能人工看一眼出了什么事。
/// </para>
/// <para>
/// <b>先写临时文件再原子替换。</b> 直接覆写时进程若在写到一半退出，留下的是半截 JSON，
/// 下次启动会退回封面取色，用户会以为设置没保存住。
/// </para>
/// </remarks>
public sealed class JsonShellBackgroundSettingsStore : IShellBackgroundSettingsStore
{
    private readonly string _path;
    private readonly ILogger<JsonShellBackgroundSettingsStore> _logger;

    /// <param name="path">默认 <c>shell-background.json</c>。测试可注入临时路径。</param>
    public JsonShellBackgroundSettingsStore(
        string? path = null,
        ILogger<JsonShellBackgroundSettingsStore>? logger = null)
    {
        _path = path ?? AppPaths.ShellBackgroundSettingsFile;
        _logger = logger ?? NullLogger<JsonShellBackgroundSettingsStore>.Instance;
    }

    public ShellBackgroundSettings Load()
    {
        if (!File.Exists(_path))
        {
            return ShellBackgroundSettings.Default;
        }

        try
        {
            var bytes = File.ReadAllBytes(_path);
            using var document = JsonDocument.Parse(bytes);
            var loaded = document.RootElement.Deserialize(
                ShellBackgroundSettingsJsonContext.Default.ShellBackgroundSettings);

            if (loaded is null)
            {
                return ShellBackgroundSettings.Default;
            }

            bool Has(string name) => document.RootElement.TryGetProperty(name, out _);

            // 源生成的 record 走参数化构造：文件里缺字段时塞 CLR 默认值，属性初始值不参与。
            // 旧文件没有图片字段或 Mica 着色字段，必须按「文件里有没有」补默认。
            loaded = loaded with
            {
                ImageFile = Has("ImageFile") ? loaded.ImageFile : string.Empty,
                ImageDim = Has("ImageDim") ? loaded.ImageDim : ShellBackgroundSettings.DefaultImageDim,
                ImageBlur = Has("ImageBlur") ? loaded.ImageBlur : ShellBackgroundSettings.DefaultImageBlur,
                ImageFocusX = Has("ImageFocusX") ? loaded.ImageFocusX : 0.5,
                ImageFocusY = Has("ImageFocusY") ? loaded.ImageFocusY : 0.5,
                ImageZoom = Has("ImageZoom") ? loaded.ImageZoom : 1,
                SolidColorArgb = Has("SolidColorArgb") ? loaded.SolidColorArgb : ShellBackgroundSettings.DefaultSolidColorArgb,
                LightSolidColorArgb = Has("LightSolidColorArgb") ? loaded.LightSolidColorArgb : ShellBackgroundSettings.DefaultLightSolidColorArgb,
                CustomSolidColorArgb = Has("CustomSolidColorArgb") ? loaded.CustomSolidColorArgb : ShellBackgroundSettings.DefaultSolidColorArgb,
                LightCustomSolidColorArgb = Has("LightCustomSolidColorArgb") ? loaded.LightCustomSolidColorArgb : ShellBackgroundSettings.DefaultLightSolidColorArgb,
                SolidColorAuto = Has("SolidColorAuto") && loaded.SolidColorAuto,
                MicaTintEnabled = Has("MicaTintEnabled") ? loaded.MicaTintEnabled : true,
            };

            // 字符串枚举会挡掉未知名字，但数字仍能反序列化成未定义的枚举值。
            if (!Enum.IsDefined(loaded.Mode))
            {
                _logger.LogWarning("窗口背景模式不在枚举范围内（{Value}），按封面取色继续", loaded.Mode);
            }

            return loaded.Normalized();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "窗口背景设置读取失败，按默认继续：{Path}", _path);

            return ShellBackgroundSettings.Default;
        }
    }

    public void Save(ShellBackgroundSettings settings)
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
                ShellBackgroundSettingsJsonContext.Default.ShellBackgroundSettings);

            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不进去只影响「下次启动是否记得」。本次会话的选择已经生效，不该因此报错。
            _logger.LogWarning(ex, "窗口背景设置写入失败：{Path}", _path);
        }
    }
}
