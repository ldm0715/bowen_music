using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

namespace Bodian.Core.Playback;

/// <summary>仅供离线研究工具使用，不属于客户端播放模型或构建。</summary>
public sealed record ResearchAudioSource
{
    public required Uri Url { get; init; }
    public required string Format { get; init; }
    public string? EKey { get; init; }
    public bool IsEncrypted => Format.Equals("mflac", StringComparison.OrdinalIgnoreCase)
        || Format.Equals("mgg", StringComparison.OrdinalIgnoreCase);
    public override string ToString() => $"ResearchAudioSource {{ Format = {Format} }}";
}

public interface IAudioSourceResolver
{
    Task<PreparedAudioSource> PrepareAsync(ResearchAudioSource source, CancellationToken cancellationToken = default);
}

/// <summary>准备后的播放地址。释放时关闭对应解密会话，不创建音频文件。</summary>
public sealed class PreparedAudioSource(Uri url, Action? release = null, long sizeBytes = 0) : IDisposable
{
    private Action? _release = release;
    public Uri Url { get; } = url;
    public long SizeBytes { get; } = sizeBytes;
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}

/// <summary>仅监听本机的媒体流代理。按 CDN Range 读取并解密，支持 mpv 跳转。</summary>
public sealed class EncryptedAudioSourceResolver : IAudioSourceResolver, IDisposable
{
    private const int ChunkSize = EKeyAudioDecryptor.SegmentSize * 64;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _startLock = new();
    private TcpListener? _listener;
    private bool _disposed;

