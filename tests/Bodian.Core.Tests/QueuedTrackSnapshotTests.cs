using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 队列条目快照的往返。
/// </summary>
/// <remarks>
/// <b>音源明细（<see cref="AudioVariant"/>）以前从没经过 JSON 往返</b> —— 播放历史那份快照只测过
/// 整体，而队列要拿它去重新取音源，字段掉了就是「恢复了却播不了」。这里逐字段盯住。
/// </remarks>
public sealed class QueuedTrackSnapshotTests
{
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "bodian-queued-track-tests",
        Guid.NewGuid().ToString("N"), "queue.json");

    private static Track Track() => new()
    {
        Id = 10250281307392909,          // 超出 int32，用来盯住 long 没被截断
        Title = "晴天",
        ArtistText = "周杰伦",
        AlbumName = "叶惠美",
        AlbumId = 12345,
        CoverImage = new Uri("https://cover.example.invalid/a.jpg"),
        Duration = TimeSpan.FromSeconds(269),
        AvailableQualities = [AudioQuality.Standard, AudioQuality.Lossless],
        AudioVariants =
        [
            new AudioVariant(AudioQuality.Lossless, "ff", "flac", 1411, 47_000_000),
            new AudioVariant(AudioQuality.Standard, "s", "mp3", 128, 4_300_000),
        ],
        RequiresVip = true,
        RequiresPurchase = false,
        HasMv = true,
    };

    private static QueuedTrack RoundTrip(QueuedTrack item, string path)
    {
        var store = new JsonPlayQueueSnapshotStore(path);
        Assert.True(store.Save(new PlayQueueSnapshot
        {
            Items = [item], CurrentTrackId = item.Id, CurrentIndex = 0, PositionSeconds = 42,
        }));

        var loaded = store.Load();
        Assert.Single(loaded.Items);
        Assert.Equal(42, loaded.PositionSeconds);
        Assert.Equal(item.Id, loaded.CurrentTrackId);

        return loaded.Items[0];
    }

    [Fact]
    public void TrackSnapshot_RoundTripsThroughDisk()
    {
        var path = NewPath();
        var restored = RoundTrip(QueuedTrack.From(Track()), path).ToTrack();

        Assert.Equal(10250281307392909, restored.Id);
        Assert.Equal("晴天", restored.Title);
        Assert.Equal("周杰伦", restored.ArtistText);
        Assert.Equal("叶惠美", restored.AlbumName);
        Assert.Equal(12345, restored.AlbumId);
        Assert.Equal(new Uri("https://cover.example.invalid/a.jpg"), restored.CoverImage);
        Assert.Equal(TimeSpan.FromSeconds(269), restored.Duration);
        Assert.Equal([AudioQuality.Lossless, AudioQuality.Standard], restored.AvailableQualities);
        Assert.True(restored.RequiresVip);
        Assert.False(restored.RequiresPurchase);
        Assert.True(restored.HasMv);
    }

    [Fact]
    public void AudioVariants_SurviveTheRoundTrip()
    {
        var path = NewPath();
        var restored = RoundTrip(QueuedTrack.From(Track()), path).ToTrack();

        Assert.Equal(2, restored.AudioVariants.Count);
        var lossless = restored.AudioVariants[0];
        Assert.Equal(AudioQuality.Lossless, lossless.Quality);
        Assert.Equal("ff", lossless.Level);
        Assert.Equal("flac", lossless.Format);
        Assert.Equal(1411, lossless.BitrateKbps);
        Assert.Equal(47_000_000, lossless.SizeBytes);
        Assert.Equal("1411kflac", lossless.RequestBitrate);
    }

    [Fact]
    public void QualitiesAreWrittenAsNames_AndTheComputedBitrateIsNotPersisted()
    {
        var path = NewPath();
        RoundTrip(QueuedTrack.From(Track()), path);
        var json = File.ReadAllText(path);

        // 写成数字的话，以后枚举重排会让旧文件静默读成别的档位。
        Assert.Contains("\"Lossless\"", json);
        // 计算出来的请求参数不该进磁盘。
        Assert.DoesNotContain("RequestBitrate", json);
        Assert.DoesNotContain("1411kflac", json);
    }

    [Fact]
    public void UnsupportedAndUndefinedQualities_AreDroppedOnRestore()
    {
        var item = QueuedTrack.From(Track()) with
        {
            AvailableQualities = [AudioQuality.Lossless, (AudioQuality)99],
        };

        var restored = RoundTrip(item, NewPath()).ToTrack();

        Assert.Equal([AudioQuality.Lossless], restored.AvailableQualities);
    }

    [Fact]
    public void TrackWithoutArtistsStillRestores_WithAnEmptyArtistList()
    {
        // 队列快照不存艺人明细：还原后为空，消费方按既有降级路径显示拼好的艺人串。
        var restored = RoundTrip(QueuedTrack.From(Track()), NewPath()).ToTrack();

        Assert.Empty(restored.Artists);
        Assert.Null(restored.Lyrics);
    }
}
