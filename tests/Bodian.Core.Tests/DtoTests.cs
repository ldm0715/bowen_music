using System.Text.Json;
using Bodian.Core.Api;
using Bodian.Core.Api.Dto;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// DTO 反序列化测试。输入全部是 <c>fixtures/</c> 下的真实响应，不是手写的样本。
/// </summary>
public sealed class DtoTests
{
    // ── payInfo 的类型不一致（本层最大的陷阱） ──────────────────────────────

    /// <summary>
    /// **这条是本层最重要的测试。**
    /// </summary>
    /// <remarks>
    /// <c>payInfo.refrain_start</c> / <c>refrain_end</c> / <c>limitfree</c> 在两个接口里的
    /// JSON 类型不同：
    /// <list type="bullet">
    /// <item><c>service/music/info</c>：**字符串** <c>"84346"</c> / <c>"0"</c></item>
    /// <item><c>search/music/list</c>：**数字** <c>84346</c> / <c>0</c></item>
    /// </list>
    /// 这个不一致只在运行时炸，而且**只在跑到第二个接口时才暴露**——先跑哪个都能过。
    /// 靠 <c>BodianJsonContext</c> 的 <c>NumberHandling.AllowReadingFromString</c> 解决。
    /// <para>两个 fixture 必须都解析成功且值相同，否则 P2 一定翻车。</para>
    /// </remarks>
    [Fact]
    public void PayInfo_RefrainStart_IsStringInMusicInfo_AndNumberInSearch()
    {
        var info = Fixtures.Load("music-info-228908.json", BodianJsonContext.Default.TrackDto);
        var infoPay = Assert.IsType<PayInfoDto>(info.PayInfo);
        Assert.Equal(84346, infoPay.RefrainStartMs);
        Assert.Equal(142843, infoPay.RefrainEndMs);
        Assert.Equal(0, infoPay.LimitFree);

        var search = Fixtures.Load("search-music-list-anon.json", BodianJsonContext.Default.SearchListPayload);
        var searchPay = Assert.IsType<PayInfoDto>(search.ResultList![0].PayInfo);
        Assert.Equal(infoPay.RefrainStartMs, searchPay.RefrainStartMs);
        Assert.Equal(infoPay.RefrainEndMs, searchPay.RefrainEndMs);
        Assert.Equal(infoPay.LimitFree, searchPay.LimitFree);
    }

    /// <summary>原始 JSON 里那三格的类型确实不同——防止有人把 fixture 改「整齐」了让上一条失去意义。</summary>
    [Fact]
    public void PayInfo_RawJsonTypes_AreActuallyDifferent()
    {
        var infoRaw = JsonDocument.Parse(Fixtures.Read("music-info-228908.json"))
            .RootElement.GetProperty("data").GetProperty("payInfo");
        var searchRaw = JsonDocument.Parse(Fixtures.Read("search-music-list-anon.json"))
            .RootElement.GetProperty("data").GetProperty("resultList")[0].GetProperty("payInfo");

        foreach (var name in new[] { "refrain_start", "refrain_end", "limitfree" })
        {
            Assert.Equal(JsonValueKind.String, infoRaw.GetProperty(name).ValueKind);
            Assert.Equal(JsonValueKind.Number, searchRaw.GetProperty(name).ValueKind);
        }
    }

    // ── 曲目 ────────────────────────────────────────────────────────────────

    [Fact]
    public void Track_MusicInfo_ParsesCoreFields()
    {
        var t = Fixtures.Load("music-info-228908.json", BodianJsonContext.Default.TrackDto);

        Assert.Equal(228908, t.Id);
        Assert.Equal("晴天", t.Name);
        Assert.Equal(269, t.DurationSeconds);
        Assert.Equal("周杰伦", t.Artist);
        Assert.Equal("叶惠美", t.Album);

        var artist = Assert.Single(t.Artists!);
        Assert.Equal("周杰伦", artist.Name);
        Assert.NotEmpty(artist.Pic!);
    }

