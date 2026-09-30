using System.Text.Json;
using Bodian.Core.Api;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 签名与 WHATWG 编码测试。
/// </summary>
public sealed class SignerTests
{
    // ── WHATWG application/x-www-form-urlencoded ───────────────────────────

    /// <summary>
    /// 逐字节对齐 Dart 的 <c>URLSearchParams</c>。**不能换成 <c>Uri.EscapeDataString</c>。**
    /// </summary>
    /// <remarks>
    /// 签名只取 query 里的字母数字，但**编码方式决定了哪些字符是字母数字**——
    /// 百分号与十六进制位会改变 seed 的字符集。所以编码表错一位，签名就整体错。
    /// </remarks>
    [Theory]
    // 空格转 +（不是 %20）
    [InlineData(" ", "+")]
    // ~ 会被编码——这是与 Uri.EscapeDataString 的分水岭
    [InlineData("~", "%7E")]
    // 保留集：字母数字与 * - . _
    [InlineData("*-._", "*-._")]
    [InlineData("abcXYZ019", "abcXYZ019")]
    // 结构字符必须被编码，否则 query 串会被提前截断
    [InlineData("=", "%3D")]
    [InlineData("&", "%26")]
    [InlineData("?", "%3F")]
    [InlineData("#", "%23")]
    [InlineData("+", "%2B")]
    // 非保留符号转大写十六进制
    [InlineData("!", "%21")]
    [InlineData("/", "%2F")]
    [InlineData(":", "%3A")]
    [InlineData("'", "%27")]
    // UTF-8 多字节
    [InlineData("周杰伦", "%E5%91%A8%E6%9D%B0%E4%BC%A6")]
    [InlineData("晴天", "%E6%99%B4%E5%A4%A9")]
    // 空串
    [InlineData("", "")]
    public void FormUrlEncode_MatchesWhatwgRules(string input, string expected)
    {
        var encoded = BodianSigner.FormUrlEncode([new KeyValuePair<string, string>("k", input)]);
        Assert.Equal($"k={expected}", encoded);
    }

    /// <summary>
    /// 与 <see cref="Uri.EscapeDataString"/> 的差异要显式守住——换实现是最容易犯的错。
    /// </summary>
    /// <remarks>
    /// 分水岭只有三处：<c>~</c>（RFC 3986 非保留字，.NET 不转义，URLSearchParams 转义）、
    /// 空格（<c>%20</c> vs <c>+</c>）、以及 <c>*</c>（.NET 转义，URLSearchParams 保留）。
    /// 其余非保留符号（如 <c>!</c>、<c>'</c>、<c>(</c>）两边**行为相同**，不构成差异点。
    /// </remarks>
    [Theory]
    [InlineData("~")]
    [InlineData(" ")]
    [InlineData("*")]
    public void FormUrlEncode_DiffersFromUriEscapeDataString(string input)
    {
        var ours = BodianSigner.FormUrlEncode([new KeyValuePair<string, string>("k", input)]);
        var theirs = "k=" + Uri.EscapeDataString(input);
        Assert.NotEqual(theirs, ours);
    }

    // ── 签名的真不变量 ─────────────────────────────────────────────────────

    /// <summary>
    /// 只有字母数字进 seed，所以**参数顺序不影响签名**。这是个真不变量，不是巧合。
    /// </summary>
    [Fact]
    public void Sign_IsIndependentOfQueryOrder()
    {
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("musicId", "228908"),
            new("uid", "-1"),
            new("token", ""),
            new("timestamp", "1790750540920"),
            new("sign", ""),
        };

        var reference = BodianSigner.Sign("play/music/v2/checkRight", pairs, null);

        var reversed = pairs.AsEnumerable().Reverse().ToList();
        Assert.Equal(reference, BodianSigner.Sign("play/music/v2/checkRight", reversed, null));

