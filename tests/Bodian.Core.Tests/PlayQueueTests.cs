using Bodian.Core.Models;
using Bodian.WinUI.Playback;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 播放队列。重点在<b>增删之后 <c>_order</c> 的下标重映射</b> ——
/// 那里错了不会立刻炸，只会让「下一首」跳到意料之外的地方。
/// </summary>
public sealed class PlayQueueTests
{
    private static Track Track(long id) => new() { Id = id, Title = $"Track {id}" };

    private static PlayQueue Queue(params long[] ids) => Queue(new Random(20261003), ids);

    private static PlayQueue Queue(Random random, params long[] ids)
    {
        var queue = new PlayQueue(random);
        queue.Replace([.. ids.Select(Track)], 0);
        return queue;
    }

    /// <summary>
    /// <c>_order</c> 必须是 <c>_items</c> 下标的一个排列。
    /// </summary>
    /// <remarks>
    /// <c>_order</c> 是私有的，所以从<strong>外部行为</strong>上验：顺序播放模式下从头走到尾，
    /// 应当不重不漏地覆盖队列里每一首。<b>调用前必须把模式设成顺序播放</b>，否则走到头会环绕、验不出来。
    /// </remarks>
    private static void AssertCoversEveryTrackOnce(PlayQueue queue)
    {
        while (queue.MovePrevious())
        {
        }

        var visited = new List<long>();

        do
        {
            visited.Add(queue.Current!.Id);
        }
        while (queue.MoveNext());

        Assert.Equal(queue.Count, visited.Count);
        Assert.Equal(visited.Count, visited.Distinct().Count());
        Assert.Equal(queue.Items.Select(t => t.Id).OrderBy(id => id), visited.OrderBy(id => id));
    }

    private static int CountChanges(PlayQueue queue, Action<PlayQueue> mutate)
    {
        var count = 0;
        queue.Changed += (_, _) => count++;

        mutate(queue);

        return count;
    }

    [Fact]
    public void Sequential_StopsAtTheEnd()
    {
        var queue = Queue(1, 2, 3);

        Assert.True(queue.MoveNext());
        Assert.True(queue.MoveNext());
        Assert.False(queue.MoveNext());
        Assert.False(queue.HasNext);
        Assert.Equal(3, queue.Current!.Id);
        Assert.Equal(2, queue.CurrentIndex);
    }

    [Fact]
    public void Sequential_DoesNotWrapBackwards()
    {
        var queue = Queue(1, 2, 3);

        Assert.False(queue.MovePrevious());
        Assert.Equal(1, queue.Current!.Id);
        Assert.False(queue.HasPrevious);
    }

    [Fact]
    public void MoveToStart_GoesBackToTheFirstTrackOfTheOrder()
    {
        var queue = Queue(1, 2, 3);

        Assert.True(queue.MoveNext());
        Assert.True(queue.MoveNext());
        Assert.Equal(3, queue.Current!.Id);

        Assert.Equal(1, CountChanges(queue, q => Assert.True(q.MoveToStart())));

        Assert.Equal(1, queue.Current!.Id);
        Assert.Equal(0, queue.CurrentIndex);
        Assert.True(queue.HasNext);
        Assert.False(queue.HasPrevious);
    }

    [Fact]
    public void MoveToStart_OnTheFirstTrack_StillRaisesChangedOnce()
    {
        var queue = Queue(1, 2, 3);

        // 游标本来就在第一位，方法仍然返回 true 并抛事件 —— 调用方靠它决定要不要重新加载。
        Assert.Equal(1, CountChanges(queue, q => Assert.True(q.MoveToStart())));
        Assert.Equal(1, queue.Current!.Id);
    }

    [Fact]
    public void MoveToStart_OnAnEmptyQueue_ReturnsFalse()
    {
        var queue = new PlayQueue();

        Assert.Equal(0, CountChanges(queue, q => Assert.False(q.MoveToStart())));
        Assert.Null(queue.Current);
        Assert.Equal(-1, queue.CurrentIndex);
    }

