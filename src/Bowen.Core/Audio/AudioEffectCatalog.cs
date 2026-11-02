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
/// <param name="TextureId">随预设循环叠加的纹理素材 id；没有纹理时为 <c>null</c>。</param>
public sealed record AudioEffectPreset(
    string Id,
    AudioEffectCategory Category,
    string Name,
    string Description,
    string FilterChain,
    string? TextureId = null);

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
    Rain,
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
/// 手机端的两组预设由原生 DSP 包处理，没有可移植的参数文件；桌面端的「经典」「增强」两页
/// 是按其效果方向自己调的，名字和曲线都归本项目所有，与手机端不再一一对应。
/// </para>
/// <para>
/// 滤镜片段不能带 <c>lavfi=[]</c> 外壳，由 <see cref="AudioEffectGraphBuilder"/> 统一包。
/// </para>
/// <para>
/// 调链条时注意三个会掉电平的坑：<c>aecho</c> 的 <c>out_gain</c> 连干声一起乘，
/// 要固定 <c>in_gain=1:out_gain=1</c>，只靠 <c>decays</c> 控制回声量；
/// <c>asubboost</c> 实测会掉 7–9 LU，需要次低频时改用 <c>virtualbass</c>；
/// <c>acompressor</c> 的阈值是线性的，<c>threshold=0.08</c> 等于 -22 dBFS，会压掉整首歌，
/// 阈值不低于 0.35 并用 <c>makeup</c> 补偿。抬增益的链条末尾统一加
/// <c>alimiter=limit=0.9:level=0</c>，避免削波，也不要开自动电平。
/// </para>
/// </remarks>
public static class AudioEffectCatalog
{
    /// <summary>黑胶底噪素材 id，随黑胶预设循环叠加。</summary>
    public const string VinylTextureId = "texture.vinyl";

    /// <summary>均衡器段数。</summary>
    public const int EqBandCount = 10;

