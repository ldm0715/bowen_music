using System.Text.Json.Serialization;
using Bodian.Core.Models;

namespace Bodian.Core.Services;

/// <summary>
/// 显示方式偏好的 JSON 上下文。
/// </summary>
/// <remarks>
/// 与 <see cref="ThemeSettingsJsonContext"/> 同一条规矩：走源生成，不开运行时反射
/// （Core 开了 <c>IsAotCompatible</c> + <c>TreatWarningsAsErrors</c>）。
/// 这份目前没有枚举项，<c>UseStringEnumConverter</c> 留着是为了以后加档位时不必再改。
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ViewModeSettings))]
internal sealed partial class ViewModeSettingsJsonContext : JsonSerializerContext;
