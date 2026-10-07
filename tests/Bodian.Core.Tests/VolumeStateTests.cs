using Bodian.WinUI.Playback;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 静音状态机。**零 IO、零 UI** —— 只管「静音之后送出去的是多少」与「什么时候该自己解除静音」。
/// </summary>
/// <remarks>
/// 这一份守的是两件不显然的事：<b>静音不改变音量本身</b>（滑块要停在原值），
/// 以及<b>用户显式调音量时必须自动解除静音</b>（否则会出现「图标是静音、音量却在变」的鬼状态）。
/// 两条都只在界面上看得见，所以只能靠这里钉住。
/// </remarks>
public sealed class VolumeStateTests
{
    [Fact]
    public void NotMuted_PassesTheVolumeThrough()
    {
        var state = new VolumeState();

        Assert.False(state.Muted);
        Assert.Equal(42, state.EffectiveVolume(42));
    }

    [Fact]
    public void Toggle_MutesAndSendsZero()
    {
        var state = new VolumeState();

        state.Toggle();

        Assert.True(state.Muted);
        Assert.Equal(0, state.EffectiveVolume(42));
    }

    [Fact]
    public void Toggle_MutedTwice_RestoresTheOriginalVolume()
    {
        // 恢复靠的就是「音量字段没被动过」这一条 —— 状态机不许自己存一份音量。
        var state = new VolumeState();

        state.Toggle();
        state.Toggle();

        Assert.False(state.Muted);
        Assert.Equal(42, state.EffectiveVolume(42));
    }

    [Fact]
    public void OnVolumeSet_UnmutesWhenTheUserRaisesTheVolume()
    {
        var state = new VolumeState();
        state.Toggle();

        var changed = state.OnVolumeSet(55);

        Assert.True(changed);
        Assert.False(state.Muted);
        Assert.Equal(55, state.EffectiveVolume(55));
    }

    [Fact]
    public void OnVolumeSet_StaysMutedWhenVolumeHitsZero()
    {
        // 拖到 0 本来就是静音的意思，不该顺手把标记抹掉 —— 否则用户得再点一次才安静得下来。
        var state = new VolumeState();
        state.Toggle();

        var changed = state.OnVolumeSet(0);

        Assert.False(changed);
        Assert.True(state.Muted);
    }

    [Fact]
    public void OnVolumeSet_ReportsNoChangeWhenNotMuted()
    {
        // 返回值驱动「要不要补一次 IsMuted 通知」，没静音时不该白报一次。
        var state = new VolumeState();

        Assert.False(state.OnVolumeSet(30));
        Assert.False(state.Muted);
    }

    [Fact]
    public void EffectiveVolume_WorksBeneathTheZeroToOneScale()
    {
        // MV 那套走 0–1，状态机不持有数值，所以两套标度都成立。
        var state = new VolumeState();

        Assert.Equal(0.8, state.EffectiveVolume(0.8));

        state.Toggle();

        Assert.Equal(0, state.EffectiveVolume(0.8));
    }
}
