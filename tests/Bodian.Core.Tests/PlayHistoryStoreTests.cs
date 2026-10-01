using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 「最近播放」的本地存储。**零网络、零真实用户目录** —— 每个测试用自己那份临时文件。
/// </summary>
public sealed class PlayHistoryStoreTests : IDisposable
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bodian-history-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_directory, "history.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonPlayHistoryStore NewStore(Clock clock, int capacity = JsonPlayHistoryStore.DefaultCapacity) =>
        new(Path_, clock, capacity: capacity);

    // ── 基本读写 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoFile_IsEmptyHistory()
    {
        var store = NewStore(new Clock(Origin));

        Assert.Empty(await store.GetRecentAsync(10, Ct));
    }

    [Fact]
    public async Task Record_ThenReadBack()
    {
        var clock = new Clock(Origin);
        var store = NewStore(clock);

        clock.Advance(TimeSpan.FromMinutes(1));
        await store.RecordAsync(TrackWith(228908, "晴天"), Ct);

        var entries = await store.GetRecentAsync(10, Ct);

        var entry = Assert.Single(entries);

        Assert.Equal(228908, entry.MusicId);
        Assert.Equal("晴天", entry.Title);
        Assert.Equal(Origin.AddMinutes(1), entry.PlayedAt);
    }

    /// <summary>最新的在最前，翻页与展示都依赖这个顺序。</summary>
    [Fact]
    public async Task NewestComesFirst()
    {
        var clock = new Clock(Origin);
        var store = NewStore(clock);

        await store.RecordAsync(TrackWith(1, "第一首"), Ct);
        clock.Advance(TimeSpan.FromMinutes(5));
        await store.RecordAsync(TrackWith(2, "第二首"), Ct);
        clock.Advance(TimeSpan.FromMinutes(5));
        await store.RecordAsync(TrackWith(3, "第三首"), Ct);

        var entries = await store.GetRecentAsync(10, Ct);

        Assert.Equal([3L, 2L, 1L], entries.Select(e => e.MusicId));
    }

    /// <summary>
    /// 同一首歌重复播放**提到最前，不新增条目**。
    /// </summary>
    /// <remarks>
    /// 不这么做的话，单曲循环一晚上会把整段历史冲掉，列表里也全是同一首歌。
    /// </remarks>
    [Fact]
    public async Task SameTrackPlayedAgain_MovesToFrontWithoutDuplicating()
    {
        var clock = new Clock(Origin);
        var store = NewStore(clock);

        await store.RecordAsync(TrackWith(1, "甲"), Ct);
        clock.Advance(TimeSpan.FromMinutes(5));
        await store.RecordAsync(TrackWith(2, "乙"), Ct);
        clock.Advance(TimeSpan.FromMinutes(5));
        await store.RecordAsync(TrackWith(1, "甲"), Ct);

        var entries = await store.GetRecentAsync(10, Ct);

        Assert.Equal(2, entries.Count);
        Assert.Equal(1, entries[0].MusicId);
        Assert.Equal(Origin.AddMinutes(10), entries[0].PlayedAt);
    }

    [Fact]
    public async Task ExceedingCapacity_DropsOldest()
    {
        var clock = new Clock(Origin);
        var store = NewStore(clock, capacity: 3);

        for (var id = 1L; id <= 5; id++)
        {
            await store.RecordAsync(TrackWith(id, $"第 {id} 首"), Ct);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        var entries = await store.GetRecentAsync(100, Ct);

        Assert.Equal([5L, 4L, 3L], entries.Select(e => e.MusicId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveLimit_ReturnsEmpty(int limit)
    {
        var store = NewStore(new Clock(Origin));

        await store.RecordAsync(TrackWith(1), Ct);

        Assert.Empty(await store.GetRecentAsync(limit, Ct));
    }

    [Fact]
    public async Task LimitIsApplied()
    {
        var clock = new Clock(Origin);
        var store = NewStore(clock);

        for (var id = 1L; id <= 5; id++)
        {
            await store.RecordAsync(TrackWith(id), Ct);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(2, (await store.GetRecentAsync(2, Ct)).Count);
    }

    [Fact]
    public async Task Clear_EmptiesHistoryOnDiskToo()
    {
        var clock = new Clock(Origin);
        var store = NewStore(clock);

        await store.RecordAsync(TrackWith(1), Ct);
        await store.ClearAsync(Ct);

        Assert.Empty(await store.GetRecentAsync(10, Ct));
        Assert.Empty(await NewStore(clock).GetRecentAsync(10, Ct));
    }

    // ── 跨进程存活 ──────────────────────────────────────────────────────────

    /// <summary>换一个新实例（模拟重启）还能读到，且顺序不乱。</summary>
    [Fact]
    public async Task PersistedAcrossInstances()
    {
        var clock = new Clock(Origin);
        var first = NewStore(clock);

        await first.RecordAsync(TrackWith(1, "甲"), Ct);
        clock.Advance(TimeSpan.FromMinutes(3));
        await first.RecordAsync(TrackWith(2, "乙"), Ct);

        var entries = await NewStore(clock).GetRecentAsync(10, Ct);

        Assert.Equal([2L, 1L], entries.Select(e => e.MusicId));
    }

    /// <summary>
    /// 文件坏了当空历史，**不抛也不删**。
    /// </summary>
    /// <remarks>
    /// 「最近播放」一页空着，远比整个应用起不来好；文件留着还能人工看一眼出了什么事。
    /// </remarks>
    [Fact]
    public async Task CorruptFile_IsTreatedAsEmptyAndKept()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path_, "{ 这不是 JSON", Ct);

        var store = NewStore(new Clock(Origin));

        Assert.Empty(await store.GetRecentAsync(10, Ct));
        Assert.True(File.Exists(Path_));
    }

    // ── 快照与还原 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 存的是快照：曲名、封面、档位都要能原样还原，否则「最近播放」既显示不出来也播不了。
    /// </summary>
    [Fact]
    public async Task Snapshot_RoundTripsThroughDisk()
    {
        var clock = new Clock(Origin);
        var store = NewStore(clock);

        var original = new Track
        {
            Id = 228908,
            Title = "晴天",
            ArtistText = "周杰伦",
            AlbumName = "叶惠美",
            CoverImage = new Uri("https://img4.kuwo.cn/star/albumcover/120/s3s94/93/211513640.jpg"),
            Duration = TimeSpan.FromSeconds(269),
            AvailableQualities = [AudioQuality.Lossless, AudioQuality.High, AudioQuality.Standard],
            RequiresVip = true,

            // 歌词轨信息**刻意不给**：历史快照不存在它，取词时会走兜底。
            Lyrics = null,
        };

        await store.RecordAsync(original, Ct);

        var restored = Assert.Single(await NewStore(clock).GetRecentAsync(1, Ct)).ToTrack();

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Title, restored.Title);
        Assert.Equal(original.ArtistText, restored.ArtistText);
        Assert.Equal(original.AlbumName, restored.AlbumName);
        Assert.Equal(original.CoverImage, restored.CoverImage);
        Assert.Equal(original.Duration, restored.Duration);
        Assert.Equal(original.AvailableQualities, restored.AvailableQualities);
        Assert.True(restored.RequiresVip);
        Assert.Null(restored.Lyrics);
        Assert.True(restored.HasPlayableQuality);
    }

    /// <summary>
    /// 枚举写成字符串，不是数字。
    /// </summary>
    /// <remarks>
    /// 数字会随枚举重排而静默变成**错的档位**。这条是守格式的，不是守功能的。
    /// </remarks>
    [Fact]
    public async Task QualityIsPersistedAsString()
    {
        var store = NewStore(new Clock(Origin));

        await store.RecordAsync(
            TrackWith(1) with { AvailableQualities = [AudioQuality.Lossless] },
            Ct);

        Assert.Contains("\"Lossless\"", await File.ReadAllTextAsync(Path_, Ct));
    }

    /// <summary>曲目 id 超出 int32 也要能存下来（实测存在 10250281307392909 这样的 id）。</summary>
    [Fact]
    public async Task LargeMusicId_SurvivesRoundTrip()
    {
        var clock = new Clock(Origin);
        var store = NewStore(clock);

        await store.RecordAsync(TrackWith(10250281307392909, "大 id"), Ct);

        var entry = Assert.Single(await NewStore(clock).GetRecentAsync(1, Ct));

        Assert.Equal(10250281307392909, entry.MusicId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveCapacity_Throws(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new JsonPlayHistoryStore(Path_, new Clock(Origin), capacity: capacity));
    }

    [Fact]
    public async Task Record_NullTrack_Throws()
    {
        var store = NewStore(new Clock(Origin));

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.RecordAsync(null!, Ct));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Track TrackWith(long id, string title = "曲目") => new()
    {
        Id = id,
        Title = title,
        ArtistText = "歌手",
        AlbumName = "专辑",
        CoverImage = new Uri("https://img4.kuwo.cn/star/albumcover/120/s3s94/93/211513640.jpg"),
        Duration = TimeSpan.FromSeconds(215),
        AvailableQualities = [AudioQuality.Lossless, AudioQuality.High],
    };

    /// <summary>
    /// <c>GetUtcNow()</c> 可以手动推进的时钟。
    /// </summary>
    /// <remarks>
    /// 不复用 <c>ManualTimeProvider</c>：那个只管 <c>GetTimestamp</c>（给歌词时钟用的），
    /// 而这里要的是可预测的**墙钟时间**。
    /// </remarks>
    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan delta) => Now += delta;
    }
}
