using Bodian.Core.Api;
using Bodian.Core.Models.Account;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 账号统计的**真实响应**对照。
/// </summary>
/// <remarks>
/// <para>
/// 两条接口的字段名最初只有反编译证据，所以这里读探针落盘的 fixture。fixture 不存在时**跳过**——
/// 这两份已经落盘（2026-10-04），跳过分支是留给新克隆的：没跑过探针的机器不会有这两份文件。
/// </para>
/// <para>
/// <b>它们存在的意义是把结论钉在实测数据上</b>：字段名一旦对不上，这里会先红，
/// 而不是等界面上显示出一个「—」才被发现。
/// </para>
/// </remarks>
public sealed class UserStatsFixtureTests
{
    [Fact]
    public void MetadataFixture_ParsesFollowCount()
    {
        const string file = "users-metadata.json";

        if (!File.Exists(Fixtures.PathOf(file)))
        {
            Assert.Skip($"没有 {file}（还没跑过探针的 metadata 命令），跳过。");
        }

        var dto = Fixtures.Load(file, BodianJsonContext.Default.UserMetadataDto);

        Assert.NotNull(dto.FollowCount);
    }

    [Fact]
    public void PlayDataFixture_ParsesPlayTime()
    {
        const string file = "playdata-user-data.json";

        if (!File.Exists(Fixtures.PathOf(file)))
        {
            Assert.Skip($"没有 {file}（还没跑过探针的 playdata 命令），跳过。");
        }

        var dto = Fixtures.Load(file, BodianJsonContext.Default.UserPlayDataDto);

        Assert.NotNull(dto.PlaySeconds);
    }

    /// <summary>
    /// <c>ucenter/users/pub/{uid}</c> 的真实响应 —— 本机账号，官方客户端显示为**大会员**。
    /// </summary>
    /// <remarks>
    /// 这条把「档位名 → 判据」的实测锚点钉在真实数据上：映射被推翻时它会先红。
    /// </remarks>
    [Fact]
    public void UserPubFixture_ResolvesToBig()
    {
        const string file = "user-pub.json";

        if (!File.Exists(Fixtures.PathOf(file)))
        {
            Assert.Skip($"没有 {file}（还没跑过探针的 users/pub 命令），跳过。");
        }

        var dto = Fixtures.Load(file, BodianJsonContext.Default.UserPubDto);

        Assert.Equal(VipBadgeKind.Big, VipStatus.ResolveBadge(dto.PayInfo));
        Assert.NotNull(VipStatus.Resolve(dto.PayInfo).ExpiresAt);
    }
}
