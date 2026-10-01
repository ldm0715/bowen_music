namespace Bodian.Core.Models.Home;

/// <summary>
/// 一个 AI 歌单的完整内容。
/// </summary>
/// <remarks>
/// 「个性化歌单」模块里每一组都是一个 AI 歌单，模块只给 3 首预览；
/// 这里是通过 <c>service/home/aiPlaylistDetail?index=N</c> 取到的完整列表（实测 30 首）。
/// </remarks>
/// <param name="Title">歌单名，与模块里那一组的标题一致。</param>
/// <param name="Subtitle">副标题，形如「为你量身打造的专属歌单」。</param>
/// <param name="BigTitle">大字标题，形如 <c>Daily Songs</c>。界面上的装饰，可为空串。</param>
/// <param name="Tracks">完整曲目。</param>
public sealed record AiPlaylist(
    string Title,
    string Subtitle,
    string BigTitle,
    IReadOnlyList<Track> Tracks);
