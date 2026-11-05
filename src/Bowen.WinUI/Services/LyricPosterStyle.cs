using Windows.UI;

namespace Bowen.WinUI.Services;

/// <summary>
/// 一张海报的视觉样式。颜色已经解析成最终值，渲染器不再关心「自动 / 预设 / 自定义」。
/// </summary>
/// <param name="FontFamily">歌词字体族；空串表示海报默认字体。</param>
/// <param name="Background">整张卡片的背景色。</param>
/// <param name="TextColor">正文、歌名与品牌文字的颜色。</param>
/// <param name="UseLightLogo">深色背景使用浅色品牌图标。</param>
public sealed record LyricPosterStyle(
    string FontFamily,
    Color Background,
    Color TextColor,
    bool UseLightLogo)
{
    public static LyricPosterStyle Default { get; } = new(
        string.Empty,
        Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
        Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A),
        UseLightLogo: false);
}
