using Bodian.Core.Services.Abstractions;

namespace Bodian.Core.Tests.Support;

/// <summary>
/// 喜欢状态的假实现：本地一个集合，写请求按 <see cref="Next"/> 出结果。
/// </summary>
/// <remarks>
/// 播放条那颗喜欢按钮与曲目行的「更多」菜单都要用它，所以放在 <c>Support</c> 里共用 ——
/// 两份各自演化的话，「写失败时状态不动」这类约定会在一边悄悄失效。
/// </remarks>
internal sealed class LikedSongsStub : ILikedSongsService
{
    public HashSet<long> Liked { get; } = [];

    /// <summary>下一次写请求的结果。</summary>
    public LikedSongsOutcome Next { get; set; } = LikedSongsOutcome.Succeeded;

    public List<(long Id, bool Liked)> Requests { get; } = [];

    public Task<bool?> IsLikedAsync(long musicId, CancellationToken cancellationToken = default)
        => Task.FromResult<bool?>(Liked.Contains(musicId));

    public Task<LikedSongsOutcome> SetLikedAsync(long musicId, bool liked,
        CancellationToken cancellationToken = default)
    {
        Requests.Add((musicId, liked));
        if (Next != LikedSongsOutcome.Succeeded) return Task.FromResult(Next);

        if (liked) Liked.Add(musicId);
        else Liked.Remove(musicId);
        return Task.FromResult(LikedSongsOutcome.Succeeded);
    }
}
