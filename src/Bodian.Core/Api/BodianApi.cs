using System.Globalization;
using System.Text.Json;
using Bodian.Core.Api.Dto;
using Bodian.Core.Api.Dto.Requests;
using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Api;

/// <inheritdoc cref="IBodianApi" />
public sealed class BodianApi : IBodianApi
{
    private readonly IBodianTransport _transport;
    private readonly BodianSession _session;
    private readonly IDeviceIdentity _device;
    private readonly ILogger<BodianApi> _logger;

    public BodianApi(
        IBodianTransport transport,
        BodianSession session,
        IDeviceIdentity device,
        ILogger<BodianApi>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(device);

        _transport = transport;
        _session = session;
        _device = device;
        _logger = logger ?? NullLogger<BodianApi>.Instance;
    }

    public async Task<PagedResult<Track>> SearchAsync(
        string keyword,
        PagedCursor cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        ArgumentNullException.ThrowIfNull(cursor);

        return await PagedList.FetchNextAsync(
            cursor,
            async (paging, token) =>
            {
                var query = new List<KeyValuePair<string, string>>(paging)
                {
                    new("keyword", keyword),
                    new("correct", "1"),
                };

                var envelope = await _transport.SendAsync(
                    new BodianRequest
                    {
                        Path = Endpoints.SearchMusicList,
                        Query = query,
                        Signed = true,
                    },
                    BodianJsonContext.Default.SearchListPayload,
                    token).ConfigureAwait(false);

                var payload = envelope.Data;
                var items = payload?.ResultList?.Select(MapTrack).ToArray() ?? [];

                _logger.LogInformation(
                    "搜索「{Keyword}」返回 {Count} 条（offset={Offset}）",
                    keyword,
                    items.Length,
                    cursor.Offset);

                return new PagedResult<Track>(items, cursor.Offset, cursor.PageSize, payload?.Total);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Track?> GetTrackAsync(long musicId, CancellationToken cancellationToken = default)
    {
        EnsureMusicId(musicId);

        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.MusicInfo,
                Query = [MusicIdPair(musicId)],
                Signed = true,
            },
            BodianJsonContext.Default.TrackDto,
            cancellationToken).ConfigureAwait(false);

        return envelope.Data is { } dto ? MapTrack(dto) : null;
    }

    public async Task<PlaybackResolution> ResolvePlaybackAsync(
        Track track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        var right = await CheckRightAsync(track.Id, cancellationToken).ConfigureAwait(false);

        if (right is null)
        {
            return new PlaybackResolution.Denied(PlaybackDenialReason.TrackUnavailable);
        }

        return right.Status switch
        {
            // 只能试听：地址来自 checkRight 的 audition，不走 audioUrl。
            3 => ResolveAudition(right),

            // 无权限。未登录与已登录要分开，UI 一个引导登录、一个如实告知。
            7 => new PlaybackResolution.Denied(
                _session.IsAuthenticated
                    ? PlaybackDenialReason.NoPermission
                    : PlaybackDenialReason.NotAuthenticated),

            _ => await ResolveFullAsync(track, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// 播放权限检查。<b>每首歌只调一次</b>：这个端点没有 <c>br</c> / <c>format</c> 参数，
    /// 结果与档位无关，放进逐档循环是多余的请求。
    /// </summary>
    private async Task<CheckRightDto?> CheckRightAsync(long musicId, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(
            new CheckRightBody { MusicId = musicId },
            BodianJsonContext.Default.CheckRightBody);

        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.CheckRight,
                Query = [MusicIdPair(musicId), new KeyValuePair<string, string>("freeSign", "")],
                JsonBody = body,
                Signed = true,
            },
            BodianJsonContext.Default.CheckRightDto,
            cancellationToken).ConfigureAwait(false);

        return envelope.Data;
    }

    /// <summary>把 <c>status=3</c> 的试听信息转成领域模型。</summary>
    private static PlaybackResolution ResolveAudition(CheckRightDto right)
    {
        var audition = right.Audition;

        // 服务端说只能试听却没给片段信息，等同于不能播。
        if (audition is null)
        {
            return new PlaybackResolution.Denied(PlaybackDenialReason.AuditionUnavailable);
        }

        // 只认 url / https 这对明文地址，不要 car_url —— 那是车机 CDN。
        var url = ToHttpUri(audition.Https) ?? ToHttpUri(audition.Url);

        if (url is null)
        {
            return new PlaybackResolution.Denied(PlaybackDenialReason.AuditionUnavailable);
        }

        var source = new AudioSource
        {
            Url = url,
            RequestedQuality = null,
            Format = audition.Format ?? "unknown",
            BitrateKbps = audition.Bitrate,
        };

        // audition 的 start / end 单位是**秒**（同一个响应里 payInfo.refrain_* 却是毫秒，别混）。
        return new PlaybackResolution.AuditionOnly(
            source,
            TimeSpan.FromSeconds(audition.StartSeconds),
            TimeSpan.FromSeconds(audition.EndSeconds));
    }

    /// <summary>
    /// 有完整权限时取音源地址。
    /// </summary>
    /// <remarks>
    /// <b>只请求一次，不做逐档重试。</b> 服务端降级时业务码仍是 200、给的就是它能给的最好档位，
    /// 再往下试只会拿到同一个地址。降级由 <see cref="AudioSource.WasDowngraded"/> 如实上报。
    /// </remarks>
    private async Task<PlaybackResolution> ResolveFullAsync(Track track, CancellationToken cancellationToken)
    {
        if (!track.HasPlayableQuality)
        {
            return new PlaybackResolution.Denied(PlaybackDenialReason.NoUsableQuality);
        }

        var quality = track.AvailableQualities[0];
        var br = AudioQualityTable.RequestBitrate(quality);
        var deviceId = _device.Value;

        // ★ 注意 AudioUrlBody 里没有 format 字段：传 format=flac 会被静默降级。
        var body = JsonSerializer.Serialize(
            new AudioUrlBody { DevId = deviceId, MusicId = track.Id, Br = br },
            BodianJsonContext.Default.AudioUrlBody);

        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.AudioUrl,
                Query =
                [
                    new KeyValuePair<string, string>("devId", deviceId),
                    MusicIdPair(track.Id),
                    new KeyValuePair<string, string>("br", br),
                    new KeyValuePair<string, string>("freeSign", ""),
                ],
                JsonBody = body,
                Signed = true,
                AcceptedCodes = [BodianErrorCode.NotPlayable, BodianErrorCode.TrackOffline],
            },
            BodianJsonContext.Default.AudioUrlDto,
            cancellationToken).ConfigureAwait(false);

        if (envelope.Code == (int)BodianErrorCode.TrackOffline)
        {
            return new PlaybackResolution.Denied(PlaybackDenialReason.TrackUnavailable);
        }

        // 20018「没有解锁付费歌曲」：checkRight 说可播但取地址被拒，如实按无权限处理。
        if (envelope.Code != (int)BodianErrorCode.Success)
        {
            _logger.LogInformation(
                "取音源被拒：musicId={MusicId} 业务码={Code}",
                track.Id,
                envelope.Code);

            return new PlaybackResolution.Denied(PlaybackDenialReason.NoPermission);
        }

        var data = envelope.Data;
        var url = ToHttpUri(data?.AudioHttpsUrl) ?? ToHttpUri(data?.AudioUrl);

        if (data is null || url is null)
        {
            return new PlaybackResolution.Denied(PlaybackDenialReason.NoStreamUrl);
        }

        var source = new AudioSource
        {
            Url = url,
            RequestedQuality = quality,
            Format = data.Format ?? "unknown",
            BitrateKbps = data.Bitrate,
        };

        if (source.WasDowngraded)
        {
            _logger.LogInformation(
                "音源被降级：musicId={MusicId} 请求 {Requested}，实得 {Format} {Bitrate}k",
                track.Id,
                br,
                source.Format,
                source.BitrateKbps);
        }

        return new PlaybackResolution.Playable(source);
    }