    /// <summary>十段标准倍频程中心频率。</summary>
    public static readonly IReadOnlyList<int> EqBandFrequencies =
        [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>增强页签的预设。</summary>
    public static IReadOnlyList<AudioEffectPreset> ViperPresets { get; } =
    [
        new("viper.3d", AudioEffectCategory.Viper, "3D环绕", "相位展宽加长反射，声像推到耳机外",
            "stereotools=slev=2.1:mlev=0.82,haas=side_gain=0.8,aecho=1:1:71|127|191:0.12|0.09|0.06,volume=0.5dB,alimiter=limit=0.9:level=0"),
        new("viper.bass", AudioEffectCategory.Viper, "超重低音", "下潜与谐波低音叠加，节奏更重",
            "virtualbass=cutoff=180:strength=2.4,bass=g=7:f=70:w=0.5,equalizer=f=110:t=q:w=1:g=2,alimiter=limit=0.9:level=0"),
        new("viper.vocal", AudioEffectCategory.Viper, "纯净人声", "抬高中频并轻压动态，人声靠前",
            "highpass=f=70,equalizer=f=280:t=q:w=1.2:g=-3,equalizer=f=2600:t=q:w=1.1:g=4,equalizer=f=6000:t=q:w=1:g=2,acompressor=threshold=0.35:ratio=2.5:attack=10:release=200:makeup=1.1"),
        new("viper.live", AudioEffectCategory.Viper, "虚拟现场", "多抽头短反射加声场展宽，像在小型现场",
            "aecho=1:1:25|45|70:0.32|0.22|0.14,stereotools=slev=1.35:mlev=1,volume=1.5dB,alimiter=limit=0.92:level=0"),
        new("viper.eq", AudioEffectCategory.Viper, "均衡器音效", "温和的 V 形曲线，低频和高频略抬",
            BuildEqChain([3, 2, 1, 0, -1, -1, 0, 1, 2, 3], preampDb: -3)),
        new("viper.ai", AudioEffectCategory.Viper, "响度均衡", "动态响度归一，拉平不同歌曲的音量",
            "dynaudnorm=framelen=500:maxgain=3:targetrms=0.35:compress=2:threshold=0.05,equalizer=f=2500:t=q:w=1:g=1.5,alimiter=limit=0.92:level=0"),
        new("viper.hifi", AudioEffectCategory.Viper, "一键HiFi", "两端轻抬并加限幅，整体更饱满",
            "equalizer=f=60:t=q:w=1:g=2,equalizer=f=12000:t=q:w=1:g=2,alimiter=limit=0.95"),
    ];

    /// <summary>经典页签的预设。</summary>
    public static IReadOnlyList<AudioEffectPreset> GalaxyPresets { get; } =
    [
        new("galaxy.clear", AudioEffectCategory.Galaxy, "清澈人声", "保留低频，只抬中高频的通透感",
            "equalizer=f=300:t=q:w=1.2:g=-2,equalizer=f=3000:t=q:w=1.1:g=3,equalizer=f=9000:t=q:w=1:g=2.5,alimiter=limit=0.95:level=0"),
        new("galaxy.bass", AudioEffectCategory.Galaxy, "重低音", "低频托底并补谐波低音，响度不掉",
            "bass=g=6:f=80:w=0.6,equalizer=f=160:t=q:w=1:g=2,virtualbass=cutoff=140:strength=2.2,alimiter=limit=0.9:level=0"),
        new("galaxy.3d", AudioEffectCategory.Galaxy, "3D环绕", "相位展宽加长反射，声场开阔",
            "stereotools=slev=1.9:mlev=0.88,haas=side_gain=0.55,aecho=1:1:83|141|219:0.16|0.11|0.07,alimiter=limit=0.9:level=0"),
        new("galaxy.vinyl", AudioEffectCategory.Galaxy, "黑胶", "温暖偏暗的模拟味，叠一层炒豆底噪",
            "highpass=f=50,equalizer=f=120:t=q:w=0.7:g=3,equalizer=f=700:t=q:w=1.2:g=-1.5,lowpass=f=8500,acompressor=threshold=0.4:ratio=1.8:attack=25:release=180:makeup=1.2,stereotools=slev=0.9",
            TextureId: VinylTextureId),
        new("galaxy.live", AudioEffectCategory.Galaxy, "LiveHouse", "多抽头混响，人声和乐器有厅堂感",
            "aecho=1:1:35|60|90:0.22|0.16|0.1,stereotools=slev=1.4:mlev=0.95,volume=0.5dB,alimiter=limit=0.92:level=0"),
        new("galaxy.night", AudioEffectCategory.Galaxy, "夜晚", "压暗高频并轻压动态，小音量也耐听",
            "lowpass=f=7500,equalizer=f=120:t=q:w=1:g=2,equalizer=f=350:t=q:w=1:g=0.5,equalizer=f=6000:t=q:w=1.2:g=-3,acompressor=threshold=0.35:ratio=2:attack=20:release=250:makeup=1.15,stereotools=slev=0.85"),
        new("galaxy.wide", AudioEffectCategory.Galaxy, "开阔宽音", "左右展宽，声像更靠外",
            "stereowiden=delay=22:feedback=0.25:crossfeed=0.2:drymix=0.85"),
        new("galaxy.car", AudioEffectCategory.Galaxy, "车载", "补低频、提清晰度，抵消车内噪声遮蔽",
            "bass=g=6:f=110:w=0.7,equalizer=f=400:t=q:w=1.1:g=-2,equalizer=f=2800:t=q:w=1:g=3.5,equalizer=f=9000:t=q:w=1:g=2,stereotools=slev=1.25,alimiter=limit=0.95:level=0"),
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
        new("ambient.rain", "雨声", "雨点落在屋顶和地面", AmbientSoundKind.Rain),
    ];

    /// <summary>全部预设，页签顺序为经典在前、增强在后。</summary>
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
