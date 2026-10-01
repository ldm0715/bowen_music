using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// <c>service/home/index</c> 的 <c>data</c>：只有布局。
/// </summary>
/// <remarks>
/// 形状取自实测样本 <c>fixtures/home-index-raw.json</c>：
/// <c>{ moduleList: [{ id, type, name, delayMs }] }</c>，12 个模块。
/// </remarks>
internal sealed class HomeIndexPayload
{
    [JsonPropertyName("moduleList")] public HomeModuleDto[]? ModuleList { get; init; }
}

/// <summary>布局里的一个模块。<b>只有身份与类型，没有内容。</b></summary>
internal sealed class HomeModuleDto
{
    [JsonPropertyName("id")] public int Id { get; init; }

    [JsonPropertyName("type")] public int Type { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("delayMs")] public int DelayMs { get; init; }
}

// ── 模块内容：按 type 分四种形状，各自一个 DTO ────────────────────────────────
//
// **为什么不写一个「大而全」的 DTO**：songList 这个键在 type 4/11 里是曲目分组、
// 在 type 5 里是歌单卡片，同一个键两种类型，**一个属性接不了**。
// 而调用方在取内容之前就知道 type（来自 home/index），所以按 type 挑 DTO 是最干净的 ——
// 不必把 JsonElement 漏进上层（那正是 Dto/README.md 禁止的）。

/// <summary>type 3 / 10：一整片曲目，没有分组。</summary>
/// <remarks>实测样本：<c>fixtures/home-module-10.json</c>（type 10，15 首）。</remarks>
internal sealed class HomeMusicListPayload
{
    [JsonPropertyName("moduleName")] public string? ModuleName { get; init; }

    [JsonPropertyName("musicList")] public TrackDto[]? MusicList { get; init; }
}

/// <summary>type 4 / 11：曲目分组。</summary>
/// <remarks>
/// 实测样本：<c>fixtures/home-module-1.json</c>（type 4，组是 <c>{id, title, songs}</c>）、
/// 以及 type 11 的 <c>{title, passRecName, songs}</c>。
/// <b>组名两个键都可能出现，取有值的那个。</b>
/// </remarks>
internal sealed class HomeSongGroupsPayload
{
    [JsonPropertyName("moduleName")] public string? ModuleName { get; init; }

    [JsonPropertyName("songList")] public HomeSongGroupDto[]? SongList { get; init; }
}

/// <summary>一个曲目分组。</summary>
internal sealed class HomeSongGroupDto
{
    [JsonPropertyName("id")] public long Id { get; init; }

    /// <summary>组名。type 4 用这个。</summary>
    [JsonPropertyName("title")] public string? Title { get; init; }

    /// <summary>组名。type 11 用这个（「你的主题歌单」）。</summary>
    [JsonPropertyName("passRecName")] public string? PassRecName { get; init; }

    [JsonPropertyName("songs")] public TrackDto[]? Songs { get; init; }

    /// <summary>两个组名键取有值的那个。</summary>
    public string? Name => string.IsNullOrWhiteSpace(Title) ? PassRecName : Title;
}

/// <summary>type 5：歌单卡片（<b>注意这里 songList 是歌单，不是曲目分组</b>）。</summary>
/// <remarks>
/// 实测样本：<c>fixtures/home-module-5.json</c>。
/// 元素是**歌单**形状（<c>id</c>/<c>name</c>/<c>pic</c>/<c>sourceType</c>），
/// 所以直接用 <see cref="PlaylistDto"/>。
/// </remarks>
internal sealed class HomePlaylistCardsPayload
{
    [JsonPropertyName("moduleName")] public string? ModuleName { get; init; }

    [JsonPropertyName("songList")] public PlaylistDto[]? SongList { get; init; }
}

/// <summary>
/// AI 歌单详情（<c>service/home/aiPlaylistDetail?index=N</c>）。
/// </summary>
/// <remarks>
/// 形状取自实测样本 <c>fixtures/ai-playlist-0.json</c>：
/// <c>{ title, subTitle, bigTitle, musicList }</c>，实测 30 首。
/// <b>没有分页</b> —— 一次给全。
/// </remarks>
internal sealed class AiPlaylistPayload
{
    [JsonPropertyName("title")] public string? Title { get; init; }

    [JsonPropertyName("subTitle")] public string? SubTitle { get; init; }

    [JsonPropertyName("bigTitle")] public string? BigTitle { get; init; }

    [JsonPropertyName("musicList")] public TrackDto[]? MusicList { get; init; }
}
