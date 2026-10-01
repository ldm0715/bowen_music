using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 外观设置的本地持久化。
/// </summary>
/// <remarks>
/// <b>接口是同步的，这是刻意的。</b> 主题必须在窗口显示出来之前就定下来，
/// 否则会先渲染一帧系统主题、再跳到用户选的那套（浅色启动闪深色的观感很差）。
/// 文件只有一个字段、读一次不到 1 毫秒，用 <c>async</c> 换来的只是
/// 「可以在窗口显示后再改」—— 而那恰好是我们要避免的。
/// <para>
/// 写回则是「改了立刻落盘」：设置项就这一个，没必要攒着批量写。
/// </para>
/// </remarks>
public interface IThemeSettingsStore
{
    /// <summary>
    /// 读当前设置。<b>任何失败都返回 <see cref="ThemeSettings.Default"/> 而不是抛异常</b> ——
    /// 一个坏掉的外观设置文件不该让应用起不来。
    /// </summary>
    ThemeSettings Load();

    /// <summary>
    /// 写回设置。写失败只记日志，不抛 —— 本次会话的选择仍然生效，只是下次启动会丢。
    /// </summary>
    void Save(ThemeSettings settings);
}
