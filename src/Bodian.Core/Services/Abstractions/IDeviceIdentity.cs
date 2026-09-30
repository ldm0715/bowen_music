namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 设备标识。同一个值同时用于 <c>devid</c> 与 <c>qimei36</c> 请求头。
/// </summary>
/// <remarks>
/// <b>生成一次后必须稳定复用。</b> 频繁变更设备标识本身就是账号风控的异常信号——
/// 所以实现必须默认指向 P0 探针用过的同一个文件（<c>%LOCALAPPDATA%\Bodian\devid.txt</c>），
/// 不能另起一个位置。
/// </remarks>
public interface IDeviceIdentity
{
    /// <summary>32 位小写十六进制。首次访问时若不存在则生成并落盘。</summary>
    string Value { get; }
}
