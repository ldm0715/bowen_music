using System.Text.Json;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

public sealed class JsonSearchHistoryStore : ISearchHistoryStore
{
    public const int Capacity = 20;
    private readonly string _path;
    private readonly ILogger<JsonSearchHistoryStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSearchHistoryStore(string? path = null, ILogger<JsonSearchHistoryStore>? logger = null)
    {
        _path = path ?? AppPaths.SearchHistoryFile;
        _logger = logger ?? NullLogger<JsonSearchHistoryStore>.Instance;
    }

    public IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            return Normalize(JsonSerializer.Deserialize(File.ReadAllBytes(_path), SearchHistoryJsonContext.Default.StringArray) ?? []);
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
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _path + ".tmp";
            var json = JsonSerializer.SerializeToUtf8Bytes(snapshot, SearchHistoryJsonContext.Default.StringArray);
            await File.WriteAllBytesAsync(temporaryPath, json).ConfigureAwait(false);
            File.Move(temporaryPath, _path, overwrite: true);
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
