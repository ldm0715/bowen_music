using Bowen.Core.Playback;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// mpv 磁盘缓存残留的清理：只删 <c>mpv-cache-*.dat</c>，目录不存在时建出来。
/// </summary>
public sealed class AudioCacheCleanerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "bowen-audio-cache-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ClearStale_RemovesOnlyMpvCacheFiles()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "mpv-cache-abc123.dat"), "a");
        File.WriteAllText(Path.Combine(_root, "mpv-cache-def456.dat"), "b");
        File.WriteAllText(Path.Combine(_root, "mpv-cache-abc.txt"), "c");
        File.WriteAllText(Path.Combine(_root, "keep.dat"), "d");

        var removed = AudioCacheCleaner.ClearStale(directory: _root);

        Assert.Equal(2, removed);
        Assert.False(File.Exists(Path.Combine(_root, "mpv-cache-abc123.dat")));
        Assert.False(File.Exists(Path.Combine(_root, "mpv-cache-def456.dat")));
        Assert.True(File.Exists(Path.Combine(_root, "mpv-cache-abc.txt")));
        Assert.True(File.Exists(Path.Combine(_root, "keep.dat")));
    }

    [Fact]
    public void ClearStale_CreatesMissingDirectory()
    {
        var missing = Path.Combine(_root, "nested");

        var removed = AudioCacheCleaner.ClearStale(directory: missing);

        Assert.Equal(0, removed);
        Assert.True(Directory.Exists(missing));
    }

    [Fact]
    public void ClearStale_EmptyDirectory_ReturnsZero()
    {
        Directory.CreateDirectory(_root);

        var removed = AudioCacheCleaner.ClearStale(directory: _root);

        Assert.Equal(0, removed);
    }
}
