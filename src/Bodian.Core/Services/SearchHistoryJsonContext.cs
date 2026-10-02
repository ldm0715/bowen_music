using System.Text.Json.Serialization;

namespace Bodian.Core.Services;

[JsonSerializable(typeof(string[]))]
internal sealed partial class SearchHistoryJsonContext : JsonSerializerContext;
