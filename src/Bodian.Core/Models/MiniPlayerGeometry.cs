namespace Bodian.Core.Models;

/// <summary>
/// 小窗的贴边收起与队列面板方向。全是纯函数，单位为物理像素。
/// </summary>
/// <remarks>
/// <para>
/// <b>收起是「改宽度」，不是「把窗口推出屏幕」。</b> 窗口被推到屏幕外时露出来的那一条
/// 是窗口自己的边缘内容（贴左时是右侧的按钮列，贴右时是封面左沿），既不是设计过的把手，
/// 切口也是硬边；而且负坐标要参与所有几何运算，部分多屏与 DPI 组合下系统还会钳制完全
/// 移出工作区的窗口。收成窄条则只是把宽度改小、贴边那一侧不动，几何全是纯函数。
/// </para>
/// <para>
/// <b>贴边判据用「外边缘与工作区边重合」，不另立阈值。</b>
/// <see cref="DesktopLyricsWindowGeometry.Constrain"/> 已经在拖动过程中把窗口吸到边缘，
/// 所以「松手时重合」等价于「松手时距边缘不超过吸附距离」—— 复用一个已有常量，
/// 不引入第二个魔法数，也不会出现「看着贴上了却没收起」的错位。
/// </para>
/// </remarks>
public static class MiniPlayerGeometry
{
    /// <summary>内容条贴了哪条边。上下贴边不算，返回 <see cref="MiniPlayerDockEdge.None"/>。</summary>
    public static MiniPlayerDockEdge EdgeAt(WindowPlacement bar, WindowPlacement workArea)
    {
        if (bar.X == workArea.X)
        {
            return MiniPlayerDockEdge.Left;
        }

        return bar.X + bar.Width == workArea.X + workArea.Width
            ? MiniPlayerDockEdge.Right
            : MiniPlayerDockEdge.None;
    }

    /// <summary>把展开的条收成贴边的窄条。贴边那一侧与高度都不动。</summary>
    public static WindowPlacement Collapse(WindowPlacement bar, MiniPlayerDockEdge edge, int collapsedWidth)
    {
        var width = Math.Max(1, collapsedWidth);

        return edge switch
        {
            MiniPlayerDockEdge.Left => bar with { Width = width },
            MiniPlayerDockEdge.Right => bar with { X = bar.X + bar.Width - width, Width = width },
            _ => bar,
        };
    }

    /// <summary>从窄条恢复成完整宽度，并把贴边那一侧重新对齐到工作区边。</summary>
    public static WindowPlacement Expand(WindowPlacement bar, MiniPlayerDockEdge edge, int width, WindowPlacement workArea)
    {
        var restored = Math.Max(1, width);

        return edge switch
        {
            MiniPlayerDockEdge.Left => bar with { X = workArea.X, Width = restored },
            MiniPlayerDockEdge.Right => bar with { X = workArea.X + workArea.Width - restored, Width = restored },
            _ => bar with { Width = restored },
        };
    }

    /// <summary>
    /// 唤起区：贴边侧工作区边缘往内 <paramref name="band"/>、纵向覆盖条高并向上下各放宽
    /// <paramref name="slack"/>。
    /// </summary>
    /// <remarks>
    /// <b>不用整条屏幕边做热区</b> —— 那会让鼠标一碰屏幕边缘就弹窗，用户去点别的应用的
    /// 边缘按钮时会疯。窄条本身可见，所以只覆盖它那一段高度就够用户找到；纵向放宽是给手抖留余量。
    /// 未贴边时返回零宽矩形，<see cref="Contains"/> 永远为假。
    /// </remarks>
    public static WindowPlacement WakeZone(WindowPlacement bar, WindowPlacement workArea, MiniPlayerDockEdge edge, int band, int slack)
    {
        var y = bar.Y - Math.Max(0, slack);
        var height = bar.Height + (2 * Math.Max(0, slack));

        return edge switch
        {
            MiniPlayerDockEdge.Left => new WindowPlacement(workArea.X, y, Math.Max(0, band), height),
            MiniPlayerDockEdge.Right => new WindowPlacement(
                workArea.X + workArea.Width - Math.Max(0, band), y, Math.Max(0, band), height),
            _ => new WindowPlacement(bar.X, y, 0, height),
        };
    }

    /// <summary>点是否落在矩形内。右边界与下边界不算在内，与光标轮询的判据一致。</summary>
    public static bool Contains(WindowPlacement rect, int x, int y)
        => x >= rect.X && x < rect.X + rect.Width
            && y >= rect.Y && y < rect.Y + rect.Height;

