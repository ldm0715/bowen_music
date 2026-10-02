using Bodian.Core.Threading;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class LatestValueDispatcherTests
{
    [Fact]
    public void BlockedConsumer_OnlyQueuesOneCallbackAndReceivesNewestPosition()
    {
        var callbacks = new Queue<Action>();
        var received = new List<string>();
        var dispatcher = new LatestValueDispatcher<string>(action => { callbacks.Enqueue(action); return true; }, received.Add);
        for (var i = 0; i < 1000; i++) Assert.True(dispatcher.Post(i.ToString()));
        Assert.Single(callbacks);
        callbacks.Dequeue()();
        Assert.Equal(["999"], received);
        Assert.Empty(callbacks);
    }

    [Fact]
    public void UpdateDuringPublish_IsDeliveredByNextCallback()
    {
        var callbacks = new Queue<Action>();
        var received = new List<string>();
        LatestValueDispatcher<string>? dispatcher = null;
        dispatcher = new(action => { callbacks.Enqueue(action); return true; }, value =>
        {
            received.Add(value);
            if (value == "first") dispatcher!.Post("second");
        });
        dispatcher.Post("first");
        callbacks.Dequeue()();
        Assert.Single(callbacks);
        callbacks.Dequeue()();
        Assert.Equal(["first", "second"], received);
    }

    [Fact]
    public void RejectedEnqueue_CanRetryWithLatestValue()
    {
        var accepted = false;
        var callbacks = new Queue<Action>();
        var received = new List<string>();
        var dispatcher = new LatestValueDispatcher<string>(action =>
        {
            if (!accepted) return false;
            callbacks.Enqueue(action);
            return true;
        }, received.Add);
        Assert.False(dispatcher.Post("old"));
        accepted = true;
        Assert.True(dispatcher.Post("new"));
        callbacks.Dequeue()();
        Assert.Equal(["new"], received);
    }

    [Fact]
    public void SynchronousConsumer_DoesNotLoseSubsequentUpdates()
    {
        var received = new List<string>();
        var dispatcher = new LatestValueDispatcher<string>(action => { action(); return true; }, received.Add);
        dispatcher.Post("first");
        dispatcher.Post("second");
        Assert.Equal(["first", "second"], received);
    }
}
