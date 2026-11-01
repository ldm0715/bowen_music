using System.Text;
using Bowen.Core.Models;

namespace Bowen.Core.Audio;

/// <summary>
/// 把音效设置编译成 mpv 的 <c>af</c> 属性字符串。
/// </summary>
/// <remarks>
/// <para>
/// <b>纯函数，不碰 mpv，也不碰文件。</b> 环境音文件由调用方先生成好再把路径传进来 ——
/// <c>amovie</c> 在 mpv 解析滤镜图时就会打开文件，文件不存在会让**整条</b> <c>af</c> 被拒绝，
/// EQ 和预设也一起消失。
/// </para>
/// <para>
/// 预设与环境音同时存在时，合成一张 <c>lavfi</c> 图：主音频先过一个 <c>anull</c> 拿到标签，
/// 再串预设滤镜；环境音用 <c>amovie</c> 当 source，最后 <c>amix</c> 合并。
/// 这正是不带额外输入 pad 的 <c>af=lavfi=[...]</c> 能接受的形状。
/// </para>
/// </remarks>
public static class AudioEffectGraphBuilder
{
    /// <summary><c>af-command</c> 用来定位环境音混音器的标签。</summary>
    public const string FilterLabel = "bowenFx";

    /// <summary>
    /// 构建 <c>af</c> 值；没有任何可生效的效果时返回 <c>null</c>（调用方应清空 <c>af</c>）。
    /// </summary>
    /// <param name="settings">已归一化的设置。</param>
    /// <param name="ambientFilePath">已存在的环境音文件；没有环境音时传 <c>null</c>。</param>
    public static string? Build(AudioEffectSettings settings, string? ambientFilePath)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Enabled)
        {
            return null;
        }

        var preset = AudioEffectCatalog.FindPreset(settings.PresetId);
        var ambient = AudioEffectCatalog.FindAmbient(settings.AmbientId);
        var hasAmbient = ambient is not null && !string.IsNullOrWhiteSpace(ambientFilePath);

        if (preset is null && !hasAmbient)
        {
            return null;
        }

        var chain = preset?.FilterChain;
        if (!hasAmbient)
        {
            return string.IsNullOrWhiteSpace(chain) ? null : $"@{FilterLabel}:lavfi=[{chain}]";
        }

        var main = string.IsNullOrWhiteSpace(chain)
            ? "anull[main]"
            : $"anull[main];[main]{chain}[fx]";
        var mainOutput = string.IsNullOrWhiteSpace(chain) ? "[main]" : "[fx]";
        var graph =
            $"{main};" +
            $"amovie={EscapeFilterPath(ambientFilePath!)}:loop=0,asetpts=N/SR/TB[amb];" +
            $"{mainOutput}[amb]amix=inputs=2:weights={AmbientWeights(settings.AmbientVolume)}:normalize=0";

        return $"@{FilterLabel}:lavfi=[{graph}]";
    }

    /// <summary>
    /// <c>amix</c> 的 weights 值：主音频恒为 1，环境音按 0–100 映射到 0–1。
    /// </summary>
    /// <remarks>
    /// 同一个格式同时给初始图和 <c>af-command</c> 用，滑杆变化时只改权重、不重建滤镜图。
    /// </remarks>
    public static string AmbientWeights(double ambientVolume)
    {
        var wet = double.IsFinite(ambientVolume)
            ? Math.Clamp(ambientVolume / 100d, 0, 1)
            : AudioEffectSettings.DefaultAmbientVolume / 100d;
        return $"1 {AudioEffectCatalog.Number(wet)}";
    }

    /// <summary>
    /// 转义 <c>amovie</c> 的路径。
    /// </summary>
    /// <remarks>
    /// 路径要穿过两层解析：滤镜图解析器吃一层反斜杠，滤镜自己的选项解析器再吃一层。
    /// 所以 <c>:</c>（盘符）要写两个反斜杠，<c>[ ] , ;</c> 写一个，单引号写三个。
    /// 反斜杠先统一成 <c>/</c>，否则每个分隔符都会被当成转义符。
    /// </remarks>
    internal static string EscapeFilterPath(string path)
    {
        var builder = new StringBuilder(path.Length + 16);
        foreach (var ch in path.Replace('\\', '/'))
        {
            switch (ch)
            {
                case '\'':
                    builder.Append("\\\\\\");
                    break;
                case ':' or '=':
                    builder.Append("\\\\");
                    break;
                case '[' or ']' or ',' or ';':
                    builder.Append('\\');
                    break;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