    public EncryptedAudioSourceResolver(HttpClient? http = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        }) { Timeout = TimeSpan.FromSeconds(45) };
    }

    public async Task<PreparedAudioSource> PrepareAsync(ResearchAudioSource source, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!source.IsEncrypted) { return new PreparedAudioSource(source.Url); }
        var decryptor = EKeyAudioDecryptor.Create(source.EKey);
        try
        {
            var (first, length) = await DownloadAsync(source.Url, 0, EKeyAudioDecryptor.SegmentSize - 1, cancellationToken)
                .ConfigureAwait(false);
            decryptor.Decrypt(first, 0);
            var flac = source.Format.Equals("mflac", StringComparison.OrdinalIgnoreCase);
            if (!first.AsSpan().StartsWith(flac ? "fLaC"u8 : "OggS"u8))
            { throw new InvalidDataException("音源解密失败：文件头与服务端格式不符"); }
            cancellationToken.ThrowIfCancellationRequested();
            var port = StartListener();
            var token = Guid.NewGuid().ToString("N");
            var session = new Session(source.Url, decryptor, first, length, flac ? "audio/flac" : "audio/ogg");
            _sessions[token] = session;
            return new PreparedAudioSource(new Uri($"http://127.0.0.1:{port}/{token}"), () =>
            {
                if (_sessions.TryRemove(token, out var removed)) { removed.Dispose(); }
            }, length);
        }
        catch
        {
            decryptor.Dispose();
            throw;
        }
    }

    private int StartListener()
    {
        lock (_startLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_listener is null)
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                _ = AcceptAsync(_listener);
            }
            return ((IPEndPoint)_listener.LocalEndpoint).Port;
        }
    }

    private async Task AcceptAsync(TcpListener listener)
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(_shutdown.Token).ConfigureAwait(false);
                _ = ServeAsync(client);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var stream = client.GetStream();
            try
            {
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                var parts = line?.Split(' ');
                string? range = null;
                var bytes = line?.Length ?? 0;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false)))
                {
                    bytes += line.Length;
                    if (bytes > 8192) { await ErrorAsync(stream, 431, "Request Header Fields Too Large", timeout.Token); return; }
                    if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) { range = line[6..].Trim(); }
                }
                if (parts is not { Length: 3 } || parts[0] is not ("GET" or "HEAD"))
                { await ErrorAsync(stream, 405, "Method Not Allowed", timeout.Token); return; }
                if (!_sessions.TryGetValue(parts[1].TrimStart('/'), out var session) || !session.TryAcquire())
                { await ErrorAsync(stream, 404, "Not Found", timeout.Token); return; }
                try
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, session.Cancellation.Token);
                    var ct = linked.Token;
                    long start = 0, end = session.Length - 1;
                    if (range is not null && !TryRange(range, session.Length, out start, out end))
                    {
                        await HeadersAsync(stream, $"HTTP/1.1 416 Range Not Satisfiable\r\nContent-Range: bytes */{session.Length}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", ct);
                        return;
                    }
                    var status = range is null ? "200 OK" : "206 Partial Content";
                    var contentRange = range is null ? "" : $"Content-Range: bytes {start}-{end}/{session.Length}\r\n";
                    await HeadersAsync(stream, $"HTTP/1.1 {status}\r\nContent-Type: {session.ContentType}\r\nContent-Length: {end - start + 1}\r\nAccept-Ranges: bytes\r\nCache-Control: no-store\r\n{contentRange}Connection: close\r\n\r\n", ct);
                    if (parts[0] == "HEAD") { return; }
                    while (start <= end)
                    {
                        byte[] buffer;
                        long aligned;
                        if (start < session.First.Length) { buffer = session.First; aligned = 0; }
                        else
                        {
                            aligned = start / EKeyAudioDecryptor.SegmentSize * EKeyAudioDecryptor.SegmentSize;
                            var limit = Math.Min(session.Length - 1, aligned + ChunkSize - 1);
                            (buffer, var length) = await DownloadAsync(session.Url, aligned, limit, ct).ConfigureAwait(false);
                            if (length != session.Length) { throw new InvalidDataException("音源长度在播放过程中变化"); }
                            session.Decryptor.Decrypt(buffer, aligned);
                        }
                        var skip = (int)(start - aligned);
                        var count = (int)Math.Min(buffer.Length - skip, end - start + 1);
                        if (count <= 0) { throw new EndOfStreamException(); }
                        await stream.WriteAsync(buffer.AsMemory(skip, count), ct).ConfigureAwait(false);
                        start += count;
                    }
                }
                finally { session.Release(); }
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // 关闭连接让 mpv 报播放失败。异常中可能有签名 URL，不能直接记日志。
            }
        }
    }

    private async Task<(byte[] Bytes, long Length)> DownloadAsync(Uri url, long start, long end, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(start, end);
        request.Headers.AcceptEncoding.ParseAdd("identity");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) { throw new IOException($"音源读取失败（HTTP {(int)response.StatusCode}）"); }
        var contentRange = response.Content.Headers.ContentRange;
        var length = contentRange?.Length ?? response.Content.Headers.ContentLength;
        if (length is null || length <= 0 || length > int.MaxValue)
        { throw new InvalidDataException("音源未提供有效文件长度"); }
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            if (contentRange?.From != start || contentRange.To is null || contentRange.To > end)
            { throw new InvalidDataException("音源分段响应不符合请求范围"); }
        }
        else if (start != 0 || length > end + 1)
        { throw new InvalidDataException("音源服务器不支持分段读取"); }
        var count = (int)Math.Min(end - start + 1, length.Value - start);
        var buffer = new byte[count];
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await input.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
        return (buffer, length.Value);
    }

    internal static bool TryRange(string header, long length, out long start, out long end)
    {
        start = 0; end = length - 1;
        if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || header.Contains(',')) { return false; }
        var pair = header[6..].Split('-');
        if (pair.Length != 2) { return false; }
        if (pair[0].Length == 0)
        {
            if (!long.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) || suffix <= 0) { return false; }
            start = Math.Max(0, length - suffix);
        }
        else
        {
            if (!long.TryParse(pair[0], NumberStyles.None, CultureInfo.InvariantCulture, out start)) { return false; }
            if (pair[1].Length > 0 && !long.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out end)) { return false; }
            end = Math.Min(end, length - 1);
        }
        return start >= 0 && start < length && end >= start;
    }

    private static Task HeadersAsync(NetworkStream stream, string headers, CancellationToken ct) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(headers), ct).AsTask();
    private static Task ErrorAsync(NetworkStream stream, int status, string reason, CancellationToken ct) =>
        HeadersAsync(stream, $"HTTP/1.1 {status} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", ct);

    public void Dispose()
    {
        lock (_startLock)
        {
            if (_disposed) { return; }
            _disposed = true;
            _shutdown.Cancel();
            _listener?.Stop();
            foreach (var pair in _sessions)
            { if (_sessions.TryRemove(pair.Key, out var session)) { session.Dispose(); } }
            if (_ownsHttp) { _http.Dispose(); }
        }
    }

    private sealed class Session(Uri url, EKeyAudioDecryptor decryptor, byte[] first, long length, string contentType) : IDisposable
    {
        private readonly object _lock = new();
        private int _readers;
        private bool _disposed;
        public Uri Url { get; } = url;
        public EKeyAudioDecryptor Decryptor { get; } = decryptor;
        public byte[] First { get; } = first;
        public long Length { get; } = length;
        public string ContentType { get; } = contentType;
        public CancellationTokenSource Cancellation { get; } = new();
        public bool TryAcquire()
        { lock (_lock) { if (_disposed) { return false; } _readers++; return true; } }
        public void Release()
        { lock (_lock) { _readers--; if (_disposed && _readers == 0) { Decryptor.Dispose(); } } }
        public void Dispose()
        { lock (_lock) { if (_disposed) { return; } _disposed = true; Cancellation.Cancel(); if (_readers == 0) { Decryptor.Dispose(); } } }
    }
}
