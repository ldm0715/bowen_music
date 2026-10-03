using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Playback;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Services.Implementations;

// Read-only audit: never print account identifiers, signed URLs, response bodies or EKey contents.
var credential = new DpapiCredentialStore().Load();
if (credential is null || !File.Exists(AppPaths.DeviceIdFile))
{
    Console.WriteLine("Saved session/device unavailable");
    return 1;
}
var session = BodianSession.CreateAnonymous();
session.Set(credential.Uid, credential.Token);
var device = new ReadOnlyDevice(File.ReadAllText(AppPaths.DeviceIdFile).Trim());
var options = new BodianTransportOptions();
using var handler = new AudioAuditHandler
{
    NativeRequest = args.Contains("--native"),
    InnerHandler = BodianHttpTransport.CreateHandler(options),
};
using var transport = new BodianHttpTransport(handler, options, session, device);
var id = args.Length > 0 && long.TryParse(args[0], out var parsed) ? parsed : 228908;
var track = await transport.SendAsync(new BodianRequest
{
    Path = Endpoints.MusicInfo, Query = [new("musicId", id.ToString())], Signed = true,
}, BodianJsonContext.Default.JsonElement);
if (track.Data.ValueKind != JsonValueKind.Object || !track.Data.TryGetProperty("audios", out var audios))
{ Console.WriteLine("Track audio entries unavailable"); return 1; }
var right = await transport.SendAsync(new BodianRequest
{
    Path = Endpoints.CheckRight, Query = [new("musicId", id.ToString()), new("freeSign", "")],
    JsonBody = JsonSerializer.Serialize(new { musicId = id }), Signed = true,
}, BodianJsonContext.Default.JsonElement);
if (!right.Data.TryGetProperty("status", out var status) || status.GetInt32() is 3 or 7)
{ Console.WriteLine("Full stream permission unavailable"); return 1; }
using var cdn = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
foreach (var variant in audios.EnumerateArray().Where(v =>
    v.TryGetProperty("format", out var format) && format.GetString() is "mflac" or "mgg"))
{
    var format = variant.GetProperty("format").GetString();
    var br = $"{variant.GetProperty("bitrate").GetString()}k{format}";
    Console.WriteLine($"Research level={variant.GetProperty("level").GetString()}, br={br}");
    try
    {
        var envelope = await transport.SendAsync(new BodianRequest
        {
            Path = Endpoints.AudioUrl,
            Query = [new("devId", device.Value), new("musicId", id.ToString()), new("br", br), new("freeSign", "")],
            JsonBody = JsonSerializer.Serialize(new { devId = device.Value, musicId = id, br }), Signed = true,
            AcceptedCodes = [BodianErrorCode.NotPlayable, BodianErrorCode.TrackOffline],
        }, BodianJsonContext.Default.JsonElement);
        if (envelope.Code != 200 || envelope.Data.ValueKind != JsonValueKind.Object)
        { Console.WriteLine($"Stream rejected: code={envelope.Code}"); continue; }
        var data = envelope.Data;
        var url = data.TryGetProperty("audioHttpsUrl", out var https) && https.GetString() is { Length: > 0 } secured
            ? secured : data.GetProperty("audioUrl").GetString();
        var source = new ResearchAudioSource
        {
            Url = new Uri(url!), Format = data.GetProperty("format").GetString()!,
            EKey = data.TryGetProperty("ekey", out var keyField) ? keyField.GetString() : null,
        };
        var size = data.TryGetProperty("size", out var sizeField) ? AudioQualityTable.ParseSize(sizeField.GetString()) : 0;
        Console.WriteLine($"Served format={source.Format}, ekeyLength={source.EKey?.Length ?? 0}, size={size}");
        if (!source.IsEncrypted) { continue; }
        if (args.Contains("--oracle") && source.EKey is { Length: > 0 } ekey)
        {
            var nativeScript = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../reverse/tools/ekey_native_verify.py"));
            if (File.Exists(nativeScript))
            {
                using var headRequest = new HttpRequestMessage(HttpMethod.Get, source.Url);
                headRequest.Headers.Range = new RangeHeaderValue(0, 511);
                using var headResponse = await cdn.SendAsync(headRequest);
                var cipherHead = Convert.ToBase64String(await headResponse.Content.ReadAsByteArrayAsync());
                var start = new ProcessStartInfo("python")
                {
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    CreateNoWindow = true, UseShellExecute = false,
                };
                start.ArgumentList.Add(nativeScript);
                using var process = Process.Start(start)!;
                await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { ekey, cipherHead }));
                process.StandardInput.Close();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                // The oracle returns only booleans/lengths. Its stderr may include local paths, so omit it.
                Console.WriteLine(process.ExitCode == 0 ? $"Native verification: {output.Trim()}" : "Native oracle unavailable or failed");
            }
        }
        using (var tailRequest = new HttpRequestMessage(HttpMethod.Get, source.Url))
        {
            tailRequest.Headers.Range = new RangeHeaderValue(null, 8192);
            using var tailResponse = await cdn.SendAsync(tailRequest);
            var tail = await tailResponse.Content.ReadAsByteArrayAsync();
            if (tail.Length >= 8)
            {
                var v1Length = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(tail.Length - 4));
                Console.WriteLine($"Footer QTag={tail.AsSpan(tail.Length - 4).SequenceEqual("QTag"u8)}, musicex={tail.AsSpan(tail.Length - 8).SequenceEqual("musicex\0"u8)}, plausibleV1Length={v1Length is > 0 and <= 1024}");
            }
        }
        using var resolver = new EncryptedAudioSourceResolver();
        try
        {
            using var prepared = await resolver.PrepareAsync(source);
            Console.WriteLine($"Media header validated, length={prepared.SizeBytes}");
        }
        catch (InvalidDataException ex) { Console.WriteLine($"Media preparation failed: {ex.Message}"); }
    }
    catch (BodianApiException ex) { Console.WriteLine($"API rejected request: code={ex.RawCode}"); }
    catch (Exception ex) { Console.WriteLine($"Request failed: {ex.GetType().Name}"); }
}
return 0;

