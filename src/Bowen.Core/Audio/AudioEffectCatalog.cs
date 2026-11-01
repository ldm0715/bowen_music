using System.Globalization;

namespace Bowen.Core.Audio;

/// <summary>音效页签。环境音不在这里，它是独立的一层。</summary>
public enum AudioEffectCategory
{
    Galaxy = 0,
    Viper = 1,
}

/// <summary>一条命名音效预设。</summary>
/// <param name="Id">持久化用的稳定 id，不要改。</param>
/// <param name="FilterChain">mpv <c>af=lavfi=[...]</c> 里的滤镜片段，逗号分隔。</param>
public sealed record AudioEffectPreset(
    string Id,
    AudioEffectCategory Category,
    string Name,
    string Description,
    string FilterChain);

/// <summary>环境音的自然类型，生成器按它选合成方式。</summary>
public enum AmbientSoundKind
{
    Fireplace,
    Waves,
    Birdsong,
    Crowds,
    Forest,
    Water,
    Bell,
    RunningWater,
    Second,
    Heartbeat,
    Fireworks,
}

/// <summary>一种环境音的展示信息。</summary>
public sealed record AmbientSoundDefinition(
    string Id,
    string Name,
    string Description,
    AmbientSoundKind Kind);

/// <summary>
/// 内置音效预设与环境音目录。
/// </summary>
/// <remarks>
/// <para>
/// <b>这些曲线是本项目自己写的，不是从手机端预设或闭源引擎里抄的。</b>
/// 手机端的银河/蝰蛇预设由原生 DSP 包处理，没有可移植的参数文件；这里用 FFmpeg
/// 滤镜做听感近似，名字只用来帮用户认出「大概是那一类」。
/// </para>
/// <para>
/// 滤镜片段不能带 <c>lavfi=[]</c> 外壳，由 <see cref="AudioEffectGraphBuilder"/> 统一包。
/// </para>
/// </remarks>
public static class AudioEffectCatalog
{
    /// <summary>均衡器段数。</summary>
    public const int EqBandCount = 10;

