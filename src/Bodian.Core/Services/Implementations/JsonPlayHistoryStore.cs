using System.Text.Json;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <inheritdoc cref="IPlayHistoryStore" />
/// <remarks>
/// <para>
/// <b>明文 JSON，不加密。</b> 它记录的是「听过什么歌」——不是凭据，<b>文件里不含账号标识</b>。
/// 归属靠「存在哪个目录」表达（<c>accounts\&lt;scope&gt;\</c>），uid 不写进内容 ——
/// DPAPI 那套只服务 token，把它套在历史文件上只会让人误以为这里面有什么敏感东西。
/// </para>
/// <para>
/// <b>按账号分目录</b>：换账号后看到的是另一个人的最近播放，那比「这台机器听过什么」更让人觉得错。
/// 未登录期间写进 <c>anonymous</c> 桶，登录后<b>不并进</b>账号那份（归属是编不出来的）。
/// </para>
/// <para>
/// <b>读失败与写失败都不是致命错误。</b> 历史文件损坏时当作空历史继续跑 ——
/// 「最近播放」一页空着，远比整个应用起不来好。
/// </para>
/// <para>
/// <b>先写临时文件再原子替换。</b> 直接往目标文件写，一旦在写到一半时进程退出，
/// 留下的就是半截 JSON，下次启动整段历史全丢。
/// </para>
/// <para>
/// 内存里只保留一份，按「最新的在前」维护；<b>上限之外的直接丢掉</b>（<see cref="Capacity"/>）。
/// </para>
/// </remarks>
public sealed class JsonPlayHistoryStore : IPlayHistoryStore
{
    /// <summary>最多留多少条。</summary>
    public const int DefaultCapacity = 500;

    private readonly string? _explicitPath;
    private readonly ICurrentAccount? _account;
    private readonly string _rootDirectory;
    private readonly TimeProvider _clock;
    private readonly ILogger<JsonPlayHistoryStore> _logger;

    /// <summary>串行化「读-改-写」。播放与翻页可能并发，没有它会出现丢更新。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<PlayHistoryEntry>? _entries;

    /// <summary>内存里那份是从哪个文件读出来的。换账号后据此丢弃缓存，见 <see cref="LoadAsync"/>。</summary>
    private string? _loadedPath;

    /// <param name="path">
    /// 显式路径。<b>只在测试里给</b>：给了之后就不再按账号解析。
    /// </param>
    /// <param name="clock">取时间戳用。测试注入固定时钟。</param>
    /// <param name="logger">日志。</param>
    /// <param name="capacity">上限，超出后丢掉最旧的。</param>
    /// <param name="account">当前账号。为 <c>null</c> 且未给 <paramref name="path"/> 时按匿名桶处理。</param>
    /// <param name="rootDirectory">
    /// 数据根目录，默认 <see cref="AppPaths.LocalAppData"/>；<b>测试指向临时目录</b>，免得碰真实数据。
    /// </param>
    public JsonPlayHistoryStore(
        string? path = null,
        TimeProvider? clock = null,
        ILogger<JsonPlayHistoryStore>? logger = null,
        int capacity = DefaultCapacity,
        ICurrentAccount? account = null,
        string? rootDirectory = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "容量必须是正数");
        }

        _explicitPath = path;
        _account = account;
        _rootDirectory = rootDirectory ?? AppPaths.LocalAppData;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<JsonPlayHistoryStore>.Instance;
        Capacity = capacity;
    }

    private string ResolvePath() => _explicitPath ?? AppPaths.AccountFilePath(
        _account?.Scope ?? AppPaths.AnonymousScope, AppPaths.PlayHistoryFileName, _rootDirectory);

    public int Capacity { get; }

    public async Task<IReadOnlyList<PlayHistoryEntry>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var entries = await LoadAsync(cancellationToken).ConfigureAwait(false);

            // 内存里已经保证「最新的在前」，这里只截断，不排序。
            return entries.Count <= limit ? [.. entries] : [.. entries.Take(limit)];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordAsync(Track track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var entries = await LoadAsync(cancellationToken).ConfigureAwait(false);

            // 同一首歌只留最新那一条 —— 历史列表里出现十遍同一首歌没有意义。
            entries.RemoveAll(entry => entry.MusicId == track.Id);
            entries.Insert(0, PlayHistoryEntry.From(track, _clock.GetUtcNow()));

            if (entries.Count > Capacity)
            {
                entries.RemoveRange(Capacity, entries.Count - Capacity);
            }

            await SaveAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _entries = [];

            await SaveAsync(_entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>懒加载一次，之后一直在内存里。返回的是内部实例，调用方在锁内改它。</summary>
    /// <remarks>
    /// <b>路径变了就必须丢掉内存里那份。</b> 缓存是「读一次就一直用」，而账号切换只改路径 ——
    /// 不比对这个的话，A 的历史会原样喂给 B，而那正是这次要修的东西。
    /// </remarks>
    private async Task<List<PlayHistoryEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        var path = ResolvePath();

        if (_entries is not null && path == _loadedPath)
        {
            return _entries;
        }

        _loadedPath = path;

        if (!File.Exists(path))
        {
            return _entries = [];
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize(bytes, PlayHistoryJsonContext.Default.ListPlayHistoryEntry);

            // 排序只为让「最新的在前」这个不变量对旧文件也成立，之后不再重复排序。
            return _entries = loaded is null
                ? []
                : [.. loaded.OrderByDescending(entry => entry.PlayedAt)];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 文件坏了就当没有历史。**不要删它**：留着还能人工看一眼出了什么事。
            _logger.LogWarning(ex, "播放历史读取失败，按空历史继续：{Path}", path);

            return _entries = [];
        }
    }

    private async Task SaveAsync(List<PlayHistoryEntry> entries, CancellationToken cancellationToken)
    {
        var path = ResolvePath();

        try
        {
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                entries,
                PlayHistoryJsonContext.Default.ListPlayHistoryEntry);

            // 先写临时文件再原子替换：直接写目标文件时，写到一半退出会留下半截 JSON。
            var temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);

            // 写到哪个文件就在哪儿作数：不更新的话，账号换回来时会以为内存那份还是这个账号的。
            _loadedPath = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不进去只影响下次启动能不能看到历史，不该让播放流程出问题。
            _logger.LogWarning(ex, "播放历史写入失败：{Path}", path);
        }
    }
}
