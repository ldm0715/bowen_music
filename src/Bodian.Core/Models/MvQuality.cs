namespace Bodian.Core.Models;

/// <summary>
/// MV 画质档位。<b>枚举值就是请求里的 <c>wifi</c> 参数</b>，不是显示顺序。
/// </summary>
/// <remarks>
/// <para>
/// 端点一次只回两条直链（<c>highUrl</c> / <c>lowUrl</c>），<b>哪一档进 <c>highUrl</c> 由请求的
/// <c>wifi</c> 决定</b>。实测（2026-10-04，《晴天》228908 与《夜曲》118980 两首复核一致，
/// 见 <c>reverse/findings/15-mv.md</c>）：
/// </para>
/// <list type="bullet">
/// <item><c>wifi=3</c> → <c>highUrl</c> 2000 kbps —— <b>不传参数时服务端给的也是这一档</b>；</item>
/// <item><c>wifi=2</c> → 1000 kbps；</item>
/// <item><c>wifi=0</c> / <c>1</c> / <c>&gt;=4</c> → 回落到 512 kbps（与 <c>lowUrl</c> 同一条）。</item>
/// </list>
/// <para>
/// <b>换档必须重新请求</b> —— 三条是三个不同的 mp4，不是同一份流的本地筛选。
/// 也因为没有比 2000 更高的一档，这个选择器在实测的两首上**只能往下切**。
/// </para>
/// </remarks>
public enum MvQuality
{
    /// <summary>高清，实测 2000 kbps。默认 —— 与不传 <c>wifi</c> 时拿到的同一档。</summary>
    High = 3,

    /// <summary>标清，实测 1000 kbps。</summary>
    Standard = 2,

    /// <summary>流畅，实测 512 kbps（与 <c>lowUrl</c> 同一条）。</summary>
    Low = 0,
}