        var shuffled = new List<KeyValuePair<string, string>> { pairs[2], pairs[4], pairs[0], pairs[3], pairs[1] };
        Assert.Equal(reference, BodianSigner.Sign("play/music/v2/checkRight", shuffled, null));
    }

    [Fact]
    public void Sign_DiffersWhenPathDiffers()
    {
        var pairs = new List<KeyValuePair<string, string>> { new("musicId", "228908") };

        Assert.NotEqual(
            BodianSigner.Sign("play/music/v2/checkRight", pairs, null),
            BodianSigner.Sign("play/music/v2/audioUrl", pairs, null));
    }

    [Fact]
    public void Sign_DiffersWhenBodyDiffers()
    {
        var pairs = new List<KeyValuePair<string, string>> { new("musicId", "228908") };

        Assert.NotEqual(
            BodianSigner.Sign("play/music/v2/checkRight", pairs, """{"musicId":228908,"freeSign":""}"""),
            BodianSigner.Sign("play/music/v2/checkRight", pairs, """{"musicId":228909,"freeSign":""}"""));
    }

    [Fact]
    public void Md5Hex_IsLowercase()
    {
        var hex = BodianSigner.Md5Hex("abc");

        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", hex);
        Assert.Equal(hex.ToLowerInvariant(), hex);
    }

    // ── 黄金用例（characterization，不是已验证事实） ───────────────────────

    /// <summary>
    /// **锁住当前签名实现的行为。**
    /// </summary>
    /// <remarks>
    /// <b>这是 characterization test，不是「已验证正确」的测试。</b>
    /// <c>fixtures/sign-golden.json</c> 的 <c>verified</c> 是 <c>false</c>：
    /// 校验由 <c>ver</c> 请求头控制，本项目钉死的 <c>1.1.7</c> 服务端**根本不校验签名**，
    /// 所以这个期望值只是「当前实现算出来的东西」，没有被服务端证实过。
    /// <para>
    /// 它的价值在于：编码表、盐的位置、body 的 md5 内外层、path 拼接位置这几处一旦被改动，
    /// 这条测试会立刻红——而不是等到 <c>ver</c> 越过门槛时全部请求一起挂掉。
    /// </para>
    /// </remarks>
    [Fact]
    public void Sign_GoldenCheckRight_Characterization()
    {
        var golden = JsonDocument.Parse(Fixtures.Read("sign-golden.json")).RootElement;

        var path = golden.GetProperty("path").GetString()!;
        var seedQuery = golden.GetProperty("seedQuery").GetString()!;
        var body = golden.GetProperty("body").GetString()!;
        var expectedSign = golden.GetProperty("sign").GetString()!;

        // 1) 编码结果与 P0 探针发出去的 query 串逐字符相同
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("musicId", "228908"),
            new("uid", "-1"),
            new("token", ""),
            new("timestamp", "1790750540920"),
            new("sign", ""),
        };
        Assert.Equal(seedQuery, BodianSigner.FormUrlEncode(pairs));

        // 2) 签名值与 P0 探针记录的一致
        Assert.Equal(expectedSign, BodianSigner.SignRaw(path, seedQuery, body));
    }

    /// <summary>
    /// **守着 <c>verified: false</c> 这个事实。**
    /// </summary>
    /// <remarks>
    /// 如果哪天有人把这份 fixture 的 <c>verified</c> 改成 <c>true</c>，他必须同时拿出
    /// 「在 <c>ver ≥ 3.5</c> 的强制校验下请求成功」的证据。在此之前那只能是 characterization。
    /// 没有这条测试，黄金用例很容易被后来者当成「协议已被证实」而放松警惕。
    /// </remarks>
    [Fact]
    public void GoldenFixture_IsStillMarkedUnverified()
    {
        var golden = JsonDocument.Parse(Fixtures.Read("sign-golden.json")).RootElement;

        Assert.False(golden.GetProperty("verified").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(golden.GetProperty("reason").GetString()));
    }
}
