namespace Bowen.Core.Models;

/// <summary>
/// 界面字体的偏好。
/// </summary>
/// <remarks>
/// <para>
/// <b>单独一份文件（font-settings.json），不与主题、歌词混在一起。</b>
/// 主题管深浅色，歌词管排版内容，字体管的是「用什么字」——三者改的频次与失败影响都不同，
/// 挤进一个文件只会让读写互相牵制。
/// </para>
/// <para>
/// <b>只记字体族名，不记字号。</b> 字号是布局，随窗口尺寸与 DPI 走；
/// 字体族是「这个人看着顺眼」的选择，值得记住。
/// </para>
/// <para>
/// <b>空字符串 = 系统默认。</b> 这样「恢复默认」不需要存一个魔法值，
/// 缺省文件、显式选默认、手滑清空三种情况都收敛到同一个行为。
/// </para>
/// </remarks>
public sealed record FontSettings
{
    /// <summary>
    /// 界面使用的字体族名；空字符串表示跟随系统默认（Segoe UI Variable Text）。
    /// </summary>
    public string FontFamily { get; set; } = string.Empty;

    /// <summary>默认设置：跟随系统。不是 JSON 字段。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public static FontSettings Default { get; } = new();

    /// <summary>把越界的值收进合法范围。</summary>
    public FontSettings Normalized() => this with { FontFamily = FontFamily?.Trim() ?? string.Empty };
}
