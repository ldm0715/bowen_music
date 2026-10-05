namespace Bodian.Core.Models;

/// <summary>在同一屏幕坐标系中限制完整窗口，并吸附可用区域的四个边缘。</summary>
public static class DesktopLyricsWindowGeometry
{
    public static WindowPlacement Constrain(WindowPlacement window, WindowPlacement workArea, int snapDistance = 0)
    {
        var width = Math.Clamp(window.Width, 1, Math.Max(1, workArea.Width));
        var height = Math.Clamp(window.Height, 1, Math.Max(1, workArea.Height));
        var right = workArea.X + Math.Max(0, workArea.Width - width);
        var bottom = workArea.Y + Math.Max(0, workArea.Height - height);
        var x = Math.Clamp(window.X, workArea.X, right);
        var y = Math.Clamp(window.Y, workArea.Y, bottom);
        var distance = Math.Max(0, snapDistance);
        if (x - workArea.X <= distance) x = workArea.X;
        else if (right - x <= distance) x = right;
        if (y - workArea.Y <= distance) y = workArea.Y;
        else if (bottom - y <= distance) y = bottom;
        return new WindowPlacement(x, y, width, height);
    }

    /// <summary>从按下时的右边缘拉伸；距离为物理像素，左上角和高度保持固定。</summary>
    public static WindowPlacement ResizeFromRight(WindowPlacement origin, int pointerDeltaX,
        int minimumWidth, int maximumWidth, WindowPlacement workArea, int snapDistance = 0)
    {
        var bounded = Constrain(origin, workArea);
        var available = Math.Max(1, workArea.X + Math.Max(1, workArea.Width) - bounded.X);
        var minimum = Math.Clamp(minimumWidth, 1, available);
        var maximum = Math.Clamp(maximumWidth, minimum, available);
        var width = (int)Math.Clamp((long)bounded.Width + pointerDeltaX, minimum, maximum);
        if (available <= maximum && available - width <= Math.Max(0, snapDistance)) width = available;
        return bounded with { Width = width };
    }

}
