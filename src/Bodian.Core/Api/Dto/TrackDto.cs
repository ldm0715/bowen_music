using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// 曲目对象。同时覆盖 <c>service/music/info</c> 与各列表接口。
/// </summary>
/// <remarks>
/// 两边**共有字段名一致**，差异只在「谁有哪几个键」：
/// <c>service/music/info</c> 独有 <see cref="LrcInfo"/>、<see cref="Categories"/>、社交计数等，
/// 搜索列表独有 <see cref="Subtitle"/>、<see cref="SearchTag"/>、<see cref="FSongName"/>。
/// 所以用同一个类型 + 可空属性即可，不必拆两个。
/// <para>
/// **id 类字段一律用 <c>long</c>**：真实 musicId 会超出 int32
/// （实测存在 <c>10250281307392909</c> 这样的 id）。
/// </para>
/// </remarks>
internal sealed class TrackDto
{
    // ── 身份 ────────────────────────────────────────────────────────────────

    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>与 <see cref="Name"/> 同值，列表接口两者都有。留作兜底。</summary>
    [JsonPropertyName("songName")] public string? SongName { get; init; }

    /// <summary>副标题。仅搜索列表有。</summary>
    [JsonPropertyName("subtitle")] public string? Subtitle { get; init; }

    /// <summary>仅搜索列表有，实测为空串。</summary>
    [JsonPropertyName("FSONGNAME")] public string? FSongName { get; init; }

    // ── 专辑与艺人 ──────────────────────────────────────────────────────────

    [JsonPropertyName("albumId")] public long AlbumId { get; init; }

    [JsonPropertyName("album")] public string? Album { get; init; }

    [JsonPropertyName("albumPic")] public string? AlbumPic { get; init; }

    [JsonPropertyName("albumPic120")] public string? AlbumPic120 { get; init; }

    [JsonPropertyName("artist")] public string? Artist { get; init; }

    [JsonPropertyName("artistId")] public long ArtistId { get; init; }

    [JsonPropertyName("artistPic")] public string? ArtistPic { get; init; }

    /// <summary>注意是**字符串**，不是数字。</summary>
    [JsonPropertyName("allArtistId")] public string? AllArtistId { get; init; }

    [JsonPropertyName("artists")] public TrackArtistDto[]? Artists { get; init; }

    // ── 时间 ────────────────────────────────────────────────────────────────

    /// <summary>时长，单位**秒**（不是毫秒）。</summary>
    [JsonPropertyName("duration")] public int DurationSeconds { get; init; }

    /// <summary>MV 时长，单位**秒**。仅 <c>music/info</c> 有。</summary>
    [JsonPropertyName("mvduration")] public int MvDurationSeconds { get; init; }

    [JsonPropertyName("releaseDate")] public string? ReleaseDate { get; init; }

    // ── 状态与标记 ──────────────────────────────────────────────────────────

    [JsonPropertyName("isMv")] public int IsMv { get; init; }

    [JsonPropertyName("vid")] public long Vid { get; init; }

    /// <summary>注意是**数字**不是 bool。</summary>
    [JsonPropertyName("online")] public int Online { get; init; }

    /// <summary>仅 <c>music/info</c> 有。这个**是 bool**（与 <see cref="Online"/> 相反）。</summary>
    [JsonPropertyName("offline")] public bool Offline { get; init; }

    [JsonPropertyName("preOnline")] public bool PreOnline { get; init; }

    [JsonPropertyName("isOriginal")] public int IsOriginal { get; init; }

    [JsonPropertyName("isNew")] public int IsNew { get; init; }

    [JsonPropertyName("isShowType")] public int IsShowType { get; init; }

    [JsonPropertyName("vidIsshow")] public int VidIsshow { get; init; }

    [JsonPropertyName("videoType")] public int VideoType { get; init; }

    [JsonPropertyName("videoFmtCodes")] public string? VideoFmtCodes { get; init; }

