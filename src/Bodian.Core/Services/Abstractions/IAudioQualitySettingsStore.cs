using Bodian.Core.Models;
namespace Bodian.Core.Services.Abstractions;
public interface IAudioQualitySettingsStore
{
    AudioQuality Load();
    void Save(AudioQuality quality);
}
