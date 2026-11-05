using System.Text.Json.Serialization;
using Bowen.Core.Models;

namespace Bowen.Core.Services;

/// <summary>歌词海报偏好的 JSON 上下文。</summary>
/// <remarks>走源生成；枚举以字符串写出，以后加模式不会因为数字顺序变动而串值。</remarks>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(LyricPosterSettings))]
internal sealed partial class LyricPosterSettingsJsonContext : JsonSerializerContext;
