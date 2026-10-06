using System.Net;
using Bodian.Core.Media;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 封面磁盘缓存。**零真实网络、零真实用户目录** —— 处理器是桩，目录是临时的。
/// </summary>
public sealed class CoverDiskCacheTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bodian-cover-cache-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri Url(string name) => new($"https://cover.example.invalid/{name}.jpg");

    private CoverDiskCache NewCache(StubHandler handler, long capacityBytes = 256L * 1024 * 1024) =>
        new(handler, _directory, capacityBytes);

    // ── 键 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Key_DependsOnTheDecodeSize()
    {
        // 列表按 256 取、播放条按 1024 取，CDN 返回的是不同字节；
        // 键里漏掉边长就会把大图位置填上小图，表现为发糊。
        var url = Url("same");

        Assert.NotEqual(CoverCacheKey.For(url, 256), CoverCacheKey.For(url, 1024));
        Assert.Equal(CoverCacheKey.For(url, 256), CoverCacheKey.For(url, 256));
    }

    [Fact]
    public void Key_DependsOnTheUrl()
    {
        Assert.NotEqual(CoverCacheKey.For(Url("a"), 256), CoverCacheKey.For(Url("b"), 256));
    }

    // ── 写穿与命中 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteThrough_PutsTheBytesOnDiskAndThenHits()
    {
        var handler = StubHandler.WithBody([1, 2, 3, 4, 5]);
        using var cache = NewCache(handler);

        Assert.False(cache.TryGetPath("key1", out _));

        await cache.WriteThroughAsync("key1", Url("a"), Ct);

        Assert.True(cache.TryGetPath("key1", out var path));
        Assert.NotNull(path);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, await File.ReadAllBytesAsync(path, Ct));
    }

    [Fact]
    public async Task WriteThrough_DoesNotDownloadTwiceForTheSameKey()
    {
        var handler = StubHandler.WithBody([1, 2, 3]);
        using var cache = NewCache(handler);

        await cache.WriteThroughAsync("key1", Url("a"), Ct);
        await cache.WriteThroughAsync("key1", Url("a"), Ct);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task WriteThrough_LeavesNoTemporaryFileBehind()
    {
        var handler = StubHandler.WithBody([1, 2, 3]);
        using var cache = NewCache(handler);

        await cache.WriteThroughAsync("key1", Url("a"), Ct);

        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    // ── 失败路径：一律不能抛 ───────────────────────────────────────────────

    [Fact]
    public async Task WriteThrough_OnHttpError_DoesNotThrowAndCachesNothing()
    {
        var handler = StubHandler.WithStatus(HttpStatusCode.NotFound);
        using var cache = NewCache(handler);

        // 由界面层 fire-and-forget 调用，漏出异常会变成未观察任务异常。
        await cache.WriteThroughAsync("key1", Url("a"), Ct);

        Assert.False(cache.TryGetPath("key1", out _));
    }

    [Fact]
    public async Task WriteThrough_OnNetworkFailure_DoesNotThrow()
    {
        var handler = StubHandler.Throwing(new HttpRequestException("断网了"));
        using var cache = NewCache(handler);

        await cache.WriteThroughAsync("key1", Url("a"), Ct);

        Assert.False(cache.TryGetPath("key1", out _));
    }

    [Fact]
    public async Task WriteThrough_OnEmptyBody_CachesNothing()
    {
        var handler = StubHandler.WithBody([]);
        using var cache = NewCache(handler);

        await cache.WriteThroughAsync("key1", Url("a"), Ct);

        Assert.False(cache.TryGetPath("key1", out _));
    }

    // ── 淘汰 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Prune_DeletesOldestFirstUntilUnderCapacity()
    {
        var handler = StubHandler.WithBody(new byte[60]);
        using var cache = NewCache(handler, capacityBytes: 100);

        // 写一份还在上限内的。**不能再写第二份去触发超限** —— 那样写穿里那个
        // fire-and-forget 的淘汰会先跑掉，这里就测不到 Prune 本身了。
        await cache.WriteThroughAsync("keep", Url("keep"), Ct);

        // 直接往目录里塞一份更旧的，模拟「上次留下的、这次超限了」。
        var stale = Path.Combine(_directory, "stale.bin");
        await File.WriteAllBytesAsync(stale, new byte[100], Ct);
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddMinutes(-10));

        var usage = await cache.PruneAsync(Ct);

        // 从旧到新删，删到不超上限为止。
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(Path.Combine(_directory, "keep.bin")));
        Assert.Equal(60, usage.Bytes);
        Assert.Equal(1, usage.FileCount);
    }

    [Fact]
    public async Task Prune_KeepsEverythingWhenUnderCapacity()
    {
        var handler = StubHandler.WithBody(new byte[10]);
        using var cache = NewCache(handler, capacityBytes: 1024);

        await cache.WriteThroughAsync("a", Url("a"), Ct);
        await cache.WriteThroughAsync("b", Url("b"), Ct);

        var usage = await cache.PruneAsync(Ct);

        Assert.Equal(2, usage.FileCount);
        Assert.Equal(20, usage.Bytes);
    }

    // ── 统计与清空 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Measure_CountsFilesOnDisk()
    {
        var handler = StubHandler.WithBody(new byte[42]);
        using var cache = NewCache(handler);

        await cache.WriteThroughAsync("a", Url("a"), Ct);
        await cache.WriteThroughAsync("b", Url("b"), Ct);

        var usage = await cache.MeasureAsync(Ct);

        Assert.Equal(2, usage.FileCount);
        Assert.Equal(84, usage.Bytes);
    }

    [Fact]
    public async Task Clear_RemovesEverythingAndResetsTheCount()
    {
        var handler = StubHandler.WithBody([1, 2, 3]);
        using var cache = NewCache(handler);

        await cache.WriteThroughAsync("a", Url("a"), Ct);
        await cache.WriteThroughAsync("b", Url("b"), Ct);

        var removed = await cache.ClearAsync(Ct);

        Assert.Equal(2, removed);
        Assert.False(cache.TryGetPath("a", out _));
        Assert.Equal(0, (await cache.MeasureAsync(Ct)).Bytes);
    }

    [Fact]
    public async Task Clear_AlsoRemovesStrayTemporaryFiles()
    {
        var handler = StubHandler.WithBody([1, 2, 3]);
        using var cache = NewCache(handler);

        await cache.WriteThroughAsync("a", Url("a"), Ct);
        await File.WriteAllBytesAsync(Path.Combine(_directory, "leftover.bin.tmp"), [1], Ct);

        await cache.ClearAsync(Ct);

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Measure_OnMissingDirectory_ReportsZero()
    {
        using var cache = NewCache(StubHandler.WithBody([1]));

        var usage = await cache.MeasureAsync(Ct);

        Assert.Equal(0, usage.Bytes);
        Assert.Equal(0, usage.FileCount);
    }

    /// <summary>可编程的 HTTP 桩：要什么响应给什么，并数一数被请求了几次。</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        private StubHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        public int RequestCount { get; private set; }

        public static StubHandler WithBody(byte[] body) =>
            new(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });

        public static StubHandler WithStatus(HttpStatusCode status) =>
            new(() => new HttpResponseMessage(status) { Content = new ByteArrayContent([]) });

        public static StubHandler Throwing(Exception exception) =>
            new(() => throw exception);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_respond());
        }
    }
}
