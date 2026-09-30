namespace Bodian.Core.Models;

/// <summary>
/// 本项目能请求的音质档位。
/// </summary>
/// <remarks>
/// <para>
/// <b>只有三档，这是实测穷尽的结果。</b> 协议里 <c>audios[].level</c> 有十几个取值，但明文可播的
/// 只有 <c>s</c> / <c>h</c> / <c>p</c> / <c>ff</c>。其中 <c>h</c> 与 <c>p</c> 请求的是同一个
/// <c>br</c>（<c>320kmp3</c>），所以实际只有三档。
/// </para>
/// <para>
/// <c>hr</c> <b>不在这里</b>：实测请求 <c>4000kflac</c> 会降级到 128k mp3，它不是有效档位。
/// <c>zp*</c> / <c>bcms</c> 是加密容器，<c>ac4</c> / <c>dd*</c> / <c>dtsx</c> 需要授权解码器，
/// 均不在本项目范围。
/// </para>
/// </remarks>
public enum AudioQuality
{
    /// <summary>流畅：<c>128kmp3</c>。曲目 level 为 <c>s</c>。</summary>
    Standard = 0,

    /// <summary>高：<c>320kmp3</c>。曲目 level 为 <c>h</c> 或 <c>p</c>。</summary>
    High = 1,

    /// <summary>无损：<c>2000kflac</c>。曲目 level 为 <c>ff</c>，实测 24bit/44.1kHz。</summary>
    Lossless = 2,
}
