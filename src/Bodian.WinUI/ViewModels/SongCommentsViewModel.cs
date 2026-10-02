using System.Collections.ObjectModel;
using Bodian.Core.Api;
using Bodian.Core.Diagnostics;
using Bodian.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>评论与回复按需读取；写请求只能由用户显式操作触发；点赞成功直接更新，发布读回确认。</summary>
public sealed partial class SongCommentsViewModel : ObservableObject
{
    private readonly IBodianApi _api;
    private readonly BodianSession _session;
    private readonly ILogger<SongCommentsViewModel> _logger;
    private readonly HashSet<long> _seenIds = [];
    private readonly HashSet<long> _replyIds = [];
    private readonly HashSet<long> _likingIds = [];
    private readonly Dictionary<(long Song, long Parent, long Reply), string> _drafts = [];
    private CancellationTokenSource? _badgeRequest;
    private long _badgeMusicId;
    private int _badgeGeneration;
    private CancellationTokenSource? _request;
    private CancellationTokenSource? _replyRequest;
    private long _musicId;
    private int _nextPage = 1;
    private int _nextReplyPage = 1;
    private int _generation;
    private int _replyGeneration;
    private int _contextGeneration;
    private int _composerGeneration;

    public SongCommentsViewModel(IBodianApi api, BodianSession session, ILogger<SongCommentsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(session);
        _api = api;
        _session = session;
        _logger = logger ?? NullLogger<SongCommentsViewModel>.Instance;
    }

