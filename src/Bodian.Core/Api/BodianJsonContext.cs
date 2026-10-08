using System.Text.Json.Serialization;
using Bodian.Core.Api.Dto;
using Bodian.Core.Api.Dto.Requests;

namespace Bodian.Core.Api;

/// <summary>
/// 源生成 JSON 上下文。**所有反序列化都必须从这里取 <c>JsonTypeInfo</c>。**
/// </summary>
/// <remarks>
/// <para>
/// 强制手段有两处：WinUI 项目开了 <c>JsonSerializerIsReflectionEnabledByDefault=false</c>，
/// Core 开了 <c>IsAotCompatible</c> + <c>TreatWarningsAsErrors</c>——任何想退化成反射的写法
/// 都是编译错误而不是运行时惊喜。第三处是 <c>IBodianTransport</c> 的签名要求调用方传
/// <see cref="JsonTypeInfo{T}"/>。
/// </para>
/// <para>
/// <b>信封不进这个上下文。</b> 源生成不支持开放泛型，所以不能写
/// <c>JsonSerializable(typeof(BodianEnvelope&lt;&gt;))</c>。传输层用 <see cref="System.Text.Json.JsonDocument"/>
/// 手工取 <c>code</c> / <c>msg</c> / <c>reqId</c>，再把 <c>data</c> 子树交给这里的类型信息。
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    // ★ 这一行解决 payInfo 的类型不一致：refrain_start / refrain_end / limitfree
    //   在 service/music/info 里是字符串、在 search/music/list 里是数字。
    //   没有它，跑第二个接口时会抛 JsonException。
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
[JsonSerializable(typeof(PublishSongCommentBody))]
[JsonSerializable(typeof(SongCommentLikeBody))]
[JsonSerializable(typeof(SongCommentsPayload))]
[JsonSerializable(typeof(TrackDto))]
[JsonSerializable(typeof(SearchListPayload))]
[JsonSerializable(typeof(SearchPayload<AlbumDto>), TypeInfoPropertyName = "SearchAlbumsPayload")]
[JsonSerializable(typeof(SearchPayload<SearchPlaylistDto>), TypeInfoPropertyName = "SearchPlaylistsPayload")]
[JsonSerializable(typeof(SearchPayload<SearchArtistDto>), TypeInfoPropertyName = "SearchArtistsPayload")]
[JsonSerializable(typeof(SearchPayload<SearchTipDto>), TypeInfoPropertyName = "SearchTipsPayload")]
[JsonSerializable(typeof(SearchTopicsPayload))]
[JsonSerializable(typeof(ComprehensiveSearchPayload))]
[JsonSerializable(typeof(PlaylistDto))]
[JsonSerializable(typeof(PlaylistCategoryDto))]
[JsonSerializable(typeof(PlaylistListPayload))]
[JsonSerializable(typeof(PlaylistTracksPayload))]
[JsonSerializable(typeof(AlbumDto))]
[JsonSerializable(typeof(PurchasedSinglesPayload))]
[JsonSerializable(typeof(PurchasedAlbumsPayload))]
[JsonSerializable(typeof(CollectedAlbumsPayload))]
[JsonSerializable(typeof(CollectedPlaylistsPayload))]
[JsonSerializable(typeof(FollowedArtistsPayload))]
[JsonSerializable(typeof(CollectMultipleStatePayload))]
[JsonSerializable(typeof(HomeIndexPayload))]
[JsonSerializable(typeof(HomeMusicListPayload))]
[JsonSerializable(typeof(HomeSongGroupsPayload))]
[JsonSerializable(typeof(HomePlaylistCardsPayload))]
[JsonSerializable(typeof(AiPlaylistPayload))]
[JsonSerializable(typeof(CategoryListPayload))]
[JsonSerializable(typeof(MusicLibraryNavDto[]))]
[JsonSerializable(typeof(MusicLibraryAlbumsPayload))]
[JsonSerializable(typeof(ArtistInfoPayload))]
[JsonSerializable(typeof(AlbumInfoPayload))]
[JsonSerializable(typeof(AlbumTracksPayload))]
[JsonSerializable(typeof(BangSectionDto[]))]
[JsonSerializable(typeof(BangMusicsPayload))]
[JsonSerializable(typeof(CheckRightDto))]
[JsonSerializable(typeof(AudioUrlDto))]
[JsonSerializable(typeof(LoginResultDto))]
[JsonSerializable(typeof(UserMetadataDto))]
[JsonSerializable(typeof(UserPlayDataDto))]
[JsonSerializable(typeof(UserPubDto))]
[JsonSerializable(typeof(MvInfoPayload))]
[JsonSerializable(typeof(LyricContentDto))]
[JsonSerializable(typeof(QrCodeDto))]
[JsonSerializable(typeof(QrCodeStatusDto))]
// 请求体也要在这里登记：签名覆盖的是「即将发出的精确字节」，所以由调用方序列化，
// 而序列化同样要拿 JsonTypeInfo<T>。
[JsonSerializable(typeof(LoginBody))]
[JsonSerializable(typeof(PhoneLoginBody))]
[JsonSerializable(typeof(CheckRightBody))]
[JsonSerializable(typeof(AudioUrlBody))]
[JsonSerializable(typeof(PlaylistMusicBody))]
[JsonSerializable(typeof(CreatePlaylistBody))]
[JsonSerializable(typeof(UpdatePlaylistBody))]
[JsonSerializable(typeof(DeletePlaylistBody))]
[JsonSerializable(typeof(CollectBody))]
internal sealed partial class BodianJsonContext : JsonSerializerContext;
