namespace Bodian.Core.Models;

/// <summary>小窗收起后贴在哪条屏幕边上。</summary>
/// <remarks>
/// 只有左右。上下贴边不收起 —— 横条小窗竖着藏起来既不好看也不好唤起。
/// </remarks>
public enum MiniPlayerDockEdge
{
    /// <summary>没贴边，完整小窗。</summary>
    None,

    /// <summary>贴在当前显示器工作区的左边。</summary>
    Left,

    /// <summary>贴在当前显示器工作区的右边。</summary>
    Right,
}

/// <summary>播放队列面板相对小窗内容条的展开方向。</summary>
public enum MiniPlayerPanelSide
{
    /// <summary>面板在条下方，窗口向下长高。</summary>
    Down,

    /// <summary>面板在条上方，窗口上边缘上移。条本身的屏幕位置不变。</summary>
    Up,
}

/// <summary>队列面板的一次展开方案。</summary>
/// <param name="Side">面板朝哪边展开。</param>
/// <param name="Window">展开后小窗窗口的完整矩形（含条与面板）。</param>
/// <param name="PanelHeight">面板分到的高度，可能被可用空间压小。</param>
public readonly record struct MiniPlayerPanelPlan(
    MiniPlayerPanelSide Side,
    WindowPlacement Window,
    int PanelHeight);
