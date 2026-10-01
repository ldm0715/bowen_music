using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// 已购单曲的信封：<c>{ musicList: [...], size: N }</c>。
/// </summary>
/// <remarks>
/// <para>
/// 元素是**曲目对象**（<c>Song</c>），所以复用 <see cref="TrackDto"/> —— 它不是专门的
/// 「已购单曲」类型（findings/06 §12.2）。
/// </para>
/// <para>
/// <b>该路径不带任何订单/购买时间/价格字段。</b> 唯一沾边的是通用的下载态与收藏态，
/// 与本项目无关。
/// </para>
/// <para>
/// <b>总数键是 <c>size</c> 不是 <c>total</c></b> —— 已购这一族两个接口都是这样。
/// 这是本项目里第三套总数键（另外两套是 <c>total</c> 与「没有总数」）。
/// </para>
/// </remarks>
internal sealed class PurchasedSinglesPayload : ITrackListPayload
{
    [JsonPropertyName("size")] public int Total { get; init; }

    [JsonPropertyName("musicList")] public TrackDto[]? MusicList { get; init; }

    IReadOnlyList<TrackDto>? ITrackListPayload.Items => MusicList;
}

/// <summary>已购专辑的信封：<c>{ albumList: [...], size: N }</c>。</summary>
internal sealed class PurchasedAlbumsPayload
{
    [JsonPropertyName("size")] public int Total { get; init; }

    [JsonPropertyName("albumList")] public AlbumDto[]? AlbumList { get; init; }
}

/// <summary>
/// 收藏的歌单（<c>service/collect/4/list</c>）的信封：<c>{ playLists: [...], total: N }</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>这个源本来是「收藏歌单」，本项目用它来驱动「收藏的专辑」一页</b> ——
/// 按用户的实测结论：移动端收藏歌单接口的内容与官方桌面端「收藏专辑」的效果一致。
/// <b>官方桌面端自己那条路（<c>service/collect/6/list</c>）已弃用</b>：
/// 它对本项目在测的账号返回 200 但 <c>data</c> 是空对象（<c>{}</c>）。
/// </para>
/// <para>
/// 形状本身是<b>高置信度</b>的：<c>playLists</c> + <c>total</c> 来自反编译 Dart 的解析代码
/// （<c>PlayListData.fromJson</c>），与同族的收藏歌单调用点对得上
/// （<c>reverse/findings/06-library-api.md</c> §11）。
/// </para>
/// <para>
/// 条目用 <see cref="AlbumDto"/> 接：它同时认<b>歌单归一化形状</b>（<c>id</c>）
/// 与<b>专辑形状</b>（<c>albumId</c>），谁有值用谁 ——
/// 官方桌面端本来就有 <c>parseJsonFromAlbumToSongList</c>（把专辑解析成歌单模型），
/// 所以这两种形状在同一条链路上都会遇到。
/// </para>
/// </remarks>
internal sealed class CollectedAlbumsPayload
{
    [JsonPropertyName("total")] public int Total { get; init; }

    [JsonPropertyName("playLists")] public AlbumDto[]? PlayLists { get; init; }
}
