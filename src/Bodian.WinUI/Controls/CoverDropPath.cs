using System.Numerics;

namespace Bodian.WinUI.Controls;

/// <summary>横向先靠近播放条、纵向逐渐加速下坠；端点严格落在封面中心。</summary>
internal static class CoverDropPath
{
    public static Vector2 Translation(Vector2 displacement, float progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        var horizontal = 1 - MathF.Pow(1 - t, 3);
        var vertical = t * t;
        return new Vector2(displacement.X * horizontal, displacement.Y * vertical);
    }

    public static Vector3 Scale(Vector2 targetScale, float progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        var eased = t * t * (3 - 2 * t);
        var size = Vector2.Lerp(Vector2.One, targetScale, eased);
        return new Vector3(size, 1);
    }
}
