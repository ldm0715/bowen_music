namespace Bowen.Core.Models;

/// <summary>
/// 落盘的外观设置。是<b>持久化形状</b>，不是给界面直接用的模型。
/// </summary>
/// <remarks>
/// 单独包一层对象而不是把枚举名裸写进文件，是为了以后加字段时不用换格式 ——
/// 裸写一个字符串的话，加第二个设置项就是一次不兼容变更。
/// </remarks>
public sealed record ThemeSettings
{
    /// <summary>外观档位。</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>
    /// 应用内的磨砂表面是否使用亚克力。<b>可为空是刻意的</b>：旧版设置文件没有这个字段，
    /// 反序列化后是 <c>null</c>，读取方按「开启」处理；新版写入时总是显式给值。
    /// </summary>
    public bool? AcrylicEffects { get; set; } = true;

    /// <summary>
    /// 启动后进入的侧栏根页。默认「我喜欢的」，与加入这个选项之前的行为一致。
    /// </summary>
    public StartupSection StartupPage { get; set; } = StartupSection.Favorites;

    /// <summary>没有设置文件时用的默认值。</summary>
    public static ThemeSettings Default { get; } = new();

    /// <summary>把未定义的枚举值收回默认；旧文件缺 <see cref="AcrylicEffects"/> 时按开启处理。</summary>
    public ThemeSettings Normalized() => this with
    {
        Theme = Enum.IsDefined(Theme) ? Theme : AppTheme.System,
        AcrylicEffects = AcrylicEffects ?? true,
        StartupPage = Enum.IsDefined(StartupPage) ? StartupPage : StartupSection.Favorites,
    };

    /// <summary>保留旧的构造签名，供现有调用点使用。</summary>
    public ThemeSettings()
    {
    }

    /// <summary>保留旧的构造签名，供现有调用点使用。</summary>
    public ThemeSettings(
        AppTheme Theme,
        bool? AcrylicEffects = null,
        StartupSection StartupPage = StartupSection.Favorites)
    {
        this.Theme = Theme;
        this.AcrylicEffects = AcrylicEffects;
        this.StartupPage = StartupPage;
    }
}
