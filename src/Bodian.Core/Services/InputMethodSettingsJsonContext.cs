using System.Text.Json.Serialization;
using Bodian.Core.Models;

namespace Bodian.Core.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(InputMethodSettings))]
internal sealed partial class InputMethodSettingsJsonContext : JsonSerializerContext;
