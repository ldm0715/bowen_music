using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class PlayQueueSnapshotStoreTests
{
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "bodian-queue-snapshot-tests",
        Guid.NewGuid().ToString("N"), "queue.json");

    private static QueuedTrack Item(long id, string title) => new() { Id = id, Title = title };

    [Fact]
    public void MissingFile_IsAnEmptyQueue_WithoutCreatingAFile()
    {
        var path = NewPath();
        var loaded = new JsonPlayQueueSnapshotStore(path).Load("anonymous");
        Assert.Empty(loaded.Items);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void QueueAndCursor_RoundTrip()
    {
        var path = NewPath();
        var store = new JsonPlayQueueSnapshotStore(path);
        Assert.True(store.Save("anonymous", new PlayQueueSnapshot
        {
            Items = [Item(1, "第一首"), Item(2, "第二首")],
            CurrentTrackId = 2,
            CurrentIndex = 1,
            PositionSeconds = 42.5,
        }));

        var loaded = store.Load("anonymous");
        Assert.Equal(2, loaded.Items.Length);
        Assert.Equal("第二首", loaded.Items[1].Title);
        Assert.Equal(2, loaded.CurrentTrackId);
        Assert.Equal(1, loaded.CurrentIndex);
        Assert.Equal(42.5, loaded.PositionSeconds);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"Items\":\"not-an-array\"}")]
    public void InvalidContent_FallsBackToAnEmptyQueue_AndPreservesFile(string json)
    {
        var path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        Assert.Empty(new JsonPlayQueueSnapshotStore(path).Load("anonymous").Items);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void EmptyObject_IsAnEmptyQueue()
    {
        // 手工编辑或损坏到只剩 {}：源生成反序列化不会保留属性上的初始化器，
        // 条目数组会读成 null，必须由 Load 补回来，否则协调器构造时取长度就崩。
        var path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        var loaded = new JsonPlayQueueSnapshotStore(path).Load("anonymous");
        Assert.Empty(loaded.Items);
    }

    [Fact]
    public void EntriesMissingTheirArrays_AreFilledIn()
    {
        // 以后给条目加数组字段时，旧文件读出来那些字段是 null —— ToTrack 会挨个用它们。
        var path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"Items\":[{\"Id\":7,\"Title\":\"第七首\"}]}");

        var item = Assert.Single(new JsonPlayQueueSnapshotStore(path).Load("anonymous").Items);
        Assert.Empty(item.AvailableQualities);
        Assert.Empty(item.AudioVariants);
    }

    [Fact]
    public void SaveFailure_IsReported()
    {
        var path = NewPath();
        Directory.CreateDirectory(path);
        Assert.False(new JsonPlayQueueSnapshotStore(path).Save("anonymous", PlayQueueSnapshot.Default));
    }
}
