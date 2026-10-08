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
/// <remarks>
/// <para>
/// <b>读要兼容两种格式</b>：这份文件一开始只存一个裸枚举（<c>"Sequential"</c>），
/// 加「记住播放列表」之后变成了对象。老文件不能因此丢掉已保存的播放模式。
/// </para>
/// <para>
/// <b>不能指望属性初始化器兜住缺键</b>：源生成反序列化对 JSON 里没有的键留零值，
/// 所以「记住播放列表」在文件里用可空布尔，读到 null 时由这里补成<b>开</b> ——
/// 补成关会让一个手改过的文件静默地把用户的播放列表关掉。
/// </para>
/// </remarks>
public sealed class JsonPlaybackSettingsStore(string? path = null, ILogger<JsonPlaybackSettingsStore>? logger = null)
    : IPlaybackSettingsStore
{
    private readonly string _path = path ?? AppPaths.PlaybackSettingsFile;
    private readonly ILogger _logger = logger ?? NullLogger<JsonPlaybackSettingsStore>.Instance;

    public PlaybackSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var bytes = File.ReadAllBytes(_path);

                // 先按新格式（对象）读。老文件是裸枚举，这一步会抛 JsonException，落到下面再试。
                try
                {
                    var document = JsonSerializer.Deserialize(
                        bytes, PlaybackSettingsJsonContext.Default.PlaybackSettingsDocument);

                    if (document is not null && Enum.IsDefined(document.Mode))
                    {
                        return new PlaybackSettings(document.Mode, document.RestoreQueue ?? true);
                    }
                }
                catch (JsonException)
                {
                    // 不是对象格式，按老格式读。
                }

                var legacy = JsonSerializer.Deserialize(bytes, PlaybackSettingsJsonContext.Default.PlayMode);
                if (Enum.IsDefined(legacy))
                {
                    return new PlaybackSettings(legacy, RestoreQueue: true);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning("读取播放偏好失败，使用默认值");
        }

        return PlaybackSettings.Default;
    }

    public void Save(PlaybackSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!Enum.IsDefined(settings.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = new PlaybackSettingsDocument { Mode = settings.Mode, RestoreQueue = settings.RestoreQueue };
            var temp = _path + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(
                document, PlaybackSettingsJsonContext.Default.PlaybackSettingsDocument));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("保存播放偏好失败");
        }
    }
}

/// <summary>
/// 落盘用的形状。<see cref="RestoreQueue"/> 可空是为了区分「文件里没这一项」与「明确写了 false」。
/// </summary>
internal sealed class PlaybackSettingsDocument
{
    public PlayMode Mode { get; init; }

    public bool? RestoreQueue { get; init; }
}

[JsonSerializable(typeof(PlaybackSettingsDocument))]
[JsonSerializable(typeof(PlayMode))]
internal partial class PlaybackSettingsJsonContext : JsonSerializerContext;