    [Fact]
    public void ListLoop_WrapsBothWays()
    {
        var queue = Queue(1, 2, 3);
        queue.Mode = PlayMode.ListLoop;

        Assert.True(queue.MoveNext());
        Assert.True(queue.MoveNext());
        Assert.True(queue.MoveNext());
        Assert.Equal(1, queue.Current!.Id);

        Assert.True(queue.MovePrevious());
        Assert.Equal(3, queue.Current!.Id);
    }

    [Fact]
    public void SingleTrack_SequentialStopsButListLoopRepeats()
    {
        var queue = Queue(1);

        Assert.False(queue.HasNext);
        Assert.False(queue.HasPrevious);
        Assert.False(queue.MoveNext());

        queue.Mode = PlayMode.ListLoop;

        Assert.True(queue.HasNext);
        Assert.True(queue.HasPrevious);
        Assert.True(queue.MoveNext());
        Assert.Equal(1, queue.Current!.Id);
    }

    [Fact]
    public void Shuffle_PlaysEveryTrackOncePerRound()
    {
        var queue = Queue(new Random(7), 1, 2, 3, 4, 5);
        queue.Mode = PlayMode.Shuffle;

        var visited = new List<long> { queue.Current!.Id };

        for (var i = 0; i < 4; i++)
        {
            Assert.True(queue.MoveNext());
            visited.Add(queue.Current!.Id);
        }

        Assert.Equal(5, visited.Distinct().Count());
        Assert.Equal(queue.Items.Select(t => t.Id).OrderBy(id => id), visited.OrderBy(id => id));
    }

    [Fact]
    public void Shuffle_ReshufflesInsteadOfRepeatingAtTheEndOfARound()
    {
        var queue = Queue(new Random(11), 1, 2, 3, 4, 5);
        queue.Mode = PlayMode.Shuffle;

        for (var i = 0; i < 4; i++)
        {
            Assert.True(queue.MoveNext());
        }

        var lastOfRound = queue.Current!.Id;

        Assert.True(queue.MoveNext());
        Assert.NotEqual(lastOfRound, queue.Current!.Id);
    }

    [Fact]
    public void Shuffle_DoesNotMoveTheCurrentTrack()
    {
        var queue = Queue(new Random(3), 1, 2, 3, 4);
        var current = queue.Current;

        queue.Mode = PlayMode.Shuffle;

        Assert.Same(current, queue.Current);
    }

    [Fact]
    public void SwitchingBackToSequential_RestoresTheVisibleOrder()
    {
        var queue = Queue(new Random(5), 1, 2, 3, 4);
        queue.MoveToItem(2);
        queue.Mode = PlayMode.Shuffle;
        var current = queue.Current;

        queue.Mode = PlayMode.Sequential;

        Assert.Same(current, queue.Current);
        Assert.Equal(2, queue.CurrentIndex);
        AssertCoversEveryTrackOnce(queue);
    }

    [Fact]
    public void Replace_KeepsTheRequestedStartIndex()
    {
        var queue = Queue(1, 2, 3);

        queue.Replace([Track(9), Track(8), Track(7)], 1);

        Assert.Equal(8, queue.Current!.Id);
        Assert.Equal(1, queue.CurrentIndex);
    }

    [Fact]
    public void MoveToItem_JumpsToThatTrack()
    {
        var queue = Queue(1, 2, 3);

        Assert.True(queue.MoveToItem(2));
        Assert.Equal(3, queue.Current!.Id);
        Assert.Equal(2, queue.CurrentIndex);

        Assert.False(queue.MoveToItem(3));
        Assert.Equal(3, queue.Current!.Id);
    }

    [Fact]
    public void InsertNext_PlaysRightAfterTheCurrentTrack()
    {
        var queue = Queue(1, 2, 3);
        queue.MoveNext();

        queue.InsertNext(Track(99));

        Assert.Equal(new long[] { 1, 2, 99, 3 }, queue.Items.Select(t => t.Id));
        Assert.True(queue.MoveNext());
        Assert.Equal(99, queue.Current!.Id);
        Assert.True(queue.MoveNext());
        Assert.Equal(3, queue.Current!.Id);
    }

    [Fact]
    public void InsertNext_WhileShuffling_StillPlaysNext()
    {
        var queue = Queue(new Random(13), 1, 2, 3, 4, 5);
        queue.Mode = PlayMode.Shuffle;

        queue.InsertNext(Track(99));

        Assert.Equal(6, queue.Count);
        Assert.True(queue.MoveNext());
        Assert.Equal(99, queue.Current!.Id);
    }

