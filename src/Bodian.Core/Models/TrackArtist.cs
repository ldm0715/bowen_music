namespace Bodian.Core.Models;

/// <summary>
/// 曲目的一位艺人。
/// </summary>
/// <param name="Id">艺人 id。</param>
/// <param name="Name">艺人名。</param>
/// <param name="Avatar">头像地址，可能为空。</param>
public sealed record TrackArtist(long Id, string Name, Uri? Avatar);
