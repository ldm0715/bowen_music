using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.ViewModels;

namespace Bodian.WinUI.Services;

/// <inheritdoc cref="IShortcutService" />
public sealed class ShortcutService : IShortcutService
{
    /// <summary>音量加减的步长。与播放条滑块拖动时的粒度无关，这是「按一下」该有的幅度。</summary>
    private const double VolumeStep = 5;

    private readonly IShortcutSettingsStore _store;
    private readonly PlayerViewModel _player;

    private ShortcutSettings _settings;

    public ShortcutService(IShortcutSettingsStore store, PlayerViewModel player)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(player);

        _store = store;
        _player = player;

        // 同步读：加速器要在窗口建起来之前装好，异步读会让头几百毫秒里按键没反应。
        _settings = store.Load().Normalized();
    }

    public IReadOnlyList<ShortcutBinding> Bindings => _settings.Bindings;

    public event EventHandler? Changed;

    public ShortcutAction? Match(ShortcutKey key, ShortcutModifiers modifiers) => _settings.Find(key, modifiers);

    public bool TryRebind(ShortcutAction action, ShortcutKey key, ShortcutModifiers modifiers, out ShortcutAction? conflict)
    {
        conflict = _settings.ConflictOf(action, key, modifiers);
        if (conflict is not null)
        {
            return false;
        }

        Apply(_settings.With(new ShortcutBinding(action, key, modifiers)));
        return true;
    }

    public void ResetToDefault() => Apply(ShortcutSettings.Default);

    public void Invoke(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.TogglePlayPause:
                _player.TogglePlayPauseCommand.Execute(null);
                break;

            case ShortcutAction.PreviousTrack:
                _player.PreviousCommand.Execute(null);
                break;

            case ShortcutAction.NextTrack:
                _player.NextCommand.Execute(null);
                break;

            // 音量走 PlayerViewModel.Volume：播放条那颗滑块绑的是同一个值，会跟着动。
            // 不落盘，与音量本身的现状一致（会话级）。
            case ShortcutAction.VolumeUp:
                _player.Volume = Math.Min(100, _player.Volume + VolumeStep);
                break;

            case ShortcutAction.VolumeDown:
                _player.Volume = Math.Max(0, _player.Volume - VolumeStep);
                break;

            case ShortcutAction.ToggleFavorite:
                _player.ToggleFavoriteCommand.Execute(null);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private void Apply(ShortcutSettings settings)
    {
        _settings = settings.Normalized();
        _store.Save(_settings);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
