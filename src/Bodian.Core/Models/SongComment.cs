namespace Bodian.Core.Models;

public enum SongCommentSort
{
    Recommended,
    Latest,
}

/// <summary>歌曲评论的展示数据；时间保留服务端的本地化文案。</summary>
public sealed record SongComment
{
    public required long Id { get; init; }
    public long UserId { get; init; }
    public long ParentId { get; init; }
    public long ReplyId { get; init; }
    public string ReplyNickname { get; init; } = "";
    public string Nickname { get; init; } = "听友";
    public Uri? AvatarUri { get; init; }
    public string Content { get; init; } = "";
    public Uri? ImageUri { get; init; }
    public string PublishTime { get; init; } = "";
    public string Location { get; init; } = "";
    public long LikeCount { get; init; }
    public long ReplyCount { get; init; }
    public bool IsLiked { get; init; }
}

public sealed record SongCommentPage(IReadOnlyList<SongComment> Items, long TotalCount, bool HasMore);
