using System.Text.Json.Serialization;
using Bodian.Core.Models;

namespace Bodian.Core.Services;

/// <summary>
/// 外观设置的 JSON 上下文。
/// </summary>
/// <remarks>
/// 与 <see cref="PlayHistoryJsonContext"/> 同一条规矩：<b>序列化一律走源生成</b>，
/// 不依赖运行时反射（Core 开了 <c>IsAotCompatible</c> + <c>TreatWarningsAsErrors</c>）。
/// <para>
/// <b>枚举写成字符串</b>（<c>"Dark"</c> 而不是 <c>2</c>）：<see cref="AppTheme"/> 的成员顺序
/// 以后可能重排，写成数字的话旧文件会静默读成别的档位。
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ThemeSettings))]
internal sealed partial class ThemeSettingsJsonContext : JsonSerializerContext;
