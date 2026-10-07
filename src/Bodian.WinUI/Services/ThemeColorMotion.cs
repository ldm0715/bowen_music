using System.Numerics;

namespace Bodian.WinUI.Services;

/// <summary>正弦缓动采样，用于过渡遮罩；不直接写入任何主题资源。</summary>
internal static class ThemeColorMotion
{
    public static Vector4 Sample(Vector4 from, Vector4 to, double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        var eased = (float)((1 - Math.Cos(t * Math.PI)) / 2);
        return Vector4.Lerp(from, to, eased);
    }
}
