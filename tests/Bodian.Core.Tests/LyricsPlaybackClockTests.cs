using Bodian.Core.Playback;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 歌词时钟：把 5Hz 的位置上报插值成每帧连续的位置。
/// </summary>
/// <remarks>
/// 用可以手动推进时间的 <see cref="ManualTimeProvider"/>，所以这些断言全是确定性的、
/// 不依赖真实时钟。断言的着力点是<b>跳变与回退</b>——渲染层最怕的就是高亮乱跳。
/// </remarks>
public sealed class LyricsPlaybackClockTests
{
    private static readonly TimeSpan Report = TimeSpan.FromMilliseconds(200);

    // ── 初始与暂停 ──────────────────────────────────────────────────────────

    [Fact]
    public void Initially_ReportsZero()
    {
        var (_, clock) = Create();

        Assert.Equal(TimeSpan.Zero, clock.Position);
        Assert.Equal(0, clock.JumpCount);
    }

    /// <summary>没在播的时候时间流逝不该动位置。</summary>
    [Fact]
    public void WhilePaused_TimeDoesNotMoveThePosition()
    {
        var (time, clock) = Create();

        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.Zero, clock.Position);
    }

    [Fact]
    public void Playing_MakesThePositionAdvanceWithTime()
    {
        var (time, clock) = Create();

        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromSeconds(1), clock.Position);
    }

    /// <summary>
    /// 暂停时用**当前输出位置**当锚点，不是引擎上报的位置 —— 后者可能滞后一个上报周期，
    /// 直接拿它当锚点会在暂停的瞬间看到回跳。
    /// </summary>
    [Fact]
    public void Pausing_FreezesAtTheCurrentOutputPosition()
    {
        var (time, clock) = Create();

        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromMilliseconds(1000));
        clock.Sync(TimeSpan.FromMilliseconds(1000));   // 引擎刚报过一次

        clock.SetPlaying(false);
        time.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(TimeSpan.FromMilliseconds(1000), clock.Position);
    }

    [Fact]
    public void Resuming_ContinuesFromWhereItFroze()
    {
        var (time, clock) = Create();

        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromMilliseconds(1000));
        clock.Sync(TimeSpan.FromMilliseconds(1000));
        clock.SetPlaying(false);

        time.Advance(TimeSpan.FromSeconds(3));
        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromMilliseconds(500));

        Assert.Equal(TimeSpan.FromMilliseconds(1500), clock.Position);
    }

    /// <summary>
    /// 暂停/恢复<b>不算</b>跳变：渲染层靠 <see cref="LyricsPlaybackClock.JumpCount"/>
    /// 决定要不要把滚动切成瞬移，暂停时瞬移一下是刺眼的。
    /// </summary>
    [Fact]
    public void PausingAndResuming_AreNotJumps()
    {
        var (_, clock) = Create();

        clock.SetPlaying(true);
        clock.SetPlaying(false);
        clock.SetPlaying(true);

        Assert.Equal(0, clock.JumpCount);
    }

    // ── 普通推进：平滑消化 ──────────────────────────────────────────────────

    /// <summary>偏差不大时不动锚点，只记一个修正量；读出来的位置立刻等于上报值。</summary>
    [Fact]
    public void SmallDeviation_IsAbsorbedWithoutMovingTheAnchor()
    {
        var (time, clock) = Create();

        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromMilliseconds(500));
        clock.Sync(TimeSpan.FromMilliseconds(600));   // 引擎说 600，时钟预测 500

        Assert.Equal(TimeSpan.FromMilliseconds(600), clock.Position);
        Assert.Equal(0, clock.JumpCount);
    }

    /// <summary>修正量在消化窗口内线性衰减掉，之后位置回到「锚点 + 真实流逝」这条线上。</summary>
    [Fact]
    public void TheCorrection_DecaysOverTheSmoothWindow()
    {
        var (time, clock) = Create();

        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromMilliseconds(500));
        clock.Sync(TimeSpan.FromMilliseconds(600));

        time.Advance(LyricsPlaybackClock.SmoothWindow);

        // 锚点在 0、已经过去 700ms，修正量消耗殆尽
        Assert.Equal(TimeSpan.FromMilliseconds(700), clock.Position);
    }

    /// <summary>
    /// <b>不会累积漂移。</b> 引擎按 200ms 节拍上报、每次都比预测晚 30ms，
    /// 十次之后位置仍然是「真实流逝 − 30ms」，没有越走越偏。
    /// </summary>
    [Fact]
    public void RepeatedReports_DoNotAccumulateDrift()
    {
        var (time, clock) = Create();
        clock.SetPlaying(true);

        for (var i = 1; i <= 10; i++)
        {
            time.Advance(Report);
            clock.Sync(TimeSpan.FromMilliseconds((200 * i) - 30));
        }

        Assert.Equal(TimeSpan.FromMilliseconds(1970), clock.Position);
        Assert.Equal(0, clock.JumpCount);
    }

    // ── 跳变：硬对齐 ────────────────────────────────────────────────────────

    /// <summary>拖动进度条造成的偏差远超阈值，直接换锚点。</summary>
    [Fact]
    public void LargeDeviation_IsAHardJump()
    {
        var (time, clock) = Create();

        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromMilliseconds(500));
        clock.Sync(TimeSpan.FromSeconds(60));

        Assert.Equal(TimeSpan.FromSeconds(60), clock.Position);
        Assert.Equal(1, clock.JumpCount);
    }

    /// <summary>阈值边界：恰好等于阈值不算跳变，超过才算（严格大于）。</summary>
    [Theory]
    [InlineData(400, 0)]
    [InlineData(401, 1)]
    public void TheJumpThreshold_IsStrictlyGreaterThan(int deviationMs, long expectedJumps)
    {
        var (_, clock) = Create();

        clock.SetPlaying(true);
        clock.Sync(TimeSpan.FromMilliseconds(deviationMs));

        Assert.Equal(expectedJumps, clock.JumpCount);
    }

    /// <summary>明确知道位置变了（拖动结束、切歌）时强制对齐，不走平滑消化。</summary>
    [Fact]
    public void ForcedSync_AlignsEvenForASmallDeviation()
    {
        var (time, clock) = Create();

        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromMilliseconds(100));
        clock.Sync(TimeSpan.FromMilliseconds(120), force: true);

        Assert.Equal(TimeSpan.FromMilliseconds(120), clock.Position);
        Assert.Equal(1, clock.JumpCount);
    }

    /// <summary>硬跳之后修正量必须归零，否则旧偏差会在新位置上继续拽一段。</summary>
    [Fact]
    public void AfterAHardJump_NoStaleCorrectionRemains()
    {
        var (time, clock) = Create();

        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromMilliseconds(100));
        clock.Sync(TimeSpan.FromMilliseconds(150));          // 平滑消化，留下 −50ms 的修正量
        clock.Sync(TimeSpan.FromSeconds(30), force: true);   // 紧接着一次硬跳

        time.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal(TimeSpan.FromMilliseconds(30100), clock.Position);
    }

    // ── 切歌与时长 ──────────────────────────────────────────────────────────

    [Fact]
    public void Reset_GoesBackToZeroAndCountsAsAJump()
    {
        var (time, clock) = Create();

        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromSeconds(30));
        clock.Sync(TimeSpan.FromSeconds(30));

        clock.Reset();

        Assert.Equal(TimeSpan.Zero, clock.Position);
        Assert.True(clock.JumpCount >= 1);
    }

    [Fact]
    public void Reset_AlsoPauses()
    {
        var (time, clock) = Create();

        clock.Reset();
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.Zero, clock.Position);
    }

    [Fact]
    public void SetDuration_ClampsThePosition()
    {
        var (time, clock) = Create();

        clock.SetPlaying(true);
        clock.SetDuration(TimeSpan.FromSeconds(3));
        clock.Sync(TimeSpan.FromSeconds(30), force: true);
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(3), clock.Position);
    }

    /// <summary>非正的时长表示「不限制」，不是「钳到零」。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveDurationMeansNoClamp(int seconds)
    {
        var (time, clock) = Create();

        clock.SetPlaying(true);
        clock.SetDuration(TimeSpan.FromSeconds(seconds));
        time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(10), clock.Position);
    }

    /// <summary>位置永远不会是负的，哪怕修正量把它拽下去。</summary>
    [Fact]
    public void Position_NeverGoesNegative()
    {
        var (time, clock) = Create();

        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromMilliseconds(10));
        clock.Sync(TimeSpan.Zero);   // 引擎说还在 0，时钟已经跑了 10ms

        Assert.True(clock.Position >= TimeSpan.Zero);
    }

    private static (ManualTimeProvider Time, LyricsPlaybackClock Clock) Create()
    {
        var time = new ManualTimeProvider();

        return (time, new LyricsPlaybackClock(time));
    }
}
