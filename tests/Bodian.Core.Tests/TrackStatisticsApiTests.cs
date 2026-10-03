using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class TrackStatisticsApiTests
{
    [Fact]
    public async Task Details_MapRealFavoriteShareAndCommentCounters()
    {
        using var handler = new ReplayHandler { Responder = _ => ReplayHandler.Json(Fixtures.Read("music-info-228908.json")) };
        using var transport = Transport(handler);
        var track = Assert.IsType<Track>(await Api(transport).GetTrackAsync(228908, TestContext.Current.CancellationToken));
        Assert.Equal(3837270, track.FavoriteCount);
        Assert.Equal(42427, track.ShareCount);
        Assert.Equal(30149, track.CommentCount);
        Assert.Single(handler.Requests);
        Assert.Equal("GET", handler.LastRequest.Method);
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData(",\"favorite\":0,\"share\":0", 0L, 0L)]
    [InlineData(",\"favorite\":-10,\"share\":-20", 0L, 0L)]
    [InlineData(",\"favorite\":\"12345\",\"share\":\"42\"", 12345L, 42L)]
    public async Task MissingZeroNegativeAndStringCounters_HaveDistinctMeaning(string fields, long? favorite, long? share)
    {
        using var handler = new ReplayHandler
        {
            Responder = _ => ReplayHandler.Json("{\"code\":200,\"data\":{\"id\":123,\"name\":\"test\"" + fields + "}}"),
        };
        using var transport = Transport(handler);
        var track = Assert.IsType<Track>(await Api(transport).GetTrackAsync(123, TestContext.Current.CancellationToken));
        Assert.Equal(favorite, track.FavoriteCount);
        Assert.Equal(share, track.ShareCount);
    }

    private static BodianHttpTransport Transport(ReplayHandler handler) => new(handler, new BodianTransportOptions(),
        BodianSession.CreateAnonymous(), new FakeDeviceIdentity(), FixedTimeProvider.Golden);
    private static BodianApi Api(BodianHttpTransport transport) => new(transport, BodianSession.CreateAnonymous(), new FakeDeviceIdentity());
}
