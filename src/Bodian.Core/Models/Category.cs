namespace Bodian.Core.Models;

/// <summary>
/// 乐库的一组分类，形如「主题」「流派」。
/// </summary>
/// <param name="Name">组名。</param>
/// <param name="SubCategories">组里的子分类。</param>
public sealed record CategoryGroup(string Name, IReadOnlyList<MusicCategory> SubCategories);

/// <summary>
/// 一个子分类，形如「网红」「古风」。
/// </summary>
/// <param name="Id">取它的歌单时用的 id。</param>
/// <param name="Name">分类名。</param>
public sealed record MusicCategory(long Id, string Name);
