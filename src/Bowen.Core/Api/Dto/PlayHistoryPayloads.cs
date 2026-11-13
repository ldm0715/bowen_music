using System.Text.Json.Serialization;

namespace Bowen.Core.Api.Dto;

/// <summary>
/// <c>playlist/history/page</c> 的响应。
/// </summary>
/// <remarks>
/// <para>
/// 信封是 <c>{total, list}</c>，<c>list</c> 的元素是**完整的歌曲对象** —— 反汇编里它被交给
/// <c>Song.fromJson</c>，不是写入时那种只含 <c>sourceId</c> 的极简形状。
/// </para>
/// <para>
/// <b>本产品当前不读这条接口。</b> 它只给探针与测试用：先确认桌面请求头能取到历史，
/// 再验证写入是否生效。见 <c>docs/play-history-sync.md</c>。
/// </para>
/// </remarks>
internal sealed record PlayHistoryPagePayload
{
    [JsonPropertyName("total")] public int Total { get; init; }

    [JsonPropertyName("list")] public TrackDto[]? List { get; init; }
}
