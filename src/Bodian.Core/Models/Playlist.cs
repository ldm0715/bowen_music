namespace Bodian.Core.Models;

/// <summary>
/// 一个歌单。侧栏的「创建的歌单」与曲库各列表共用。
/// </summary>
/// <remarks>
/// <para>
/// <b>只映射真正有消费方的字段</b>（与 <see cref="Track"/> 同一条规矩）：侧栏要
/// <see cref="Id"/> 与 <see cref="Name"/>，歌单卡片要 <see cref="CoverImage"/> 与
/// <see cref="MusicCount"/>，详情页头部要曲目数。底层 DTO 里还有创建者、简介、私密标志等
/// 十几个字段，**没有消费方就不上来**。
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
}
