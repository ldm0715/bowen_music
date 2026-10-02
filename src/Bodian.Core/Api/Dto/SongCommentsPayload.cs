using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>comments/v3/new 与 comments/v3/hot；见 reverse/findings/08-comments.md。</summary>
public sealed record SongCommentsPayload
{
    [JsonPropertyName("comment_list")] public SongCommentDto[]? Comments { get; init; }
    [JsonPropertyName("comment_total")] public long TotalCount { get; init; }
    [JsonPropertyName("more")] public bool More { get; init; }
}

public sealed record SongCommentDto
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("uid")] public long UserId { get; init; }
    [JsonPropertyName("parentId")] public long? ParentId { get; init; }
    [JsonPropertyName("replyId")] public long? ReplyId { get; init; }
    [JsonPropertyName("userInfo")] public CommentUserDto? UserInfo { get; init; }
    [JsonPropertyName("replyUserInfo")] public CommentUserDto? ReplyUserInfo { get; init; }
    [JsonPropertyName("nickname")] public string? Nickname { get; init; }
    [JsonPropertyName("headImg")] public string? Avatar { get; init; }
    [JsonPropertyName("content")] public string? Content { get; init; }
    [JsonPropertyName("imgUrl")] public string? Image { get; init; }
    [JsonPropertyName("createTime")] public string? CreateTime { get; init; }
    [JsonPropertyName("publishTime")] public string? PublishTime { get; init; }
    [JsonPropertyName("ipCity")] public string? City { get; init; }
    [JsonPropertyName("ipProvince")] public string? Province { get; init; }
    [JsonPropertyName("likeCount")] public long LikeCount { get; init; }
    [JsonPropertyName("replyCount")] public long ReplyCount { get; init; }
    [JsonPropertyName("like")] public int Like { get; init; }
    [JsonPropertyName("anonymous")] public int Anonymous { get; init; }
}

public sealed record CommentUserDto
{
    [JsonPropertyName("nickname")] public string? Nickname { get; init; }
    [JsonPropertyName("headImg")] public string? Avatar { get; init; }
    [JsonPropertyName("ipCity")] public string? City { get; init; }
}
