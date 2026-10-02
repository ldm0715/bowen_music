using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto.Requests;

/// <summary>字段来自 comment_song_v3_publisher.dart；空的可选参数与官方一样省略。</summary>
internal sealed record PublishSongCommentBody
{
    [JsonPropertyName("moduleId")] public long MusicId { get; init; }
    [JsonPropertyName("moduleType")] public int ModuleType => 2;
    [JsonPropertyName("uid")] public long UserId { get; init; }
    [JsonPropertyName("content")] public required string Content { get; init; }
    [JsonPropertyName("parentId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ParentId { get; init; }
    [JsonPropertyName("replyId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ReplyId { get; init; }
    [JsonPropertyName("anonymous")] public int Anonymous { get; init; }
}

/// <summary>
/// 歌曲 v3 评论与回复的点赞。静态调用链：歌曲界面 → CommentV2.doCommentV3Like →
/// ApiService.townletCardCommentLike；moduleType=2，op=1 点赞 / op=2 取消。
/// </summary>
internal sealed record SongCommentLikeBody
{
    [JsonPropertyName("moduleId")] public long MusicId { get; init; }
    [JsonPropertyName("moduleType")] public int ModuleType => 2;
    [JsonPropertyName("uid")] public long UserId { get; init; }
    [JsonPropertyName("commentId")] public long CommentId { get; init; }
    [JsonPropertyName("op")] public int Operation { get; init; }
    [JsonPropertyName("parentId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ParentId { get; init; }
}
