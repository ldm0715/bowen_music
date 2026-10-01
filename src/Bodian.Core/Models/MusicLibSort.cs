namespace Bodian.Core.Models;

/// <summary>
/// 乐库子类专辑列表的排序方式。
/// </summary>
/// <remarks>
/// <b>服务端要的是字符串 <c>"1"</c> / <c>"2"</c>，不是数字。</b>
/// 这是乐库这条链路卡最久的一处：传数字（哪怕 <c>"0"</c>）服务端会回 <c>200</c>
/// 但 <c>data</c> 是**空对象**，不报错 —— 看起来像"参数不全"或"没有数据"。
/// <para>
/// 取值来自反编译产物里 <c>_AlbumSection</c> 的构造：
/// <c>"精品" → "1"</c>、<c>"最新" → "2"</c>（也就是那两个 tab）。
/// </para>
/// </remarks>
public enum MusicLibSort
{
    /// <summary>精品。<c>sort="1"</c>。</summary>
    Curated = 1,

    /// <summary>最新。<c>sort="2"</c>。</summary>
    Newest = 2,
}

/// <summary>
/// <see cref="MusicLibSort"/> 与线上取值的换算。
/// </summary>
/// <remarks>
/// <b>写成一个函数而不是 <c>ToString()</c>：</b> 枚举名与线上字符串无关，
/// 靠 <c>ToString()</c> 会隐式地把"改名"变成"改协议"。
/// </remarks>
public static class MusicLibSortExtensions
{
    public static string ToRequestValue(this MusicLibSort sort) => sort switch
    {
        MusicLibSort.Curated => "1",
        MusicLibSort.Newest => "2",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "未知的排序方式"),
    };
}
