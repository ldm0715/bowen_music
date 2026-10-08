using System.Text.Json;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <summary>
/// 搜索关键词历史，按作用域分文件（<c>accounts\&lt;scope&gt;\search-history.json</c>）。
/// </summary>
/// <remarks>
/// <b>没有内存缓存</b>：每次 <see cref="Load"/> 都真读文件，所以账号切换不需要额外的失效处理 ——
/// 内存里那份由 <c>SearchViewModel</c> 自己持有，它在账号变更时会重读。
/// </remarks>
public sealed class JsonSearchHistoryStore : ISearchHistoryStore
{
    public const int Capacity = 20;

    /// <summary>显式路径。<b>只在测试里给</b>：给了之后就不再按账号解析。</summary>
    private readonly string? _explicitPath;
    private readonly ICurrentAccount? _account;
    private readonly string _rootDirectory;
    private readonly ILogger<JsonSearchHistoryStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSearchHistoryStore(
        string? path = null,
        ILogger<JsonSearchHistoryStore>? logger = null,
        ICurrentAccount? account = null,
        string? rootDirectory = null)
    {
        _explicitPath = path;
        _account = account;
        _rootDirectory = rootDirectory ?? AppPaths.LocalAppData;
        _logger = logger ?? NullLogger<JsonSearchHistoryStore>.Instance;
    }

    private string ResolvePath() => _explicitPath ?? AppPaths.AccountFilePath(
        _account?.Scope ?? AppPaths.AnonymousScope, AppPaths.SearchHistoryFileName, _rootDirectory);

    public IReadOnlyList<string> Load()
    {
        var path = ResolvePath();

        try
        {
            if (!File.Exists(path)) return [];
            return Normalize(JsonSerializer.Deserialize(File.ReadAllBytes(path), SearchHistoryJsonContext.Default.StringArray) ?? []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "读取搜索历史失败");
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<string> keywords)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        var snapshot = Normalize(keywords);
        var path = ResolvePath();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(directory);
            var temporaryPath = path + ".tmp";
            var json = JsonSerializer.SerializeToUtf8Bytes(snapshot, SearchHistoryJsonContext.Default.StringArray);
            await File.WriteAllBytesAsync(temporaryPath, json).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "保存搜索历史失败");
        }
        finally { _gate.Release(); }
    }

    private static string[] Normalize(IEnumerable<string> keywords) => keywords
        .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
        .Select(keyword => keyword.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(Capacity).ToArray();
}
