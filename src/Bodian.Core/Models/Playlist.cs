namespace Bodian.Core.Models;

/// <summary>
/// 一个歌单。侧栏的「创建的歌单」与曲库各列表共用。
/// </summary>
/// <remarks>
/// <para>
/// <b>只映射真正有消费方的字段</b>（与 <see cref="Track"/> 同一条规矩）：侧栏要
/// <see cref="Id"/> 与 <see cref="Name"/>，歌单卡片要 <see cref="CoverImage"/> 与
/// <see cref="MusicCount"/>，详情页头部要曲目数、播放数、创建者与简介。
/// </para>
/// <para>
/// <b>详情专属字段只在 <c>BodianApi.GetPlaylistInfoAsync</c> 里填</b>（创建者、简介、播放数、
/// 收藏数、收藏时间）。列表来源 —— 侧栏、搜索、收藏列表 —— 走的是另一个映射，这些字段留默认值，
/// 即「这个来源没给」。这与 <see cref="Album"/> 是同一个先例：它的
/// <c>ArtistText</c> / <c>ReleaseDate</c> / <c>Description</c> 也只有详情请求会填。
/// </para>
/// <para>
/// <see cref="MusicCount"/> 是<b>服务端给的计数，不可信</b>：不可用曲目会被省略，
/// 实测官方把标称 121 首的歌单首页只回了 99 首。判断「还有没有下一页」只能看
/// <c>PagedCursor.Exhausted</c>，不要拿它算页数。
/// </para>
/// </remarks>
public sealed record Playlist
{
    /// <summary>歌单 id。用它拼 <c>service/playlist/{id}/musicList</c>。</summary>
    public required long Id { get; init; }

    /// <summary>歌单名。</summary>
    public required string Name { get; init; }

    /// <summary>服务端标称的曲目数。<b>只用于展示。</b></summary>
    public int MusicCount { get; init; }

    /// <summary>封面地址。</summary>
    public Uri? CoverImage { get; init; }

    /// <summary>
    /// 歌单来源，**直接就是取曲目时要填的 <c>source</c>**。原样来自服务端，不做归一化。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 账号歌单（自建与「我喜欢」）是 <c>5</c>；而<b>发现页里的歌单实测是 <c>13</c></b>，
    /// 不是文档说的公开集合默认值 <c>4</c>。所以这里**服务端给什么就存什么** ——
    /// 归一化会让取曲目时填错 <c>source</c>，而服务端对不上的值只回空、不报错，
    /// 表现就是「点进去是空歌单」。
    /// </para>
    /// <para>字段缺失时是 <c>0</c>，调用方要自己兜底。</para>
    /// <para>
    /// 它必须存在：拿一个歌单去取曲目时，<c>source</c> 填错的表现为**曲目列表直接是空的**。
    /// </para>
    /// </remarks>
    public int SourceType { get; init; }

    /// <summary>是不是隐私歌单。界面在封面右下角画一颗锁来区分。</summary>
    /// <remarks>
    /// 来自响应的 <c>isPrivate</c> —— 服务端给的是**数字**不是布尔
    /// （见 <c>PlaylistDto.IsPrivate</c>），映射时归一成布尔。
    /// 新建歌单时本地插入的那一行由开关直接带上。
    /// </remarks>
    public bool IsPrivate { get; init; }

    // ── 以下都是详情专属字段（见类型注释）────────────────────────────────

    /// <summary>创建者 uid。<c>0</c> 表示这个来源没给。</summary>
    /// <remarks>
    /// 判断「这个歌单是不是我自己创建的」就靠它跟当前账号 uid 比 ——
    /// 从搜索结果点进自己创建的公开歌单时，<c>SourceType</c> 不是账号歌单的 5，只有它能认出来。
    /// </remarks>
    public long CreatorId { get; init; }

    /// <summary>创建者昵称。空串表示这个来源没给。</summary>
    public string CreatorName { get; init; } = "";

    /// <summary>创建者头像。**只有详情会给**。</summary>
    public Uri? CreatorCover { get; init; }

    /// <summary>歌单简介。实测可能很长（整篇企划文案），界面上要折叠。</summary>
    public string Description { get; init; } = "";

    /// <summary>全站播放数。<b>只用于展示</b>，且**只有详情会给**。</summary>
    public long PlayCount { get; init; }

    /// <summary>
    /// 全站收藏人数，**不是当前账号的收藏态** —— 与 <c>music/info</c> 的 <c>favorite</c> 同类陷阱。
    /// </summary>
    public long CollectedCount { get; init; }

    /// <summary>
    /// 当前账号的收藏时间。<b>存在即「已收藏」</b>，未收藏或未登录时是空串。
    /// </summary>
    /// <remarks>
    /// 这是**个人态**，方向与上面那些内容字段不同。它留在这里的理由只有一个：
    /// 收藏态与歌单元数据来自**同一个响应**（<c>service/playlist/info</c>），
    /// 丢掉它再单独查一次是白跑一趟。别把它当成歌单自身的属性去别处用。
    /// </remarks>
    public string CollectTime { get; init; } = "";

    /// <summary>有简介可展示。简介空时界面上整个折叠区都不该出现。</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>拿到了创建者信息，头部那一行才有得显示。</summary>
    public bool HasCreator => CreatorId > 0 || !string.IsNullOrWhiteSpace(CreatorName);

    /// <summary>当前账号是否已收藏这个歌单。判据是 <see cref="CollectTime"/> 存在，**不是 <c>isFond</c>**。</summary>
    public bool IsCollected => !string.IsNullOrEmpty(CollectTime);
}
