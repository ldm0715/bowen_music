namespace Bodian.Core.Models;

/// <summary>歌曲声明的一条真实音源，保留 level、格式与码率，避免按产品名称猜 br。</summary>
public sealed record AudioVariant(AudioQuality Quality, string Level, string Format, int BitrateKbps, long SizeBytes = 0)
{
    public string RequestBitrate => $"{BitrateKbps}k{Format}";
}
