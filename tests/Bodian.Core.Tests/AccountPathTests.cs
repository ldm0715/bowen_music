using Bodian.Core.Api;
using Bodian.Core.Services;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 账号作用域的路径拼装与 uid 校验。零真实用户目录 —— 用注入的根目录。
/// </summary>
public sealed class AccountPathTests
{
    private const string Root = @"C:\tmp\bodian-account-paths";

    [Fact]
    public void AccountFilePath_IsUnderTheAccountsDirectory()
    {
        var path = AppPaths.AccountFilePath("12345", "history.json", Root);

        Assert.Equal(Path.Combine(Root, "accounts", "12345", "history.json"), path);
    }

    [Fact]
    public void AnonymousScope_GetsItsOwnBucket()
    {
        var path = AppPaths.AccountFilePath(AppPaths.AnonymousScope, "queue.json", Root);

        Assert.Equal(Path.Combine(Root, "accounts", "anonymous", "queue.json"), path);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void UnsafeScope_FallsBackToAnonymous(string scope)
    {
        // uid 来自服务端。真让它带着分隔符或 .. 进来，写入就跑出数据目录了 ——
        // 回落方向是「写进匿名桶」，比「写到数据目录外面」安全。
        var path = AppPaths.AccountDirectory(scope, Root);

        Assert.Equal(Path.Combine(Root, "accounts", AppPaths.AnonymousScope), path);
    }

    [Fact]
    public void AccountsDirectory_SitsUnderLocalAppData()
    {
        Assert.Equal(Path.Combine(AppPaths.LocalAppData, "accounts"), AppPaths.AccountsDirectory);
    }

    [Fact]
    public void DeviceId_IsNotUnderTheAccountsDirectory()
    {
        // 这是整个设计里最要紧的一条：设备标识按账号分会变成 N 个设备标识，是账号风控的异常信号。
        Assert.DoesNotContain(AppPaths.AccountsDirectoryName, AppPaths.DeviceIdFile);
        Assert.StartsWith(AppPaths.LocalAppData, AppPaths.DeviceIdFile);
    }

    [Fact]
    public void AnonymousUid_MapsToAnonymousScope()
    {
        var session = BodianSession.CreateAnonymous();

        Assert.Equal(BodianSession.AnonymousUid, session.Uid);
        Assert.Equal(AppPaths.AnonymousScope, session.Scope);
    }
}
