using System.Text.Json.Serialization;
using Bodian.Core.Models;

namespace Bodian.Core.Services;

/// <summary>
/// 播放历史的 JSON 上下文。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="CredentialJsonContext"/> 同一条规矩：<b>所有序列化都走源生成</b>，
/// 不依赖运行时反射（Core 开了 <c>IsAotCompatible</c> + <c>TreatWarningsAsErrors</c>）。
/// </para>
/// <para>
/// <b>枚举写成字符串</b>（<c>"Lossless"</c> 而不是 <c>2</c>）：这份文件要在版本之间活下去，
/// 而枚举的数值顺序随时可能被重排，届时旧文件会静默解析成错的档位。
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<PlayHistoryEntry>))]
internal sealed partial class PlayHistoryJsonContext : JsonSerializerContext;