    /// <summary>
    /// 唤起闩锁。返回本帧要不要唤起，以及下一帧的 <c>armed</c>。
    /// </summary>
    /// <remarks>
    /// <b>没有它就会抖。</b> 收起的那一刻光标往往还停在唤起区里，下一帧立刻又满足唤起条件，
    /// 于是「收起 → 展开 → 收起」反复。贴边收起时把 <c>armed</c> 置 false，光标<b>离开唤起区
    /// 一次</b>之后才重新武装 —— 收起后必须先把鼠标挪开再回来，才唤得起。
    /// </remarks>
    public static (bool Wake, bool Armed) WakeTick(bool armed, bool cursorInWakeZone)
        => (armed && cursorInWakeZone, armed || !cursorInWakeZone);

    /// <summary>
    /// 在 <paramref name="bar"/> 不变的前提下，算队列面板朝哪边开、窗口变成多大。
    /// </summary>
    /// <param name="panelHeight">面板理想高度。</param>
    /// <param name="gap">条与面板之间的透明间隙。</param>
    /// <param name="minimumPanelHeight">两侧都放不下时可接受的面板下限；压缩后仍小于它就返回 <c>null</c>（这一下不打开）。</param>
    /// <returns>没地方开面板时返回 <c>null</c>。</returns>
    public static MiniPlayerPanelPlan? PlanPanel(
        WindowPlacement bar,
        WindowPlacement workArea,
        int panelHeight,
        int gap,
        int minimumPanelHeight)
    {
        var spacing = Math.Max(0, gap);
        var wanted = Math.Max(1, panelHeight);
        var needed = wanted + spacing;

        var below = workArea.Y + workArea.Height - (bar.Y + bar.Height);
        var above = bar.Y - workArea.Y;

        if (below >= needed)
        {
            return new MiniPlayerPanelPlan(
                MiniPlayerPanelSide.Down,
                bar with { Height = bar.Height + needed },
                wanted);
        }

        if (above >= needed)
        {
            return new MiniPlayerPanelPlan(
                MiniPlayerPanelSide.Up,
                bar with { Y = bar.Y - needed, Height = bar.Height + needed },
                wanted);
        }

        // 两边都不够：选空间大的那边，把面板压到该侧可用高度。
        var room = Math.Max(below, above);
        if (room - spacing < Math.Max(1, minimumPanelHeight))
        {
            return null;
        }

        var shrunk = room - spacing;

        return below >= above
            ? new MiniPlayerPanelPlan(MiniPlayerPanelSide.Down, bar with { Height = bar.Height + room }, shrunk)
            : new MiniPlayerPanelPlan(
                MiniPlayerPanelSide.Up,
                bar with { Y = bar.Y - room, Height = bar.Height + room },
                shrunk);
    }

    /// <summary>
    /// 悬停抽屉的方案：**固定向下**，下方放不下整个抽屉时把抽屉压到可用高度。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="PlanPanel"/> 只差一条：队列面板看两边空间挑方向，悬停抽屉永远是向下的。
    /// 所以这里不做方向判定，只做「向下 + 不越出工作区」，并且同样保证条的屏幕位置不动 ——
    /// 窗口只是向下长高，抽屉矮一点而已，不会把条顶走。
    /// </remarks>
    /// <param name="drawerHeight">抽屉理想高度。</param>
    /// <param name="gap">条与抽屉之间的透明间隙。</param>
    /// <param name="minimumDrawerHeight">压到这个高度以下就不值得弹了，返回 <c>null</c>。</param>
    /// <returns>下方连最小高度都放不下时返回 <c>null</c>。</returns>
    public static MiniPlayerPanelPlan? PlanDrawer(
        WindowPlacement bar,
        WindowPlacement workArea,
        int drawerHeight,
        int gap,
        int minimumDrawerHeight)
    {
        var spacing = Math.Max(0, gap);
        var wanted = Math.Max(1, drawerHeight);

        var below = workArea.Y + workArea.Height - (bar.Y + bar.Height);
        var available = below - spacing;

        if (available < Math.Max(1, minimumDrawerHeight))
        {
            return null;
        }

        var height = Math.Min(wanted, available);

        return new MiniPlayerPanelPlan(
            MiniPlayerPanelSide.Down,
            bar with { Height = bar.Height + spacing + height },
            height);
    }
}
