using System.Text.Json.Serialization;

namespace Bodian.Core.Models;

/// <summary>歌曲声明的一条真实音源，保留 level、格式与码率，避免按产品名称猜 br。</summary>
public sealed record AudioVariant(AudioQuality Quality, string Level, string Format, int BitrateKbps, long SizeBytes = 0)
{
    /// <summary>请求参数里的 br 值。由码率与格式拼出，每次读都在算，不参与序列化。</summary>
    [JsonIgnore]
    public string RequestBitrate => $"{BitrateKbps}k{Format}";
}
