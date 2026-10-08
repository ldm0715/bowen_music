using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Tests.Support;
using Bodian.WinUI.Playback;
using Bodian.WinUI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 「只加载一次」的守卫必须按账号判等。
/// </summary>
/// <remarks>
/// 换账号后页面实例与它的视图模型都还在（导航栈和闲置页面缓存持有它们），
/// 只看「加载过没有」会让新账号看到上一个账号的列表 ——
/// 表现是切号后点侧栏「我喜欢的 / 收藏的歌单」，看到的还是上一个人的。
/// </remarks>
public sealed class AccountScopedListTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PagedList<int> Numbers(FakeCurrentAccount account, out Func<int> fetchCount)
    {
        var fetches = 0;
        fetchCount = () => fetches;

        return new PagedList<int>(
            (cursor, _) =>
            {
                fetches++;
                return Task.FromResult(new PagedResult<int>([1], cursor.Offset, cursor.PageSize, null));
            },
            NullLogger.Instance,
            "测试列表",
            "暂无内容",
            account: account);
    }

    [Fact]
    public async Task PagedList_ReloadsWhenTheAccountChanges()
    {
        var account = new FakeCurrentAccount();
        account.SignIn("111");

        var list = Numbers(account, out var fetches);

        await list.EnsureLoadedAsync(Ct);
        await list.EnsureLoadedAsync(Ct);
        Assert.Equal(1, fetches());

        account.SignIn("222");
        await list.EnsureLoadedAsync(Ct);
        Assert.Equal(2, fetches());

        // 新账号也只拉一次。
        await list.EnsureLoadedAsync(Ct);
        Assert.Equal(2, fetches());

        // 登出后的匿名桶同样是「另一个账号」。
        account.SignOut();
        await list.EnsureLoadedAsync(Ct);
        Assert.Equal(3, fetches());
    }

    [Fact]
    public async Task PagedList_WithoutAnAccount_StillLoadsOnce()
    {
        // 不传账号（离线宿主与不认识账号的宿主）：退回原来的行为，
        // 不因为判据里多了一个字段就变成每次都拉。
        var fetches = 0;
        var list = new PagedList<int>(
            (cursor, _) =>
            {
                fetches++;
                return Task.FromResult(new PagedResult<int>([], cursor.Offset, cursor.PageSize, null));
            },
            NullLogger.Instance,
            "测试列表",
            "暂无内容");

        await list.EnsureLoadedAsync(Ct);
        await list.EnsureLoadedAsync(Ct);

        Assert.Equal(1, fetches);
    }

    [Fact]
    public async Task PlaylistTracks_ReloadsWhenTheAccountChanges()
    {
        var account = new FakeCurrentAccount();
        account.SignIn("111");

        using var coordinator = new PlaybackCoordinator(
            new PlaybackApiStub(), new FakePlaybackEngine(), new FakePlayHistoryStore());

        var list = new CountingPlaylistTracks(coordinator, account);

        await list.EnsureLoadedAsync(Ct);
        await list.EnsureLoadedAsync(Ct);
        Assert.Equal(1, list.ResolveCalls);

        account.SignIn("222");
        await list.EnsureLoadedAsync(Ct);
        Assert.Equal(2, list.ResolveCalls);
    }

    /// <summary>
    /// 「我喜欢的」那一族的最小替身：只数「为谁拉过一次」。
    /// </summary>
    /// <remarks>
    /// <c>ResolvePlaylistAsync</c> 返回 <c>null</c>（「没有这个歌单」），于是
    /// <c>ReloadAsync</c> 到此为止、不再发后续请求 —— 守卫本身照样被走到。
    /// </remarks>
    private sealed class CountingPlaylistTracks(PlaybackCoordinator coordinator, ICurrentAccount account)
        : PlaylistTracksViewModel(new PlaybackApiStub(), coordinator, NullLogger.Instance, account)
    {
        public int ResolveCalls { get; private set; }

        protected override Task<Playlist?> ResolvePlaylistAsync(CancellationToken cancellationToken)
        {
            ResolveCalls++;
            return Task.FromResult<Playlist?>(null);
        }

        protected override string MissingText => "没有这个歌单。";

        protected override string EmptyText => "还没有歌。";
    }
}
