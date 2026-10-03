using Bodian.WinUI.Controls;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 发现页卡片区的分页算式。边界（宽度为 0、不足一步、换宽度锚点、末页夹取）是这一族的
/// off-by-one 高发区，所以算式本身抽成无 UI 依赖的文件单独验，见 Controls/CardStripPaging.cs。
/// </summary>
public sealed class CardStripPagingTests
{
    /// <summary>
    /// 步长 = 卡片宽 + 间隔。真值来自 <c>Themes/Tokens.xaml</c> 的 SizeHome* 与 SpaceHomeCardGap ——
    /// 发现页四种排法的卡片宽度各不相同，算式本身与具体取值无关。
    /// </summary>
    private const double Step = 128;

    [Theory]
    [InlineData(0, 1)]      // 还没量到宽度
    [InlineData(-5, 1)]     // 异常值也不该崩
    [InlineData(127, 1)]    // 不足一步，退化成一张
    [InlineData(128, 1)]
    [InlineData(255, 1)]
    [InlineData(256, 2)]
    [InlineData(1000, 7)]
    [InlineData(1256, 9)]
    public void PageSize_FloorsAtOne(double width, int expected)
        => Assert.Equal(expected, CardStripPaging.PageSize(width, Step));

    [Fact]
    public void PageSize_TreatsNonPositiveStepAsSingleCard()
        => Assert.Equal(1, CardStripPaging.PageSize(1000, 0));

    [Fact]
    public void Resolve_SinglePage_HidesBothArrows()
    {
        // AI 歌单那几组实测就是 3 张 —— 这也是发现页里最常见的情况。
        var page = CardStripPaging.Resolve(1256, Step, cardCount: 3, anchorIndex: 0);

        Assert.Equal(0, page.FirstIndex);
        Assert.Equal(3, page.Count);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(1, page.PageCount);
        Assert.False(page.HasPrevious);
        Assert.False(page.HasNext);
    }

    [Fact]
    public void Resolve_EmptyList_IsOneEmptyPage()
    {
        var page = CardStripPaging.Resolve(1256, Step, cardCount: 0, anchorIndex: 0);

        Assert.Equal(0, page.Count);
        Assert.False(page.HasPrevious);
        Assert.False(page.HasNext);
    }

    [Fact]
    public void Resolve_LastPage_ClampsToRemainder()
    {
        // 20 张、一页 9 张 → 3 页（9 / 9 / 2）。
        var page = CardStripPaging.Resolve(1256, Step, cardCount: 20, anchorIndex: 19);

        Assert.Equal(2, page.PageIndex);
        Assert.Equal(18, page.FirstIndex);
        Assert.Equal(2, page.Count);
        Assert.True(page.HasPrevious);
        Assert.False(page.HasNext);
    }

    [Fact]
    public void Resolve_MiddlePage_ShowsBothArrows()
    {
        var page = CardStripPaging.Resolve(1256, Step, cardCount: 20, anchorIndex: 9);

        Assert.Equal(1, page.PageIndex);
        Assert.Equal(9, page.FirstIndex);
        Assert.Equal(9, page.Count);
        Assert.True(page.HasPrevious);
        Assert.True(page.HasNext);
    }

    [Fact]
    public void Resolve_AnchorBeyondEnd_LandsOnLastPage()
        => Assert.Equal(2, CardStripPaging.Resolve(1256, Step, cardCount: 20, anchorIndex: 100).PageIndex);

    [Fact]
    public void Resolve_NegativeAnchor_GoesToFirstPage()
        => Assert.Equal(0, CardStripPaging.Resolve(1256, Step, cardCount: 20, anchorIndex: -1).PageIndex);

    [Fact]
    public void Resolve_KeepsAnchorCardVisibleWhenWidthShrinks()
    {
        // 变窄前：一页 9 张、停在第 1 页 → 锚点卡片是第 9 张。
        var before = CardStripPaging.Resolve(1256, Step, cardCount: 20, anchorIndex: 9);
        Assert.Equal(9, before.FirstIndex);

        // 变窄后：一页 8 张（1024 / 128）。锚点卡片（第 9 张）必须仍在眼前。
        var after = CardStripPaging.Resolve(1024, Step, cardCount: 20, anchorIndex: before.FirstIndex);

        Assert.Equal(1, after.PageIndex);
        Assert.Equal(8, after.FirstIndex);
        Assert.True(after.FirstIndex <= 9 && 9 < after.FirstIndex + after.Count);
    }
}
