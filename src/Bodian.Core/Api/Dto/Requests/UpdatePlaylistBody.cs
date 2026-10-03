using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto.Requests;

/// <summary>
/// <c>service/playlist</c> 编辑歌单的请求体（PUT）。
/// </summary>
/// <remarks>
/// <para>
/// <b>五个键都有反汇编字面量证据</b>（<c>edit_user_playlist.dart:3796-3914</c>），
/// 但整个 PUT 路径<b>尚未实测</b>。
/// </para>
/// <para>
/// <b>没有 <c>private</c> 键</b> —— 官方编辑页没有隐私开关，创建之后改不了隐私。
/// 不要在这里加，静态证据不支持。
/// </para>
/// <para>
/// <see cref="Pic"/> 要传<b>原始串</b>（<c>Playlist.CoverRawUrl</c>），不是规范化过的 <c>Uri</c>：
/// 服务端是否接受改写过的地址没有验证过。
/// </para>
/// </remarks>
internal sealed record UpdatePlaylistBody
{
    /// <summary>歌单 id。**这个键名原本是按数组槽序推断的，现已取到字面量证据**（<c>0x1440b0c</c>）。</summary>
    [JsonPropertyName("id")] public required long Id { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("description")] public required string Description { get; init; }

    /// <summary>封面地址。不改封面时<b>必须回传原值</b>，传空串是否会清掉封面未验证。</summary>
    [JsonPropertyName("pic")] public required string Pic { get; init; }

    /// <summary>
    /// 标签。<b>分类 id 数组</b>，id 来自 <c>service/category/list</c>，官网上限 3 个。
    /// 歌单当前已有的标签从 <c>service/playlist/info</c> 的 <c>categories</c> 读回。
    /// </summary>
    [JsonPropertyName("categoryList")] public required int[] CategoryList { get; init; }
}
