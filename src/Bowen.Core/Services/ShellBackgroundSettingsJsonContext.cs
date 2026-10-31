using System.Text.Json.Serialization;
using Bowen.Core.Models;

namespace Bowen.Core.Services;

/// <summary>
/// 主窗口外壳背景设置的 JSON 上下文。
/// </summary>
/// <remarks>
/// 与主题设置同一条规矩：序列化走源生成，枚举写成字符串，避免成员顺序变化后旧文件读成别的模式。
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ShellBackgroundSettings))]
internal sealed partial class ShellBackgroundSettingsJsonContext : JsonSerializerContext;