    public ObservableCollection<SongCommentRow> Items { get; } = [];
    public ObservableCollection<SongCommentRow> Replies { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeText))]
    [NotifyPropertyChangedFor(nameof(HasBadgeCount))]
    [NotifyPropertyChangedFor(nameof(BadgeAutomationName))]
    public partial long? BadgeCount { get; set; }

    public string BadgeText => BadgeCount is { } count ? CommentCountLabel.Format(count) : "";
    public bool HasBadgeCount => BadgeCount.HasValue;
    public string BadgeAutomationName => HasBadgeCount ? $"歌曲评论 · {BadgeText} 条" : "歌曲评论";

    /// <summary>进播放页只读取歌曲详情的 comment 字段，不加载评论列表；同一首歌复用已知数量。</summary>
    public async Task LoadBadgeAsync(long musicId)
    {
        if (musicId <= 0 || musicId > int.MaxValue) { SetBadge(musicId, null); return; }
        if (_badgeMusicId == musicId && (BadgeCount.HasValue || _badgeRequest is not null)) return;
        CancelBadgeRequest();
        _badgeMusicId = musicId;
        BadgeCount = null;
        var request = new CancellationTokenSource();
        _badgeRequest = request;
        var generation = ++_badgeGeneration;
        try
        {
            var track = await _api.GetTrackAsync(musicId, request.Token);
            if (generation != _badgeGeneration || request.IsCancellationRequested || _badgeMusicId != musicId) return;
            BadgeCount = track?.CommentCount;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // 数量获取失败只隐藏角标，不阻止用户打开评论。
            if (generation == _badgeGeneration) _logger.LogDebug(ex, "歌曲 {MusicId} 评论角标数量获取失败", musicId);
        }
        finally
        {
            if (generation == _badgeGeneration) _badgeRequest = null;
            request.Dispose();
        }
    }

    public void CancelBadgeRequest()
    {
        _badgeGeneration++;
        _badgeRequest?.Cancel();
        _badgeRequest = null;
    }

    private void SetBadge(long musicId, long? count)
    {
        CancelBadgeRequest();
        _badgeMusicId = musicId;
        BadgeCount = count is { } known ? Math.Max(0, known) : null;
    }


    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string SongTitle { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecommended))]
    [NotifyPropertyChangedFor(nameof(IsLatest))]
    public partial SongCommentSort Sort { get; set; } = SongCommentSort.Recommended;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalText))]
    public partial long TotalCount { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalText))]
    public partial bool HasLoaded { get; set; }

    [ObservableProperty]
    public partial bool LoadFailed { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComposerEnabled))]
    [NotifyPropertyChangedFor(nameof(ComposerPlaceholder))]
    [NotifyPropertyChangedFor(nameof(TotalText))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    public partial bool CommentsUnavailable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThreadOpen))]
    [NotifyPropertyChangedFor(nameof(IsListOpen))]
    [NotifyPropertyChangedFor(nameof(PanelTitle))]
    [NotifyPropertyChangedFor(nameof(ComposerPlaceholder))]
    [NotifyPropertyChangedFor(nameof(PublishLabel))]
    public partial SongCommentRow? SelectedRoot { get; set; }

    [ObservableProperty]
    public partial long ReplyTotal { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreRepliesCommand))]
    public partial bool IsRepliesLoading { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreRepliesCommand))]
    public partial bool RepliesHasMore { get; set; }

    [ObservableProperty]
    public partial bool RepliesHasLoaded { get; set; }

    [ObservableProperty]
    public partial bool RepliesLoadFailed { get; set; }

    [ObservableProperty]
    public partial string RepliesError { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyPropertyChangedFor(nameof(DraftCount))]
    public partial string DraftText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyPropertyChangedFor(nameof(IsComposerEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishLabel))]
    public partial bool IsPosting { get; set; }

    [ObservableProperty]
    public partial bool IsAnonymous { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReplyTarget))]
    [NotifyPropertyChangedFor(nameof(ReplyTargetText))]
    [NotifyPropertyChangedFor(nameof(ComposerPlaceholder))]
    public partial SongCommentRow? ReplyTarget { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFeedback))]
    public partial string Feedback { get; set; } = "";

    [ObservableProperty]
    public partial bool FeedbackIsError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImageOpen))]
    public partial Uri? PreviewImageUri { get; set; }

    public bool IsAuthenticated => _session.IsAuthenticated;
    public bool IsComposerEnabled => !CommentsUnavailable && !IsPosting && IsAuthenticated;
    public bool IsThreadOpen => SelectedRoot is not null;
    public bool IsListOpen => !IsThreadOpen;
    public string PanelTitle => IsThreadOpen ? "评论详情" : "评论";
    public bool IsImageOpen => PreviewImageUri is not null;
    public bool HasFeedback => Feedback.Length > 0;
    public bool HasReplyTarget => ReplyTarget is not null;
    public string ReplyTargetText => ReplyTarget is null ? "" : $"回复 @{ReplyTarget.Nickname}";
    public string ComposerPlaceholder => CommentsUnavailable ? "这首歌的音源暂不支持评论" : !IsAuthenticated ? "登录后可以评论、回复和点赞"
        : ReplyTarget is not null ? $"回复 @{ReplyTarget.Nickname}…" : IsThreadOpen ? $"回复 @{SelectedRoot!.Nickname}…" : "说说你听这首歌的感受…";
    public string PublishLabel => IsPosting ? "发送中…" : IsThreadOpen ? "发送回复" : "发送评论";
    public string DraftCount => $"{DraftText.Length}/1000";
    public bool IsRecommended => Sort == SongCommentSort.Recommended;
    public bool IsLatest => Sort == SongCommentSort.Latest;
    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => HasLoaded && !HasItems && !IsLoading && !LoadFailed;
    public bool IsInitialLoading => IsLoading && !HasItems;
    public bool IsLoadingMore => IsLoading && HasItems;
    public bool ShowInitialError => LoadFailed && !HasItems;
    public bool ShowInitialRetry => ShowInitialError && !CommentsUnavailable;
    public bool ShowMoreError => LoadFailed && HasItems;
    public bool ShowLoadMore => HasMore && !IsLoading && !LoadFailed;
    public bool ShowEnd => HasLoaded && HasItems && !HasMore && !IsLoading && !LoadFailed;
    public bool ShowRepliesEmpty => RepliesHasLoaded && Replies.Count == 0 && !IsRepliesLoading && !RepliesLoadFailed;
    public bool ShowMoreReplies => RepliesHasMore && !IsRepliesLoading && !RepliesLoadFailed;
    public bool ShowRepliesEnd => RepliesHasLoaded && Replies.Count > 0 && !RepliesHasMore && !IsRepliesLoading && !RepliesLoadFailed;
    public string TotalText => CommentsUnavailable ? "暂不可用" : HasLoaded ? $"{TotalCount:N0} 条评论" : "听听大家怎么说";
    private bool CanLoadMore => IsOpen && HasMore && !IsLoading;
    private bool CanLoadMoreReplies => IsOpen && IsThreadOpen && RepliesHasMore && !IsRepliesLoading;
    private bool CanPublish => IsOpen && !CommentsUnavailable && IsAuthenticated && !IsPosting && !string.IsNullOrWhiteSpace(DraftText) && DraftText.Length <= 1000;
    private (long Song, long Parent, long Reply) DraftKey => (_musicId, SelectedRoot?.Id ?? 0, ReplyTarget is { IsReply: true } reply ? reply.Id : 0);

    public Task ShowAsync(long musicId, string title)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(musicId);
        SaveDraft();
        _contextGeneration++;
        _composerGeneration++;
        CancelReplies();
        SelectedRoot = null;
        ReplyTarget = null;
        PreviewImageUri = null;
        _musicId = musicId;
        if (_badgeMusicId != musicId) SetBadge(musicId, null);
        SongTitle = title;
        Feedback = "";
        RestoreDraft();
        IsOpen = true;
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(IsComposerEnabled));
        return ReloadAsync();
    }

    public Task ChangeSortAsync(SongCommentSort sort)
    {
        if (!Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(sort));
        if (Sort == sort) return Task.CompletedTask;
        Sort = sort;
        return IsOpen ? ReloadAsync() : Task.CompletedTask;
    }

    public void Close()
    {
        SaveDraft();
        _contextGeneration++;
        _composerGeneration++;
        IsOpen = false;
        CancelPending();
        CancelReplies();
        PreviewImageUri = null;
        SelectedRoot = null;
        ReplyTarget = null;
        IsLoading = false;
        UpdateState();
    }

    public async Task OpenThreadAsync(SongCommentRow root)
    {
        if (!IsOpen || root.IsReply) return;
        SaveDraft();
        _composerGeneration++;
        SelectedRoot = root;
        ReplyTarget = null;
        Feedback = "";
        RestoreDraft();
        await ReloadRepliesAsync();
    }

    public async Task ReplyToAsync(SongCommentRow row)
    {
        if (!IsOpen) return;
        var context = _contextGeneration;
        var rootId = row.IsReply ? row.ParentId : row.Id;
        if (!row.IsReply && SelectedRoot?.Id != row.Id) await OpenThreadAsync(row);
        if (context != _contextGeneration || !IsOpen || SelectedRoot?.Id != rootId) return;
        SaveDraft();
        _composerGeneration++;
        ReplyTarget = row;
        RestoreDraft();
    }

    [RelayCommand]
    public void CloseThread()
    {
        SaveDraft();
        _composerGeneration++;
        CancelReplies();
        SelectedRoot = null;
        ReplyTarget = null;
        Feedback = "";
        RestoreDraft();
    }

    [RelayCommand]
    private void ClearReplyTarget()
    {
        SaveDraft();
        _composerGeneration++;
        ReplyTarget = null;
        RestoreDraft();
    }

    public void OpenImage(Uri? uri)
    {
        if (IsOpen && uri?.Scheme is "http" or "https") PreviewImageUri = uri;
    }

    [RelayCommand]
    public void CloseImage() => PreviewImageUri = null;

    [RelayCommand]
    private Task RefreshAsync() => IsThreadOpen ? ReloadRepliesAsync() : ReloadAsync();

    private Task ReloadAsync()
    {
        CancelPending();
        Items.Clear();
        _seenIds.Clear();
        _nextPage = 1;
        TotalCount = 0;
        HasLoaded = false;
        CommentsUnavailable = false;
        HasMore = true;
        IsLoading = false;
        LoadFailed = false;
        ErrorMessage = "";
        return LoadPageAsync();
    }

    private Task ReloadRepliesAsync()
    {
        CancelReplies();
        Replies.Clear();
        _replyIds.Clear();
        _nextReplyPage = 1;
        ReplyTotal = 0;
        RepliesHasLoaded = false;
        RepliesHasMore = true;
        RepliesLoadFailed = false;
        RepliesError = "";
        return LoadRepliesPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private Task LoadMoreAsync() => LoadPageAsync();

    [RelayCommand]
    private Task RetryAsync() => LoadPageAsync();

    [RelayCommand(CanExecute = nameof(CanLoadMoreReplies))]
    private Task LoadMoreRepliesAsync() => LoadRepliesPageAsync();

    [RelayCommand]
    private Task RetryRepliesAsync() => LoadRepliesPageAsync();

    private async Task LoadPageAsync()
    {
        if (!IsOpen || IsLoading || !HasMore) return;
        var request = new CancellationTokenSource();
        _request = request;
        var generation = ++_generation;
        var context = _contextGeneration;
        IsLoading = true;
        LoadFailed = false;
        ErrorMessage = "";
        UpdateState();
        try
        {
            var page = await _api.GetSongCommentsAsync(_musicId, Sort, _nextPage, request.Token);
            if (generation != _generation || context != _contextGeneration || request.IsCancellationRequested || !IsOpen) return;
            foreach (var item in page.Items)
                if (_seenIds.Add(item.Id)) Items.Add(new SongCommentRow(item, this, _nextPage, Sort));
            TotalCount = page.TotalCount;
            SetBadge(_musicId, page.TotalCount);
            HasLoaded = true;
            HasMore = page.HasMore && page.Items.Count > 0;
            _nextPage++;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (generation != _generation || !IsOpen) return;
            LoadFailed = true;
            CommentsUnavailable = ex is NotSupportedException;
            if (CommentsUnavailable)
            {
                HasMore = false;
                ErrorMessage = "这首歌的音源暂不支持评论。";
                _logger.LogInformation("歌曲 {MusicId} 的音源 id 超出评论服务支持范围", _musicId);
            }
            else
            {
                _logger.LogWarning(ex, "歌曲 {MusicId} 评论加载失败", _musicId);
                ErrorMessage = HasItems ? "更多评论暂时加载失败，请重试。" : "评论暂时加载失败，请稍后重试。";
            }
        }
        finally
        {
            if (generation == _generation)
            {
                _request = null;
                IsLoading = false;
                UpdateState();
            }
            request.Dispose();
        }
    }

    private async Task LoadRepliesPageAsync()
    {
        if (!IsOpen || SelectedRoot is null || IsRepliesLoading || !RepliesHasMore) return;
        var root = SelectedRoot;
        var request = new CancellationTokenSource();
        _replyRequest = request;
        var generation = ++_replyGeneration;
        var context = _contextGeneration;
        IsRepliesLoading = true;
        RepliesLoadFailed = false;
        UpdateState();
        try
        {
            var page = await _api.GetSongCommentRepliesAsync(_musicId, root.Id, _nextReplyPage, request.Token);
            if (generation != _replyGeneration || context != _contextGeneration || request.IsCancellationRequested || !IsOpen) return;
            foreach (var item in page.Items)
                if (_replyIds.Add(item.Id)) Replies.Add(new SongCommentRow(item with { ParentId = root.Id }, this, _nextReplyPage, Sort));
            ReplyTotal = page.TotalCount;
            UpdateComment(root.Comment with { ReplyCount = page.TotalCount });
            RepliesHasLoaded = true;
            RepliesHasMore = page.HasMore && page.Items.Count > 0;
            _nextReplyPage++;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (generation != _replyGeneration || !IsOpen) return;
            _logger.LogWarning(ex, "歌曲 {MusicId}，主评论 {CommentId} 回复读取失败", _musicId, root.Id);
            RepliesLoadFailed = true;
            RepliesError = "回复暂时加载失败，请重试。";
        }
        finally
        {
            if (generation == _replyGeneration)
            {
                _replyRequest = null;
                IsRepliesLoading = false;
                UpdateState();
            }
            request.Dispose();
        }
    }

    public async Task ToggleLikeAsync(SongCommentRow row)
    {
        if (!IsOpen || !_likingIds.Add(row.Id)) return;
        var musicId = _musicId;
        var context = _contextGeneration;
        var sessionRevision = _session.Revision;
        var desired = !row.IsLiked;
        row.IsLiking = true;
        Feedback = "";
        try
        {
            if (!IsAuthenticated) { SetFeedback("登录后可以点赞。", true); return; }
            await _api.SetSongCommentLikeAsync(musicId, row.Id, desired, row.ParentId);
            if (context != _contextGeneration || sessionRevision != _session.Revision || !IsOpen) return;
            // 成功后直接更新当前行；用户要求点赞交互不额外读取或刷新列表。
            var updated = row.Comment with
            {
                IsLiked = desired,
                LikeCount = row.IsLiked == desired ? row.LikeCount : Math.Max(0, row.LikeCount + (desired ? 1 : -1)),
            };
            row.Update(updated);
            UpdateComment(updated);
            _logger.LogInformation("评论 {Operation} 界面已更新：歌曲 {MusicId}，评论 {CommentId}，已点赞 {Liked}，点赞数 {LikeCount}",
                desired ? "点赞" : "取消点赞", musicId, row.Id, desired, updated.LikeCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "评论点赞操作失败：歌曲 {MusicId}，评论 {CommentId}", musicId, row.Id);
            if (context == _contextGeneration && IsOpen) SetFeedback(WriteError(ex, desired ? "点赞" : "取消点赞"), true);
        }
        finally { row.IsLiking = false; _likingIds.Remove(row.Id); }
    }

    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task PublishAsync()
    {
        if (!CanPublish) return;
        var text = DraftText.Trim();
        var musicId = _musicId;
        var context = _contextGeneration;
        var composer = _composerGeneration;
        var sessionRevision = _session.Revision;
        var parentId = SelectedRoot?.Id ?? 0;
        var replyId = ReplyTarget is { IsReply: true } reply ? reply.Id : 0;
        var previousIds = (parentId > 0 ? Replies : Items).Select(item => item.Id).ToHashSet();
        var accepted = false;
        IsPosting = true;
        Feedback = "";
        try
        {
            var publishedId = await _api.PublishSongCommentAsync(musicId, text, parentId, replyId, IsAnonymous);
            accepted = true;
            if (context != _contextGeneration || composer != _composerGeneration || sessionRevision != _session.Revision || !IsOpen) return;
            if (parentId > 0) await ReloadRepliesAsync();
            else { Sort = SongCommentSort.Latest; await ReloadAsync(); }
            if (context != _contextGeneration || composer != _composerGeneration || sessionRevision != _session.Revision || !IsOpen) return;
            var rows = parentId > 0 ? Replies : Items;
            bool Matches(SongComment comment) => publishedId is { } id ? comment.Id == id
                : !previousIds.Contains(comment.Id) && comment.Content == text && comment.UserId.ToString() == _session.Uid;
            var found = rows.FirstOrDefault(row => Matches(row.Comment))?.Comment;
            // 回复按时间正序，新回复可能在末页；只读末页核对，不把未出现在首页误判为发送失败。
            if (found is null && parentId > 0 && RepliesHasLoaded && ReplyTotal > 30)
            {
                var lastPage = (int)Math.Min(int.MaxValue, Math.Max(1, (ReplyTotal + 29) / 30));
                var tail = await _api.GetSongCommentRepliesAsync(musicId, parentId, lastPage);
                if (context != _contextGeneration || composer != _composerGeneration || sessionRevision != _session.Revision || !IsOpen) return;
                found = tail.Items.FirstOrDefault(Matches);
            }
            var verified = found is not null;
            SetFeedback(verified ? parentId > 0 ? "回复已发送" : "评论已发送" : "请求已受理，暂未在列表中显示，请刷新确认。", false);
            _logger.LogInformation("评论发布读回：歌曲 {MusicId}，主评论 {ParentId}，评论 id {CommentId}，读取到 id {ReadId}，确认 {Verified}",
                musicId, parentId, publishedId, found?.Id, verified);
            if (verified)
            {
                DraftText = "";
                _drafts.Remove(DraftKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "评论发布失败或未确认：歌曲 {MusicId}，主评论 {ParentId}，回复目标 {ReplyId}", musicId, parentId, replyId);
            if (context == _contextGeneration && composer == _composerGeneration && IsOpen)
                SetFeedback(accepted ? "发送请求已受理，但暂时无法确认结果，请刷新检查。" : WriteError(ex, "发送"), true);
        }
        finally { IsPosting = false; }
    }

    private void UpdateComment(SongComment comment)
    {
        foreach (var row in Items.Concat(Replies).Where(row => row.Id == comment.Id)) row.Update(comment);
        if (SelectedRoot?.Id == comment.Id) SelectedRoot.Update(comment);
    }

    private void SetFeedback(string text, bool error) { FeedbackIsError = error; Feedback = text; }

    private static string WriteError(Exception error, string operation) => error switch
    {
        BodianApiException api => $"{operation}失败（{api.RawCode}）：{LogRedactor.Redact(api.ServerMessage ?? "请稍后再试")}",
        TimeoutException or OperationCanceledException => "请求超时，结果尚未确认，请刷新检查后再试。",
        HttpRequestException => "网络请求未完成，请刷新确认结果后再试。",
        InvalidOperationException => "登录状态已改变，请重新登录后检查结果。",
        _ => $"{operation}未完成，请稍后再试。",
    };

    private void SaveDraft()
    {
        if (_musicId <= 0) return;
        if (string.IsNullOrWhiteSpace(DraftText)) _drafts.Remove(DraftKey);
        else _drafts[DraftKey] = DraftText;
        if (_drafts.Count > 32) _drafts.Remove(_drafts.Keys.First());
    }

    private void RestoreDraft() => DraftText = _drafts.GetValueOrDefault(DraftKey, "");

    private void CancelPending() { _generation++; _request?.Cancel(); _request = null; }
    private void CancelReplies() { _replyGeneration++; _replyRequest?.Cancel(); _replyRequest = null; IsRepliesLoading = false; }

    private void UpdateState()
    {
        foreach (var property in new[] { nameof(HasItems), nameof(IsEmpty), nameof(IsInitialLoading), nameof(IsLoadingMore),
            nameof(ShowInitialError), nameof(ShowInitialRetry), nameof(ShowMoreError), nameof(ShowLoadMore), nameof(ShowEnd), nameof(ShowRepliesEmpty),
            nameof(ShowMoreReplies), nameof(ShowRepliesEnd) }) OnPropertyChanged(property);
        LoadMoreRepliesCommand.NotifyCanExecuteChanged();
    }
}
