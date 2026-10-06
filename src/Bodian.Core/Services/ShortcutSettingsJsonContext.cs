using System.Text.Json.Serialization;
using Bodian.Core.Models;

namespace Bodian.Core.Services;

/// <summary>
/// 快捷键键位的 JSON 上下文。
/// </summary>
/// <remarks>
/// <b><c>UseStringEnumConverter</c> 在这里是必须的，不只是习惯。</b>
/// 键位表里存着 <see cref="ShortcutKey"/> 与 <see cref="ShortcutAction"/> 两个枚举，
/// 写成数字的话，以后往枚举中间插一个成员就会让旧文件静默读成别的动作 ——
/// 用户会发现「上一首」的键突然变成了别的功能。写成名字就不会。
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ShortcutSettings))]
internal sealed partial class ShortcutSettingsJsonContext : JsonSerializerContext;
