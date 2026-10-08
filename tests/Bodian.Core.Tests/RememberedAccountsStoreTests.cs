using System.Runtime.Versioning;
using System.Text;
using Bodian.Core.Models.Account;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 「记住的账号」清单的落盘。需要真实 DPAPI，所以整类标 Windows-only。
/// </summary>
/// <remarks>
/// 目录指向临时目录，绝不碰 <c>%LOCALAPPDATA%\Bowen</c>。
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class RememberedAccountsStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "bodian-remembered-" + Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "remembered-accounts.dat");

    private static readonly DateTimeOffset Origin = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    public RememberedAccountsStoreTests() => Directory.CreateDirectory(_dir);

    /// <summary>只删自己建的临时目录。**绝不碰 %LOCALAPPDATA%**。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private DpapiRememberedAccountsStore NewStore() => new(Path_);

    private static BodianCredential Credential(string uid, string token = "tok", string? nickname = null) =>
        new(uid, token, nickname ?? $"账号{uid}");

    [Fact]
    public void MissingFile_IsAnEmptyList()
    {
        Assert.Empty(NewStore().Load());
    }

    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var store = NewStore();

        store.Remember(
            new BodianCredential("50303440", "tok_secret", "小音波", "https://x.invalid/a.png", true,
                Origin.AddDays(30), VipBadgeKind.Big),
            Origin);

        var entry = Assert.Single(NewStore().Load());

        Assert.Equal("50303440", entry.Credential.Uid);
        Assert.Equal("tok_secret", entry.Credential.Token);
        Assert.Equal("小音波", entry.Credential.Nickname);
        Assert.Equal("https://x.invalid/a.png", entry.Credential.AvatarUrl);
        Assert.True(entry.Credential.IsVip);
        Assert.Equal(VipBadgeKind.Big, entry.Credential.VipBadge);
        Assert.Equal(Origin, entry.LastUsedAt);
    }

    [Fact]
    public void Load_OrdersByLastUsedDescending()
    {
        var store = NewStore();

        store.Remember(Credential("1"), Origin);
        store.Remember(Credential("2"), Origin.AddHours(1));
        store.Remember(Credential("3"), Origin.AddHours(2));

        Assert.Equal(["3", "2", "1"], NewStore().Load().Select(entry => entry.Credential.Uid));
    }

    [Fact]
    public void Remember_SameUidReplacesInsteadOfDuplicating()
    {
        var store = NewStore();

        store.Remember(Credential("1", token: "old"), Origin);
        store.Remember(Credential("1", token: "new"), Origin.AddHours(1));

        var entry = Assert.Single(NewStore().Load());

        Assert.Equal("new", entry.Credential.Token);
    }

    [Fact]
    public void Remember_BeyondTheCap_DropsTheLeastRecentlyUsed()
    {
        var store = NewStore();

        for (var i = 1; i <= DpapiRememberedAccountsStore.MaxAccounts; i++)
        {
            store.Remember(Credential(i.ToString()), Origin.AddMinutes(i));
        }

        // 第 6 个来了，最久未用的那个（1）该被淘汰。
        store.Remember(Credential("new"), Origin.AddHours(1));

        var uids = NewStore().Load().Select(entry => entry.Credential.Uid).ToList();

        Assert.Equal(DpapiRememberedAccountsStore.MaxAccounts, uids.Count);
        Assert.DoesNotContain("1", uids);
        Assert.Contains("new", uids);
    }

    [Fact]
    public void Remember_WithAnOldTimestamp_IsRankedByThatTimestamp_NotByInsertOrder()
    {
        var store = NewStore();

        for (var i = 1; i <= DpapiRememberedAccountsStore.MaxAccounts; i++)
        {
            store.Remember(Credential(i.ToString()), Origin.AddMinutes(i));
        }

        // 排序依据只有「上次使用时刻」一条，不为「刚插进来的」开特例。
        // 实际调用方（BodianLogin）永远传当前时刻，所以这条只是把规则钉死：
        // 传了个很早的时刻，它就该按时间排在后面。
        store.Remember(Credential("stale"), Origin.AddDays(-30));

        Assert.DoesNotContain("stale", NewStore().Load().Select(entry => entry.Credential.Uid));
    }

    [Fact]
    public void Forget_RemovesOnlyThatAccount()
    {
        var store = NewStore();

        store.Remember(Credential("1"), Origin);
        store.Remember(Credential("2"), Origin.AddMinutes(1));

        store.Forget("1");

        var entry = Assert.Single(NewStore().Load());
        Assert.Equal("2", entry.Credential.Uid);
    }

    [Fact]
    public void Forget_UnknownUid_IsANoOp()
    {
        var store = NewStore();
        store.Remember(Credential("1"), Origin);

        store.Forget("nope");

        Assert.Single(NewStore().Load());
    }

    [Fact]
    public void CorruptFile_IsAnEmptyList_AndTheFileIsKept()
    {
        File.WriteAllBytes(Path_, Encoding.UTF8.GetBytes("这不是 DPAPI 密文"));

        Assert.Empty(NewStore().Load());

        // 不替用户做删除决定：留着还能人工看一眼出了什么事。
        Assert.True(File.Exists(Path_));
    }

    [Fact]
    public void WrittenFile_IsNotPlaintext()
    {
        NewStore().Remember(Credential("50303440", token: "tok_secret_sentinel"), Origin);

        var raw = File.ReadAllBytes(Path_);

        // 里面是**可用的 token**，与 session.dat 同级敏感：磁盘上不能出现明文 uid / token。
        Assert.DoesNotContain("tok_secret_sentinel", Encoding.UTF8.GetString(raw));
        Assert.DoesNotContain("50303440", Encoding.UTF8.GetString(raw));
    }

    [Fact]
    public void Entropy_IsNotTheSessionOne_SoABlobCannotBeRepurposed()
    {
        // 把清单文件冒充成 session.dat 必须解不开。两份用的 entropy 不同，这里用
        // 「session 的 store 读不懂清单文件」来断言这件事（反过来同理，机制相同）。
        NewStore().Remember(Credential("50303440"), Origin);

        var asSession = new DpapiCredentialStore(Path_);

        Assert.Null(asSession.Load());
    }
}
