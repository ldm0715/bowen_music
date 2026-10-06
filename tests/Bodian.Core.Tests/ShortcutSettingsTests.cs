using Bodian.Core.Models;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 快捷键键位表的模型逻辑。**零 IO** —— 只管「哪一份算合规、手势怎么查」。
/// </summary>
/// <remarks>
/// 这一份的重点不是默认值长什么样，而是<b>坏输入不能把整套快捷键搞失灵</b>：
/// 键位表是手改得动的明文文件，改坏一个键位不该让另外五个动作一起失效，
/// 也不该出现两个动作抢同一个手势（那样后一个等于被静默吃掉）。
/// </remarks>
public sealed class ShortcutSettingsTests
{
    private static ShortcutSettings With(params ShortcutBinding[] bindings) => new() { Bindings = bindings };

    [Fact]
    public void Default_CoversEveryAction()
    {
        foreach (var action in ShortcutActions.All)
        {
            Assert.NotNull(ShortcutSettings.Default.For(action));
        }
    }

    [Fact]
    public void Default_MatchesTheReferenceKeys()
    {
        AssertGesture(ShortcutAction.TogglePlayPause, ShortcutKey.Space, ShortcutModifiers.None);
        AssertGesture(ShortcutAction.PreviousTrack, ShortcutKey.Left, ShortcutModifiers.Control);
        AssertGesture(ShortcutAction.NextTrack, ShortcutKey.Right, ShortcutModifiers.Control);
        AssertGesture(ShortcutAction.VolumeUp, ShortcutKey.Up, ShortcutModifiers.Control);
        AssertGesture(ShortcutAction.VolumeDown, ShortcutKey.Down, ShortcutModifiers.Control);
        AssertGesture(ShortcutAction.ToggleFavorite, ShortcutKey.L, ShortcutModifiers.Control);
    }

    [Fact]
    public void Normalized_LeavesTheDefaultAlone()
    {
        var normalized = ShortcutSettings.Default.Normalized();

        Assert.Equal(ShortcutSettings.Default.Bindings, normalized.Bindings);
    }

    [Fact]
    public void Normalized_FillsInMissingActions()
    {
        // 文件里只写了「下一首」，其余五个应当补回默认，而不是整体作废。
        var normalized = With(
            new ShortcutBinding(ShortcutAction.NextTrack, ShortcutKey.N, ShortcutModifiers.Control)).Normalized();

        Assert.Equal(ShortcutActions.All.Length, normalized.Bindings.Length);
        AssertGestureIn(normalized, ShortcutAction.NextTrack, ShortcutKey.N, ShortcutModifiers.Control);
        AssertGestureIn(normalized, ShortcutAction.TogglePlayPause, ShortcutKey.Space, ShortcutModifiers.None);
    }

    [Fact]
    public void Normalized_RejectsBareLetterKeys()
    {
        // 裸字母会把打字废掉，退回默认。
        var normalized = With(
            new ShortcutBinding(ShortcutAction.NextTrack, ShortcutKey.N, ShortcutModifiers.None)).Normalized();

        AssertGestureIn(normalized, ShortcutAction.NextTrack, ShortcutKey.Right, ShortcutModifiers.Control);
    }

    [Fact]
    public void Normalized_AllowsBareSpaceAndFunctionKeys()
    {
        var normalized = With(
            new ShortcutBinding(ShortcutAction.ToggleFavorite, ShortcutKey.F5, ShortcutModifiers.None)).Normalized();

        AssertGestureIn(normalized, ShortcutAction.ToggleFavorite, ShortcutKey.F5, ShortcutModifiers.None);
    }

    [Fact]
    public void Normalized_RejectsKeysOutsideTheEnum()
    {
        // Escape（0x1B）刻意不在枚举里，文件里手写进来也要被剔掉。
        var normalized = With(
            new ShortcutBinding(ShortcutAction.NextTrack, (ShortcutKey)0x1B, ShortcutModifiers.Control)).Normalized();

        AssertGestureIn(normalized, ShortcutAction.NextTrack, ShortcutKey.Right, ShortcutModifiers.Control);
    }

