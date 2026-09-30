namespace Bodian.Core.Models;

/// <summary>
/// 这首歌有哪些歌词轨。拉歌词前用它决定请求 <c>lrcx=1</c> 还是 <c>0</c>。
/// </summary>
/// <remarks>
/// 曲目详情接口（<c>service/music/info</c>）会带 <c>lrc_info</c>，搜索结果**不带** ——
/// 所以 <see cref="Track.Lyrics"/> 是可空的：为 <c>null</c> 时只能先试逐字版，
/// 拿到空串再退逐行版。
/// </remarks>
/// <param name="HasLineByLine">有逐行轨。</param>
/// <param name="HasWordByWord">有逐字轨。为 <c>false</c> 时请求 <c>lrcx=1</c> 只会拿到空串。</param>
public sealed record TrackLyricInfo(bool HasLineByLine, bool HasWordByWord);
