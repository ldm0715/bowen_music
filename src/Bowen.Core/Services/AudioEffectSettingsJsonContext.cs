using System.Text.Json.Serialization;
using Bowen.Core.Models;

namespace Bowen.Core.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AudioEffectSettings))]
internal sealed partial class AudioEffectSettingsJsonContext : JsonSerializerContext;
