using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// 乐库分类树（<c>play/music/library/navigation</c>）。
/// </summary>
/// <remarks>
/// <b><c>data</c> 是裸数组</b>，不是包一层对象 —— 与 <c>service/home/index</c> 那种不同。
/// 形状取自实测样本 <c>fixtures/musiclib-navigation.json</c>。
/// </remarks>
internal sealed class MusicLibraryNavDto
{
    /// <summary>分类 id。**字符串**，如 <c>"2005"</c>。</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>中文名，如「流行」。**界面上的分类名用这个。**</summary>
    [JsonPropertyName("ptypeName")] public string? PTypeName { get; init; }

    /// <summary>大字标题，如 <c>POP MUSIC</c>（含换行）。装饰用。</summary>
    [JsonPropertyName("internalTitle")] public string? InternalTitle { get; init; }

    /// <summary>同一标题的单行形式，如 <c>POP MUSIC</c>。</summary>
    [JsonPropertyName("externalTitle")] public string? ExternalTitle { get; init; }

    /// <summary>分类简介，实测每类都有且不短。</summary>
    [JsonPropertyName("longDesc")] public string? LongDesc { get; init; }

    [JsonPropertyName("coverPic")] public string? CoverPic { get; init; }

    /// <summary>该分类下的专辑总数。实测从 686 到 84227 不等。</summary>
    [JsonPropertyName("albumTotal")] public int AlbumTotal { get; init; }

    [JsonPropertyName("priority")] public int Priority { get; init; }

    /// <summary>子类。<b>每一类下的子类名与 <c>longDescTitle</c> 是界面要用的。</b></summary>
    [JsonPropertyName("childList")] public MusicLibraryChildDto[]? ChildList { get; init; }
}

/// <summary>乐库的一个子类，如「国语流行」。</summary>
internal sealed class MusicLibraryChildDto
{
    /// <summary>子类 id（如 <c>"8101"</c>）。<b>取该子类专辑列表时要用的那个 id</b>（尚未接通）。</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>子类名，如「国语流行」。**没有 <c>longDesc</c> 里的中文名时可退回这个。**</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>父类的 type id（如 <c>"3401"</c>）。**注意不是父项自己的 <c>id</c>。**</summary>
    [JsonPropertyName("ptypeId")] public string? PTypeId { get; init; }

    [JsonPropertyName("longDesc")] public string? LongDesc { get; init; }

    /// <summary>形如 <c>[ 国语流行 - Chinese Pop ]</c>，界面上的小标题。</summary>
    [JsonPropertyName("longDescTitle")] public string? LongDescTitle { get; init; }

    /// <summary>
    /// 该子类的**代表专辑**（不是列表）。
    /// </summary>
    /// <remarks>
    /// 这是 <c>navigation</c> 顺手带的样本，用来画卡片封面刚好 ——
    /// **一个子类只有这一张**，完整列表要另调 <c>play/music/library/albums</c>（见 findings 07）。
    /// 形状与 <see cref="AlbumDto"/> 相同，所以直接复用。
    /// </remarks>
    [JsonPropertyName("albumVo")] public AlbumDto? AlbumVo { get; init; }
}


/// <summary>
/// 子类专辑列表（<c>play/music/library/albums</c>）的 <c>data</c>：<c>{ total, list }</c>。
/// </summary>
/// <remarks>
/// 形状取自实测样本 <c>fixtures/musiclib-albums-8101.json</c>（国语流行，<c>total</c> 5177）。
/// 元素与 <c>service/album/{id}</c> 同一个专辑形状，所以复用 <see cref="AlbumDto"/>。
/// </remarks>
internal sealed class MusicLibraryAlbumsPayload
{
    [JsonPropertyName("total")] public int Total { get; init; }

    [JsonPropertyName("list")] public AlbumDto[]? List { get; init; }
}
