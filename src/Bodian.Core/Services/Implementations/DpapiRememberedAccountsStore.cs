using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <summary>
/// 记住的账号清单，用 DPAPI（当前用户范围）加密后落盘。
/// </summary>
/// <remarks>
/// <para>
/// 加密方式与 <see cref="DpapiCredentialStore"/> 同款，但 <b>entropy 不同</b>
/// （<c>Bodian.Accounts.v1</c> 对 <c>Bodian.Session.v1</c>）：两份都装得下可用的 token，
/// 保护域不该可互换 —— entropy 相同的话，把这份文件改名成 <c>session.dat</c> 也能解开。
/// </para>
/// <para>
/// <b>上限 <see cref="MaxAccounts"/> 条，超出丢最久未用的。</b> 一份能直接登进账号的 token 清单，
/// 无上限只是白担风险；界面上也放不下。
/// </para>
/// <para>
/// <b>读失败一律当空清单，且不删文件。</b> 与其它 store 一致：宁可列表空着。
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DpapiRememberedAccountsStore : IRememberedAccountsStore
{
    /// <summary>最多记住几个账号。</summary>
    public const int MaxAccounts = 5;

    /// <summary>DPAPI 附加熵。<b>与 <c>session.dat</c> 那份不同，别改成一样。</b></summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Bodian.Accounts.v1");

    /// <summary>「读-改-写」是三次文件操作，没有它并发写会丢更新。</summary>
    private readonly Lock _gate = new();

    private readonly string _path;
    private readonly ILogger<DpapiRememberedAccountsStore> _logger;

    /// <param name="path">默认 <see cref="AppPaths.RememberedAccountsFile"/>。测试可注入临时路径。</param>
    /// <param name="logger">日志。</param>
    public DpapiRememberedAccountsStore(
        string? path = null,
        ILogger<DpapiRememberedAccountsStore>? logger = null)
    {
        _path = path ?? AppPaths.RememberedAccountsFile;
        _logger = logger ?? NullLogger<DpapiRememberedAccountsStore>.Instance;
    }

    public IReadOnlyList<RememberedAccount> Load()
    {
        lock (_gate)
        {
            return Read();
        }
    }

    public void Remember(BodianCredential credential, DateTimeOffset usedAt)
    {
        ArgumentNullException.ThrowIfNull(credential);

        lock (_gate)
        {
            var accounts = Read().Where(entry => entry.Credential.Uid != credential.Uid).ToList();
            accounts.Add(new RememberedAccount(credential, usedAt));

            // 越界丢最久未用的。排序依据只有 usedAt 一条，不为「刚插进来的」开特例 ——
            // 调用方（BodianLogin）永远传当前时刻，所以那条天然排在最前，特例只会多一个分支。
            var keep = accounts
                .OrderByDescending(entry => entry.LastUsedAt)
                .Take(MaxAccounts)
                .OrderByDescending(entry => entry.LastUsedAt)
                .ToList();

            if (keep.Count < accounts.Count)
            {
                _logger.LogInformation(
                    "记住的账号超过 {Max} 个，已淘汰最久未用的 {Dropped} 个",
                    MaxAccounts,
                    accounts.Count - keep.Count);
            }

            Write(keep);
        }
    }

    public void Forget(string uid)
    {
        ArgumentNullException.ThrowIfNull(uid);

        lock (_gate)
        {
            var accounts = Read();

            if (!accounts.Any(entry => entry.Credential.Uid == uid))
            {
                return;
            }

            Write(accounts.Where(entry => entry.Credential.Uid != uid).ToList());
        }
    }

    /// <summary>读取并排序。<b>调用方必须持锁。</b></summary>
    private List<RememberedAccount> Read()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);

            var document = JsonSerializer.Deserialize(plain, RememberedAccountsJsonContext.Default.RememberedAccountsDocument);

            return document?.Accounts is not { Length: > 0 } accounts
                ? []
                : [.. accounts
                    // 元素类型可空：JSON 里写了个 null 元素是完全可能的（手改过的文件），
                    // 不挡掉的话下面读 LastUsedAt 直接空引用。
                    .OfType<RememberedAccount>()
                    .OrderByDescending(entry => entry.LastUsedAt)];
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException
            or IOException or UnauthorizedAccessException)
        {
            // 换了 Windows 账号、文件被外部改动过、或半截写入 —— 当空清单。
            // **不删文件**：留着还能人工看一眼出了什么事。
            _logger.LogWarning(ex, "记住的账号清单读取失败，按空清单继续：{Path}", _path);
            return [];
        }
    }

    /// <summary>落盘。<b>调用方必须持锁。</b></summary>
    private void Write(List<RememberedAccount> accounts)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = new RememberedAccountsDocument { Accounts = [.. accounts] };
            var plain = JsonSerializer.SerializeToUtf8Bytes(
                document, RememberedAccountsJsonContext.Default.RememberedAccountsDocument);

            var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

            // 先写临时文件再原子替换：直接写目标文件时，写到一半退出会留下半截密文。
            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // 写不进去只影响「切换账号」列表，不该让登录/切号这些流程出问题。
            _logger.LogWarning(ex, "记住的账号清单写入失败：{Path}", _path);
        }
    }
}

/// <summary>落盘的顶层形状。用一个对象而不是裸数组，以后加字段（如清单级别的迁移版本）才有地方放。</summary>
internal sealed class RememberedAccountsDocument
{
    public RememberedAccount?[]? Accounts { get; init; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RememberedAccountsDocument))]
internal sealed partial class RememberedAccountsJsonContext : JsonSerializerContext;
