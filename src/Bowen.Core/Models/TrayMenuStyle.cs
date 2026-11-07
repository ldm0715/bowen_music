using System.Text.Json.Serialization;
using Bowen.Core.Services;

namespace Bowen.Core.Models;

/// <summary>
/// 托盘右键菜单的形态。
/// </summary>
/// <remarks>
/// <b>落盘时必须写成字符串</b>（见 <c>ThemeSettingsJsonContext</c>）：枚举顺序以后可能重排，
/// 写成数字会让旧设置文件静默读成另一种形态。
/// </remarks>
[JsonConverter(typeof(TolerantEnumConverter<TrayMenuStyle>))]
public enum TrayMenuStyle
{
    /// <summary>轻量原生菜单，保持加入这个选项之前的行为。</summary>
    Compact,

    /// <summary>内联控制面板，右键直接调音量、模式、音效、音质与播放列表。</summary>
    Efficient,
}
