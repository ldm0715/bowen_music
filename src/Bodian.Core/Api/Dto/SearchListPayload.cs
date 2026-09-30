using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// <c>search/music/list</c> 的 <c>data</c>。
/// </summary>
/// <remarks>
/// <para>
/// 各家列表接口的**数组字段名不同**（搜索是 <c>resultList</c>，歌单是 <c>list</c>，
/// 收藏是 <c>playLists</c> / <c>albumList</c>），所以每个端点族要一个具体 DTO，
/// 再用内部接口抽出「取列表 + 取总数」。
/// </para>
/// <para>
/// <b>不能用 <c>ResultListPayload&lt;T&gt;</c> 这类开放泛型</b>——源生成不支持开放泛型
/// （见本目录 README）。P1 只做搜索这一个，其余端点族 P7 补。
/// </para>
/// <para>
/// <b><see cref="Total"/> 不可信</b>：服务端可能省略不可用条目的计数，实测出现过
/// 标称 121 首、第一页只回 99 首的情况。翻页游标只能按「请求页边界」推进。
/// </para>
/// </remarks>
internal sealed class SearchListPayload : ITrackListPayload
{
    [JsonPropertyName("pn")] public int PageNumber { get; init; }

    [JsonPropertyName("rn")] public int PageSize { get; init; }

    [JsonPropertyName("total")] public int Total { get; init; }

    [JsonPropertyName("duplicate")] public bool Duplicate { get; init; }

    [JsonPropertyName("resultList")] public TrackDto[]? ResultList { get; init; }

    IReadOnlyList<TrackDto>? ITrackListPayload.Items => ResultList;
}

/// <summary>
/// 把「取列表 + 取总数」从各家不同的字段名里抽出来。
/// </summary>
/// <remarks>
/// 这个接口是**手写实现**的（每个 payload 显式实现一次），不是靠反射或约定——
/// 源生成环境下没有隐式映射。
/// </remarks>
internal interface ITrackListPayload
{
    IReadOnlyList<TrackDto>? Items { get; }

    int Total { get; }
}
