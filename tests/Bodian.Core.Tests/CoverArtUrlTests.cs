using System.Text.Json;
using Bodian.Core.Media;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 封面地址改写。断言用的是 fixture 里的真实地址，不是手编的样例。
/// </summary>
/// <remarks>
/// 这条改写建立在实测上：同一个 <c>/star/albumcover/&lt;尺寸&gt;/</c> 路径，
/// 把 <c>.webp</c> 换成 <c>.jpg</c> 返回的是 200 <c>image/jpeg</c>（对 4 张专辑、两种路径前缀都成立）。
/// </remarks>
public sealed class CoverArtUrlTests
{
    private static string RealCover(string field)
    {
        using var document = JsonDocument.Parse(Fixtures.Read("music-info-228908.json"));

        return document.RootElement.GetProperty("data").GetProperty(field).GetString()!;
    }

    /// <summary>曲目详情给的是 webp，要变成 jpg 且尺寸提到 500。</summary>
    [Fact]
    public void Jpeg_RewritesTheRealWebpCover()
    {
        var original = RealCover("albumPic120");

        Assert.EndsWith(".webp", original, StringComparison.Ordinal);

        var rewritten = CoverArtUrl.Jpeg(new Uri(original));

        Assert.NotNull(rewritten);
        Assert.EndsWith(".jpg", rewritten.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("/star/albumcover/500/", rewritten.AbsolutePath, StringComparison.Ordinal);

        // 专辑 id 那一段不能被改动 —— 改错了就是另一张专辑的图。
        Assert.EndsWith("/211513640.jpg", rewritten.AbsolutePath, StringComparison.Ordinal);
    }

    /// <summary>
    /// 已经是 jpg 的（搜索列表那种 700px）只改尺寸，不动后缀。
    /// </summary>
    /// <remarks>
    /// 拿搜索列表的样本：同一首歌在详情里是 <c>albumPic120.webp</c>、<c>albumPic.webp</c>，
    /// 在搜索列表里是 <c>albumPic.jpg</c> —— 两条路径的格式本来就不一致，这里两条都要覆盖。
    /// </remarks>
    [Fact]
    public void Jpeg_OnAnAlreadyJpegCover_OnlyChangesTheSize()
    {
        using var document = JsonDocument.Parse(Fixtures.Read("search-music-list-anon.json"));

        var original = document.RootElement
            .GetProperty("data").GetProperty("resultList")[0].GetProperty("albumPic").GetString()!;

        Assert.EndsWith(".jpg", original, StringComparison.Ordinal);

        var rewritten = CoverArtUrl.Jpeg(new Uri(original));

        Assert.NotNull(rewritten);
        Assert.Contains("/star/albumcover/500/", rewritten.AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith("/211513640.jpg", rewritten.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Jpeg_KeepsTheQueryString()
    {
        var rewritten = CoverArtUrl.Jpeg(new Uri("https://img4.kuwo.cn/star/albumcover/240/s3s94/93/1.webp?v=2"));

        Assert.Equal("?v=2", rewritten!.Query);
    }

    [Theory]
    [InlineData("https://img4.kuwo.cn/star/starheads/0/s4s0/88/2227585000.jpg")]
    [InlineData("https://img3.kuwo.cn/star/starheads/700/s4s0/88/2227585000.jpg")]
    public void Jpeg_RequestsAThumbnailForYoasobi(string url)
    {
        var rewritten = CoverArtUrl.Jpeg(new Uri(url), 256);

        Assert.Equal("/star/starheads/256/s4s0/88/2227585000.jpg", rewritten!.AbsolutePath);
        Assert.Equal(new Uri(url).Host, rewritten.Host);
    }

    [Fact]
    public void Jpeg_ArtistThumbnailKeepsTheQueryString()
    {
        var rewritten = CoverArtUrl.Jpeg(new Uri("https://img4.kuwo.cn/star/starheads/0/s4s0/88/2227585000.jpg?v=2"), 256);

        Assert.Equal("?v=2", rewritten!.Query);
    }

    [Fact]
    public void Jpeg_LeavesTheWorkingKuwoPlaylistCoverAlone()
    {
        var cover = new Uri("http://img1.kwcdn.kuwo.cn/star/userpl2015/81/23/1568684821020_182253281_o.jpg");

        Assert.Same(cover, CoverArtUrl.Jpeg(cover, 256));
        Assert.Null(CoverArtUrl.Fallback(cover));
    }

    [Fact]
    public void Fallback_RepairsTheTencentPlaylistCoverAndKeepsItsQuery()
    {
        var cover = new Uri("http://y.gtimg.cn/music/photo/album_500/71/500_albumpic_60671_o.jpg?n=1");

        // 原地址先正常尝试，只有图片加载失败才使用备用地址。
        Assert.Same(cover, CoverArtUrl.Jpeg(cover, 256));
        var fallback = CoverArtUrl.Fallback(cover);

        Assert.Equal("http://y.gtimg.cn/music/photo/album_500/71/500_albumpic_60671_0.jpg?n=1", fallback!.AbsoluteUri);
        Assert.Null(CoverArtUrl.Fallback(fallback));
    }

    [Theory]
    [InlineData("https://img4.kuwo.cn/star/userpl2015/81/23/1568684821020_182253281_o.jpg")]
    [InlineData("https://example.com/music/photo/album_500/71/500_albumpic_60671_o.jpg")]
    [InlineData("https://y.gtimg.cn.example.com/music/photo/album_500/71/500_albumpic_60671_o.jpg")]
    [InlineData("https://y.gtimg.cn/other/500_albumpic_60671_o.jpg")]
    [InlineData("https://y.gtimg.cn/music/photo/album_500/71/custom_o.jpg")]
    [InlineData("https://y.gtimg.cn/music/photo/album_500/71/500_albumpic_60671_0.jpg?n=1")]
    public void Fallback_DoesNotGuessForOtherCovers(string url)
    {
        Assert.Null(CoverArtUrl.Fallback(new Uri(url)));
    }

    [Fact]
    public void Fallback_OnNull_IsNull()
    {
        Assert.Null(CoverArtUrl.Fallback(null));
    }

    /// <summary>不认识的地址一律原样返回 —— 改写只对实测过的形状做。</summary>
    [Theory]
    [InlineData("https://img4.kuwo.cn/star/userhead/240/s3s94/93/1.webp")]   // 不是 albumcover
    [InlineData("https://img4.kuwo.cn/star/albumcover/abc/1.webp")]           // 尺寸段不是数字
    [InlineData("https://example.com/star/albumcover/240/1.webp")]            // 不是酷我域名
    [InlineData("https://img4.kuwo.cn/star/albumcover/240")]                  // 缺尾部路径
    public void Jpeg_LeavesUnknownShapesAlone(string url)
    {
        var rewritten = CoverArtUrl.Jpeg(new Uri(url));

        Assert.Equal(url, rewritten!.OriginalString);
    }

    [Fact]
    public void Jpeg_OnNull_IsNull()
    {
        Assert.Null(CoverArtUrl.Jpeg(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Jpeg_RejectsNonPositiveSize(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CoverArtUrl.Jpeg(new Uri("https://img4.kuwo.cn/star/albumcover/240/1.webp"), size));
    }
}
