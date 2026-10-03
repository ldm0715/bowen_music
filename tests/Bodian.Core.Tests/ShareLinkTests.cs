using Bodian.Core.Services;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 分享链接是本地拼的，没有接口参与（文档 2.8）。
/// </summary>
/// <remarks>
/// <b>这些断言只能证明「拼法符合文档记载的模板」，不能证明链接能打开。</b>
/// 尤其 <c>opusId</c> 留空这一处，官方客户端里它取自分享视图上下文，本项目没有这个字段。
/// </remarks>
public sealed class ShareLinkTests
{
    [Fact]
    public void BuildTrackLink_MatchesTheDocumentedTemplate()
    {
        var link = ShareLinks.BuildTrackLink(228908, "50303440");

        Assert.Equal("https://h5app.kuwo.cn/m/bodian/playMusic.html?uid=50303440&musicId=228908&opusId=", link);
    }

    /// <summary>uid 是外部输入，必须转义；域名与路径是常量，保持原样。</summary>
    [Fact]
    public void BuildTrackLink_EscapesTheUid()
    {
        var link = ShareLinks.BuildTrackLink(1, "a b&c=d");

        Assert.Equal("https://h5app.kuwo.cn/m/bodian/playMusic.html?uid=a%20b%26c%3Dd&musicId=1&opusId=", link);
    }

    /// <summary>未登录也允许分享，uid 就是匿名的 -1。</summary>
    [Fact]
    public void BuildTrackLink_AllowsTheAnonymousUid()
    {
        var link = ShareLinks.BuildTrackLink(228908, "-1");

        Assert.Contains("uid=-1&", link, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void BuildTrackLink_RejectsNonPositiveMusicIds(long musicId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShareLinks.BuildTrackLink(musicId, "1"));
    }
}
