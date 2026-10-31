namespace Bowen.Core.Media;

/// <summary>氛围色要适配的主题。暗色与亮色使用不同的目标明度区间。</summary>
public enum AmbientTheme
{
    /// <summary>浅色主题：偏高明度、低彩度，作为浅底上的柔和色晕。</summary>
    Light = 0,

    /// <summary>深色主题：抬高到仍能读出彩色，同时避免在深底上发灰。</summary>
    Dark = 1,
}