    private static Track MapTrack(TrackDto dto)
    {
        var (requiresVip, requiresPurchase) = PayTypeReader.Resolve(dto.PayInfo);

        return new Track
        {
            Id = dto.Id,
            Title = dto.Name ?? dto.SongName ?? "(未知曲目)",
            ArtistText = dto.Artist ?? JoinArtists(dto.Artists),
            Artists = dto.Artists?.Select(a => new TrackArtist(a.Id, a.Name ?? "", ToHttpUri(a.Pic))).ToArray() ?? [],
            AlbumName = dto.Album,

            // 120px 那张更省流量，列表里够用；缺了才退回大图。
            CoverImage = ToHttpUri(dto.AlbumPic120) ?? ToHttpUri(dto.AlbumPic),

            Duration = TimeSpan.FromSeconds(dto.DurationSeconds),
            AvailableQualities = AudioQualityTable.BuildRequestChain(
                dto.Audios?.Select(a => a.Level) ?? []),
            RequiresVip = requiresVip,
            RequiresPurchase = requiresPurchase,
        };
    }

    private static string JoinArtists(TrackArtistDto[]? artists) =>
        artists is { Length: > 0 }
            ? string.Join("、", artists.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n)))
            : "";

    private static KeyValuePair<string, string> MusicIdPair(long musicId) =>
        new("musicId", musicId.ToString(CultureInfo.InvariantCulture));

    private static void EnsureMusicId(long musicId)
    {
        // 协议约束：musicId 必须匹配 ^\d{1,20}$ 且不能全为 0。
        if (musicId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(musicId), musicId, "musicId 必须是正数");
        }
    }

    /// <summary>只接受 http / https 且不带 userinfo 的地址。规则见 <see cref="HttpUrl"/>。</summary>
    private static Uri? ToHttpUri(string? value) => HttpUrl.TryParse(value);
}
