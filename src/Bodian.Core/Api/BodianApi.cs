using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bodian.Core.Api.Dto;
using Bodian.Core.Api.Dto.Requests;
using Bodian.Core.Api.Paging;
using Bodian.Core.Lyrics;
using Bodian.Core.Models;
using Bodian.Core.Models.Home;
using Bodian.Core.Models.Lyrics;
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
                        Signed = false,
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

    public async Task<IReadOnlyList<SearchResultSection>> SearchComprehensiveAsync(string keyword, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        var envelope = await _transport.SendAsync(new BodianRequest
        {
            Path = Endpoints.SearchComprehensive,
            Query = [new("keyword", keyword)],
            Signed = false,
        }, BodianJsonContext.Default.ComprehensiveSearchPayload, cancellationToken).ConfigureAwait(false);
        var sections = new List<SearchResultSection>();
        foreach (var section in envelope.Data?.Content ?? [])
        {
            if (section.Tracks is { Length: > 0 } tracks)
                sections.Add(new() { Category = SearchResultCategory.Tracks, Tracks = tracks.Select(MapTrack).ToArray() });
            if (section.Playlists is { Length: > 0 } playlists)
                sections.Add(new() { Category = SearchResultCategory.Playlists, Playlists = playlists.Select(MapSearchPlaylist).ToArray() });
            if (section.Albums is { Length: > 0 } albums)
                sections.Add(new() { Category = SearchResultCategory.Albums, Albums = albums.Select(MapAlbum).ToArray() });
            if (section.Artists is { Length: > 0 } artists)
                sections.Add(new() { Category = SearchResultCategory.Artists, Artists = artists.Select(MapSearchArtist).ToArray() });
        }
        return sections;
    }

    private static Playlist MapSearchPlaylist(SearchPlaylistDto dto) => new()
    {
        Id = dto.Id, Name = dto.Name ?? "(未命名歌单)", CoverImage = ToHttpUri(dto.Pic),
        MusicCount = dto.MusicCount, SourceType = dto.Source,
    };

    private static Artist MapSearchArtist(SearchArtistDto dto) => new()
    {
        Id = dto.Id, Name = dto.Name ?? "(未命名歌手)", CoverImage = ToHttpUri(dto.Pic),
        SongCount = dto.SongCount, AlbumCount = dto.AlbumCount,
    };

    /// <summary>
    /// 歌手详情的条目。
    /// </summary>
    /// <remarks>
    /// 名字与头像在导航过来时已经知道了，正常取到详情就会被这份覆盖成服务端的最新值。
    /// 名字为空时保留占位而不是空串 —— 详情页头部留一片空白比一个明显是占位的词更难排查。
    /// <para>
    /// <b>id 由调用方传入</b>：响应里也有 <c>id</c>，但本项目只需要「我请求的那个」，
    /// 多映射一个字段就多一处可能与请求不一致的来源。
    /// </para>
    /// </remarks>
    private static Artist MapArtistInfo(long artistId, ArtistInfoDto dto) => new()
    {
        Id = artistId,
        Name = string.IsNullOrWhiteSpace(dto.Name) ? "(未命名歌手)" : dto.Name,
        CoverImage = ToHttpUri(dto.Pic),
        SongCount = dto.MusicCount,
        AlbumCount = dto.AlbumCount,
        AliasName = dto.AliasName ?? "",
        FansCount = dto.FansCount,
        Description = dto.Description ?? "",
    };

    public Task<PagedResult<Album>> SearchAlbumsAsync(string keyword, PagedCursor cursor, CancellationToken cancellationToken = default) =>
        SearchPageAsync(keyword, Endpoints.SearchAlbumList, cursor, BodianJsonContext.Default.SearchAlbumsPayload, MapAlbum, cancellationToken);

    public Task<PagedResult<Playlist>> SearchPlaylistsAsync(string keyword, PagedCursor cursor, CancellationToken cancellationToken = default) =>
        SearchPageAsync(keyword, Endpoints.SearchPlaylistList, cursor, BodianJsonContext.Default.SearchPlaylistsPayload,
            MapSearchPlaylist, cancellationToken);

    public Task<PagedResult<Artist>> SearchArtistsAsync(string keyword, PagedCursor cursor, CancellationToken cancellationToken = default) =>
        SearchPageAsync(keyword, Endpoints.SearchArtistList, cursor, BodianJsonContext.Default.SearchArtistsPayload,
            MapSearchArtist, cancellationToken);

    public async Task<IReadOnlyList<string>> GetSearchSuggestionsAsync(string keyword, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        var envelope = await _transport.SendAsync(new BodianRequest
        {
            Path = Endpoints.SearchTips, Query = [new("keyword", keyword)], Signed = false,
        }, BodianJsonContext.Default.SearchTipsPayload, cancellationToken).ConfigureAwait(false);
        return envelope.Data?.ResultList?.Select(dto => dto.Word).OfType<string>()
            .Where(word => !string.IsNullOrWhiteSpace(word)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
    }

    public async Task<IReadOnlyList<SearchHotWord>> GetSearchHotWordsAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await _transport.SendAsync(new BodianRequest { Path = Endpoints.SearchTopics, Signed = false },
            BodianJsonContext.Default.SearchTopicsPayload, cancellationToken).ConfigureAwait(false);
        return envelope.Data?.HotWords?.Where(dto => !string.IsNullOrWhiteSpace(dto.Keyword))
            .OrderBy(dto => dto.Rank).Select(dto => new SearchHotWord(dto.Keyword!, dto.Rank)).ToArray() ?? [];
    }

    public Task<PagedResult<Track>> GetArtistTracksAsync(long artistId, PagedCursor cursor, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(artistId);
        return FetchSearchPageAsync(Endpoints.ArtistTracks(artistId), [], cursor,
            BodianJsonContext.Default.SearchListPayload, payload => payload.ResultList?.Select(MapTrack).ToArray() ?? [], payload => payload.Total, cancellationToken);
    }

    public Task<PagedResult<Album>> GetArtistAlbumsAsync(long artistId, PagedCursor cursor, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(artistId);
        return FetchSearchPageAsync(Endpoints.ArtistAlbums(artistId), [], cursor,
            BodianJsonContext.Default.SearchAlbumsPayload, payload => payload.ResultList?.Select(MapAlbum).ToArray() ?? [], payload => payload.Total, cancellationToken);
    }

    public async Task<Artist?> GetArtistInfoAsync(long artistId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(artistId);

        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.ArtistInfo(artistId),
                Query = [],
                Signed = false,
            },
            BodianJsonContext.Default.ArtistInfoPayload,
            cancellationToken).ConfigureAwait(false);

        return envelope.Data?.ArtistInfo is { } dto ? MapArtistInfo(artistId, dto) : null;
    }

    private Task<PagedResult<TModel>> SearchPageAsync<TDto, TModel>(string keyword, string path, PagedCursor cursor,
        JsonTypeInfo<SearchPayload<TDto>> typeInfo, Func<TDto, TModel> map, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        return FetchSearchPageAsync(path, [new("keyword", keyword)], cursor, typeInfo,
            payload => payload.ResultList?.Select(map).ToArray() ?? [], payload => payload.Total, cancellationToken);
    }

    private Task<PagedResult<TModel>> FetchSearchPageAsync<TPayload, TModel>(string path,
        IReadOnlyList<KeyValuePair<string, string>> query, PagedCursor cursor, JsonTypeInfo<TPayload> typeInfo,
        Func<TPayload, TModel[]> items, Func<TPayload, int?> total, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        return PagedList.FetchNextAsync(cursor, async (paging, token) =>
        {
            var envelope = await _transport.SendAsync(new BodianRequest
            {
                Path = path, Query = [.. paging, .. query], Signed = false,
            }, typeInfo, token).ConfigureAwait(false);
            return new PagedResult<TModel>(envelope.Data is { } data ? items(data) : [], cursor.Offset, cursor.PageSize,
                envelope.Data is { } payload ? total(payload) : null);
        }, cancellationToken);
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
        => await ResolvePlaybackCoreAsync(track, null, cancellationToken).ConfigureAwait(false);

    public Task<PlaybackResolution> ResolvePlaybackAsync(
        Track track, AudioQuality preferredQuality, CancellationToken cancellationToken = default)
        => ResolvePlaybackCoreAsync(track, preferredQuality, cancellationToken);

    private async Task<PlaybackResolution> ResolvePlaybackCoreAsync(
        Track track, AudioQuality? preferredQuality, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (preferredQuality is { } preferred && !Enum.IsDefined(preferred))
        {
            throw new ArgumentOutOfRangeException(nameof(preferredQuality));
        }

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

            _ => await ResolveFullAsync(track, preferredQuality, cancellationToken).ConfigureAwait(false),
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

        if (!AudioQualityTable.IsPlayableFormat(audition.Format))
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
    /// 按用户偏好选一个受支持的明文音源，只请求一次。
    /// 服务端可能在业务码 200 时降级，由 <see cref="AudioSource.WasDowngraded"/> 如实上报。
    /// </remarks>
    private async Task<PlaybackResolution> ResolveFullAsync(Track track, AudioQuality? preferredQuality, CancellationToken cancellationToken)
    {
        if (!track.HasPlayableQuality)
        {
            return new PlaybackResolution.Denied(PlaybackDenialReason.NoUsableQuality);
        }

        var variant = AudioQualityTable.SelectVariant(track, preferredQuality);
        var selected = variant?.Quality ?? track.AvailableQualities
            .Where(q => Enum.IsDefined(q) && (preferredQuality is null || q <= preferredQuality))
            .OrderDescending().Cast<AudioQuality?>().FirstOrDefault();
        if (selected is not { } quality)
        {
            return new PlaybackResolution.Denied(PlaybackDenialReason.NoUsableQuality);
        }
        var br = variant?.RequestBitrate ?? AudioQualityTable.RequestBitrate(quality);
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

        // 高级档位依赖官方移动端；异常返回的加密或授权格式也不得交给播放引擎。
        if (!AudioQualityTable.IsPlayableFormat(data.Format))
        {
            return new PlaybackResolution.Denied(PlaybackDenialReason.NoUsableQuality);
        }

        var source = new AudioSource
        {
            Url = url,
            RequestedQuality = quality,
            RequestedVariant = variant,
            Format = data.Format ?? "unknown",
            BitrateKbps = data.Bitrate,
            SizeBytes = AudioQualityTable.ParseSize(data.Size),
        };
        if (source.SizeBytes <= 0)
        {
            var matching = track.AudioVariants.FirstOrDefault(v =>
                AudioQualityTable.IsSupportedVariant(v) && AudioQualityTable.MatchesServed(v.Format, v.BitrateKbps, source.Format, source.BitrateKbps));
            source = source with { SizeBytes = matching?.SizeBytes ?? 0 };
        }

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

    // ── 曲库 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 账号歌单（自建与「我喜欢」）在 <c>musicList</c> 里的 <c>source</c> 取值。
    /// </summary>
    /// <remarks>
    /// 文档 2.2：<c>source</c> 取 1–6，默认 4（公开集合）；自建歌单与「我喜欢」**都是 5**。
    /// 两者共用同一个值，所以取曲目时不必先判断歌单是哪种。
    /// </remarks>
    private const int AccountPlaylistSource = 5;

    public async Task<IReadOnlyList<Playlist>> GetCreatedPlaylistsAsync(
        CancellationToken cancellationToken = default)
    {
        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.PlaylistUserCreate,
                Query = [UidPair()],
                Signed = true,
            },
            BodianJsonContext.Default.PlaylistListPayload,
            cancellationToken).ConfigureAwait(false);

        var playlists = envelope.Data?.PlayLists?.Select(MapPlaylist).ToArray() ?? [];

        _logger.LogInformation("自建歌单 {Count} 个", playlists.Length);

        return playlists;
    }

    /// <inheritdoc cref="IBodianApi.CreatePlaylistAsync"/>
    public async Task<long> CreatePlaylistAsync(string name, bool isPrivate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var trimmed = name.Trim();

        if (trimmed.Length == 0)
        {
            throw new ArgumentException("歌单名不能为空。", nameof(name));
        }

        RequireAuthenticated();
        var revision = _session.Revision;
        var body = new CreatePlaylistBody { Name = trimmed, Private = isPrivate };

        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.PlaylistCrud,
                Verb = BodianHttpVerb.Post,
                Signed = true,
                JsonBody = JsonSerializer.Serialize(body, BodianJsonContext.Default.CreatePlaylistBody),
            },
            BodianJsonContext.Default.PlaylistDto,
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "新建歌单请求已受理：名字 {Name}，隐私 {IsPrivate}，接口 {Path}，业务码 {Code}，reqId {RequestId}",
            trimmed, isPrivate, Endpoints.PlaylistCrud, envelope.Code, envelope.RequestId);

        if (revision != _session.Revision)
        {
            throw new InvalidOperationException("登录状态已改变，请重新检查歌单。");
        }

        // 实测回执只有 id（文档 2.4）：没有 id 就是没建成，
        // 不能返回一个假的 0 让界面插进一行空歌单。
        if (envelope.Data is not { Id: > 0 } created)
        {
            throw new InvalidOperationException("服务端没有返回新歌单的 id。");
        }

        return created.Id;
    }

    /// <inheritdoc cref="IBodianApi.DeletePlaylistAsync"/>
    public async Task DeletePlaylistAsync(long playlistId, CancellationToken cancellationToken = default)
    {
        EnsurePlaylistId(playlistId);
        RequireAuthenticated();
        var revision = _session.Revision;

        // 服务端收的是数组（实测一次能删多个），本项目一次只删一个。
        var body = new DeletePlaylistBody { PlaylistIds = [playlistId] };

        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.PlaylistCrud,
                Verb = BodianHttpVerb.Delete,
                Signed = true,
                JsonBody = JsonSerializer.Serialize(body, BodianJsonContext.Default.DeletePlaylistBody),
            },
            BodianJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "删除歌单请求已受理：歌单 {PlaylistId}，接口 {Path}，业务码 {Code}，reqId {RequestId}",
            playlistId, Endpoints.PlaylistCrud, envelope.Code, envelope.RequestId);

        if (revision != _session.Revision)
        {
            throw new InvalidOperationException("登录状态已改变，请重新检查歌单。");
        }
    }

    /// <inheritdoc cref="IBodianApi.UpdatePlaylistAsync"/>
    public async Task UpdatePlaylistAsync(
        long playlistId,
        string name,
        string description,
        string pic,
        IReadOnlyList<int> categoryIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(pic);
        ArgumentNullException.ThrowIfNull(categoryIds);

        EnsurePlaylistId(playlistId);

        // 服务端不校验空白名（新建时的实测结论），这条必须客户端自己挡。
        var trimmed = name.Trim();

        if (trimmed.Length == 0)
        {
            throw new ArgumentException("歌单名不能为空。", nameof(name));
        }

        RequireAuthenticated();
        var revision = _session.Revision;

        var body = new UpdatePlaylistBody
        {
            Id = playlistId,
            Name = trimmed,
            Description = description,
            Pic = pic,
            CategoryList = [.. categoryIds],
        };

        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.PlaylistCrud,
                Verb = BodianHttpVerb.Put,
                Signed = true,
                JsonBody = JsonSerializer.Serialize(body, BodianJsonContext.Default.UpdatePlaylistBody),
            },
            BodianJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "编辑歌单请求已受理：歌单 {PlaylistId}，名字 {Name}，标签 {TagCount} 个，接口 {Path}，业务码 {Code}，reqId {RequestId}",
            playlistId, trimmed, categoryIds.Count, Endpoints.PlaylistCrud, envelope.Code, envelope.RequestId);

        if (revision != _session.Revision)
        {
            throw new InvalidOperationException("登录状态已改变，请重新检查歌单。");
        }
    }

    /// <inheritdoc cref="IBodianApi.UploadPlaylistCoverAsync"/>
    public async Task<string> UploadPlaylistCoverAsync(
        long playlistId,
        byte[] imageBytes,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        if (imageBytes.Length == 0)
        {
            throw new ArgumentException("封面内容为空。", nameof(imageBytes));
        }

        EnsurePlaylistId(playlistId);
        RequireAuthenticated();
        var revision = _session.Revision;
        var path = Endpoints.PlaylistUploadPic(playlistId);

        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = path,
                Verb = BodianHttpVerb.Post,
                Signed = true,
                File = new BodianFormFile("file", fileName, contentType, imageBytes),
            },
            BodianJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "上传歌单封面请求已受理：歌单 {PlaylistId}，{Bytes} 字节，接口 {Path}，业务码 {Code}，reqId {RequestId}",
            playlistId, imageBytes.Length, path, envelope.Code, envelope.RequestId);

        if (revision != _session.Revision)
        {
            throw new InvalidOperationException("登录状态已改变，请重新检查歌单。");
        }

        var url = ExtractImageUrl(envelope.Data);

        // 拿不到地址就不能往下走 —— 调用方是要拿它去填 pic 的，填个空值等于把封面清掉。
        if (url.Length == 0)
        {
            throw new InvalidOperationException("服务端没有返回封面的地址。");
        }

        return url;
    }

    /// <summary>
    /// 从封面上传的回执里取封面地址。
    /// </summary>
    /// <remarks>
    /// <b>键名未实测</b>：反汇编只知道回执会经 <c>fromImgJson</c> 解析出一个 URL
    /// （<c>api_service.dart</c> 的 <c>uploadPlaylistImageToService</c>），具体键名没取到证据。
    /// 所以按候选键逐个试，并兼容 <c>data</c> 本身就是字符串的形态。
    /// </remarks>
    private static string ExtractImageUrl(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.String)
        {
            return data.GetString() ?? "";
        }

        if (data.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        foreach (var key in (string[])["imgUrl", "imgurl", "pic", "url", "cover", "coverUrl"])
        {
            if (data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return "";
    }

    public async Task<Playlist?> GetLikedPlaylistAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.PlaylistFond,
                Query = [UidPair()],
                Signed = true,
            },
            BodianJsonContext.Default.PlaylistDto,
            cancellationToken).ConfigureAwait(false);

        // 账号没有红心歌单时对象里没有 id（文档 2.3），按「没有这个歌单」返回 null。
        return envelope.Data is { Id: > 0 } dto ? MapPlaylist(dto) : null;
    }

    public async Task<PagedResult<Track>> GetPlaylistTracksAsync(
        long playlistId,
        int source,
        PagedCursor cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        EnsurePlaylistId(playlistId);
        RequireAuthenticated();

        var sourceText = source.ToString(CultureInfo.InvariantCulture);

        return await PagedList.FetchNextAsync(
            cursor,
            async (paging, token) =>
            {
                var query = new List<KeyValuePair<string, string>>(paging)
                {
                    new("source", sourceText),
                };

                var envelope = await _transport.SendAsync(
                    new BodianRequest
                    {
                        Path = Endpoints.PlaylistTracks(playlistId),
                        Query = query,
                        Signed = true,
                    },
                    BodianJsonContext.Default.PlaylistTracksPayload,
                    token).ConfigureAwait(false);

                var payload = envelope.Data;
                var items = payload?.List?.Select(MapTrack).ToArray() ?? [];

                _logger.LogInformation(
                    "歌单 {PlaylistId} 返回 {Count} 首（offset={Offset}）",
                    playlistId,
                    items.Length,
                    cursor.Offset);

                return new PagedResult<Track>(items, cursor.Offset, cursor.PageSize, payload?.Total);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task AddPlaylistMusicAsync(long playlistId, IReadOnlyList<long> musicIds,
        CancellationToken cancellationToken = default) =>
        WritePlaylistMusicAsync(Endpoints.PlaylistMusic, playlistId, musicIds, "加歌", cancellationToken);

    public Task RemovePlaylistMusicAsync(long playlistId, IReadOnlyList<long> musicIds,
        CancellationToken cancellationToken = default) =>
        WritePlaylistMusicAsync(Endpoints.PlaylistMusicDelete, playlistId, musicIds, "删歌", cancellationToken);

    /// <summary>单次条数的上下限来自文档 2.4。</summary>
    private const int MaxPlaylistMusicBatch = 100;

    private async Task WritePlaylistMusicAsync(string path, long playlistId, IReadOnlyList<long> musicIds,
        string operation, CancellationToken cancellationToken)
    {
        EnsurePlaylistId(playlistId);
        ArgumentNullException.ThrowIfNull(musicIds);
        if (musicIds.Count is < 1 or > MaxPlaylistMusicBatch)
        {
            throw new ArgumentOutOfRangeException(nameof(musicIds), musicIds.Count,
                $"单次必须是 1–{MaxPlaylistMusicBatch} 首。");
        }

        foreach (var musicId in musicIds)
        {
            if (musicId <= 0) throw new ArgumentOutOfRangeException(nameof(musicIds), musicId, "曲目 id 必须是正数");
        }

        RequireAuthenticated();
        var revision = _session.Revision;
        var body = new PlaylistMusicBody { PlayListId = playlistId, MusicIdList = [.. musicIds] };
        var envelope = await _transport.SendAsync(new BodianRequest
        {
            Path = path,
            Verb = BodianHttpVerb.Post,
            Signed = true,
            JsonBody = JsonSerializer.Serialize(body, BodianJsonContext.Default.PlaylistMusicBody),
        }, BodianJsonContext.Default.JsonElement, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("歌单{Operation}请求已受理：歌单 {PlaylistId}，曲目 {MusicIds}，接口 {Path}，业务码 {Code}，reqId {RequestId}",
            operation, playlistId, string.Join(',', musicIds), path, envelope.Code, envelope.RequestId);
        if (revision != _session.Revision) throw new InvalidOperationException("登录状态已改变，请重新检查歌单。");
    }

    public async Task<ShareOutcome> ReportTrackShareAsync(long musicId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(musicId);

        static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

        // 四个参数都是 int。shareTo=5 是「复制链接」，但它同样计入分享数（文档 2.8）。
        var envelope = await _transport.SendAsync(new BodianRequest
        {
            Path = Endpoints.ShareText,
            Query =
            [
                new("shareTo", Text(Endpoints.ShareToCopyLink)),
                new("shareSource", Text(Endpoints.ShareSourceSong)),
                new("sourceId", musicId.ToString(CultureInfo.InvariantCulture)),
                new("playlistType", Text(Endpoints.SharePlaylistType)),
            ],
            Signed = true,
            AcceptedCodes = [BodianErrorCode.ShareUnsupported],
        }, BodianJsonContext.Default.JsonElement, cancellationToken).ConfigureAwait(false);

        var outcome = envelope.IsSuccess ? ShareOutcome.Succeeded
            : envelope.ErrorCode == BodianErrorCode.ShareUnsupported ? ShareOutcome.Unsupported
            : ShareOutcome.Failed;
        _logger.LogInformation(
            "分享上报：歌曲 {MusicId}，接口 {Path}，shareTo {ShareTo}，业务码 {Code}，结果 {Outcome}，reqId {RequestId}",
            musicId, Endpoints.ShareText, Endpoints.ShareToCopyLink, envelope.Code, outcome, envelope.RequestId);
        return outcome;
    }

    // ── 专辑 ────────────────────────────────────────────────────────────────

    public async Task<Album?> GetAlbumAsync(long albumId, CancellationToken cancellationToken = default)
    {
        if (albumId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(albumId), albumId, "albumId 必须是正数");
        }

        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.AlbumDetail(albumId),
                Query = [],
                Signed = false,
            },
            BodianJsonContext.Default.AlbumInfoPayload,
            cancellationToken).ConfigureAwait(false);

        return envelope.Data?.AlbumInfo is { } dto ? MapAlbum(dto) : null;
    }

    public async Task<PagedResult<Track>> GetAlbumTracksAsync(
        long albumId,
        PagedCursor cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        if (albumId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(albumId), albumId, "albumId 必须是正数");
        }

        return await PagedList.FetchNextAsync(
            cursor,
            async (paging, token) =>
            {
                var envelope = await _transport.SendAsync(
                    new BodianRequest
                    {
                        Path = Endpoints.AlbumTracks(albumId),
                        Query = new List<KeyValuePair<string, string>>(paging),
                        Signed = false,
                    },
                    BodianJsonContext.Default.AlbumTracksPayload,
                    token).ConfigureAwait(false);

                var payload = envelope.Data;
                var items = payload?.ResultList?.Select(MapTrack).ToArray() ?? [];

                _logger.LogInformation(
                    "专辑 {AlbumId} 返回 {Count} 首（offset={Offset}）",
                    albumId,
                    items.Length,
                    cursor.Offset);

                return new PagedResult<Track>(items, cursor.Offset, cursor.PageSize, payload?.Total);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PagedResult<Album>> GetMusicLibraryAlbumsAsync(
        string pTypeId,
        string cTypeId,
        MusicLibSort sort,
        PagedCursor cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cTypeId);
        ArgumentNullException.ThrowIfNull(cursor);

        var sortValue = sort.ToRequestValue();

        return await PagedList.FetchNextAsync(
            cursor,
            async (paging, token) =>
            {
                var query = new List<KeyValuePair<string, string>>(paging)
                {
                    new("pTypeId", pTypeId),
                    new("cTypeId", cTypeId),

                    // ★ 字符串 "1"/"2"，不是数字。传错的话服务端回 200 + 空 data。
                    new("sort", sortValue),
                };

                var envelope = await _transport.SendAsync(
                    new BodianRequest
                    {
                        Path = Endpoints.MusicLibraryAlbums,
                        Query = query,

                        // GET（参数走 query）。POST + JSON body 会 500。
                        Signed = false,
                    },
                    BodianJsonContext.Default.MusicLibraryAlbumsPayload,
                    token).ConfigureAwait(false);

                var payload = envelope.Data;
                var items = payload?.List?.Select(MapAlbum).ToArray() ?? [];

                _logger.LogInformation(
                    "乐库 {PType}/{CType}（sort={Sort}）返回 {Count} 张专辑（offset={Offset}）",
                    pTypeId,
                    cTypeId,
                    sortValue,
                    items.Length,
                    cursor.Offset);

                return new PagedResult<Album>(items, cursor.Offset, cursor.PageSize, payload?.Total);
            },
            cancellationToken).ConfigureAwait(false);
    }

    // ── 乐库（MusicLib）─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<MusicCategoryGroup>> GetMusicLibraryAsync(
        CancellationToken cancellationToken = default)
    {
        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                // 无参数；实测桌面请求头即可（不像 albums 那样必须移动端头）。
                Path = Endpoints.MusicLibraryNavigation,
                Query = [],
                Signed = false,
            },
            BodianJsonContext.Default.MusicLibraryNavDtoArray,
            cancellationToken).ConfigureAwait(false);

        var groups = envelope.Data?
            .Where(dto => !string.IsNullOrWhiteSpace(dto.PTypeName))
            .Select(dto => new MusicCategoryGroup(
                dto.Id ?? "",
                dto.PTypeName!,
                // internalTitle 里带换行（形如 "POP\nMUSIC"），界面上要压成一行。
                (dto.ExternalTitle ?? dto.InternalTitle ?? "").ReplaceLineEndings(" ").Trim(),
                dto.LongDesc ?? "",
                ToHttpUri(dto.CoverPic),
                dto.AlbumTotal,
                [.. (dto.ChildList ?? [])
                    .Where(child => !string.IsNullOrWhiteSpace(child.Name))
                    .Select(child => new MusicCategoryChild(
                        child.Id ?? "",
                        child.Name!,
                        child.LongDescTitle ?? "",
                        child.LongDesc ?? "",
                        child.AlbumVo is { } album ? MapAlbum(album) : null))]))
            .ToArray() ?? [];

        _logger.LogInformation(
            "乐库 {Groups} 个大类、共 {Children} 个子类",
            groups.Length,
            groups.Sum(group => group.Children.Count));

        return groups;
    }

    // ── 乐库（分类歌单）──────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CategoryGroup>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                // 无参数、不要求登录（实测匿名即可）。
                Path = Endpoints.CategoryList,
                Query = [],
                Signed = false,
            },
            BodianJsonContext.Default.CategoryListPayload,
            cancellationToken).ConfigureAwait(false);

        var groups = envelope.Data?.Categories?
            .Where(dto => !string.IsNullOrWhiteSpace(dto.Name))
            .Select(dto => new CategoryGroup(
                dto.Name!,
                [.. (dto.SubCategories ?? [])
                    .Where(sub => sub.Id > 0 && !string.IsNullOrWhiteSpace(sub.Name))
                    .Select(sub => new MusicCategory(sub.Id, sub.Name!))]))
            .Where(group => group.SubCategories.Count > 0)
            .ToArray() ?? [];

        _logger.LogInformation(
            "乐库 {Groups} 组分类、共 {Categories} 个子分类",
            groups.Length,
            groups.Sum(group => group.SubCategories.Count));

        return groups;
    }

    public async Task<PagedResult<Playlist>> GetCategoryPlaylistsAsync(
        long categoryId,
        PagedCursor cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        if (categoryId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(categoryId), categoryId, "categoryId 必须是正数");
        }

        return await PagedList.FetchNextAsync(
            cursor,
            async (paging, token) =>
            {
                var envelope = await _transport.SendAsync(
                    new BodianRequest
                    {
                        Path = Endpoints.CategoryPlaylists(categoryId),
                        Query = new List<KeyValuePair<string, string>>(paging),
                        Signed = false,

                        // 这一族复用的是歌单列表的信封 DTO，与收藏歌单同一个。
                    },
                    BodianJsonContext.Default.PlaylistListPayload,
                    token).ConfigureAwait(false);

                var payload = envelope.Data;
                var items = payload?.PlayLists?.Select(MapPlaylist).ToArray() ?? [];

                _logger.LogInformation(
                    "乐库分类 {CategoryId} 返回 {Count} 个歌单（offset={Offset}）",
                    categoryId,
                    items.Length,
                    cursor.Offset);

                return new PagedResult<Playlist>(items, cursor.Offset, cursor.PageSize, payload?.Total);
            },
            cancellationToken).ConfigureAwait(false);
    }

    // ── 排行榜 ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BangSection>> GetBangSectionsAsync(
        CancellationToken cancellationToken = default)
    {
        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                // 无参数、不要求登录（实测匿名即可）。
                Path = Endpoints.HomeBangNew,
                Query = [],
                Signed = false,
            },
            BodianJsonContext.Default.BangSectionDtoArray,
            cancellationToken).ConfigureAwait(false);

        var sections = envelope.Data?
            .Select(dto => new BangSection(
                dto.ModuleName ?? "",

                // 「H5榜单」那组里的条目没有 id（是外部 H5 链接），取不了曲目，滤掉。
                [.. (dto.BangList ?? [])
                    .Where(bang => bang.Id > 0 && !string.IsNullOrWhiteSpace(bang.Name))
                    .Select(MapBang)]))
            .Where(section => section.Bangs.Count > 0)
            .ToArray() ?? [];

        _logger.LogInformation(
            "排行榜 {Sections} 组、共 {Bangs} 个榜",
            sections.Length,
            sections.Sum(section => section.Bangs.Count));

        return sections;
    }

    public async Task<PagedResult<Track>> GetBangTracksAsync(
        long bangId,
        PagedCursor cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        if (bangId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bangId), bangId, "bangId 必须是正数");
        }

        return await PagedList.FetchNextAsync(
            cursor,
            async (paging, token) =>
            {
                var envelope = await _transport.SendAsync(
                    new BodianRequest
                    {
                        Path = Endpoints.BangMusics(bangId),
                        Query = new List<KeyValuePair<string, string>>(paging),
                        Signed = false,
                    },
                    BodianJsonContext.Default.BangMusicsPayload,
                    token).ConfigureAwait(false);

                var payload = envelope.Data;
                var items = payload?.Musics?.Select(MapTrack).ToArray() ?? [];

                return new PagedResult<Track>(items, cursor.Offset, cursor.PageSize, payload?.Total);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static Bang MapBang(BangDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name ?? "",
        CoverImage = ToHttpUri(dto.Pic),
        UpdateText = dto.PubStr ?? "",
        PreviewTracks = [.. (dto.Musics ?? []).Select(MapTrack)],
    };

    public async Task<AiPlaylist?> GetAiPlaylistAsync(
        int index,
        string passRecName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passRecName);

        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "index 不能是负数");
        }

        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.AiPlaylistDetail,
                Query =
                [
                    new KeyValuePair<string, string>(
                        "index",
                        index.ToString(CultureInfo.InvariantCulture)),

                    // 空串也要带：格式化串两端都有这两个参数，实测空串能正常拿到
                    // 「个性化歌单」的歌单。
                    new KeyValuePair<string, string>("passRecName", passRecName),
                ],
                Signed = false,
            },
            BodianJsonContext.Default.AiPlaylistPayload,
            cancellationToken).ConfigureAwait(false);

        if (envelope.Data is not { } payload)
        {
            return null;
        }

        var tracks = payload.MusicList?.Select(MapTrack).ToArray() ?? [];

        _logger.LogInformation(
            "AI 歌单 index={Index}（{Title}）{Count} 首",
            index,
            payload.Title,
            tracks.Length);

        return new AiPlaylist(
            payload.Title ?? "",
            payload.SubTitle ?? "",
            payload.BigTitle ?? "",
            tracks);
    }

    // ── 发现页 ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<HomeModule>> GetHomeModulesAsync(
        CancellationToken cancellationToken = default)
    {
        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                // 这个端点**无参数**，也不必签名（实测匿名即可）。
                Path = Endpoints.HomeIndex,
                Query = [],
                Signed = false,
            },
            BodianJsonContext.Default.HomeIndexPayload,
            cancellationToken).ConfigureAwait(false);

        var modules = envelope.Data?.ModuleList?
            .Select(dto => new HomeModule(dto.Id, dto.Type, dto.Name ?? "", dto.DelayMs))
            .ToArray() ?? [];

        _logger.LogInformation(
            "发现页布局 {Count} 个模块，其中可渲染 {Supported} 个",
            modules.Length,
            modules.Count(module => module.IsSupported));

        return modules;
    }

    public async Task<HomeFeed?> GetHomeModuleAsync(
        HomeModule module,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (!module.IsSupported)
        {
            return null;
        }

        var query = new List<KeyValuePair<string, string>>
        {
            new("moduleId", module.Id.ToString(CultureInfo.InvariantCulture)),
        };

        // ★ 按 type 挑解析类型：songList 这个键在 type 4/11 里是曲目分组、
        //   在 type 5 里是歌单卡片，同一个键两种形状，一个 DTO 接不了。
        var request = new BodianRequest
        {
            Path = Endpoints.HomeModule,
            Query = query,
            Signed = false,
        };

        var feed = module.Type switch
        {
            5 => await ReadPlaylistCardsAsync(request, cancellationToken).ConfigureAwait(false),
            4 or 11 => await ReadSongGroupsAsync(request, module.Type, cancellationToken).ConfigureAwait(false),
            3 or 10 => await ReadMusicListAsync(request, cancellationToken).ConfigureAwait(false),
            _ => null,
        };

        _logger.LogInformation(
            "发现页模块 {Id}（{Name}，type={Type}）解析出 {Sections} 组",
            module.Id,
            module.Name,
            module.Type,
            feed?.Sections.Count ?? 0);

        return feed;
    }

    private async Task<HomeFeed?> ReadMusicListAsync(BodianRequest request, CancellationToken cancellationToken)
    {
        var payload = (await _transport.SendAsync(
            request, BodianJsonContext.Default.HomeMusicListPayload, cancellationToken).ConfigureAwait(false)).Data;

        var tracks = payload?.MusicList?.Select(MapTrack).ToArray() ?? [];

        return Build(payload?.ModuleName, tracks.Length == 0 ? [] : [new HomeSection("", null, ToCards(tracks))]);
    }

    /// <summary>
    /// 曲目分组（type 4 / 11）。
    /// </summary>
    /// <param name="moduleType">
    /// 用它决定分组的 <c>id</c> 是不是 AI 歌单序号。
    /// <b>不能靠「id 大于 0」判断</b>：type 11 的分组压根没有 id 字段、反序列化后是 0，
    /// 而 <c>index=0</c> 恰好是合法的 AI 序号 —— 那样 type 11 的分组点进去会打开「潮趣日推」。
    /// </param>
    private async Task<HomeFeed?> ReadSongGroupsAsync(
        BodianRequest request,
        int moduleType,
        CancellationToken cancellationToken)
    {
        var payload = (await _transport.SendAsync(
            request, BodianJsonContext.Default.HomeSongGroupsPayload, cancellationToken).ConfigureAwait(false)).Data;

        // 「个性化歌单」（type 4）与「你的主题歌单」（type 11）的分组都能点开成完整歌单。
        var openable = moduleType is 4 or 11;

        var sections = payload?.SongList?
            .Select((group, position) => (group, position))
            .Where(pair => pair.group.Songs is { Length: > 0 })
            .Select(pair => new HomeSection(
                pair.group.Name ?? "",
                null,
                ToCards([.. pair.group.Songs!.Select(MapTrack)]),

                // ★ index 用**位置**，不是分组自带的 id：
                //   type 11 压根没有 id 字段（反序列化后是 0），而 type 4 的 id
                //   在实测样本里就是位置。passRecName 只有 type 11 有。
                openable
                    ? new AiPlaylistRef(pair.position, pair.group.PassRecName ?? "")
                    : null))
            .ToArray() ?? [];

        return Build(payload?.ModuleName, sections);
    }

    private async Task<HomeFeed?> ReadPlaylistCardsAsync(BodianRequest request, CancellationToken cancellationToken)
    {
        var payload = (await _transport.SendAsync(
            request, BodianJsonContext.Default.HomePlaylistCardsPayload, cancellationToken).ConfigureAwait(false)).Data;

        var playlists = payload?.SongList?.Select(MapPlaylist).ToArray() ?? [];

        var cards = playlists.Select(playlist => new HomeCard
        {
            Title = playlist.Name,
            Subtitle = playlist.MusicCount > 0 ? $"{playlist.MusicCount} 首" : "",
            CoverImage = playlist.CoverImage,

            // 实测发现的歌单是公开集合（sourceType 4），点它要按那个 source 取曲目。
            Playlist = playlist,
        }).ToArray();

        return Build(payload?.ModuleName, cards.Length == 0 ? [] : [new HomeSection("", null, cards)]);
    }

    /// <summary>把「每组若干曲目」统一成卡片。</summary>
    private static HomeCard[] ToCards(IReadOnlyList<Track> tracks) =>
        [.. tracks.Select(track => new HomeCard
        {
            Title = track.Title,
            Subtitle = track.ArtistText,
            CoverImage = track.CoverImage,
            Track = track,
        })];

    /// <summary>一组都没有时返回 <c>null</c> —— 那表示「这个模块这次没有内容」，不是空标题。</summary>
    private static HomeFeed? Build(string? title, HomeSection[] sections) =>
        sections.Length == 0 ? null : new HomeFeed(title ?? "", sections);

    // ── 已购与收藏 ──────────────────────────────────────────────────────────

    public async Task<PagedResult<Track>> GetPurchasedSinglesAsync(
        PagedCursor cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        RequireAuthenticated();

        return await PagedList.FetchNextAsync(
            cursor,
            async (paging, token) =>
            {
                var envelope = await _transport.SendAsync(
                    new BodianRequest
                    {
                        Path = Endpoints.PurchasedSingles,
                        Query = new List<KeyValuePair<string, string>>(paging),
                        Signed = true,
                    },
                    BodianJsonContext.Default.PurchasedSinglesPayload,
                    token).ConfigureAwait(false);

                var payload = envelope.Data;
                var items = payload?.MusicList?.Select(MapTrack).ToArray() ?? [];

                _logger.LogInformation(
                    "已购单曲返回 {Count} 首（offset={Offset}）",
                    items.Length,
                    cursor.Offset);

                return new PagedResult<Track>(items, cursor.Offset, cursor.PageSize, payload?.Total);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PagedResult<Album>> GetPurchasedAlbumsAsync(
        PagedCursor cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        RequireAuthenticated();

        return await FetchAlbumsAsync(
            Endpoints.PurchasedAlbums,
            new List<KeyValuePair<string, string>>(),
            cursor,
            BodianJsonContext.Default.PurchasedAlbumsPayload,
            payload => payload.AlbumList,
            payload => payload.Total,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 收藏的专辑用的 <c>source</c>：<b>4 = 收藏歌单</b>。
    /// </summary>
    /// <remarks>
    /// 按用户实测：移动端收藏歌单接口的内容与官方桌面端「收藏专辑」的效果一致，
    /// 所以本项目用这一条，官方桌面端自己那条（<c>service/collect/6/list</c>）已弃用 ——
    /// 它对本项目在测的账号返回 200 但 <c>data</c> 是空对象。
    /// </remarks>
    /// <summary>收藏族的读端点。<b>歌单与专辑共用它</b>，靠元素的 <c>sourceType</c> 区分。</summary>
    private const int CollectedSource = Endpoints.CollectSourcePlaylistAlbum;

    /// <summary>关注歌手列表一次取回的条数（照官方客户端固定 <c>rn=400</c>）。</summary>
    private const int FollowedArtistPageSize = 400;

    public async Task<PagedResult<Album>> GetCollectedAlbumsAsync(
        PagedCursor cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        var uid = UidPair().Value;

        return await FetchAlbumsAsync(
            Endpoints.CollectList(CollectedSource),
            [
                new KeyValuePair<string, string>("userId", uid),
                new KeyValuePair<string, string>("fromUid", uid),
            ],
            cursor,
            BodianJsonContext.Default.CollectedAlbumsPayload,
            // 混合列表：**排除** sourceType == 4 的歌单，其余都当专辑（歌单见 GetCollectedPlaylistsAsync）。
            // 用「排除歌单」而不是「等于专辑」：专辑条目可能**不带 sourceType**（实测有这种形状，
            // 见 AlbumApiTests.CollectedAlbums_AcceptsAlbumShapedItems），写 `== 6` 会把它们漏掉。
            payload => payload.PlayLists?.Where(a => a.SourceType != Endpoints.CollectedPlaylistType).ToArray(),
            payload => payload.Total,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PagedResult<Playlist>> GetCollectedPlaylistsAsync(
        PagedCursor cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        var uid = UidPair().Value;

        return await PagedList.FetchNextAsync(
            cursor,
            async (paging, token) =>
            {
                var query = new List<KeyValuePair<string, string>>(paging)
                {
                    new("userId", uid),
                    new("fromUid", uid),
                };

                var envelope = await _transport.SendAsync(
                    new BodianRequest
                    {
                        Path = Endpoints.CollectList(CollectedSource),
                        Query = query,
                        Signed = true,
                    },
                    BodianJsonContext.Default.CollectedPlaylistsPayload,
                    token).ConfigureAwait(false);

                var payload = envelope.Data;
                // 同一条端点的混合列表：只保留 sourceType == 4 的歌单。
                var items = payload?.PlayLists?
                    .Where(p => p.SourceType == Endpoints.CollectedPlaylistType)
                    .Select(MapPlaylist)
                    .ToArray() ?? [];

                _logger.LogInformation("收藏歌单返回 {Count} 个（offset={Offset}）", items.Length, cursor.Offset);

                return new PagedResult<Playlist>(items, cursor.Offset, cursor.PageSize, payload?.Total);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Artist>> GetFollowedArtistsAsync(CancellationToken cancellationToken = default)
    {
        var uid = UidPair().Value;

        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.CollectList(Endpoints.CollectSourceArtist),
                Query =
                [
                    new("userId", uid),
                    new("fromUid", uid),
                    new("pn", "1"),
                    new("rn", FollowedArtistPageSize.ToString(CultureInfo.InvariantCulture)),
                ],
                Signed = true,
            },
            BodianJsonContext.Default.FollowedArtistsPayload,
            cancellationToken).ConfigureAwait(false);

        var artists = envelope.Data?.ArtistList?.Select(MapFollowedArtist).ToArray() ?? [];

        _logger.LogInformation("关注歌手 {Count} 位", artists.Length);

        return artists;
    }

    public async Task<Playlist?> GetPlaylistInfoAsync(long playlistId, int source,
        CancellationToken cancellationToken = default)
    {
        EnsurePlaylistId(playlistId);

        // ★ 这里**不**做登录校验（与旧的「查收藏态」不同）：歌单详情是公开端点，
        //   匿名也要拿得到创建者、简介、播放数。匿名时 collectTime 自然不会出现，
        //   IsCollected 落成 false，与「匿名不能收藏」自洽。
        try
        {
            var envelope = await _transport.SendAsync(
                new BodianRequest
                {
                    Path = Endpoints.PlaylistInfo(playlistId),

                    // source 必填，缺了回 -10 参数错误（文档 2.2）。
                    Query = [new KeyValuePair<string, string>("source", source.ToString(CultureInfo.InvariantCulture))],
                    Signed = true,
                },
                BodianJsonContext.Default.PlaylistDto,
                cancellationToken).ConfigureAwait(false);

            // data 是空对象（source 填错、或这个来源没有这个歌单）时按「查不到」处理。
            // 服务端对「无数据」与「无效 source」返回同一个空对象，不报错 —— 这是第二处
            // 「对不上就回空」，第一处在 musicList，见文档 2.2。
            return envelope.Data is { Id: > 0 } dto ? MapPlaylistInfo(dto) : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取歌单 {PlaylistId} 详情失败", playlistId);
            return null;
        }
    }

    /// <inheritdoc />
    public Task SetPlaylistCollectedAsync(long playlistId, int source, bool collected,
        CancellationToken cancellationToken = default)
    {
        EnsurePlaylistId(playlistId);

        // 收藏歌单/专辑的报文里**没有** token（findings/13 §3.2）。
        return WriteCollectAsync(source, playlistId, collected, token: null, "收藏歌单", cancellationToken);
    }

    /// <inheritdoc />
    public Task SetArtistFollowedAsync(long artistId, bool followed,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(artistId);

        // 关注歌手的报文比收藏歌单**多一个 token**（findings/13 §4.2）。
        return WriteCollectAsync(Endpoints.CollectSourceArtist, artistId, followed, _session.Token, "关注歌手", cancellationToken);
    }

    /// <inheritdoc />
    public Task SetAlbumCollectedAsync(long albumId, bool collected,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(albumId);

        // 收藏专辑走 source=6（歌单是 4），同样**不带 token**；2026-10-03 真机往返实测。
        return WriteCollectAsync(Endpoints.CollectSourceAlbum, albumId, collected, token: null, "收藏专辑", cancellationToken);
    }

    public async Task<bool?> IsAlbumCollectedAsync(long albumId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(albumId);

        if (!_session.IsAuthenticated)
        {
            return null;
        }

        try
        {
            var envelope = await _transport.SendAsync(
                new BodianRequest
                {
                    Path = Endpoints.CollectMultipleState,
                    Query =
                    [
                        new("source", Endpoints.CollectSourceAlbum.ToString(CultureInfo.InvariantCulture)),
                        new("sourceIds", albumId.ToString(CultureInfo.InvariantCulture)),
                    ],
                    Signed = true,
                },
                BodianJsonContext.Default.CollectMultipleStatePayload,
                cancellationToken).ConfigureAwait(false);

            var match = envelope.Data?.Result?.FirstOrDefault(r => r.Id == albumId);

            // 没回这个 id 时按「无法判定」处理，不要当成未收藏。
            return match?.Collect;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取专辑 {AlbumId} 收藏状态失败", albumId);
            return null;
        }
    }

    /// <summary>
    /// <c>service/collect</c> 的共同写入流程。
    /// </summary>
    /// <remarks>
    /// <c>op</c> 由**目标态**算好（<c>true</c> → <c>1</c> 收藏/关注，<c>false</c> → <c>2</c> 取消）——
    /// 服务端把它当一次「设置」而不是「切换」，所以这里必须传确定的目标态，
    /// 不能指望服务端翻转。见 <c>reverse/findings/13-collect-playlist-follow-artist.md</c> §2。
    /// </remarks>
    private async Task WriteCollectAsync(int source, long targetId, bool collected, string? token,
        string operation, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var uid = long.Parse(_session.Uid, CultureInfo.InvariantCulture);
        var revision = _session.Revision;

        var body = new CollectBody
        {
            Source = source,
            SourceId = [targetId],
            Op = collected ? 1 : 2,
            Uid = uid,
            Token = token,
        };

        var envelope = await _transport.SendAsync(new BodianRequest
        {
            Path = Endpoints.Collect,
            Verb = BodianHttpVerb.Post,
            Signed = true,
            JsonBody = JsonSerializer.Serialize(body, BodianJsonContext.Default.CollectBody),
        }, BodianJsonContext.Default.JsonElement, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "{Operation}请求已受理：目标 {TargetId}，source {Source}，op {Op}，业务码 {Code}，reqId {RequestId}",
            operation, targetId, source, body.Op, envelope.Code, envelope.RequestId);

        if (revision != _session.Revision) throw new InvalidOperationException("登录状态已改变，请重新检查。");
    }

    /// <summary>
    /// 两个专辑列表接口的共同流程。
    /// </summary>
    /// <remarks>
    /// <b>总数键由调用方传进来</b>，因为这一族不统一：已购是 <c>size</c>、收藏是 <c>total</c>。
    /// 抽公共基类字段会在遇到另一种键时静默变成 0 —— 而那正是「翻页永远停在第一页」的原因。
    /// </remarks>
    private async Task<PagedResult<Album>> FetchAlbumsAsync<TPayload>(
        string path,
        List<KeyValuePair<string, string>> extraQuery,
        PagedCursor cursor,
        JsonTypeInfo<TPayload> typeInfo,
        Func<TPayload, AlbumDto[]?> select,
        Func<TPayload, int> selectTotal,
        CancellationToken cancellationToken)
        where TPayload : class
    {
        return await PagedList.FetchNextAsync(
            cursor,
            async (paging, token) =>
            {
                var query = new List<KeyValuePair<string, string>>(paging);
                query.AddRange(extraQuery);

                var envelope = await _transport.SendAsync(
                    new BodianRequest
                    {
                        Path = path,
                        Query = query,
                        Signed = true,
                    },
                    typeInfo,
                    token).ConfigureAwait(false);

                var payload = envelope.Data;
                var items = payload is null ? [] : select(payload)?.Select(MapAlbum).ToArray() ?? [];
                var total = payload is null ? (int?)null : selectTotal(payload);

                _logger.LogInformation(
                    "{Path} 返回 {Count} 个专辑（offset={Offset}）",
                    path,
                    items.Length,
                    cursor.Offset);

                return new PagedResult<Album>(items, cursor.Offset, cursor.PageSize, total);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetLyricAsync(
        long musicId,
        int lrcx,
        CancellationToken cancellationToken = default)
    {
        EnsureMusicId(musicId);

        var envelope = await _transport.SendAbsoluteAsync(
            BodianLyricPayload.BuildRequestUri(musicId, lrcx),
            BodianJsonContext.Default.LyricContentDto,
            cancellationToken).ConfigureAwait(false);

        // 服务端回显的版式只用于排查：版式真相以文本里有没有 [kuwo:] 为准，不靠这个字段做分支。
        if (envelope.Lrcx is { } served && served != lrcx)
        {
            _logger.LogDebug("歌词版式回显与请求不符：请求 {Requested}，回显 {Served}", lrcx, served);
        }

        return BodianLyricPayload.DecodeContent(envelope.Data?.Content);
    }

    public async Task<LyricDocument> GetLyricsAsync(
        Track track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        EnsureMusicId(track.Id);

        // 歌词轨信息未知时先赌逐字版：有逐字轨的歌是多数，且逐字版能降级成逐行显示。
        var tryWordByWord = track.Lyrics?.HasWordByWord ?? true;
        var lrcx = tryWordByWord ? BodianLyricPayload.WordByWord : BodianLyricPayload.LineByLine;

        var text = await GetLyricAsync(track.Id, lrcx, cancellationToken).ConfigureAwait(false);

        // 空串不是错误，但逐字版为空时必须自己退版 —— 服务端不做回退。
        // 两种原因都走这一支：这首歌只有逐行轨，或者 lrc_info 与歌词站不一致（轨信息过期）。
        // 多一次请求换「有歌词可看」，比恪守「已知有逐字轨就不退」划算。
        if (tryWordByWord && string.IsNullOrWhiteSpace(text))
        {
            text = await GetLyricAsync(track.Id, BodianLyricPayload.LineByLine, cancellationToken)
                .ConfigureAwait(false);
        }

        return BodianLyricParser.Parse(text, track.Duration);
    }

    private static AudioVariant[] MapAudioVariants(TrackDto dto) => dto.Audios?
        .Select(a => AudioQualityTable.ParseVariant(a.Level, a.Format, a.Bitrate, a.Size))
        .OfType<AudioVariant>().Distinct().ToArray() ?? [];

    private static Track MapTrack(TrackDto dto)
    {
        var (requiresVip, requiresPurchase) = PayTypeReader.Resolve(dto.PayInfo);

        return new Track
        {
            Id = dto.Id,
            Title = dto.Name ?? dto.SongName ?? "(未知曲目)",
            CommentCount = dto.Comment is { } count ? Math.Max(0, count) : null,
            FavoriteCount = dto.Favorite is { } favorites ? Math.Max(0, favorites) : null,
            ShareCount = dto.Share is { } shares ? Math.Max(0, shares) : null,
            ArtistText = dto.Artist ?? JoinArtists(dto.Artists),
            Artists = dto.Artists?.Select(a => new TrackArtist(a.Id, a.Name ?? "", ToHttpUri(a.Pic))).ToArray() ?? [],
            AlbumName = dto.Album,
            AlbumId = dto.AlbumId,

            // 120px 那张更省流量，列表里够用；缺了才退回大图。
            CoverImage = ToHttpUri(dto.AlbumPic120) ?? ToHttpUri(dto.AlbumPic),

            Duration = TimeSpan.FromSeconds(dto.DurationSeconds),
            AudioVariants = MapAudioVariants(dto),
            AvailableQualities = MapAudioVariants(dto).Select(v => v.Quality).Distinct().OrderDescending().ToArray(),
            RequiresVip = requiresVip,
            RequiresPurchase = requiresPurchase,

            // 搜索结果不带 lrc_info，那里会是 null —— 取词策略对 null 有兜底。
            Lyrics = dto.LrcInfo is { } lrc
                ? new TrackLyricInfo(lrc.Lrc == 1, lrc.Lrcx == 1)
                : null,
        };
    }

    private static Album MapAlbum(AlbumDto dto) => new()
    {
        // 两种形状的主键不同（albumId / id），由 DTO 自己挑，调用方不必关心。
        Id = dto.EffectiveId,

        // 名字缺失也要给个能显示的东西：列表里一行空白比「(未命名专辑)」更难排查。
        Name = string.IsNullOrWhiteSpace(dto.Name) ? "(未命名专辑)" : dto.Name,

        ArtistText = dto.EffectiveArtist ?? "",
        ArtistId = dto.ArtistId,
        CoverImage = ToHttpUri(dto.Pic),
        MusicCount = dto.MusicCount,
        ArtistCover = ToHttpUri(dto.ArtistPic),
        ReleaseDate = dto.ShowTime ?? "",
        Description = dto.Info ?? "",
    };

    /// <summary>
    /// 关注歌手列表的条目映射。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="MapArtistInfo"/> 分开写：两者的键名与来源都不同
    /// （详情是 <c>artistInfo</c> 嵌套、这里是 <c>artistList</c> 平铺），共用会让两处互相牵制。
    /// 这里<b>没有简介</b> —— 关注列表不给 <c>desc</c>。
    /// </remarks>
    private static Artist MapFollowedArtist(FollowedArtistDto dto) => new()
    {
        Id = dto.Id,
        Name = string.IsNullOrWhiteSpace(dto.Name) ? "(未命名歌手)" : dto.Name,
        CoverImage = ToHttpUri(dto.Pic),
        SongCount = dto.MusicCount,
        AlbumCount = dto.AlbumCount,
        AliasName = dto.AliasName ?? "",
        FansCount = dto.FansCount,
    };

    private static Playlist MapPlaylist(PlaylistDto dto) => new()
    {
        Id = dto.Id,

        // 名字缺失也要给个能显示的东西：侧栏上一行空白比「(未命名歌单)」更难排查。
        Name = string.IsNullOrWhiteSpace(dto.Name) ? "(未命名歌单)" : dto.Name,

        MusicCount = dto.MusicCount,
        CoverImage = ToHttpUri(dto.Pic),

        // 原串另存一份：编辑歌单要把它回传回去，规范化过的地址没验证过能不能用。
        CoverRawUrl = dto.Pic ?? "",

        // ★ 服务端给什么就存什么，**不做归一化**。
        //   实测发现页里的歌单 sourceType 是 13，不是文档说的公开集合默认值 4 ——
        //   自作主张改写成 4 会让取曲目时填错 source，而服务端对不上的值只回空、不报错，
        //   表现就是「点进去是空歌单」，极难查。
        //   字段缺失时留 0，由调用方决定怎么办（账号歌单那边本来就知道自己是 5，不靠它）。
        SourceType = dto.SourceType,

        // 服务端给的是数字（0/1），这里归一成布尔给界面用。
        IsPrivate = dto.IsPrivate != 0,
    };

    /// <summary>
    /// 歌单详情（<c>service/playlist/info/{id}</c>）的映射：在列表映射之上补详情专属字段。
    /// </summary>
    /// <remarks>
    /// 用 <c>with</c> 而不是另写一份 —— <see cref="MapPlaylist"/> 里的名字兜底、封面转 Uri、
    /// <c>SourceType</c> 不归一化这几条规矩只需维护一处，两个映射不会漂移。
    /// </remarks>
    private static Playlist MapPlaylistInfo(PlaylistDto dto) => MapPlaylist(dto) with
    {
        CreatorId = dto.CreatorId,
        CreatorName = dto.CreatorName ?? "",
        CreatorCover = ToHttpUri(dto.CreatorIcon),
        Description = dto.Description ?? "",
        PlayCount = dto.PlayNum,

        // praise 与 collectedCnt 在实测样本里相等，优先取语义正确的 collectedCnt，
        // 某个来源只给了 praise 时也能兜住（文档 2.2）。
        CollectedCount = dto.CollectedCount > 0 ? dto.CollectedCount : dto.Praise,

        // 存在即已收藏。空键与空串都当「未收藏」，省得后面各处判 null（findings/13 §3.3）。
        CollectTime = dto.CollectTime ?? "",

        // 标签只有详情会给（2026-10-03 实测）。id 是 int，这里统一成 MusicCategory 的 long。
        Categories = dto.Categories is { Length: > 0 } categories
            ? [.. categories.Select(c => new MusicCategory(c.Id, c.Name ?? ""))]
            : [],
    };

    /// <summary>
    /// 账号类查询的 <c>userId</c>。
    /// </summary>
    /// <remarks>
    /// <b>未登录时直接抛，不返回空列表。</b> 曲库接口在匿名会话下没有意义，
    /// 返回空会让「侧栏一片空白」看起来像服务端没数据，而真正的原因是没有会话。
    /// </remarks>
    private KeyValuePair<string, string> UidPair()
    {
        RequireAuthenticated();
        return new("userId", _session.Uid);
    }

    /// <summary>
    /// 曲库接口的前置条件。
    /// </summary>
    /// <remarks>
    /// <b>取曲目<b>不发</b> <c>userId</c> 参数</b>（那个端点的参数只有 <c>source/pn/rn</c>，
    /// 多加一个未经验证的键有风险），但登录校验一样要做：<c>source=5</c> 是账号歌单，
    /// 匿名会话下服务端只会回空，界面看起来像「这个歌单是空的」。
    /// </remarks>
    private void RequireAuthenticated()
    {
        if (!_session.IsAuthenticated)
        {
            throw new InvalidOperationException("曲库接口需要登录后才能调用。");
        }
    }

    private static void EnsurePlaylistId(long playlistId)
    {
        if (playlistId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playlistId), playlistId, "playlistId 必须是正数");
        }
    }

    private static string JoinArtists(TrackArtistDto[]? artists) =>
        artists is { Length: > 0 }
            ? string.Join("、", artists.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n)))
            : "";

    private static KeyValuePair<string, string> MusicIdPair(long musicId) =>
        new("musicId", musicId.ToString(CultureInfo.InvariantCulture));

    public async Task<SongCommentPage> GetSongCommentsAsync(long musicId, SongCommentSort sort, int page = 1,
        CancellationToken cancellationToken = default)
    {
        EnsureCommentMusicId(musicId);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        var path = sort switch
        {
            SongCommentSort.Recommended => Endpoints.SongCommentsRecommended,
            SongCommentSort.Latest => Endpoints.SongCommentsLatest,
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        };
        var envelope = await _transport.SendAsync(new BodianRequest
        {
            Path = path,
            Platform = "android",
            Signed = false,
            Query =
            [
                new("moduleType", "2"),
                new("moduleId", musicId.ToString(CultureInfo.InvariantCulture)),
                new("pn", page.ToString(CultureInfo.InvariantCulture)),
                new("rn", "30"),
            ],
        }, BodianJsonContext.Default.SongCommentsPayload, cancellationToken).ConfigureAwait(false);
        // 缺失 data 不能冒充「没有评论」，否则协议错误会静默隐藏。
        var payload = envelope.Data ?? throw new JsonException("评论响应缺少 data。");
        return MapComments(payload);
    }

    public async Task<SongCommentPage> GetSongCommentRepliesAsync(long musicId, long parentId, int page = 1,
        CancellationToken cancellationToken = default)
    {
        EnsureCommentMusicId(musicId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parentId);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        var envelope = await _transport.SendAsync(new BodianRequest
        {
            Path = Endpoints.SongCommentReplies,
            Platform = "android",
            Query =
            [
                new("moduleType", "2"),
                new("moduleId", musicId.ToString(CultureInfo.InvariantCulture)),
                new("parentId", parentId.ToString(CultureInfo.InvariantCulture)),
                new("pn", page.ToString(CultureInfo.InvariantCulture)),
                new("rn", "30"),
            ],
        }, BodianJsonContext.Default.SongCommentsPayload, cancellationToken).ConfigureAwait(false);
        return MapComments(envelope.Data ?? throw new JsonException("回复响应缺少 data。"));
    }

    public async Task<long?> PublishSongCommentAsync(long musicId, string content, long parentId = 0, long replyId = 0,
        bool anonymous = false, CancellationToken cancellationToken = default)
    {
        EnsureCommentMusicId(musicId);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentOutOfRangeException.ThrowIfNegative(parentId);
        ArgumentOutOfRangeException.ThrowIfNegative(replyId);
        if (replyId > 0 && parentId == 0) throw new ArgumentException("回复需要所属主评论 id。", nameof(parentId));
        RequireAuthenticated();
        var revision = _session.Revision;
        var body = new PublishSongCommentBody
        {
            MusicId = musicId,
            UserId = long.Parse(_session.Uid, CultureInfo.InvariantCulture),
            Content = content.Trim(),
            ParentId = parentId > 0 ? parentId : null,
            ReplyId = replyId > 0 ? replyId : null,
            Anonymous = anonymous ? 1 : 0,
        };
        var envelope = await _transport.SendAsync(new BodianRequest
        {
            Path = Endpoints.SongCommentPublish,
            Platform = "android",
            Verb = BodianHttpVerb.Post,
            JsonBody = JsonSerializer.Serialize(body, BodianJsonContext.Default.PublishSongCommentBody),
        }, BodianJsonContext.Default.JsonElement, cancellationToken).ConfigureAwait(false);
        var id = ReadPublishedCommentId(envelope.Data);
        _logger.LogInformation("评论发布请求已受理：歌曲 {MusicId}，主评论 {ParentId}，回复目标 {ReplyId}，评论 id {CommentId}，业务码 {Code}，reqId {RequestId}",
            musicId, parentId, replyId, id, envelope.Code, envelope.RequestId);
        if (revision != _session.Revision) throw new InvalidOperationException("登录状态已改变，请重新检查评论。");
        return id;
    }

    public async Task SetSongCommentLikeAsync(long musicId, long commentId, bool liked, long parentId = 0,
        CancellationToken cancellationToken = default)
    {
        EnsureCommentMusicId(musicId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commentId);
        ArgumentOutOfRangeException.ThrowIfNegative(parentId);
        RequireAuthenticated();
        var revision = _session.Revision;
        var body = new SongCommentLikeBody
        {
            MusicId = musicId,
            CommentId = commentId,
            UserId = long.Parse(_session.Uid, CultureInfo.InvariantCulture),
            Operation = liked ? 1 : 2,
            ParentId = parentId > 0 ? parentId : null,
        };
        var envelope = await _transport.SendAsync(new BodianRequest
        {
            Path = Endpoints.SongCommentLike,
            Platform = "android",
            Verb = BodianHttpVerb.Post,
            JsonBody = JsonSerializer.Serialize(body, BodianJsonContext.Default.SongCommentLikeBody),
        }, BodianJsonContext.Default.JsonElement, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("评论 {Operation} 请求已受理：歌曲 {MusicId}，评论 {CommentId}，主评论 {ParentId}，接口 {Path}，moduleType {ModuleType}，op {Op}，业务码 {Code}，reqId {RequestId}",
            liked ? "点赞" : "取消点赞", musicId, commentId, parentId, Endpoints.SongCommentLike,
            body.ModuleType, body.Operation, envelope.Code, envelope.RequestId);
        if (revision != _session.Revision) throw new InvalidOperationException("登录状态已改变，请重新检查点赞状态。");
    }

    private static long? ReadPublishedCommentId(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Number && data.TryGetInt64(out var number)) return number > 0 ? number : null;
        if (data.ValueKind == JsonValueKind.String && long.TryParse(data.GetString(), out var textId)) return textId > 0 ? textId : null;
        if (data.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "id", "commentId" })
                if (data.TryGetProperty(key, out var value) && ReadPublishedCommentId(value) is { } id) return id;
        }
        return null;
    }

    private static SongCommentPage MapComments(SongCommentsPayload payload)
    {
        var items = (payload.Comments ?? []).Where(dto => dto.Id > 0).Select(dto => new SongComment
        {
            Id = dto.Id,
            UserId = dto.UserId,
            ParentId = dto.ParentId ?? 0,
            ReplyId = dto.ReplyId ?? 0,
            ReplyNickname = dto.ReplyUserInfo?.Nickname ?? "",
            Nickname = dto.Anonymous != 0 ? "匿名听友" : FirstNonEmpty(dto.Nickname, dto.UserInfo?.Nickname, "听友"),
            AvatarUri = dto.Anonymous != 0 ? null : ToHttpUri(FirstNonEmpty(dto.Avatar, dto.UserInfo?.Avatar)),
            Content = dto.Content ?? "",
            ImageUri = ToHttpUri(dto.Image),
            PublishTime = FirstNonEmpty(dto.PublishTime, dto.CreateTime),
            Location = FirstNonEmpty(dto.City, dto.Province, dto.UserInfo?.City),
            LikeCount = Math.Max(0, dto.LikeCount),
            ReplyCount = Math.Max(0, dto.ReplyCount),
            IsLiked = dto.Like != 0,
        }).ToArray();
        return new SongCommentPage(items, Math.Max(0, payload.TotalCount), payload.More);
    }

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static void EnsureCommentMusicId(long musicId)
    {
        EnsureMusicId(musicId);
        // 评论服务的 moduleId 只接受 Java Integer；部分音源的长 id 不能查询评论。
        if (musicId > int.MaxValue) throw new NotSupportedException("这首歌的音源暂不支持评论。");
    }

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
