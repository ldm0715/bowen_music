namespace Bodian.Core.Models;

/// <summary>客户端支持的三个明文产品档位；请求参数来自曲目的 AudioVariants。</summary>
public enum AudioQuality
{
    Standard = 0,
    High = 1,
    Lossless = 2,
}