    [JsonPropertyName("showButton")] public int ShowButton { get; init; }

    [JsonPropertyName("showAd")] public int ShowAd { get; init; }

    [JsonPropertyName("isRedSong")] public int IsRedSong { get; init; }

    [JsonPropertyName("bodianPayAlbum")] public int BodianPayAlbum { get; init; }

    [JsonPropertyName("bodianPayAlbumOffline")] public int BodianPayAlbumOffline { get; init; }

    [JsonPropertyName("psrc")] public string? Psrc { get; init; }

    /// <summary>酷我资源 id。字段确实存在，但**本项目无用途**，不要拿它去试酷我侧旧接口。</summary>
    [JsonPropertyName("musicRid")] public string? MusicRid { get; init; }

    // ── 仅 music/info ───────────────────────────────────────────────────────

    [JsonPropertyName("isPay")] public int IsPay { get; init; }

    [JsonPropertyName("tpay")] public int Tpay { get; init; }

    [JsonPropertyName("downloadAdvert")] public int DownloadAdvert { get; init; }

    [JsonPropertyName("haveAudition")] public bool HaveAudition { get; init; }

    [JsonPropertyName("hasKnowledge")] public bool HasKnowledge { get; init; }

    [JsonPropertyName("isbatch")] public int IsBatch { get; init; }

    [JsonPropertyName("cpId")] public int CpId { get; init; }

    [JsonPropertyName("kw_cp")] public int KwCp { get; init; }

    [JsonPropertyName("data_source")] public int DataSource { get; init; }

    [JsonPropertyName("terminalOnline")] public string? TerminalOnline { get; init; }

    [JsonPropertyName("bak3")] public int Bak3 { get; init; }

    [JsonPropertyName("ambientSound")] public string? AmbientSound { get; init; }

    [JsonPropertyName("spectrum")] public string? Spectrum { get; init; }

    [JsonPropertyName("mvPic")] public string? MvPic { get; init; }

    [JsonPropertyName("language")] public string? Language { get; init; }

    /// <summary>全站收藏数，**不是**「当前用户是否已收藏」。</summary>
    [JsonPropertyName("favorite")] public long Favorite { get; init; }

    [JsonPropertyName("share")] public long Share { get; init; }

    [JsonPropertyName("comment")] public long Comment { get; init; }

    [JsonPropertyName("lrcUpdateTime")] public string? LrcUpdateTime { get; init; }

    [JsonPropertyName("categorys")] public CategoryDto[]? Categories { get; init; }

    /// <summary>这首歌有哪些歌词轨。P4 拉歌词前先用它决定请求 <c>lrcx=1</c> 还是 <c>0</c>。</summary>
    [JsonPropertyName("lrc_info")] public LrcInfoDto? LrcInfo { get; init; }

    /// <summary>官方给的歌词效果预设，P5 可作观感参考。</summary>
    [JsonPropertyName("lrcEffect")] public LrcEffectDto? LrcEffect { get; init; }

    // ── 两者都有 ────────────────────────────────────────────────────────────

    [JsonPropertyName("audios")] public AudioEntryDto[]? Audios { get; init; }

    [JsonPropertyName("payInfo")] public PayInfoDto? PayInfo { get; init; }

    [JsonPropertyName("mediaBasicInfo")] public MediaBasicInfoDto? MediaBasicInfo { get; init; }

    [JsonPropertyName("searchTag")] public SearchTagDto? SearchTag { get; init; }
}

/// <summary>艺人条目，形如 <c>{ id, name, pic }</c>。</summary>
internal sealed class TrackArtistDto
{
    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("pic")] public string? Pic { get; init; }
}

