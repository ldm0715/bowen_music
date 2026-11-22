using Bowen.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bowen.WinUI.ViewModels;

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
        ToggleContentCommand = new RelayCommand(() => IsContentExpanded = !IsContentExpanded);
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
    public bool HasReplies => ReplyCount > 0;

    /// <summary>「N 条回复」的展示文案。无可点语义 —— 入口是旁边那颗「查看」。</summary>
    public string ReplyCountText => HasReplies ? $"{ReplyCount:N0} 条回复" : "";

    /// <summary>正文展开按钮的文案。</summary>
    public string ContentToggleText => IsContentExpanded ? "收起" : "更多";
    public string LikeActionText => IsLiking ? "处理中" : $"{(IsLiked ? "取消点赞" : "点赞")}，{LikeCount:N0} 个赞";
    public int SourcePage { get; }
    public SongCommentSort SourceSort { get; }
    public IAsyncRelayCommand LikeCommand { get; }
    public IAsyncRelayCommand DetailCommand { get; }
    public IAsyncRelayCommand ReplyCommand { get; }
    public IRelayCommand ImageCommand { get; }
    public IRelayCommand ToggleContentCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LikeActionText))]
    [NotifyCanExecuteChangedFor(nameof(LikeCommand))]
    public partial bool IsLiking { get; set; }

    /// <summary>正文是否已展开。折叠态由模板限制行数，展开后不再截断。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentToggleText))]
    [NotifyPropertyChangedFor(nameof(ShowContentToggle))]
    public partial bool IsContentExpanded { get; set; }

    /// <summary>
    /// 正文在折叠态下是否真的超出了行数上限。
    /// </summary>
    /// <remarks>
    /// 只有模板里那个 <c>TextBlock</c> 量得出来（<c>IsTextTrimmed</c>），所以由它的 SizeChanged 回写。
    /// 不这么做的替代方案是按字符数猜，长英文和中文混排时会猜错。
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContentToggle))]
    public partial bool IsContentTrimmed { get; set; }

    /// <summary>
    /// 正文的显式换行是否已超过折叠行数。
    /// </summary>
    /// <remarks>
    /// 只做确定性兜底：6 行以上一定有内容被折叠。换行不足时由模板里
    /// <c>TextBlock</c> 的量算结果（<see cref="IsContentTrimmed"/>）决定。
    /// </remarks>
    public bool ContentHasMoreLines =>
        _comment.Content.Count(c => c == '\n') >= Formats.CommentCollapsedLines;

    /// <summary>「更多 / 收起」这颗按钮要不要露出来。展开态要留着，否则点开就收不回去了。</summary>
    public bool ShowContentToggle => IsContentTrimmed || IsContentExpanded || ContentHasMoreLines;

    /// <summary>
    /// 这一行是不是「评论详情」顶部的那一条。
    /// </summary>
    /// <remarks>
    /// 详情页下方就是回复列表，头部那一行不用再给「查看」。模板按它隐藏那颗按钮。
    /// 列表与详情共用同一个实例，所以置位与复位由 <c>SongCommentsViewModel.SetThreadRoot</c> 成对管理 ——
    /// 漏掉复位会让列表里那一条也丢掉「查看」。
    /// </remarks>
    [ObservableProperty]
    public partial bool IsThreadRoot { get; set; }

    public void Update(SongComment comment)
    {
        if (comment.Id != Id) throw new ArgumentException("不能替换成另一条评论。", nameof(comment));
        _comment = comment;
        foreach (var property in new[] { nameof(Comment), nameof(Nickname), nameof(AvatarUri), nameof(Content), nameof(ImageUri),
            nameof(PublishTime), nameof(Location), nameof(LikeCount), nameof(ReplyCount), nameof(IsLiked), nameof(ReplyCaption),
            nameof(HasReplyCaption), nameof(HasReplies), nameof(ReplyCountText), nameof(LikeActionText),
            nameof(ContentHasMoreLines), nameof(ShowContentToggle) }) OnPropertyChanged(property);
        ImageCommand.NotifyCanExecuteChanged();
    }
}
