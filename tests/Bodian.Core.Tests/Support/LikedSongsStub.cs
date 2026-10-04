using Bodian.Core.Services;
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

    public Task<LikedSongsBatchOutcome> SetLikedManyAsync(
        IReadOnlyList<long> musicIds, bool liked = true,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var id in musicIds)
        {
            Requests.Add((id, liked));
        }

        if (Next != LikedSongsOutcome.Succeeded)
        {
            return Task.FromResult(new LikedSongsBatchOutcome(Next, 0, 0, false));
        }

        foreach (var id in musicIds)
        {
            if (liked) Liked.Add(id);
            else Liked.Remove(id);
        }

        // 假的就一次报完：真实的逐首实现会一条一条报，这里只验调用方对进度的反应。
        progress?.Report(new BatchProgress(musicIds.Count, musicIds.Count));

        return Task.FromResult(new LikedSongsBatchOutcome(
            LikedSongsOutcome.Succeeded, musicIds.Count, 0, false));
    }
}