/// <summary>
/// 一档音源。
/// </summary>
/// <remarks>
/// 两条实测约束：
/// <list type="bullet">
/// <item><see cref="Bitrate"/> 与 <see cref="Size"/> 都是**字符串**，<see cref="Size"/> 还带单位
/// （<c>"52.83Mb"</c>，<c>zp</c> 档是占位串 <c>"zpMb"</c>）。**不要直接解析成数字。**</item>
/// <item>数组顺序**不按档位高低排**（实测首元素是 <c>bcms</c>、<c>ff</c> 排第六）。
/// 选档必须按 <see cref="Level"/> 查表，不能靠顺序。</item>
/// </list>
/// </remarks>
internal sealed class AudioEntryDto
{
    /// <summary>档位标识：<c>s</c> / <c>h</c> / <c>p</c> / <c>ff</c> / <c>zp</c> / <c>bcms</c> …</summary>
    [JsonPropertyName("level")] public string? Level { get; init; }

    [JsonPropertyName("format")] public string? Format { get; init; }

    [JsonPropertyName("bitrate")] public string? Bitrate { get; init; }

    [JsonPropertyName("size")] public string? Size { get; init; }
}

/// <summary>ReplayGain / EBU R128 响度信息。</summary>
internal sealed class MediaBasicInfoDto
{
    [JsonPropertyName("gain")] public double Gain { get; init; }

    [JsonPropertyName("peak")] public double Peak { get; init; }

    [JsonPropertyName("lra")] public double Lra { get; init; }
}

/// <summary>曲目标签。</summary>
internal sealed class CategoryDto
{
    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }
}

/// <summary>
/// 歌词轨清单。<c>1</c> = 有该轨。
/// </summary>
/// <remarks>
/// <see cref="Lrcx"/> 决定请求逐字版还是逐行版：该曲没有逐字轨时，服务端对 <c>lrcx=1</c>
/// 返回**空串**而不做逐行回退，所以要先看这里再决定请求哪一版。
/// </remarks>
internal sealed class LrcInfoDto
{
    [JsonPropertyName("lrc")] public int Lrc { get; init; }

    [JsonPropertyName("lrcx")] public int Lrcx { get; init; }

    [JsonPropertyName("lrcs")] public int Lrcs { get; init; }

    [JsonPropertyName("lrc_roma")] public int LrcRoma { get; init; }

    [JsonPropertyName("lrcx_roma")] public int LrcxRoma { get; init; }

    [JsonPropertyName("lrc_homophone")] public int LrcHomophone { get; init; }

    [JsonPropertyName("lrcx_homophone")] public int LrcxHomophone { get; init; }

    [JsonPropertyName("lrc_kana")] public int LrcKana { get; init; }

    [JsonPropertyName("lrcx_kana")] public int LrcxKana { get; init; }
}

/// <summary>官方歌词效果预设。</summary>
internal sealed class LrcEffectDto
{
    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("font")] public string? Font { get; init; }

    [JsonPropertyName("color")] public string? Color { get; init; }
}

/// <summary>搜索结果的角标。</summary>
/// <remarks>
/// <see cref="Top"/> 与 <see cref="Bottom"/> **都是可选的**——实测 5 条结果里有 2 条带
/// <c>bottom</c>、3 条不带，所以两个属性都可空。
/// </remarks>
internal sealed class SearchTagDto
{
    [JsonPropertyName("top")] public SearchTagItemDto? Top { get; init; }

    [JsonPropertyName("mid")] public SearchTagItemDto[]? Mid { get; init; }

    [JsonPropertyName("bottom")] public SearchTagItemDto? Bottom { get; init; }
}

/// <summary>
/// 角标条目。实测字段固定为这五个。
/// </summary>
/// <remarks>
/// 还带一个 <c>jumpInfo</c>，实测是空对象 <c>{}</c> 且语义未知，**有意不建模**——
/// 没有语义的字段声明成对象只会变成噪音。见本目录的 README。
/// </remarks>
internal sealed class SearchTagItemDto
{
    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("type")] public int Type { get; init; }

    [JsonPropertyName("backColor")] public string? BackColor { get; init; }

    [JsonPropertyName("fontColor")] public string? FontColor { get; init; }
}
