using Bowen.Core.Services;

namespace Bowen.Core.Audio;

/// <summary>
/// 程序生成环境音循环。
/// </summary>
/// <remarks>
/// <para>
/// <b>不在仓库里放音频素材。</b> 手机端那 11 个 MP3 有版权，不能随公开 GPL 包分发；
/// 这里用简单的噪声/振荡器合成近似环境，首次选中时写到本地缓存，删掉会自动重建。
/// </para>
/// <para>
/// 输出是 22.05 kHz、16-bit、立体声的 12 秒无缝循环：多生成 2 秒尾巴，交叉淡入到开头，
/// 这样 <c>amovie</c> 循环播放时不会在接缝处听到“咔哒”。
/// </para>
/// </remarks>
public static class AmbientSoundGenerator
{
    /// <summary>输出采样率。环境音不需要 44.1 kHz，低一档能显著缩小缓存。</summary>
    public const int SampleRate = 22050;

    /// <summary>输出声道数。</summary>
    public const int Channels = 2;

    /// <summary>循环长度（秒）。</summary>
    public const int DurationSeconds = 12;

    private const int FadeSeconds = 2;
    private const float Peak = 0.55f;

    /// <summary>
    /// 确保环境音文件存在，返回路径。
    /// </summary>
    /// <param name="sound">要生成的环境音。</param>
    /// <param name="directory">缓存目录；默认 <see cref="AppPaths.AmbientSoundsDirectory"/>。</param>
    public static string Ensure(
        AmbientSoundDefinition sound,
        string? directory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sound);
        var folder = directory ?? AppPaths.AmbientSoundsDirectory;
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, sound.Id + ".wav");
        if (File.Exists(path) && new FileInfo(path).Length > 44)
        {
            return path;
        }

        var temporary = path + ".tmp";
        Generate(sound, temporary, cancellationToken);
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    /// <summary>生成到指定路径。测试直接调这个，不经过缓存判断。</summary>
    public static void Generate(
        AmbientSoundDefinition sound,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var outputFrames = SampleRate * DurationSeconds;
        var totalFrames = outputFrames + SampleRate * FadeSeconds;
        var left = new float[totalFrames];
        var right = new float[totalFrames];

        // 每个声音固定种子：同一台机器反复生成结果一致，缓存坏了重建也不会换个音色。
        var seed = unchecked((int)((uint)sound.Kind * 2654435761u) ^ 0x5F3759DF);
        var randomLeft = new Random(seed);
        var randomRight = new Random(unchecked(seed + 0x61C88647));

        switch (sound.Kind)
        {
            case AmbientSoundKind.Fireplace:
                Fireplace(left, right, randomLeft, randomRight, totalFrames);
                break;
            case AmbientSoundKind.Waves:
                Waves(left, right, randomLeft, randomRight, totalFrames);
                break;
            case AmbientSoundKind.Birdsong:
                Birdsong(left, right, randomLeft, randomRight, totalFrames);
                break;
            case AmbientSoundKind.Crowds:
                Crowds(left, right, randomLeft, randomRight, totalFrames);
                break;
            case AmbientSoundKind.Forest:
                Forest(left, right, randomLeft, randomRight, totalFrames);
                break;
            case AmbientSoundKind.Water:
                Water(left, right, randomLeft, randomRight, totalFrames);
                break;
            case AmbientSoundKind.Bell:
                Bell(left, right, randomLeft, randomRight, totalFrames);
                break;
            case AmbientSoundKind.RunningWater:
                RunningWater(left, right, randomLeft, randomRight, totalFrames);
                break;
            case AmbientSoundKind.Second:
                Second(left, right, randomLeft, randomRight, totalFrames);
                break;
            case AmbientSoundKind.Heartbeat:
                Heartbeat(left, right, totalFrames);
                break;
            case AmbientSoundKind.Fireworks:
                Fireworks(left, right, randomLeft, randomRight, totalFrames);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(sound));
        }

        cancellationToken.ThrowIfCancellationRequested();
        CrossfadeLoop(left, right, outputFrames, SampleRate * FadeSeconds);
        Normalize(left, right, outputFrames);
        WriteWave(path, left, right, outputFrames);
    }

    // ── 各种环境音 ──────────────────────────────────────────────────────────

    private static void Fireplace(float[] left, float[] right, Random rl, Random rr, int frames)
    {
        var lowL = new OnePole(0.004f);
        var lowR = new OnePole(0.004f);
        var crackleL = 0f;
        var crackleR = 0f;
        var nextCrackle = 0;
        for (var i = 0; i < frames; i++)
        {
            var baseL = lowL.Process(Noise(rl)) * 1.6f;
            var baseR = lowR.Process(Noise(rr)) * 1.6f;

            if (i >= nextCrackle)
            {
                crackleL = 0.35f + (float)rl.NextDouble() * 0.55f;
                crackleR = 0.35f + (float)rr.NextDouble() * 0.55f;
                nextCrackle = i + rl.Next(SampleRate / 30, SampleRate / 6);
            }

            left[i] = baseL + crackleL * Noise(rl) * 0.9f;
            right[i] = baseR + crackleR * Noise(rr) * 0.9f;
            crackleL *= 0.992f;
            crackleR *= 0.992f;
        }
    }

    private static void Waves(float[] left, float[] right, Random rl, Random rr, int frames)
    {
        var lowL = new OnePole(0.02f);
        var lowR = new OnePole(0.02f);
        var foamL = new OnePole(0.25f);
        var foamR = new OnePole(0.25f);
        const double baseHz = 0.07;
        for (var i = 0; i < frames; i++)
        {
            var t = i / (double)SampleRate;
            var swell = 0.35 + 0.65 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * baseHz * t));
            var swell2 = 0.5 + 0.5 * Math.Sin(2 * Math.PI * baseHz * 0.37 * t + 1.7);
            var envelope = (float)(swell * (0.6 + 0.4 * swell2));
            var bodyL = lowL.Process(Noise(rl)) * envelope;
            var bodyR = lowR.Process(Noise(rr)) * envelope;
            var foam = (float)(0.08 * Math.Pow(swell, 4));
            left[i] = bodyL * 2.2f + foamL.Process(Noise(rl)) * foam;
            right[i] = bodyR * 2.2f + foamR.Process(Noise(rr)) * foam;
        }
    }

    private static void Birdsong(float[] left, float[] right, Random rl, Random rr, int frames)
    {
        ForestBed(left, right, rl, rr, frames, 0.55f);
        var next = SampleRate / 2;
        while (next < frames)
        {
            var start = next;
            var duration = rl.Next(SampleRate / 12, SampleRate / 4);
            var baseHz = 1600 + rl.NextDouble() * 1800;
            var sweep = 400 + rl.NextDouble() * 900;
            var amp = 0.12f + (float)rl.NextDouble() * 0.18f;
            for (var i = 0; i < duration && start + i < frames; i++)
            {
                var phase = i / (double)SampleRate;
                var env = Math.Sin(Math.PI * i / duration);
                var freq = baseHz + sweep * Math.Sin(2 * Math.PI * 2.1 * phase);
                var sample = (float)(Math.Sin(2 * Math.PI * freq * phase) * env * amp);
                var pan = 0.15f * (float)Math.Sin(2 * Math.PI * 0.13 * phase);
                left[start + i] += sample * (1 - pan);
                right[start + i] += sample * (1 + pan);
            }

            next += rl.Next(SampleRate / 2, SampleRate * 3 / 2);
        }
    }

    private static void Crowds(float[] left, float[] right, Random rl, Random rr, int frames)
    {
        var lowL = new OnePole(0.08f);
        var lowR = new OnePole(0.08f);
        var highL = new OnePole(0.8f);
        var highR = new OnePole(0.8f);
        for (var i = 0; i < frames; i++)
        {
            var t = i / (double)SampleRate;
            var murmur = (float)(0.55 + 0.45 * Math.Sin(2 * Math.PI * 0.11 * t) * Math.Sin(2 * Math.PI * 0.037 * t + 0.8));
            var room = lowL.Process(Noise(rl)) * 0.7f;
            var voice = (lowL.Process(Noise(rl)) - highL.Process(Noise(rl))) * murmur;
            left[i] = room + voice * 1.4f;
            var roomR = lowR.Process(Noise(rr)) * 0.7f;
            var voiceR = (lowR.Process(Noise(rr)) - highR.Process(Noise(rr))) * murmur;
            right[i] = roomR + voiceR * 1.4f;
        }
    }

    private static void Forest(float[] left, float[] right, Random rl, Random rr, int frames)
        => ForestBed(left, right, rl, rr, frames, 1f);

    private static void ForestBed(float[] left, float[] right, Random rl, Random rr, int frames, float level)
    {
        var lowL = new OnePole(0.03f);
        var lowR = new OnePole(0.03f);
        var leafL = new OnePole(0.35f);
        var leafR = new OnePole(0.35f);
        for (var i = 0; i < frames; i++)
        {
            var t = i / (double)SampleRate;
            var wind = (float)(0.5 + 0.5 * Math.Sin(2 * Math.PI * 0.05 * t + 0.4));
            left[i] = (lowL.Process(Noise(rl)) * 1.8f + leafL.Process(Noise(rl)) * 0.18f) * wind * level;
            right[i] = (lowR.Process(Noise(rr)) * 1.8f + leafR.Process(Noise(rr)) * 0.18f) * wind * level;
        }
    }

    private static void Water(float[] left, float[] right, Random rl, Random rr, int frames)
    {
        var lowL = new OnePole(0.12f);
        var lowR = new OnePole(0.12f);
        for (var i = 0; i < frames; i++)
        {
            left[i] = lowL.Process(Noise(rl)) * 0.45f;
            right[i] = lowR.Process(Noise(rr)) * 0.45f;
        }

        var next = SampleRate / 4;
        while (next < frames)
        {
            AddPing(left, right, next, 600 + rl.NextDouble() * 900, 0.09f, rl, rr);
            next += rl.Next(SampleRate / 3, SampleRate * 3 / 2);
        }
    }

    private static void Bell(float[] left, float[] right, Random rl, Random rr, int frames)
    {
        ForestBed(left, right, rl, rr, frames, 0.22f);
        var next = SampleRate;
        while (next < frames)
        {
            var baseHz = 520 + rl.NextDouble() * 780;
            var duration = SampleRate * 3;
            for (var i = 0; i < duration && next + i < frames; i++)
            {
                var t = i / (double)SampleRate;
                var env = Math.Exp(-3.2 * t);
                var sample = (float)(
                    Math.Sin(2 * Math.PI * baseHz * t) * 0.55 +
                    Math.Sin(2 * Math.PI * baseHz * 2.76 * t) * 0.22 +
                    Math.Sin(2 * Math.PI * baseHz * 5.4 * t) * 0.1) * (float)env;
                left[next + i] += sample * 0.5f;
                right[next + i] += sample * 0.5f;
            }

            next += rl.Next(SampleRate * 2, SampleRate * 5);
        }
    }

    private static void RunningWater(float[] left, float[] right, Random rl, Random rr, int frames)
    {
        var lowL = new OnePole(0.2f);
        var lowR = new OnePole(0.2f);
        var bubbleL = new OnePole(0.02f);
        var bubbleR = new OnePole(0.02f);
        for (var i = 0; i < frames; i++)
        {
            var t = i / (double)SampleRate;
            var flow = (float)(0.75 + 0.25 * Math.Sin(2 * Math.PI * 0.19 * t + 1.2));
            left[i] = (lowL.Process(Noise(rl)) * 0.55f + bubbleL.Process(Noise(rl)) * 0.3f) * flow;
            right[i] = (lowR.Process(Noise(rr)) * 0.55f + bubbleR.Process(Noise(rr)) * 0.3f) * flow;
        }

        var next = SampleRate / 8;
        while (next < frames)
        {
            AddPing(left, right, next, 300 + rl.NextDouble() * 500, 0.06f, rl, rr);
            next += rl.Next(SampleRate / 6, SampleRate / 2);
        }
    }

    private static void Second(float[] left, float[] right, Random rl, Random rr, int frames)
    {
        var lowL = new OnePole(0.01f);
        var lowR = new OnePole(0.01f);
        for (var i = 0; i < frames; i++)
        {
            left[i] = lowL.Process(Noise(rl)) * 0.03f;
            right[i] = lowR.Process(Noise(rr)) * 0.03f;
        }

        for (var start = 0; start < frames; start += SampleRate)
        {
            var length = SampleRate / 200;
            for (var i = 0; i < length && start + i < frames; i++)
            {
                var env = (float)Math.Exp(-80.0 * i / SampleRate);
                var sample = (float)Math.Sin(2 * Math.PI * 2400 * i / SampleRate) * env * 0.35f;
                left[start + i] += sample;
                right[start + i] += sample * 0.85f;
            }
        }
    }

    private static void Heartbeat(float[] left, float[] right, int frames)
    {
        var period = SampleRate * 0.9;
        for (var start = 0.0; start < frames; start += period)
        {
            AddThump(left, right, (int)start, 58, 0.55f);
            AddThump(left, right, (int)(start + SampleRate * 0.16), 46, 0.35f);
        }
    }

    private static void Fireworks(float[] left, float[] right, Random rl, Random rr, int frames)
    {
        var lowL = new OnePole(0.01f);
        var lowR = new OnePole(0.01f);
        for (var i = 0; i < frames; i++)
        {
            left[i] = lowL.Process(Noise(rl)) * 0.04f;
            right[i] = lowR.Process(Noise(rr)) * 0.04f;
        }

        var next = SampleRate;
        while (next < frames)
        {
            var boomLength = SampleRate / 2;
            for (var i = 0; i < boomLength && next + i < frames; i++)
            {
                var t = i / (double)SampleRate;
                var env = Math.Exp(-6.0 * t);
                var sample = (float)(Math.Sin(2 * Math.PI * (55 + 20 * Math.Exp(-3 * t)) * t) * env) * 0.7f;
                left[next + i] += sample;
                right[next + i] += sample;
            }

            for (var c = 0; c < 24; c++)
            {
                var at = next + rl.Next(SampleRate / 8, SampleRate);
                AddPing(left, right, at, 900 + rl.NextDouble() * 2400, 0.12f, rl, rr);
            }

            next += rl.Next(SampleRate * 2, SampleRate * 4);
        }
    }

    // ── 合成工具 ────────────────────────────────────────────────────────────

    private static float Noise(Random random) => (float)(random.NextDouble() * 2 - 1);

    private static void AddPing(
        float[] left,
        float[] right,
        int start,
        double frequency,
        float amplitude,
        Random rl,
        Random rr)
    {
        var length = SampleRate / 8;
        var pan = (float)(rl.NextDouble() * 1.2 - 0.6);
        for (var i = 0; i < length && start + i < left.Length; i++)
        {
            var t = i / (double)SampleRate;
            var env = Math.Exp(-34.0 * t);
            var sample = (float)(Math.Sin(2 * Math.PI * frequency * t) * env) * amplitude;
            left[start + i] += sample * (1 - pan) * 0.6f;
            right[start + i] += sample * (1 + pan) * 0.6f;
        }

        _ = rr;
    }

    private static void AddThump(float[] left, float[] right, int start, double frequency, float amplitude)
    {
        var length = SampleRate / 3;
        for (var i = 0; i < length && start + i < left.Length; i++)
        {
            var t = i / (double)SampleRate;
            var env = Math.Exp(-18.0 * t);
            var sample = (float)(Math.Sin(2 * Math.PI * frequency * t) * env) * amplitude;
            left[start + i] += sample;
            right[start + i] += sample;
        }
    }

    private static void CrossfadeLoop(float[] left, float[] right, int outputFrames, int fadeFrames)
    {
        for (var i = 0; i < fadeFrames && i < outputFrames; i++)
        {
            var t = i / (float)fadeFrames;
            left[i] = left[i] * t + left[outputFrames + i] * (1 - t);
            right[i] = right[i] * t + right[outputFrames + i] * (1 - t);
        }
    }

    private static void Normalize(float[] left, float[] right, int frames)
    {
        var peak = 0f;
        for (var i = 0; i < frames; i++)
        {
            peak = Math.Max(peak, Math.Abs(left[i]));
            peak = Math.Max(peak, Math.Abs(right[i]));
        }

        if (peak <= 0.0001f)
        {
            return;
        }

        var scale = Peak / peak;
        for (var i = 0; i < frames; i++)
        {
            left[i] *= scale;
            right[i] *= scale;
        }
    }

    private static void WriteWave(string path, float[] left, float[] right, int frames)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var dataBytes = frames * Channels * sizeof(short);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)Channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * Channels * sizeof(short));
        writer.Write((short)(Channels * sizeof(short)));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        for (var i = 0; i < frames; i++)
        {
            writer.Write(ToPcm16(left[i]));
            writer.Write(ToPcm16(right[i]));
        }
    }

    private static short ToPcm16(float value) =>
        (short)Math.Clamp((int)MathF.Round(Math.Clamp(value, -1f, 1f) * short.MaxValue), short.MinValue, short.MaxValue);

    private struct OnePole
    {
        private readonly float _alpha;
        private float _value;

        public OnePole(float alpha) => _alpha = alpha;

        public float Process(float input)
        {
            _value += _alpha * (input - _value);
            return _value;
        }
    }
}
