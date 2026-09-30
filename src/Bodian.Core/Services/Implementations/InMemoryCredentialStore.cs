using Bodian.Core.Services.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <summary>
/// 只放在内存里的凭据存储。
/// </summary>
/// <remarks>
/// <b>这是生产代码，不是测试专用。</b> 两个正经用途：
/// <list type="bullet">
/// <item>单元测试与 UI 预览——不该碰真实磁盘，更不该污染 <c>%LOCALAPPDATA%\Bodian</c></item>
/// <item>显式的「不持久化会话」运行模式（例如只读的诊断入口）</item>
/// </list>
/// </remarks>
public sealed class InMemoryCredentialStore : ICredentialStore
{
    private BodianCredential? _credential;

    public InMemoryCredentialStore(BodianCredential? initial = null) => _credential = initial;

    public BodianCredential? Load() => _credential;

    public void Save(BodianCredential credential) => _credential = credential;

    public bool Clear()
    {
        var had = _credential is not null;
        _credential = null;
        return had;
    }
}
