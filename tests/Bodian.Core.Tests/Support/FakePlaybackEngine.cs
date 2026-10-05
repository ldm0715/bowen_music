using Bodian.WinUI.Playback;

namespace Bodian.Core.Tests.Support;

/// <summary>
/// 只记账、不出声的播放引擎。
/// </summary>
/// <remarks>
/// 记下最后一次 <see cref="LoadAsync"/> 收到的东西与调用次数，够断言「换了没有、换成了什么」。
/// 状态与进度可以在测试里直接改 —— 协调器只读它们。
/// </remarks>
internal sealed class FakePlaybackEngine : IPlaybackService
{
    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public PlaybackState State { get; set; }

    public TimeSpan Position { get; set; }

    public TimeSpan Duration => TimeSpan.FromSeconds(300);

    public double Volume => 100;

    public PlaybackSource? Last { get; private set; }

    public int LoadCount { get; private set; }

    /// <summary>置上之后下一次 <see cref="LoadAsync"/> 抛一次 <see cref="IOException"/>，然后自动复位。</summary>
    public bool FailOnce { get; set; }

    /// <summary>模拟一次「这一首放完了」，让协调器走自动续播那条路。</summary>
    /// <remarks>
    /// <b>先置 <see cref="PlaybackState.Stopped"/> 再触发</b>，对齐真引擎
    /// （<c>LibMpvPlaybackService.OnEndFile</c> 也是先 <c>SetState(Stopped)</c> 再抛 <c>Ended</c>）。
    /// 不这样，测试里「放完之后按播放」永远走不到 Idle/Stopped 那条分支，也就验不出真机上为什么没声音。
    /// </remarks>
    public void RaiseEnded()
    {
        State = PlaybackState.Stopped;
        Ended?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged { add { } remove { } }

    public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged { add { } remove { } }

    public event EventHandler? Ended;

    public event EventHandler<PlaybackFailedEventArgs>? Failed { add { } remove { } }

    public Task LoadAsync(PlaybackSource source, CancellationToken cancellationToken = default)
    {
        LoadCount++;

        if (FailOnce)
        {
            FailOnce = false;
            throw new IOException("Synthetic load failure");
        }

        Last = source;
        Position = source.Start ?? TimeSpan.Zero;
        State = source.InitiallyPaused ? PlaybackState.Paused : PlaybackState.Playing;

        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        State = PlaybackState.Paused;

        return Task.CompletedTask;
    }

    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        State = PlaybackState.Playing;

        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        Position = position;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        State = PlaybackState.Stopped;

        return Task.CompletedTask;
    }

    public void SetVolume(double volume)
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