sealed class ReadOnlyDevice(string value) : IDeviceIdentity { public string Value { get; } = value; }

sealed class AudioAuditHandler : DelegatingHandler
{
    public bool NativeRequest { get; init; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var isAudio = request.RequestUri!.AbsolutePath.EndsWith("audioUrl", StringComparison.Ordinal);
        if (NativeRequest && isAudio)
        {
            var uri = request.RequestUri.OriginalString;
            var pairs = uri[(uri.IndexOf('?') + 1)..].Split('&').Where(p => !p.StartsWith("sign=", StringComparison.Ordinal)).ToList();
            var br = pairs.Single(p => p.StartsWith("br=", StringComparison.Ordinal))[3..];
            pairs.Add("format=" + br[(br.IndexOf('k') + 1)..]);
            var query = string.Join("&", pairs);
            // Native libkpk: MD5(salt + sorted alphanumeric(query) + /api/path), no JSON body.
            var signature = Bodian.Probe.BodianSigner.SignRaw(request.RequestUri.AbsolutePath, query, null);
            request.RequestUri = new Uri(uri[..uri.IndexOf('?')] + "?" + query + "&sign=" + signature);
            request.Content?.Dispose(); request.Content = null;
            request.Headers.Remove("ver"); request.Headers.TryAddWithoutValidation("ver", "5.9.8");
            request.Headers.Remove("plat"); request.Headers.TryAddWithoutValidation("plat", "android");
        }
        var response = await base.SendAsync(request, ct);
        if (isAudio)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            { Console.WriteLine($"audioUrl fields: {string.Join(",", data.EnumerateObject().Select(p => p.Name))}"); }
            else if (doc.RootElement.TryGetProperty("code", out var code))
            { Console.WriteLine($"audioUrl code={code.GetRawText()}"); }
        }
        return response;
    }
}