    /// <summary>十段标准倍频程中心频率。</summary>
    public static readonly IReadOnlyList<int> EqBandFrequencies =
        [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>蝰蛇页签的预设。</summary>
    public static IReadOnlyList<AudioEffectPreset> ViperPresets { get; } =
    [
        new("viper.3d", AudioEffectCategory.Viper, "3D环绕", "拉开左右声场，人声仍留在中间",
            "stereotools=slev=1.6:mlev=0.92,haas=side_gain=0.18"),
        new("viper.bass", AudioEffectCategory.Viper, "超重低音", "低频增强，节奏更有冲击力",
            "virtualbass=cutoff=130:strength=2.2,asubboost=dry=0.9:wet=0.35:decay=0.4:feedback=0.6:cutoff=100"),
        new("viper.vocal", AudioEffectCategory.Viper, "纯净人声", "压低低频，人声干净靠前",
            "highpass=f=90,equalizer=f=250:t=q:w=1.2:g=-3,equalizer=f=2600:t=q:w=1.1:g=4,equalizer=f=5600:t=q:w=1:g=2,acompressor=threshold=0.08:ratio=3:attack=8:release=180"),
        new("viper.live", AudioEffectCategory.Viper, "虚拟现场", "短混响加声场展宽，像在小型现场",
            "aecho=0.8:0.7:40|70:0.3|0.2,stereotools=slev=1.3:mlev=0.95"),
        new("viper.eq", AudioEffectCategory.Viper, "均衡器音效", "温和的 V 形曲线，低频和高频略抬",
            BuildEqChain([3, 2, 1, 0, -1, -1, 0, 1, 2, 3], preampDb: -3)),
        new("viper.ai", AudioEffectCategory.Viper, "AI智能音效", "动态响度与中频增强（近似，不是模型）",
            "dynaudnorm=f=150,acompressor=threshold=0.1:ratio=2.5:attack=15:release=250,equalizer=f=2500:t=q:w=1:g=1.5"),
        new("viper.hifi", AudioEffectCategory.Viper, "一键HiFi", "两端轻抬并加限幅，整体更饱满",
            "equalizer=f=60:t=q:w=1:g=2,equalizer=f=12000:t=q:w=1:g=2,alimiter=limit=0.95"),
    ];

    /// <summary>银河页签的预设。</summary>
    public static IReadOnlyList<AudioEffectPreset> GalaxyPresets { get; } =
    [
        new("galaxy.clear", AudioEffectCategory.Galaxy, "清澈人声", "中频清晰，齿音不刺",
            "highpass=f=80,equalizer=f=300:t=q:w=1.2:g=-2,equalizer=f=3000:t=q:w=1.1:g=3,equalizer=f=7000:t=q:w=1:g=1.5"),
        new("galaxy.bass", AudioEffectCategory.Galaxy, "重低音", "低频托底，高频同步收一点",
            "bass=g=9:f=90:w=0.7,asubboost=dry=0.85:wet=0.4:decay=0.35:feedback=0.55:cutoff=90"),
        new("galaxy.3d", AudioEffectCategory.Galaxy, "3D环绕", "更宽的声场，适合耳机",
            "stereotools=slev=1.8:mlev=0.86,haas=side_gain=0.22"),
        new("galaxy.vinyl", AudioEffectCategory.Galaxy, "黑胶", "收高频并加一点短反射，模拟唱片感",
            "highpass=f=60,lowpass=f=11000,equalizer=f=300:t=q:w=1:g=-1,aecho=0.85:0.6:18:0.12"),
        new("galaxy.live", AudioEffectCategory.Galaxy, "LiveHouse", "中等混响，人声和乐器有厅堂感",
            "aecho=0.8:0.7:50|90:0.25|0.18,stereotools=slev=1.4:mlev=0.9"),
        new("galaxy.night", AudioEffectCategory.Galaxy, "夜晚", "压高频，长时间听不累",
            "lowpass=f=12000,equalizer=f=250:t=q:w=1:g=1,equalizer=f=8000:t=q:w=1:g=-2"),
        new("galaxy.wide", AudioEffectCategory.Galaxy, "开阔宽音", "左右展宽，声像更靠外",
            "stereowiden=delay=22:feedback=0.25:crossfeed=0.2:drymix=0.85"),
        new("galaxy.car", AudioEffectCategory.Galaxy, "车载", "低频和中频略抬，适合车内环境",
            "equalizer=f=100:t=q:w=1:g=2,equalizer=f=3000:t=q:w=1:g=1,stereotools=slev=1.2:mlev=1"),
    ];

    /// <summary>环境音目录，顺序与手机端一致。</summary>
    public static IReadOnlyList<AmbientSoundDefinition> AmbientSounds { get; } =
    [
        new("ambient.fireplace", "壁炉", "木柴燃烧的噼啪声", AmbientSoundKind.Fireplace),
        new("ambient.waves", "海浪", "缓慢起伏的海浪", AmbientSoundKind.Waves),
        new("ambient.birdsong", "鸟鸣", "林间的鸟叫", AmbientSoundKind.Birdsong),
        new("ambient.crowds", "人潮", "远处模糊的人声", AmbientSoundKind.Crowds),
        new("ambient.forest", "森林", "风穿过树林", AmbientSoundKind.Forest),
        new("ambient.water", "水滴", "零散的水滴声", AmbientSoundKind.Water),
        new("ambient.bell", "风铃", "偶尔响起的风铃", AmbientSoundKind.Bell),
        new("ambient.running_water", "流水", "持续的溪流", AmbientSoundKind.RunningWater),
        new("ambient.second", "秒针", "清晰的钟表秒针", AmbientSoundKind.Second),
        new("ambient.heartbeat", "心跳", "缓慢的双拍心跳", AmbientSoundKind.Heartbeat),
        new("ambient.fireworks", "烟火", "远处的爆裂与回响", AmbientSoundKind.Fireworks),
    ];

    /// <summary>全部预设，页签顺序为银河在前、蝰蛇在后。</summary>
    public static IReadOnlyList<AudioEffectPreset> AllPresets { get; } =
        [.. GalaxyPresets, .. ViperPresets];

    /// <summary>按 id 找预设，大小写不敏感。</summary>
    public static AudioEffectPreset? FindPreset(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : AllPresets.FirstOrDefault(preset =>
                string.Equals(preset.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>按 id 找环境音，大小写不敏感。</summary>
    public static AmbientSoundDefinition? FindAmbient(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : AmbientSounds.FirstOrDefault(sound =>
                string.Equals(sound.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>按页签取预设列表。</summary>
    public static IReadOnlyList<AudioEffectPreset> PresetsFor(AudioEffectCategory category) =>
        category == AudioEffectCategory.Viper ? ViperPresets : GalaxyPresets;

    /// <summary>
    /// 把十段增益拼成 <c>equalizer</c> 链；全零返回空串。
    /// </summary>
    /// <param name="bands">按 <see cref="EqBandFrequencies"/> 排列的增益，单位 dB。</param>
    /// <param name="preampDb">链首的 <c>volume</c> 增益，抬曲线时用来留 headroom。</param>
    public static string BuildEqChain(IReadOnlyList<double> bands, double preampDb = 0)
    {
        ArgumentNullException.ThrowIfNull(bands);
        var stages = new List<string>(EqBandCount + 1);
        if (Math.Abs(preampDb) > 0.001)
        {
            stages.Add($"volume={Number(preampDb)}dB");
        }

        for (var index = 0; index < EqBandCount && index < bands.Count; index++)
        {
            var gain = bands[index];
            if (!double.IsFinite(gain) || Math.Abs(gain) < 0.05)
            {
                continue;
            }

            stages.Add(
                $"equalizer=f={EqBandFrequencies[index].ToString(CultureInfo.InvariantCulture)}" +
                $":t=q:w=1.2:g={Number(Math.Clamp(gain, -12, 12))}");
        }

        return string.Join(',', stages);
    }

    /// <summary>滤镜参数里的数字统一用点号小数，避免中文区域设置写出逗号。</summary>
    internal static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
