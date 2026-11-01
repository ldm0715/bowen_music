using Bowen.Core.Audio;
using Xunit;

namespace Bowen.Core.Tests;

public sealed class AmbientSoundGeneratorTests
{
    [Fact]
    public void EveryAmbientSound_ExtractsAValidMp3()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bowen-ambient-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Span<byte> header = stackalloc byte[3];
            foreach (var sound in AudioEffectCatalog.AmbientSounds)
            {
                var path = AmbientSoundGenerator.Ensure(sound, folder);

                Assert.True(File.Exists(path), $"{sound.Id} 没有解压出来");
                Assert.True(new FileInfo(path).Length > 20_000, $"{sound.Id} 文件过小");

                using var stream = File.OpenRead(path);
                Assert.Equal(3, stream.Read(header));
                var isId3 = header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'3';
                var isFrame = header[0] == 0xFF && (header[1] & 0xE0) == 0xE0;
                Assert.True(isId3 || isFrame, $"{sound.Id} 不是有效的 MP3");
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