    /// <summary><c>duration</c> 与 <c>payInfo.refrain_*</c> 在同一响应里单位不同：前者秒、后者毫秒。</summary>
    [Fact]
    public void Track_DurationIsSeconds_ButRefrainIsMilliseconds()
    {
        var t = Fixtures.Load("music-info-228908.json", BodianJsonContext.Default.TrackDto);

        Assert.Equal(269, t.DurationSeconds);
        // 84346 毫秒 = 84.3 秒。若误按秒处理，试听片段会落到曲末
        Assert.Equal(84346, t.PayInfo!.RefrainStartMs);
        Assert.True(t.PayInfo.RefrainStartMs < t.DurationSeconds * 1000);
    }

    /// <summary><c>audios[]</c> 的 <c>bitrate</c> / <c>size</c> 是字符串，且顺序不按档位排。</summary>
    [Fact]
    public void AudioEntries_AreStrings_AndNotOrderedByLevel()
    {
        var t = Fixtures.Load("music-info-228908.json", BodianJsonContext.Default.TrackDto);
        var audios = Assert.IsType<AudioEntryDto[]>(t.Audios);

        Assert.Equal(13, audios.Length);
        Assert.Equal("bcms", audios[0].Level);     // 首元素不是最高档
        Assert.Equal("ff", audios[5].Level);

        var lossless = audios.Single(a => a.Level == "ff");
        Assert.Equal("2000", lossless.Bitrate);    // 字符串，不是数字
        Assert.Equal("52.83Mb", lossless.Size);    // 带单位
        Assert.Contains(audios, a => a.Size == "zpMb");   // zp 档是占位串
    }

    /// <summary>搜索列表独有的字段，与 music/info 的字段集合不同。</summary>
    [Fact]
    public void Track_SearchList_HasItsOwnFields()
    {
        var p = Fixtures.Load("search-music-list-anon.json", BodianJsonContext.Default.SearchListPayload);

        Assert.Equal(5, p.Total);
        Assert.Equal(5, p.ResultList!.Length);
        Assert.False(p.Duplicate);

        var first = p.ResultList[0];
        Assert.NotNull(first.Subtitle);
        Assert.NotNull(first.FSongName);
    }

    /// <summary><c>searchTag</c> 的 <c>top</c> / <c>bottom</c> 都是可选的（5 条里只有 2 条带 bottom）。</summary>
    [Fact]
    public void SearchTag_TopAndBottomAreOptional()
    {
        var p = Fixtures.Load("search-music-list-anon.json", BodianJsonContext.Default.SearchListPayload);

        Assert.All(p.ResultList!, t => Assert.NotNull(t.SearchTag));
        Assert.All(p.ResultList!, t => Assert.NotEmpty(t.SearchTag!.Mid!));

        Assert.Equal(2, p.ResultList!.Count(t => t.SearchTag!.Bottom is not null));
        Assert.All(p.ResultList!, t =>
        {
            Assert.NotNull(t.SearchTag!.Top);
            Assert.NotNull(t.SearchTag.Top!.Name);
        });
    }

    /// <summary>music/info 独有的歌词轨清单——P4 靠它决定请求哪一版歌词。</summary>
    [Fact]
    public void Track_MusicInfo_CarriesLyricTrackInfo()
    {
        var t = Fixtures.Load("music-info-228908.json", BodianJsonContext.Default.TrackDto);

        Assert.Equal(1, t.LrcInfo!.Lrc);
        Assert.Equal(1, t.LrcInfo.Lrcx);          // 有逐字轨，所以该请求 lrcx=1
        Assert.Equal("scaleIn", t.LrcEffect!.Name);
        Assert.NotEmpty(t.Categories!);
    }

    /// <summary><c>favorite</c> 是全站收藏数，不是「当前用户是否已收藏」。</summary>
    [Fact]
    public void Track_FavoriteIsASiteWideCounter()
    {
        var t = Fixtures.Load("music-info-228908.json", BodianJsonContext.Default.TrackDto);

        Assert.True(t.Favorite > 1_000_000);
        Assert.True(t.Comment > 1_000);
    }

    // ── 播放 ────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckRight_TrialCarriesAudition()
    {
        var c = Fixtures.Load("checkright-228908.json", BodianJsonContext.Default.CheckRightDto);

        Assert.Equal(3, c.Status);                       // 3 = 只能试听
        var a = Assert.IsType<AuditionDto>(c.Audition);
        Assert.Equal(0, a.StartSeconds);                 // 秒
        Assert.Equal(29, a.EndSeconds);
        Assert.Equal(269, a.DurationSeconds);
        Assert.Equal("mp3", a.Format);
    }

