using System.Text.Json.Serialization;
using Bowen.Core.Models;

namespace Bowen.Core.Services;

/// <summary>
/// 歌词页偏好的 JSON 上下文。
/// </summary>
/// <remarks>
/// 与 <see cref="ThemeSettingsJsonContext"/> 同一条规矩：走源生成，不开运行时反射。
/// <c>Alignment</c>、<c>CoverStyle</c>、<c>BackgroundMode</c> 等枚举都按名字写盘，
/// 文件可读，加档位时也不必迁移旧值。
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(LyricsSettings))]
internal sealed partial class LyricsSettingsJsonContext : JsonSerializerContext;
