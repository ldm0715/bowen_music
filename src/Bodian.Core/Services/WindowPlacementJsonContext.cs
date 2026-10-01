using System.Text.Json.Serialization;
using Bodian.Core.Models;

namespace Bodian.Core.Services;

/// <summary>
/// 窗口位置记录的 JSON 上下文。
/// </summary>
/// <remarks>
/// 与其余几个上下文同一条规矩：<b>序列化一律走源生成</b>，不依赖运行时反射。
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WindowPlacement))]
internal sealed partial class WindowPlacementJsonContext : JsonSerializerContext;
