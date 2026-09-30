using Bodian.Core.Models.Lyrics;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 歌词动效的纯数学。
/// </summary>
/// <remarks>
/// 参数取值源自 <c>jayfunc/BetterLyrics</c>（GPL-3.0）的
/// <c>LyricsAnimator</c>，见 <see cref="LyricEffectMath"/> 的说明。
/// 这里守的是「渲染层照抄这些函数就行，不必自己推公式」。
/// </remarks>
public sealed class LyricEffectMathTests
{
    // ── 距离因子 ────────────────────────────────────────────────────────────

    [Fact]
    public void DistanceFactor_IsZeroOnThePlayingLine()
        => Assert.Equal(0, LyricEffectMath.DistanceFactor(300, 300, spaceBefore: 200, spaceAfter: 400));

    /// <summary>当前行之上用上半空间做分母，之下用下半空间 —— 当前行在视口里通常不居中。</summary>
    [Theory]
    [InlineData(100, 300, 1.0)]     // 在上一半：|100−300| / 200 = 1
    [InlineData(500, 300, 0.5)]     // 在下一半：|500−300| / 400 = 0.5
    public void DistanceFactor_UsesTheSpaceOnTheMatchingSide(double lineY, double playingY, double expected)
    {
        var factor = LyricEffectMath.DistanceFactor(lineY, playingY, spaceBefore: 200, spaceAfter: 400);

        Assert.Equal(expected, factor, precision: 6);
    }

    [Fact]
    public void DistanceFactor_ClampsTo1()
        => Assert.Equal(1, LyricEffectMath.DistanceFactor(lineY: 0, playingLineY: 3000, spaceBefore: 100, spaceAfter: 100));

    /// <summary>空间为零时不能除零 —— 视口还没测量出来就会走到这里。</summary>
    [Fact]
    public void DistanceFactor_HandlesZeroSpace()
        => Assert.Equal(0, LyricEffectMath.DistanceFactor(0, 300, spaceBefore: 0, spaceAfter: 0));

    // ── 缓动 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 0.75)]
    [InlineData(1, 1)]
    public void EaseOutQuad_MatchesTheCurve(double t, double expected)
        => Assert.Equal(expected, LyricEffectMath.EaseOutQuad(t), precision: 6);

