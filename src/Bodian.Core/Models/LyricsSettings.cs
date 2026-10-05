namespace Bodian.Core.Models;

/// <summary>
/// 歌词页的偏好。
/// </summary>
/// <remarks>
/// <para>
/// <b>与 <see cref="DesktopLyricsSettings"/> 分开一份文件</b>：那份管桌面歌词条的外观，
/// 这份管全屏歌词页显示什么 —— 不同界面的偏好挤进一个文件，只会让读写互相牵制。
/// </para>
/// <para>
/// <b>译文不是单独一条轨。</b> 外文歌那份歌词内容里原文与译文成对出现，解析时就分好了
/// （<see cref="Lyrics.LyricLine.IsTranslation"/>）。这里的开关只决定要不要把它们
/// 从文档里剔掉，<b>不产生任何网络请求</b>。
/// </para>
/// </remarks>
public sealed record LyricsSettings
{
    /// <summary>
    /// 歌词页是否显示译文。
    /// </summary>
    /// <remarks>
    /// <b>默认开。</b> 译文本来就在歌词内容里，客户端此前一直显示着；
    /// 这个开关的意义是给用户一个关掉它的办法，不是新增一种显示。
    /// </remarks>
    public bool ShowTranslation { get; init; } = true;

    /// <summary>默认设置：显示译文。</summary>
    public static LyricsSettings Default { get; } = new();

    /// <summary>
    /// 把越界的值收进合法范围。
    /// </summary>
    /// <remarks>
    /// 目前只有一个布尔项，没有可越界的取值。方法保留是为了与其它设置同一个形状 ——
    /// 以后加字号之类的项时，不必再去改 <c>JsonLyricsSettingsStore</c> 的调用点。
    /// </remarks>
    public LyricsSettings Normalized() => this;
}
