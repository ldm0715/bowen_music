using System.Text.Json;
using System.Text.Json.Serialization;
using Bowen.Core.Models;

namespace Bowen.Core.Services;

/// <summary>
/// 统一配置文件的 JSON 上下文。
/// </summary>
/// <remarks>
/// <para>
/// <b>顶层用 camelCase 写盘</b>，读取同时接受 PascalCase —— 迁移要读旧文件，
/// 而旧文件是 PascalCase。未知字段默认忽略，向前兼容不需要额外配置。
/// </para>
/// <para>
/// 允许注释与尾逗号，方便用户手改；写入端仍然输出纯 JSON，注释会在下次保存时丢失。
/// </para>
/// <para>
/// 每个 section 都单独登记一条 <see cref="JsonSerializableAttribute"/>：
/// 整份反序列化失败时要按 section 逐个重读，只把坏掉的那一段回默认。
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ThemeSettings))]
[JsonSerializable(typeof(FontSettings))]
[JsonSerializable(typeof(ShellBackgroundSettings))]
[JsonSerializable(typeof(PlaybackSettings))]
[JsonSerializable(typeof(VolumeSettings))]
[JsonSerializable(typeof(AudioQualityPreferences))]
[JsonSerializable(typeof(AudioEffectSettings))]
[JsonSerializable(typeof(LyricsSettings))]
[JsonSerializable(typeof(DesktopLyricsSettings))]
[JsonSerializable(typeof(LyricPosterSettings))]
[JsonSerializable(typeof(ViewModeSettings))]
[JsonSerializable(typeof(ShortcutSettings))]
[JsonSerializable(typeof(UpdatePreferences))]
[JsonSerializable(typeof(ProxySettings))]
[JsonSerializable(typeof(InputMethodSettings))]
[JsonSerializable(typeof(WindowSettings))]
[JsonSerializable(typeof(WindowPlacement))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
