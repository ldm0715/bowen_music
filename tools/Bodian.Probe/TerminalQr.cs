using System.Text;
using QRCoder;

namespace Bodian.Probe;

/// <summary>把二维码直接画在终端里——扫码登录只在终端完成，落 PNG 再打开是多余的一步。</summary>
internal static class TerminalQr
{
    private const string Dark = "██";
    private const string Light = "  ";
    private const char DarkChar = '█';
    private const char Full = '█';
    private const char UpperHalf = '▀';
    private const char LowerHalf = '▄';
    private const char Blank = ' ';

    /// <summary>
    /// 模块矩阵转文本这一步交给 QRCoder 自己做，只在它的输出上做半块压缩。
    /// 不自己遍历 <c>QRCodeData.ModuleMatrix</c>：行列读反会得到转置的二维码，
    /// 三个定位角的位置看起来仍然正常，但扫不出来——这种错很难靠肉眼发现。
    /// </summary>
    /// <remarks>
    /// 不用 <c>GetGraphicSmall</c>：它按浅色终端背景设计，静默区会被画成实心块，也没法指定深浅字符。
    /// 压缩是每两行合成一行，上黑下白用 <c>▀</c>、上白下黑用 <c>▄</c>，行数减半以适配终端的宽高比。
    /// </remarks>
    public static void Draw(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);

        var text = new AsciiQRCode(data).GetGraphic(
            repeatPerModule: 1,
            darkColorString: Dark,
            whiteSpaceString: Light,
            drawQuietZones: true);

        var lines = text.TrimEnd('\n').Split('\n');
        var width = lines[0].Length;

        for (var y = 0; y < lines.Length; y += 2)
        {
            var top = lines[y];

            // 行数为奇数时最后一行没有下半个，补一行空白，静默区少半行不影响扫码。
            var bottom = y + 1 < lines.Length ? lines[y + 1] : new string(Blank, width);

            var compact = new StringBuilder(width / 2);

            for (var x = 0; x < width; x += 2)
            {
                compact.Append((top[x] == DarkChar, bottom[x] == DarkChar) switch
                {
                    (true, true) => Full,
                    (true, false) => UpperHalf,
                    (false, true) => LowerHalf,
                    _ => Blank,
                });
            }

            Console.WriteLine(compact.ToString());
        }
    }
}