    [Fact]
    public void Operate_OnEmptyQueue_StartsPlayingImmediately()
    {
        var queue = new PlayQueue();

        queue.Append(Track(4));

        Assert.Equal(1, queue.Count);
        Assert.Equal(4, queue.Current!.Id);
        Assert.Equal(0, queue.CurrentIndex);
    }

    [Fact]
    public void InsertNext_OnEmptyQueue_StartsPlayingImmediately()
    {
        var queue = new PlayQueue();

        queue.InsertNext(Track(5));

        Assert.Equal(1, queue.Count);
        Assert.Equal(5, queue.Current!.Id);
    }

    [Fact]
    public void Append_GoesToTheEndOfTheQueue()
    {
        var queue = Queue(1, 2);
        queue.MoveNext();

        queue.Append(Track(7));

        Assert.Equal(new long[] { 1, 2, 7 }, queue.Items.Select(t => t.Id));
        Assert.Equal(2, queue.Current!.Id);
        AssertCoversEveryTrackOnce(queue);
    }

    [Fact]
    public void Append_AndInsertNext_KeepEveryTrackReachable()
    {
        var queue = Queue(1, 2, 3);
        queue.MoveNext();

        queue.Append(Track(4));
        queue.InsertNext(Track(5));
        queue.MoveNext();

        queue.Append(Track(6));

        Assert.Equal(6, queue.Count);
        AssertCoversEveryTrackOnce(queue);
    }

    [Fact]
    public void RemoveItem_BeforeTheCursor_KeepsTheCurrentTrack()
    {
        var queue = Queue(1, 2, 3);
        queue.MoveToItem(2);

        Assert.True(queue.RemoveItem(0));

        Assert.Equal(3, queue.Current!.Id);
        Assert.Equal(new long[] { 2, 3 }, queue.Items.Select(t => t.Id));
        AssertCoversEveryTrackOnce(queue);
    }

    [Fact]
    public void RemoveItem_AfterTheCursor_KeepsTheCurrentTrack()
    {
        var queue = Queue(1, 2, 3);

        Assert.True(queue.RemoveItem(2));

        Assert.Equal(1, queue.Current!.Id);
        Assert.Equal(new long[] { 1, 2 }, queue.Items.Select(t => t.Id));
        AssertCoversEveryTrackOnce(queue);
    }

    [Fact]
    public void RemoveItem_AtTheEnd_ClampsTheCursor()
    {
        var queue = Queue(1, 2, 3);
        queue.MoveToItem(2);

        Assert.True(queue.RemoveItem(2));

        Assert.Equal(2, queue.Current!.Id);
        Assert.Equal(1, queue.CurrentIndex);
        Assert.False(queue.HasNext);
        AssertCoversEveryTrackOnce(queue);
    }

    [Fact]
    public void RemoveItem_TheCurrentTrack_MovesOnToTheNextOne()
    {
        var queue = Queue(1, 2, 3);
        queue.MoveNext();

        Assert.True(queue.RemoveItem(1));

        Assert.Equal(3, queue.Current!.Id);
        AssertCoversEveryTrackOnce(queue);
    }

    [Fact]
    public void RemoveItem_WhileShuffling_KeepsTheRoundIntact()
    {
        var queue = Queue(new Random(17), 1, 2, 3, 4, 5);
        queue.Mode = PlayMode.Shuffle;

        var current = queue.Current;
        Assert.True(queue.MoveNext());
        Assert.True(queue.RemoveItem(0));

        Assert.Equal(4, queue.Count);
        Assert.NotSame(current, queue.Current);
    }

    [Fact]
    public void RemovingEverything_MakesTheQueueEmpty()
    {
        var queue = Queue(1);

        Assert.True(queue.RemoveItem(0));

        Assert.Equal(0, queue.Count);
        Assert.Equal(-1, queue.CurrentIndex);
        Assert.Null(queue.Current);
        Assert.False(queue.MoveNext());
        Assert.False(queue.MovePrevious());
    }

