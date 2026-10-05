using System.Text.Json.Serialization;
using Bodian.Core.Models;

namespace Bodian.Core.Services;

/// <summary>
/// 桌面歌词设置的 JSON 上下文。
/// </summary>
/// <remarks>
/// 与 <see cref="ThemeSettingsJsonContext"/> 同一条规矩：走源生成，不开运行时反射；
/// <b>枚举写成字符串</b>，成员顺序以后重排也不会把旧文件静默读成别的档位。
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(DesktopLyricsSettings))]
internal sealed partial class DesktopLyricsSettingsJsonContext : JsonSerializerContext;
