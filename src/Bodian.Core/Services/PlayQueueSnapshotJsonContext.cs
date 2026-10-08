using System.Text.Json.Serialization;
using Bodian.Core.Models;

namespace Bodian.Core.Services;

/// <summary>
/// 队列快照的源生成上下文。只注册顶层类型，其余（条目数组、音源明细、封面地址）由生成器
/// 顺着闭包带出来，与播放历史的上下文同款。
/// </summary>
/// <remarks>
/// <b><c>UseStringEnumConverter</c> 必须有。</b> 快照里存着 <see cref="AudioQuality"/>
/// （音源明细的档位与可用档位列表），写成数字的话，以后枚举重排会让旧文件静默读成<b>别的档位</b>。
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(PlayQueueSnapshot))]
internal sealed partial class PlayQueueSnapshotJsonContext : JsonSerializerContext;
