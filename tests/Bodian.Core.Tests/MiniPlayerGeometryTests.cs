using Bodian.Core.Models;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class MiniPlayerGeometryTests
{
    private static readonly WindowPlacement WorkArea = new(100, 50, 1200, 800);

    private static readonly WindowPlacement Bar = new(400, 300, 420, 80);

    private const int SmallWorkBottom = 400;

    // ── 贴边判定 ────────────────────────────────────────────────────────────

    [Fact]
    public void EdgeAt_FlushLeft_DocksLeft()
        => Assert.Equal(MiniPlayerDockEdge.Left, MiniPlayerGeometry.EdgeAt(new(100, 300, 420, 80), WorkArea));

    [Fact]
    public void EdgeAt_FlushRight_DocksRight()
        => Assert.Equal(MiniPlayerDockEdge.Right, MiniPlayerGeometry.EdgeAt(new(880, 300, 420, 80), WorkArea));

    [Theory]
    [InlineData(101)]
    [InlineData(879)]
    public void EdgeAt_OnePixelInward_StaysFree(int x)
        => Assert.Equal(MiniPlayerDockEdge.None, MiniPlayerGeometry.EdgeAt(new(x, 300, 420, 80), WorkArea));

    [Theory]
    [InlineData(50)]
    [InlineData(770)]
    public void EdgeAt_FlushTopOrBottom_StaysFree(int y)
        => Assert.Equal(MiniPlayerDockEdge.None, MiniPlayerGeometry.EdgeAt(new(400, y, 420, 80), WorkArea));

    // ── 收起与展开 ──────────────────────────────────────────────────────────

    [Fact]
    public void Collapse_Left_KeepsLeftEdgeAndHeight()
        => Assert.Equal(new WindowPlacement(100, 300, 8, 80),
            MiniPlayerGeometry.Collapse(new(100, 300, 420, 80), MiniPlayerDockEdge.Left, 8));

    [Fact]
    public void Collapse_Right_KeepsRightEdgeAndHeight()
        => Assert.Equal(new WindowPlacement(1292, 300, 8, 80),
            MiniPlayerGeometry.Collapse(new(880, 300, 420, 80), MiniPlayerDockEdge.Right, 8));

    [Fact]
    public void Collapse_NotDocked_LeavesWindowAlone()
        => Assert.Equal(Bar, MiniPlayerGeometry.Collapse(Bar, MiniPlayerDockEdge.None, 8));

    [Theory]
    [InlineData(MiniPlayerDockEdge.Left)]
    [InlineData(MiniPlayerDockEdge.Right)]
    public void Collapse_ThenExpand_RestoresExactBarWidth(MiniPlayerDockEdge edge)
    {
        var bar = edge == MiniPlayerDockEdge.Left ? new WindowPlacement(100, 300, 420, 80) : new(880, 300, 420, 80);

        var expanded = MiniPlayerGeometry.Expand(
            MiniPlayerGeometry.Collapse(bar, edge, 8), edge, 420, WorkArea);

        Assert.Equal(bar, expanded);
    }

    [Fact]
    public void Expand_Left_AnchorsToWorkAreaLeftEdge()
        => Assert.Equal(new WindowPlacement(100, 300, 420, 80),
            MiniPlayerGeometry.Expand(new(100, 300, 8, 80), MiniPlayerDockEdge.Left, 420, WorkArea));

    [Fact]
    public void Expand_Right_AnchorsToWorkAreaRightEdge()
    {
        var expanded = MiniPlayerGeometry.Expand(new(1292, 300, 8, 80), MiniPlayerDockEdge.Right, 420, WorkArea);

        Assert.Equal(new WindowPlacement(880, 300, 420, 80), expanded);
        Assert.Equal(WorkArea.X + WorkArea.Width, expanded.X + expanded.Width);
    }

    [Fact]
    public void Expand_AfterCollapse_KeepsVerticalPosition()
    {
        var collapsed = MiniPlayerGeometry.Collapse(new(880, 300, 420, 80), MiniPlayerDockEdge.Right, 8);
        var expanded = MiniPlayerGeometry.Expand(collapsed, MiniPlayerDockEdge.Right, 420, WorkArea);

        Assert.Equal(collapsed.Y, expanded.Y);
        Assert.Equal(collapsed.Height, expanded.Height);
    }

    // ── 唤起区 ──────────────────────────────────────────────────────────────

    [Fact]
    public void WakeZone_Left_SitsOnWorkAreaLeftEdge()
        => Assert.Equal(new WindowPlacement(100, 292, 8, 96),
            MiniPlayerGeometry.WakeZone(new(100, 300, 8, 80), WorkArea, MiniPlayerDockEdge.Left, 8, 8));

    [Fact]
    public void WakeZone_Right_SitsOnWorkAreaRightEdge()
        => Assert.Equal(new WindowPlacement(1292, 292, 8, 96),
            MiniPlayerGeometry.WakeZone(new(1292, 300, 8, 80), WorkArea, MiniPlayerDockEdge.Right, 8, 8));

    [Fact]
    public void WakeZone_SpansBarHeightPlusSlack()
    {
        var zone = MiniPlayerGeometry.WakeZone(new(100, 300, 8, 80), WorkArea, MiniPlayerDockEdge.Left, 8, 8);

        Assert.Equal(80 + (2 * 8), zone.Height);
    }

    [Fact]
    public void WakeZone_OnNegativeCoordinateMonitor_UsesNegativeX()
        => Assert.Equal(new WindowPlacement(-1920, 292, 8, 96),
            MiniPlayerGeometry.WakeZone(new(-1920, 300, 8, 80), new(-1920, 0, 1920, 1040),
                MiniPlayerDockEdge.Left, 8, 8));

    [Fact]
    public void WakeZone_NotDocked_IsEmptyAndNeverMatches()
    {
        var zone = MiniPlayerGeometry.WakeZone(new(400, 300, 420, 80), WorkArea, MiniPlayerDockEdge.None, 8, 8);

        Assert.Equal(0, zone.Width);
        Assert.False(MiniPlayerGeometry.Contains(zone, 400, 300));
    }

    // ── 命中 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(100, 200, true)]
    [InlineData(149, 239, true)]
    [InlineData(150, 200, false)]
    [InlineData(100, 240, false)]
    [InlineData(99, 200, false)]
    public void Contains_UsesHalfOpenIntervals(int x, int y, bool expected)
        => Assert.Equal(expected, MiniPlayerGeometry.Contains(new(100, 200, 50, 40), x, y));

    // ── 唤起闩锁 ────────────────────────────────────────────────────────────

    [Fact]
    public void WakeTick_ArmedAndCursorInside_Wakes()
        => Assert.Equal((true, true), MiniPlayerGeometry.WakeTick(true, true));

    [Fact]
    public void WakeTick_CollapsedAndCursorInside_DoesNotWakeUntilMouseLeftOnce()
    {
        // 收起的那一刻 armed 被置 false，光标还停在唤起区里 —— 不能立刻又展开。
        Assert.Equal((false, false), MiniPlayerGeometry.WakeTick(false, true));

        // 光标挪开一次，重新武装；这时再回来才唤得起。
        Assert.Equal((false, true), MiniPlayerGeometry.WakeTick(false, false));
        Assert.Equal((true, true), MiniPlayerGeometry.WakeTick(true, true));
    }

    [Fact]
    public void WakeTick_CursorOutside_ReArms()
        => Assert.Equal((false, true), MiniPlayerGeometry.WakeTick(false, false));

    [Fact]
    public void WakeTick_ArmedAndCursorOutside_StaysArmed()
        => Assert.Equal((false, true), MiniPlayerGeometry.WakeTick(true, false));

    // ── 队列面板方向 ────────────────────────────────────────────────────────

    // 生产配置里条与面板之间没有间隙（连成一块），测试按同一组数走。
    private static MiniPlayerPanelPlan? Plan(WindowPlacement bar, WindowPlacement? workArea = null)
        => MiniPlayerGeometry.PlanPanel(bar, workArea ?? WorkArea, 320, 0, 120);

    [Fact]
    public void Panel_BottomRoomEnough_OpensDown()
    {
        var plan = Plan(Bar);

        Assert.NotNull(plan);
        Assert.Equal(MiniPlayerPanelSide.Down, plan.Value.Side);
        Assert.Equal(320, plan.Value.PanelHeight);
        Assert.Equal(new WindowPlacement(400, 300, 420, 80 + 320), plan.Value.Window);
    }

    [Fact]
    public void Panel_BottomRoomExactlyPanelHeight_OpensDown()
    {
        // 下方正好 320：差一像素就该翻面，所以这个边界必须落在「向下」。
        var plan = Plan(new(400, 450, 420, 80));

        Assert.NotNull(plan);
        Assert.Equal(MiniPlayerPanelSide.Down, plan.Value.Side);
        Assert.Equal(320, plan.Value.PanelHeight);
    }

    [Fact]
    public void Panel_BottomRoomOnePixelShort_OpensUp()
    {
        var plan = Plan(new(400, 451, 420, 80));

        Assert.NotNull(plan);
        Assert.Equal(MiniPlayerPanelSide.Up, plan.Value.Side);
        Assert.Equal(320, plan.Value.PanelHeight);
        Assert.Equal(new WindowPlacement(400, 451 - 320, 420, 80 + 320), plan.Value.Window);
    }

    [Fact]
    public void Panel_Up_MovesTopUpAndGrowsHeight()
    {
        var bar = new WindowPlacement(400, 451, 420, 80);
        var plan = Plan(bar);

        Assert.NotNull(plan);
        Assert.Equal(bar.Y - 320, plan.Value.Window.Y);
        Assert.Equal(bar.Height + 320, plan.Value.Window.Height);

        // 关键不变量：条的屏幕位置一动不动，只是窗口往上长了。
        Assert.Equal(bar.Y, plan.Value.Window.Y + (plan.Value.Window.Height - bar.Height));
    }

    [Fact]
    public void Panel_Down_KeepsTopAndGrowsHeight()
    {
        var bar = Bar;
        var plan = Plan(bar);

        Assert.NotNull(plan);
        Assert.Equal(bar.Y, plan.Value.Window.Y);
        Assert.Equal(bar.Height + 320, plan.Value.Window.Height);
    }

    [Theory]
    [InlineData(450)]
    [InlineData(451)]
    public void Panel_KeepsLeftEdgeAndWidth(int barY)
    {
        var bar = new WindowPlacement(880, barY, 420, 80);
        var plan = Plan(bar);

        Assert.NotNull(plan);
        Assert.Equal(880, plan.Value.Window.X);
        Assert.Equal(420, plan.Value.Window.Width);

        // 贴右边时窗口右缘始终贴平工作区右边。
        Assert.Equal(WorkArea.X + WorkArea.Width, plan.Value.Window.X + plan.Value.Window.Width);
    }

    [Fact]
    public void Panel_NeitherSideFits_ShrinksToLargerRoom()
    {
        // 工作区只剩 400 高：条高 80，上下合计 320 < 320 的两倍，两边都不够。
        var work = new WindowPlacement(0, 0, 1200, SmallWorkBottom);
        var plan = Plan(new(400, 200, 420, 80), work);

        Assert.NotNull(plan);
        Assert.Equal(MiniPlayerPanelSide.Up, plan.Value.Side);
        Assert.Equal(200, plan.Value.PanelHeight);
        Assert.Equal(new WindowPlacement(400, 0, 420, 80 + 200), plan.Value.Window);
    }

    [Fact]
    public void Panel_NeitherSideFitsAndBelowMinimum_ReturnsNull()
    {
        // 上下各只有 35，压完低于 120 的下限 —— 这一下不开面板。
        var work = new WindowPlacement(0, 0, 1200, 150);

        Assert.Null(Plan(new(400, 35, 420, 80), work));
    }

    [Theory]
    [InlineData(300)]
    [InlineData(450)]
    [InlineData(451)]
    [InlineData(700)]
    public void Panel_PlanRoundTrip_WindowStaysInsideWorkArea(int barY)
    {
        var plan = Plan(new(400, barY, 420, 80));

        Assert.NotNull(plan);
        var window = plan.Value.Window;
        Assert.True(window.Y >= WorkArea.Y);
        Assert.True(window.Y + window.Height <= WorkArea.Y + WorkArea.Height);
    }

    // ── 悬停抽屉：固定向下 ──────────────────────────────────────────────────

    private static MiniPlayerPanelPlan? Drawer(WindowPlacement bar, WindowPlacement? workArea = null)
        => MiniPlayerGeometry.PlanDrawer(bar, workArea ?? WorkArea, 36, 0, 28);

    [Fact]
    public void Drawer_PlentyOfRoom_UsesWantedHeight()
    {
        var plan = Drawer(new(400, 300, 420, 80));

        Assert.NotNull(plan);
        Assert.Equal(MiniPlayerPanelSide.Down, plan.Value.Side);
        Assert.Equal(36, plan.Value.PanelHeight);
        Assert.Equal(new WindowPlacement(400, 300, 420, 80 + 36), plan.Value.Window);
    }

    [Fact]
    public void Drawer_BarNearTheBottom_StillOpensDownAtReducedHeight()
    {
        // 下方只剩 800 + 50 - 818 = 32 —— 够 28 的下限，所以照弹，只是矮一点；
        // **绝不能翻到上面去**。
        var plan = Drawer(new(400, 738, 420, 80));

        Assert.NotNull(plan);
        Assert.Equal(MiniPlayerPanelSide.Down, plan.Value.Side);
        Assert.Equal(32, plan.Value.PanelHeight);
        Assert.Equal(new WindowPlacement(400, 738, 420, 80 + 32), plan.Value.Window);
    }

    [Fact]
    public void Drawer_NoRoomBelow_ReturnsNull()
    {
        // 条已经贴住工作区底边，下方一点空间都没有。
        Assert.Null(Drawer(new(400, 770, 420, 80)));
    }

    [Theory]
    [InlineData(300, 36)]
    [InlineData(738, 32)]
    public void Drawer_KeepsBarPositionAndWidth(int barY, int expectedHeight)
    {
        var bar = new WindowPlacement(400, barY, 420, 80);
        var plan = Drawer(bar);

        Assert.NotNull(plan);
        Assert.Equal(bar.X, plan.Value.Window.X);
        Assert.Equal(bar.Y, plan.Value.Window.Y);
        Assert.Equal(bar.Width, plan.Value.Window.Width);
        Assert.Equal(expectedHeight, plan.Value.PanelHeight);
    }

}
