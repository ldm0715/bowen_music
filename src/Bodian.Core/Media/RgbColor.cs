namespace Bodian.Core.Media;

/// <summary>
/// 一个不带透明度的颜色。
/// </summary>
/// <remarks>
/// <b>刻意不用 <c>Windows.UI.Color</c> 或 <c>System.Drawing.Color</c></b>：
/// 前者会把 <c>Bodian.Core</c> 拖进 Windows 依赖（那个项目是纯 <c>net10.0</c>，
/// 协议与歌词逻辑全靠它可单测），后者在 .NET 上已经不是跨平台的了。
/// 自己写一个三字节的结构，两边都能用。
/// </remarks>
/// <param name="R">红。</param>
/// <param name="G">绿。</param>
/// <param name="B">蓝。</param>
public readonly record struct RgbColor(byte R, byte G, byte B);
