using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bodian.Core.Services.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <summary>
/// 用 DPAPI（当前用户范围）加密后落盘的凭据存储。
/// </summary>
/// <remarks>
/// <para>
/// <b>DPAPI 就放在纯 <c>net10.0</c> 的 Core 里，不需要挪去 WinUI</b>——
/// <c>System.Security.Cryptography.ProtectedData</c> 有 <c>lib/net10.0</c> 资产，
/// 能直接引用、能编译，测试也跑得动。
/// </para>
/// <para>
/// 但<b>必须标 <see cref="SupportedOSPlatformAttribute"/></b>：该包在 API 契约层面把
/// <c>ProtectedData.Protect</c>/<c>Unprotect</c> 标成了 Windows-only，从平台中立的
/// <c>net10.0</c> 调用会触发 <b>CA1416</b>（实测如此——不是运行时才报）。
/// 标注之后 Windows-only 这件事从约定变成了编译期可见：调用方（WinUI，
/// TFM 是 <c>net10.0-windows10.0.*</c>）天然满足，而一个中立的控制台项目若误用会直接编译不过。
/// </para>
/// <para>
/// <b>路径、entropy、JSON 字段名三者都必须与 P0 探针的 <c>SessionStore</c> 一致</b>，
/// 否则已登录的会话会读不出来。改动其中任何一个都等于改凭据文件格式。
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStore : ICredentialStore
{
    /// <summary>DPAPI 附加熵。**与探针相同，不要改。**</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Bodian.Session.v1");

    private readonly string _path;

    /// <param name="path">默认 <see cref="AppPaths.CredentialFile"/>（与探针共用）。测试可注入临时路径。</param>
    public DpapiCredentialStore(string? path = null) => _path = path ?? AppPaths.CredentialFile;

    public BodianCredential? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);

            return JsonSerializer.Deserialize(plain, CredentialJsonContext.Default.BodianCredential);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            // 换了 Windows 账号、或文件被外部改动过——当作没有会话，别让应用起不来。
            return null;
        }
    }

    public void Save(BodianCredential credential)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var plain = JsonSerializer.SerializeToUtf8Bytes(credential, CredentialJsonContext.Default.BodianCredential);
        var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

        File.WriteAllBytes(_path, encrypted);
    }

    public bool Clear()
    {
        if (!File.Exists(_path))
        {
            return false;
        }

        File.Delete(_path);
        return true;
    }
}