    [Fact]
    public void RemoveItem_OutOfRange_IsIgnored()
    {
        var queue = Queue(1, 2);

        Assert.False(queue.RemoveItem(2));
        Assert.False(queue.RemoveItem(-1));
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void Clear_EmptiesTheQueue()
    {
        var queue = Queue(1, 2, 3);

        queue.Clear();

        Assert.Equal(0, queue.Count);
        Assert.Null(queue.Current);
        Assert.False(queue.HasNext);
    }

    [Fact]
    public void Mutations_RaiseChanged()
    {
        var queue = Queue(1, 2);

        Assert.Equal(1, CountChanges(queue, q => q.Append(Track(3))));
        Assert.Equal(1, CountChanges(queue, q => q.InsertNext(Track(4))));
        Assert.Equal(1, CountChanges(queue, q => q.RemoveItem(0)));
        Assert.Equal(1, CountChanges(queue, q => q.Mode = PlayMode.ListLoop));
        Assert.Equal(1, CountChanges(queue, q => q.MoveNext()));
        Assert.Equal(1, CountChanges(queue, q => q.Clear()));
    }

    [Fact]
    public void SettingTheSameMode_DoesNotRaiseChanged()
    {
        var queue = Queue(1, 2);

        Assert.Equal(0, CountChanges(queue, q => q.Mode = PlayMode.Sequential));
    }

    [Fact]
    public void UnknownMode_IsRejected()
    {
        var queue = Queue(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Mode = (PlayMode)99);
    }

    // ── 批量追加与去重 ──────────────────────────────────────────────────────

    [Fact]
    public void Append_WhenTheTrackIsAlreadyQueued_DoesNothing()
    {
        var queue = Queue(1, 2);

        Assert.False(queue.Append(Track(2)));

        Assert.Equal(new long[] { 1, 2 }, queue.Items.Select(t => t.Id));
    }

    [Fact]
    public void Append_WhenTheTrackIsNew_ReportsAdded()
    {
        var queue = Queue(1, 2);

        Assert.True(queue.Append(Track(3)));

        Assert.Equal(new long[] { 1, 2, 3 }, queue.Items.Select(t => t.Id));
    }

    [Fact]
    public void AppendRange_SkipsWhatIsAlreadyQueued_AndDuplicatesWithinTheBatch()
    {
        var queue = Queue(1, 2);

        var added = queue.AppendRange([Track(2), Track(3), Track(3), Track(4)]);

        Assert.Equal(2, added);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, queue.Items.Select(t => t.Id));
        AssertCoversEveryTrackOnce(queue);
    }

    [Fact]
    public void AppendRange_OnEmptyQueue_StartsAtTheFirstAdded()
    {
        var queue = new PlayQueue();

        var added = queue.AppendRange([Track(4), Track(5)]);

        Assert.Equal(2, added);
        Assert.Equal(4, queue.Current!.Id);
        Assert.Equal(0, queue.CurrentIndex);
        Assert.True(queue.HasNext);
    }

    /// <summary>没有有效 Id 的曲目加进去也播不了，而且无从去重，直接跳过。</summary>
    [Fact]
    public void AppendRange_SkipsTracksWithoutAValidId()
    {
        var queue = Queue(1);

        var added = queue.AppendRange([Track(0), Track(-1), Track(2)]);

        Assert.Equal(1, added);
        Assert.Equal(new long[] { 1, 2 }, queue.Items.Select(t => t.Id));
    }

    [Fact]
    public void AppendRange_AddingNothing_LeavesTheQueueAndTheCursorAlone()
    {
        var queue = Queue(1, 2);

        var changes = 0;
        queue.Changed += (_, _) => changes++;

        Assert.Equal(0, queue.AppendRange([Track(1), Track(2)]));
        Assert.Equal(0, changes);
        Assert.Equal(1, queue.Current!.Id);
    }

    [Fact]
    public void AppendRange_KeepsTheCurrentTrackAndRaisesChangedOnce()
    {
        var queue = Queue(1, 2);
        queue.MoveNext();

        Assert.Equal(1, CountChanges(queue, q => q.AppendRange([Track(3), Track(4)])));

        Assert.Equal(2, queue.Current!.Id);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, queue.Items.Select(t => t.Id));
        AssertCoversEveryTrackOnce(queue);
    }
}
