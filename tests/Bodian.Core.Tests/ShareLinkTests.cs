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

    [Fact]
    public void BuildAlbumLink_MatchesTheDocumentedTemplate()
    {
        var link = ShareLinks.BuildAlbumLink(1293, "50303440");

        Assert.Equal("https://h5app.kuwo.cn/m/bodian/album.html?uid=50303440&albumid=1293", link);
    }

    /// <summary>
    /// 参数名是 <c>albumid</c> <b>全小写</b>，与歌曲链接的 <c>musicId</c> 驼峰写法不同。
    /// </summary>
    /// <remarks>
    /// 官方模板就是这么拼的（文档 2.8 的链接模板表）。改成驼峰会得到一条打不开的链接，
    /// 而且没有任何一处会报错 —— 这条守着别「顺手统一」成 musicId 那种写法。
    /// </remarks>
    [Fact]
    public void BuildAlbumLink_UsesTheLowerCaseParamName()
    {
        var link = ShareLinks.BuildAlbumLink(1293, "1");

        Assert.Contains("albumid=1293", link, StringComparison.Ordinal);
        Assert.DoesNotContain("albumId", link, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAlbumLink_EscapesTheUid()
    {
        var link = ShareLinks.BuildAlbumLink(1, "a b&c=d");

        Assert.Equal("https://h5app.kuwo.cn/m/bodian/album.html?uid=a%20b%26c%3Dd&albumid=1", link);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void BuildAlbumLink_RejectsNonPositiveAlbumIds(long albumId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShareLinks.BuildAlbumLink(albumId, "1"));
    }

    [Fact]
    public void BuildArtistLink_MatchesTheDocumentedTemplate()
    {
        var link = ShareLinks.BuildArtistLink(336, "50303440");

        Assert.Equal("https://h5app.kuwo.cn/m/bodian/singer.html?uid=50303440&singerId=336", link);
    }

    /// <summary>
    /// 歌手链接的参数名是驼峰 <c>singerId</c>，而专辑是<b>全小写</b> <c>albumid</c>。
    /// </summary>
    /// <remarks>
    /// 两条都逐字照抄官方模板，所以这个不一致是<b>真的</b>、不该被"顺手统一"。
    /// 写错只会得到一条打不开的链接，没有任何一处会报错 —— 靠这条守住。
    /// </remarks>
    [Fact]
    public void BuildArtistLink_UsesTheCamelCaseParamName()
    {
        var link = ShareLinks.BuildArtistLink(336, "1");

        Assert.Contains("singerId=336", link, StringComparison.Ordinal);
        Assert.DoesNotContain("singerid", link, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArtistLink_EscapesTheUid()
    {
        var link = ShareLinks.BuildArtistLink(1, "a b&c=d");

        Assert.Equal("https://h5app.kuwo.cn/m/bodian/singer.html?uid=a%20b%26c%3Dd&singerId=1", link);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void BuildArtistLink_RejectsNonPositiveArtistIds(long artistId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShareLinks.BuildArtistLink(artistId, "1"));
    }
}
