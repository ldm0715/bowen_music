using System.Text.Json;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <summary>
/// 播放队列快照存成明文 JSON。写法与 <see cref="JsonInputMethodSettingsStore"/> 一致：
/// 先写临时文件再 <c>Move</c> 覆盖，中途断电不会留下半个文件。
/// </summary>
/// <remarks>
/// <b>这份文件可能被频繁重写</b>（每次增删切歌 + 防抖），而且内容有几百首 —— 调用方应当把它
/// 放到后台线程去写，见 <c>PlaybackCoordinator</c> 的落盘路径。
/// </remarks>
public sealed class JsonPlayQueueSnapshotStore(
    string? path = null,
    ILogger<JsonPlayQueueSnapshotStore>? logger = null) : IPlayQueueSnapshotStore
{
    private readonly string _path = path ?? AppPaths.PlayQueueFile;
    private readonly ILogger<JsonPlayQueueSnapshotStore> _logger = logger ?? NullLogger<JsonPlayQueueSnapshotStore>.Instance;

    public PlayQueueSnapshot Load()
    {
        if (!File.Exists(_path)) return PlayQueueSnapshot.Default;
        try
        {
            var loaded = JsonSerializer.Deserialize(File.ReadAllBytes(_path),
                PlayQueueSnapshotJsonContext.Default.PlayQueueSnapshot);
            return loaded is null ? PlayQueueSnapshot.Default : Normalize(loaded);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "播放队列快照读取失败，按空队列继续：{Path}", _path);
            return PlayQueueSnapshot.Default;
        }
    }

    /// <summary>
    /// 把读到的快照补齐成可用的形状。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>不能指望属性上写的初始化器。</b> 源生成反序列化对 JSON 里没有的键一律留类型的零值：
    /// 条目数组会变成 <c>null</c>、开关会变成 <c>false</c>，哪怕声明里明明写着「默认空数组」
    /// 「默认开」。真让这个 null 流下去，协调器构造时取它长度就抛空引用，<b>应用直接起不来</b>。
    /// </para>
    /// <para>
    /// 顶层条目数组缺失就整份退回默认（文件损坏或被人手工改过）；条目里的两个数组同理补齐 ——
    /// 那是以后给条目加字段时旧文件会走到的路。
    /// </para>
    /// </remarks>
    private static PlayQueueSnapshot Normalize(PlayQueueSnapshot loaded)
    {
        var items = loaded.Items;
        if (items is null) { return PlayQueueSnapshot.Default; }

        var buffer = new QueuedTrack[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            buffer[i] = item with
            {
                AvailableQualities = item.AvailableQualities ?? [],
                AudioVariants = item.AudioVariants ?? [],
            };
        }

        return loaded with { Items = buffer };
    }

    public bool Save(PlayQueueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, PlayQueueSnapshotJsonContext.Default.PlayQueueSnapshot);
            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "播放队列快照写入失败：{Path}", _path);
            return false;
        }
    }
}
