using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// 分类树（<c>service/category/list</c>）。
/// </summary>
/// <remarks>
/// 形状取自实测样本 <c>fixtures/category-list.json</c>：
/// <c>{ total, customCategory, categories }</c>。本项目**只消费 <c>categories</c>** ——
/// <c>customCategory</c>（推荐 / 歌单）拿 id 去打歌单接口返回空，它不是子分类 id，
/// 所以有意不建模。
/// </remarks>
internal sealed class CategoryListPayload
{
    [JsonPropertyName("total")] public int Total { get; init; }

    [JsonPropertyName("categories")] public CategoryGroupDto[]? Categories { get; init; }
}

/// <summary>一组分类，形如「主题」「流派」。</summary>
internal sealed class CategoryGroupDto
{
    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>这一组自己的图标。界面目前用不到（组名当标签就够），**有意不映射**。</summary>
    [JsonPropertyName("subCategories")] public MusicCategoryDto[]? SubCategories { get; init; }
}

/// <summary>一个子分类，形如「网红」「古风」。**它的 <c>id</c> 才是取歌单用的那个。**</summary>
internal sealed class MusicCategoryDto
{
    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }
}
