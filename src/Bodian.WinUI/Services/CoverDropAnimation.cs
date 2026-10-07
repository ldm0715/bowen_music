using Bodian.WinUI.Playback;
using Microsoft.UI.Xaml;

namespace Bodian.WinUI.Services;

/// <summary>捕获点击时的封面，确认播放成功后交给共享封面动画层。</summary>
internal sealed class CoverDropAnimation : IDisposable
{
    private const string ResourceKey = "BodianCoverDropAnimation";
    private readonly CoverTransitionAnimator _animator;
    private readonly FrameworkElement _destination;
    private readonly PlaybackCoordinator _playback;
    private PendingCover? _pending;
    private bool _disposed;
    private sealed record PendingCover(long TrackId, CoverTransitionAnimator.Snapshot Cover);

    public CoverDropAnimation(CoverTransitionAnimator animator, FrameworkElement destination, PlaybackCoordinator playback)
    {
        _animator = animator;
        _destination = destination;
        _playback = playback;
        playback.Started += OnPlaybackStarted;
        playback.Blocked += OnPlaybackBlocked;
        Application.Current.Resources[ResourceKey] = this;
    }

    public static void Request(FrameworkElement? source, long trackId)
    {
        if (!AppMotion.IsEnabled || source is null) return;
        if (Application.Current.Resources.TryGetValue(ResourceKey, out var value) && value is CoverDropAnimation motion)
        {
            motion.Cancel();
            if (motion._animator.Capture(source) is { } cover)
                motion._pending = new PendingCover(trackId, cover);
        }
    }

    private void OnPlaybackStarted(object? sender, PlaybackStartedEventArgs args)
    {
        var pending = _pending;
        Cancel();
        if (_disposed || pending is null || pending.TrackId != args.Track.Id) return;
        _ = _animator.PlayDropAsync(pending.Cover, _destination);
    }

    private void OnPlaybackBlocked(object? sender, PlaybackBlockedEventArgs args) => Cancel();

    public void Cancel()
    {
        _pending = null;
        _animator.CancelFlight();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cancel();
        _playback.Started -= OnPlaybackStarted;
        _playback.Blocked -= OnPlaybackBlocked;
        if (Application.Current.Resources.TryGetValue(ResourceKey, out var value) && ReferenceEquals(value, this))
            Application.Current.Resources.Remove(ResourceKey);
    }
}
