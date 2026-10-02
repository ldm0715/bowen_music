using Bodian.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bodian.WinUI.ViewModels;

/// <summary>可更新的评论行；列表和详情共用它，让点赞状态保持一致。</summary>
public sealed partial class SongCommentRow : ObservableObject
{
    private SongComment _comment;

    public SongCommentRow(SongComment comment, SongCommentsViewModel owner, int page, SongCommentSort sort)
    {
        _comment = comment;
        SourcePage = page;
        SourceSort = sort;
        LikeCommand = new AsyncRelayCommand(() => owner.ToggleLikeAsync(this), () => !IsLiking);
        DetailCommand = new AsyncRelayCommand(() => owner.OpenThreadAsync(this));
        ReplyCommand = new AsyncRelayCommand(() => owner.ReplyToAsync(this));
        ImageCommand = new RelayCommand(() => owner.OpenImage(ImageUri), () => ImageUri is not null);
    }

    public SongComment Comment => _comment;
    public long Id => _comment.Id;
    public long ParentId => _comment.ParentId;
    public bool IsReply => ParentId > 0;
    public string Nickname => _comment.Nickname;
    public Uri? AvatarUri => _comment.AvatarUri;
    public string Content => _comment.Content;
    public Uri? ImageUri => _comment.ImageUri;
    public string PublishTime => _comment.PublishTime;
    public string Location => _comment.Location;
    public long LikeCount => _comment.LikeCount;
    public long ReplyCount => _comment.ReplyCount;
    public bool IsLiked => _comment.IsLiked;
    public string ReplyCaption => string.IsNullOrWhiteSpace(_comment.ReplyNickname) ? "" : $"回复 @{_comment.ReplyNickname}";
    public bool HasReplyCaption => ReplyCaption.Length > 0;
    public string ReplyActionText => ReplyCount > 0 ? $"{ReplyCount:N0} 条回复" : "回复";
    public string LikeActionText => IsLiking ? "处理中" : $"{(IsLiked ? "取消点赞" : "点赞")}，{LikeCount:N0} 个赞";
    public int SourcePage { get; }
    public SongCommentSort SourceSort { get; }
    public IAsyncRelayCommand LikeCommand { get; }
    public IAsyncRelayCommand DetailCommand { get; }
    public IAsyncRelayCommand ReplyCommand { get; }
    public IRelayCommand ImageCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LikeActionText))]
    [NotifyCanExecuteChangedFor(nameof(LikeCommand))]
    public partial bool IsLiking { get; set; }

    public void Update(SongComment comment)
    {
        if (comment.Id != Id) throw new ArgumentException("不能替换成另一条评论。", nameof(comment));
        _comment = comment;
        foreach (var property in new[] { nameof(Comment), nameof(Nickname), nameof(AvatarUri), nameof(Content), nameof(ImageUri),
            nameof(PublishTime), nameof(Location), nameof(LikeCount), nameof(ReplyCount), nameof(IsLiked), nameof(ReplyCaption),
            nameof(HasReplyCaption), nameof(ReplyActionText), nameof(LikeActionText) }) OnPropertyChanged(property);
        ImageCommand.NotifyCanExecuteChanged();
    }
}