    /// <summary>
    /// 请求 <c>format=flac</c> 被服务端静默降级成 mp3——**必须校验返回值，不能相信请求参数**。
    /// 这两份 fixture 就是这条回归测试的素材。
    /// </summary>
    [Fact]
    public void AudioUrl_ReportedFormatMustBeChecked_NotTheRequestedOne()
    {
        var lossless = Fixtures.Load("audiourl-lossless-flac.json", BodianJsonContext.Default.AudioUrlDto);
        var downgraded = Fixtures.Load("audiourl-formatflac-downgraded.json", BodianJsonContext.Default.AudioUrlDto);

        Assert.Equal("flac", lossless.Format);
        Assert.Equal(2000, lossless.Bitrate);            // 这里的 bitrate 确实是数字
        Assert.Equal("52.83Mb", lossless.Size);
        Assert.Equal(269, lossless.DurationSeconds);

        // 同一个请求形态，服务端只给了 mp3——只看请求参数会以为拿到了无损
        Assert.Equal("mp3", downgraded.Format);
        Assert.Equal(320, downgraded.Bitrate);
        Assert.NotEqual(lossless.Format, downgraded.Format);
    }

    // ── 登录 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// fixture 已脱敏（<c>id</c> / <c>bid</c> / <c>token</c> 都是 <c>"&lt;redacted&gt;"</c>），
    /// 所以这里只断言**未被脱敏**的部分。身份一致性规则是纯函数，另测。
    /// </summary>
    [Fact]
    public void Login_ParsesRedactedFixture()
    {
        var login = Fixtures.Load("login-users-login.json", BodianJsonContext.Default.LoginResultDto);

        Assert.Equal("<redacted>", login.Token);

        // 脱敏串读不出数值：返回 null，而不是抛 JsonException 让整份 fixture 读不进来
        Assert.Null(login.Id);
        Assert.Null(login.Bid);
        Assert.Null(login.UserInfo!.Id);

        Assert.Equal(3, login.UserInfo.AuthType);
        Assert.Equal(1, login.PayInfo!.IsVip);
        Assert.Equal(2, login.PayInfo.VipType);
        Assert.True(login.PayInfo.IsVipBoolean);
        Assert.Equal(0, login.UserFreeInfo!.FreeAdIsFree);
        Assert.True(login.PayInfo.ActExpireDate > 0);
    }

    /// <summary>
    /// 账号级 payInfo 与曲目级 payInfo **同名不同构**——两个类型必须各自解析自己的那份。
    /// </summary>
    [Fact]
    public void PayInfo_TwoDifferentShapes_DoNotCollide()
    {
        var track = Fixtures.Load("music-info-228908.json", BodianJsonContext.Default.TrackDto);
        var login = Fixtures.Load("login-users-login.json", BodianJsonContext.Default.LoginResultDto);

        var trackPay = Assert.IsType<PayInfoDto>(track.PayInfo);
        var accountPay = Assert.IsType<AccountPayInfoDto>(login.PayInfo);

        // 曲目级有播放/下载权限位
        Assert.NotNull(trackPay.Download);
        Assert.NotNull(trackPay.PayTagIndex);

        // 账号级有会员到期时间，两者没有共同字段
        Assert.True(accountPay.ActExpireDate > 0);
    }

    /// <summary>真实 musicId 会超出 int32（实测存在 10250281307392909），所以 id 类字段必须是 long。</summary>
    [Fact]
    public void TrackId_IsWideEnoughForRealIds()
    {
        const long realId = 10250281307392909;

        Assert.True(realId > int.MaxValue, "这个 id 本来就超出 int32，测试前提不成立");
        Assert.Equal(typeof(long), typeof(TrackDto).GetProperty(nameof(TrackDto.Id))!.PropertyType);

        // 该 id 对应的响应是 20012「歌曲已下线」
        var offline = JsonDocument.Parse(Fixtures.Read("music-info-10250281307392909.json")).RootElement;
        Assert.Equal(20012, offline.GetProperty("code").GetInt32());
        Assert.False(offline.TryGetProperty("data", out _));
    }
}
