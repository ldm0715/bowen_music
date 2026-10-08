using System.Text.Json.Serialization;
using Bodian.Core.Models;

namespace Bodian.Core.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(VolumeSettings))]
internal sealed partial class VolumeSettingsJsonContext : JsonSerializerContext;
