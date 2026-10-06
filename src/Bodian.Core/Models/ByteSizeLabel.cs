using System.Globalization;

namespace Bodian.Core.Models;

/// <summary>
/// 把字节数写成人看的大小。
/// </summary>
/// <remarks>
/// 放在 Core 是为了能离屏单测：四舍五入与进位边界（1023 / 1024、999.95 KB）最容易写错，
/// 而设置页上显示成「1024.0 KB」或「0.0 GB」这种数字一眼就能看出不对。
/// </remarks>
public static class ByteSizeLabel
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>如 <c>12.3 MB</c>。<b>不足 1 KB 时按整数 B 显示</b>，不写小数。</summary>
    public static string Format(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        double value = bytes;
        var unit = 0;

        // 用 1024 而不是 1000：与资源管理器口径一致，用户对得上号。
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        if (unit == 0)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{bytes} B");
        }

        // 四舍五入之后可能又够进一位：1048575 字节会算成 1023.999 KB，
        // 按一位小数显示就成了「1024 KB」—— 一眼假的数字。再进一次。
        if (Math.Round(value, 1) >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {Units[unit]}");
    }
}
