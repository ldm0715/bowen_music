namespace Bodian.Core.Models;

/// <summary>
/// 界面外观。
/// </summary>
/// <remarks>
/// <b>落盘时必须写成字符串</b>（见 <see cref="Services.ThemeSettingsJsonContext"/>）：
/// 枚举的数值顺序随时可能被重排，写成数字的话旧设置文件会静默解析成错的档位。
/// </remarks>
public enum AppTheme
{
    /// <summary>跟随系统的浅色 / 深色设置。默认值。</summary>
    System,

    /// <summary>强制浅色。</summary>
    Light,

    /// <summary>强制深色。</summary>
    Dark,
}
