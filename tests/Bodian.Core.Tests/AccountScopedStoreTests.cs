using Bodian.Core.Models;
using Bodian.Core.Services;
using Bodian.Core.Services.Implementations;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 三份账号级数据按作用域分开存：历史、搜索历史、播放队列。
/// </summary>
/// <remarks>
/// <b>根目录指向临时目录</b>，否则这些用例会读到、写到本机真实数据。
/// </remarks>
public sealed class AccountScopedStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "bodian-account-scope-tests", Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static Track Track(long id) => new()
    {
        Id = id,
        Title = $"Track {id}",
        AvailableQualities = [AudioQuality.Lossless],
    };

    // ── 播放历史 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task History_IsScoped_AndTheInMemoryCopyFollowsTheSwitch()
    {
        var account = new FakeCurrentAccount();
        account.SignIn("111");

        var store = new JsonPlayHistoryStore(account: account, rootDirectory: _root);

        await store.RecordAsync(Track(1), Ct);
        Assert.Single(await store.GetRecentAsync(10, Ct));

        // 换账号：另一份历史。**这是本次改动的重点** —— store 内存里缓存着上一份，
        // 只换路径不清缓存的话，A 的历史会原样喂给 B。
        account.SignIn("222");
        Assert.Empty(await store.GetRecentAsync(10, Ct));

        await store.RecordAsync(Track(2), Ct);
        Assert.Equal(2, (await store.GetRecentAsync(10, Ct)).Single().MusicId);

        // 换回 A：它那份原样还在。
        account.SignIn("111");
        Assert.Equal(1, (await store.GetRecentAsync(10, Ct)).Single().MusicId);
    }

    [Fact]
    public async Task History_ForAnonymous_GoesToItsOwnBucket()
    {
        var store = new JsonPlayHistoryStore(account: new FakeCurrentAccount(), rootDirectory: _root);

        await store.RecordAsync(Track(7), Ct);

        var anonymous = Path.Combine(_root, AppPaths.AccountsDirectoryName, AppPaths.AnonymousScope,
            AppPaths.PlayHistoryFileName);

        Assert.True(File.Exists(anonymous));
    }

    [Fact]
    public async Task History_WritesSeparateFilesPerAccount()
    {
        var account = new FakeCurrentAccount();
        var store = new JsonPlayHistoryStore(account: account, rootDirectory: _root);

        account.SignIn("111");
        await store.RecordAsync(Track(1), Ct);

        account.SignIn("222");
        await store.RecordAsync(Track(2), Ct);

        Assert.True(File.Exists(Scoped("111", AppPaths.PlayHistoryFileName)));
        Assert.True(File.Exists(Scoped("222", AppPaths.PlayHistoryFileName)));
    }

    // ── 搜索历史 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchHistory_IsScoped()
    {
        var account = new FakeCurrentAccount();
        account.SignIn("111");

        var store = new JsonSearchHistoryStore(account: account, rootDirectory: _root);

        await store.SaveAsync(["晴天"]);
        Assert.Equal(["晴天"], store.Load());

        account.SignIn("222");
        Assert.Empty(store.Load());

        await store.SaveAsync(["夜曲"]);
        Assert.Equal(["夜曲"], store.Load());

        account.SignIn("111");
        Assert.Equal(["晴天"], store.Load());
    }

    // ── 播放队列 ───────────────────────────────────────────────────────────

    [Fact]
    public void Queue_IsScoped_AndTheScopeIsExplicit()
    {
        var store = new JsonPlayQueueSnapshotStore(rootDirectory: _root);

        store.Save("111", new PlayQueueSnapshot { Items = [new QueuedTrack { Id = 1, Title = "A" }] });

        Assert.Single(store.Load("111").Items);
        Assert.Empty(store.Load("222").Items);

        // 不想清掉的那一份在 DeleteAllScopes 之前先确认一下：它删的是所有作用域。
        Assert.Single(store.Load("111").Items);
    }

    [Fact]
    public async Task DeleteAllScopes_RemovesEveryAccountsQueue_ButNotTheHistory()
    {
        var store = new JsonPlayQueueSnapshotStore(rootDirectory: _root);

        store.Save("111", new PlayQueueSnapshot { Items = [new QueuedTrack { Id = 1, Title = "A" }] });
        store.Save("222", new PlayQueueSnapshot { Items = [new QueuedTrack { Id = 2, Title = "B" }] });

        var account = new FakeCurrentAccount();
        account.SignIn("111");
        var history = new JsonPlayHistoryStore(account: account, rootDirectory: _root);
        await history.RecordAsync(Track(1), Ct);

        Assert.True(store.DeleteAllScopes());

        Assert.Empty(store.Load("111").Items);
        Assert.Empty(store.Load("222").Items);

        // 抹的是「记住播放列表」，最近播放不该跟着没。
        Assert.True(File.Exists(Scoped("111", AppPaths.PlayHistoryFileName)));
    }

    [Fact]
    public void DeleteAllScopes_WithNoAccountsDirectory_IsNotAnError()
    {
        Assert.True(new JsonPlayQueueSnapshotStore(rootDirectory: _root).DeleteAllScopes());
    }

    private string Scoped(string scope, string fileName) =>
        AppPaths.AccountFilePath(scope, fileName, _root);
}
