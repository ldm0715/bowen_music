using System.Text;
using Bowen.Core.Audio;
using Xunit;

namespace Bowen.Core.Tests;

public sealed class AmbientSoundGeneratorTests
{
    [Fact]
    public void EveryAmbientSound_GeneratesAValidSeamlessWaveFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bowen-ambient-tests", Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var sound in AudioEffectCatalog.AmbientSounds)
            {
                var path = AmbientSoundGenerator.Ensure(sound, folder);

                Assert.True(File.Exists(path));
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);

                Assert.Equal("RIFF", Encoding.ASCII.GetString(reader.ReadBytes(4)));
                reader.ReadInt32();
                Assert.Equal("WAVE", Encoding.ASCII.GetString(reader.ReadBytes(4)));
                Assert.Equal("fmt ", Encoding.ASCII.GetString(reader.ReadBytes(4)));
                Assert.Equal(16, reader.ReadInt32());
                Assert.Equal(1, reader.ReadInt16());
                Assert.Equal(AmbientSoundGenerator.Channels, reader.ReadInt16());
                Assert.Equal(AmbientSoundGenerator.SampleRate, reader.ReadInt32());
                reader.ReadInt32();
                reader.ReadInt16();
                Assert.Equal(16, reader.ReadInt16());
                Assert.Equal("data", Encoding.ASCII.GetString(reader.ReadBytes(4)));
                var dataBytes = reader.ReadInt32();

                Assert.Equal(
                    AmbientSoundGenerator.SampleRate * AmbientSoundGenerator.DurationSeconds
                    * AmbientSoundGenerator.Channels * sizeof(short),
                    dataBytes);
                Assert.Equal(stream.Length, stream.Position + dataBytes);

                var peak = 0;
                var energy = 0d;
                var samples = dataBytes / sizeof(short);
                for (var i = 0; i < samples; i++)
                {
                    var value = reader.ReadInt16();
                    peak = Math.Max(peak, Math.Abs((int)value));
                    energy += value * (double)value;
                }

                Assert.True(peak > 1000, $"{sound.Id} 几乎没有声音");
                Assert.True(peak <= short.MaxValue * 0.6, $"{sound.Id} 峰值过高：{peak}");
                Assert.True(Math.Sqrt(energy / samples) > 100, $"{sound.Id} 能量过低");
            }
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void Ensure_ReusesAnExistingFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bowen-ambient-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var sound = AudioEffectCatalog.AmbientSounds[0];
            var first = AmbientSoundGenerator.Ensure(sound, folder);
            var stamp = File.GetLastWriteTimeUtc(first);
            Thread.Sleep(20);

            var second = AmbientSoundGenerator.Ensure(sound, folder);

            Assert.Equal(first, second);
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(second));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