    [Fact]
    public void EaseOutQuad_IsMonotonic()
    {
        var previous = -1.0;

        for (var i = 0; i <= 100; i++)
        {
            var value = LyricEffectMath.EaseOutQuad(i / 100.0);

            Assert.True(value >= previous, $"在 t={i / 100.0} 处回退了");
            previous = value;
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    public void EaseOutQuad_ClampsOutOfRangeInput(double t, double expected)
        => Assert.Equal(expected, LyricEffectMath.EaseOutQuad(t), precision: 6);

    // ── 错峰延迟 ────────────────────────────────────────────────────────────

    /// <summary>波源自己不延迟，否则整条波浪会整体后移。</summary>
    [Fact]
    public void StaggerDelay_IsZeroAtTheWaveSource()
        => Assert.Equal(0, LyricEffectMath.StaggerDelay(0, 0.5, 0.75, 0.4, 0.4));

    [Fact]
    public void StaggerDelay_GrowsWithDistance()
    {
        var previous = -1.0;

        for (var index = 0; index <= 20; index++)
        {
            var value = LyricEffectMath.StaggerDelay(index, 0.5, 0.75, 0.4, 0.4);

            Assert.True(value >= previous, $"在 visibleIndex={index} 处回退了");
            previous = value;
        }
    }

    /// <summary>预算 = 时长 × 比例，且被上限卡住；延迟永远不超过预算。</summary>
    [Theory]
    [InlineData(0.5, 0.375)]   // 0.5 × 0.75，没有触到上限
    [InlineData(1.0, 0.4)]     // 1.0 × 0.75 = 0.75，被 0.4 卡住
    public void StaggerDelay_IsBoundedByTheBudget(double scrollDuration, double expectedBudget)
    {
        var budget = Math.Min(0.4, scrollDuration * 0.75);
        Assert.Equal(expectedBudget, budget, precision: 6);

        Assert.True(LyricEffectMath.StaggerDelay(1000, scrollDuration, 0.75, 0.4, 0.4) <= budget + 1e-9);
    }

    /// <summary>上游默认关闭这个效果，关掉的方式就是把系数设成 0。</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void StaggerDelay_IsDisabledByANonPositiveFactor(double perLineFactor)
        => Assert.Equal(0, LyricEffectMath.StaggerDelay(5, 0.5, 0.75, 0.4, perLineFactor));

    [Fact]
    public void StaggerDelay_HandlesZeroScrollDuration()
        => Assert.Equal(0, LyricEffectMath.StaggerDelay(5, 0, 0.75, 0.4, 0.4));

    // ── 滚动时长 ────────────────────────────────────────────────────────────

    /// <summary>距离为零（就坐在当前行上）时，时长就是基准值，三段时长都不参与。</summary>
    [Fact]
    public void ScrollDuration_DegeneratesToTheBaseOnThePlayingLine()
        => Assert.Equal(
            0.3,
            LyricEffectMath.ScrollDuration(0.3, distanceFactor: 0, 0, 10, 0.5, 0.5),
            precision: 6);

    /// <summary>最远的行拿到满增量，取上段还是下段由它在可见窗口里的位置决定。</summary>
    [Fact]
    public void ScrollDuration_BlendsTopAndBottomByVisiblePosition()
    {
        // 位于可见窗口最上方（factor = 0）→ 全取上段增量
        Assert.Equal(
            0.7,
            LyricEffectMath.ScrollDuration(0.3, distanceFactor: 1, visibleIndex: 0, totalVisible: 10, 0.7, 0.5),
            precision: 6);

        // 位于可见窗口最下方（factor = 1）→ 全取下段增量
        Assert.Equal(
            0.5,
            LyricEffectMath.ScrollDuration(0.3, distanceFactor: 1, visibleIndex: 10, totalVisible: 10, 0.7, 0.5),
            precision: 6);
    }

    /// <summary>可见行数为 0 时不能除零。</summary>
    [Fact]
    public void ScrollDuration_HandlesAnEmptyVisibleWindow()
        => Assert.Equal(
            0.3,
            LyricEffectMath.ScrollDuration(0.3, distanceFactor: 0, visibleIndex: 0, totalVisible: 0, 0.5, 0.5),
            precision: 6);

    // ── 扫光进度 ────────────────────────────────────────────────────────────

    [Fact]
    public void RegionPlayProgress_IsZeroBeforeAnythingStarts()
        => Assert.Equal(0, LyricEffectMath.RegionPlayProgress([10, 10, 10], [0, 0, 0], regionWidth: 30));

    [Fact]
    public void RegionPlayProgress_AccumulatesByCharacterWidth()
        => Assert.Equal(
            0.5,
            LyricEffectMath.RegionPlayProgress([10, 10, 10], [1, 0.5, 0], regionWidth: 30),
            precision: 6);

    /// <summary>遇到第一个没唱完的字就停 —— 它后面的字一定还没开口。</summary>
    [Fact]
    public void RegionPlayProgress_StopsAtTheFirstUnfinishedCharacter()
        => Assert.Equal(
            0.25,
            LyricEffectMath.RegionPlayProgress([10, 10, 10, 10], [1, 0, 1, 1], regionWidth: 40),
            precision: 6);

    [Fact]
    public void RegionPlayProgress_IsAllPlayedWhenDone()
        => Assert.Equal(1, LyricEffectMath.RegionPlayProgress([10, 10, 10], [1, 1, 1], regionWidth: 30));

    /// <summary>字宽的和可能不等于视觉行宽（字距、右边界），所以结果要钳住。</summary>
    [Fact]
    public void RegionPlayProgress_ClampsTo1()
        => Assert.Equal(1, LyricEffectMath.RegionPlayProgress([10, 10], [1, 1], regionWidth: 5));

    [Fact]
    public void RegionPlayProgress_HandlesAZeroWidthRegion()
        => Assert.Equal(0, LyricEffectMath.RegionPlayProgress([10], [1], regionWidth: 0));

    /// <summary>长度不齐时按短的算，不越界。</summary>
    [Fact]
    public void RegionPlayProgress_HandlesMismatchedLengths()
        => Assert.Equal(
            0.2,
            LyricEffectMath.RegionPlayProgress([10], [1, 1, 1], regionWidth: 50),
            precision: 6);
}