    [Fact]
    public void Normalized_RejectsUnknownModifierBits()
    {
        // Windows 键不在允许的位里。
        var normalized = With(
            new ShortcutBinding(ShortcutAction.NextTrack, ShortcutKey.Right, (ShortcutModifiers)8)).Normalized();

        AssertGestureIn(normalized, ShortcutAction.NextTrack, ShortcutKey.Right, ShortcutModifiers.Control);
    }

    [Fact]
    public void Normalized_GivesAConflictingGestureToTheEarlierActionOnly()
    {
        // 后一项抄了前一项的手势：后一项退回默认，不能两个动作共用一个手势。
        var normalized = With(
            new ShortcutBinding(ShortcutAction.TogglePlayPause, ShortcutKey.Space, ShortcutModifiers.None),
            new ShortcutBinding(ShortcutAction.NextTrack, ShortcutKey.Space, ShortcutModifiers.None)).Normalized();

        AssertGestureIn(normalized, ShortcutAction.TogglePlayPause, ShortcutKey.Space, ShortcutModifiers.None);
        AssertGestureIn(normalized, ShortcutAction.NextTrack, ShortcutKey.Right, ShortcutModifiers.Control);
    }

    [Fact]
    public void Find_RequiresTheModifierSetToMatchExactly()
    {
        var settings = ShortcutSettings.Default;

        Assert.Equal(ShortcutAction.NextTrack, settings.Find(ShortcutKey.Right, ShortcutModifiers.Control));

        // 多一个 Shift 就是另一个手势，不该命中 Ctrl+→。
        Assert.Null(settings.Find(ShortcutKey.Right, ShortcutModifiers.Control | ShortcutModifiers.Shift));
        Assert.Null(settings.Find(ShortcutKey.Right, ShortcutModifiers.None));
    }

    [Fact]
    public void ConflictOf_IgnoresTheActionItself()
    {
        var settings = ShortcutSettings.Default;

        // 改「下一首」时，它自己占着的 Ctrl+→ 不该算冲突。
        Assert.Null(settings.ConflictOf(ShortcutAction.NextTrack, ShortcutKey.Right, ShortcutModifiers.Control));

        // 拿「上一首」的键去查，占位者就是「上一首」。
        Assert.Equal(
            ShortcutAction.PreviousTrack,
            settings.ConflictOf(ShortcutAction.NextTrack, ShortcutKey.Left, ShortcutModifiers.Control));
    }

    [Fact]
    public void With_ReplacesTheExistingBindingInsteadOfAppending()
    {
        var updated = ShortcutSettings.Default.With(
            new ShortcutBinding(ShortcutAction.NextTrack, ShortcutKey.N, ShortcutModifiers.Control));

        Assert.Equal(ShortcutActions.All.Length, updated.Bindings.Length);
        AssertGestureIn(updated, ShortcutAction.NextTrack, ShortcutKey.N, ShortcutModifiers.Control);
    }

    [Fact]
    public void With_AppendsWhenTheActionHadNoBinding()
    {
        var updated = With(new ShortcutBinding(ShortcutAction.NextTrack, ShortcutKey.N, ShortcutModifiers.Control))
            .With(new ShortcutBinding(ShortcutAction.ToggleFavorite, ShortcutKey.F5, ShortcutModifiers.None));

        Assert.Equal(2, updated.Bindings.Length);
        AssertGestureIn(updated, ShortcutAction.ToggleFavorite, ShortcutKey.F5, ShortcutModifiers.None);
    }

    private static void AssertGesture(ShortcutAction action, ShortcutKey key, ShortcutModifiers modifiers) =>
        AssertGestureIn(ShortcutSettings.Default, action, key, modifiers);

    private static void AssertGestureIn(
        ShortcutSettings settings, ShortcutAction action, ShortcutKey key, ShortcutModifiers modifiers)
    {
        var binding = settings.For(action);
        Assert.NotNull(binding);
        Assert.Equal(key, binding.Key);
        Assert.Equal(modifiers, binding.Modifiers);
    }
}
