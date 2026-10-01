namespace Bodian.Core.Models;

/// <summary>
/// 上次关闭时的窗口位置与大小，单位是**物理像素**。
/// </summary>
/// <remarks>
/// <b>存物理像素而不是逻辑像素</b>：<c>AppWindow</c> 那套 API 用的就是物理像素，
/// 存成它要的形态可以原样还原、不做换算。代价是显示器缩放变过之后窗口的「视觉大小」
/// 会跟着变 —— 但位置仍然对得上，比按逻辑像素存、还原时换算错了好。
/// </remarks>
/// <param name="X">窗口左边缘的屏幕坐标。</param>
/// <param name="Y">窗口上边缘的屏幕坐标。</param>
/// <param name="Width">宽度。</param>
/// <param name="Height">高度。</param>
public sealed record WindowPlacement(int X, int Y, int Width, int Height);
