namespace Bodian.WinUI.Playback;

/// <summary>
/// 音量的静音维度：一个开关，外加「显式改音量就解除静音」这条规则。
/// </summary>
/// <remarks>
/// <para>
/// <b>只管标记，不持有音量本身。</b> 播放条/歌词页那套音量是 0–100、MV 那套是 0–1，
/// 把数值收进来就得跟着标度走；<see cref="EffectiveVolume"/> 只做「静音就压成 0、否则原样返回」，
/// 两个标度都成立，两处视图模型共用这一份。
/// </para>
/// <para>
/// <b>为什么要记住"静音前是多少"这件事没有实现成字段。</b> 静音时音量字段本身不动
/// （滑块必须停在原值），所以「恢复」就是把标记翻回去，<see cref="EffectiveVolume"/> 自然又送出原值 ——
/// 多存一份 <c>volumeBeforeMute</c> 反而会在两者不同步时变成第二个真相来源。
/// </para>
/// </remarks>
public sealed class VolumeState
{
    /// <summary>是否处于静音。</summary>
    public bool Muted { get; private set; }

    /// <summary>切换静音。标记必变，所以没有返回值 —— 调用方切完总要补一次属性通知。</summary>
    public void Toggle() => Muted = !Muted;

    /// <summary>
    /// 音量被显式改到 <paramref name="volume"/>（拖滑条、或音量快捷键）——只要不为 0，静音随之解除。
    /// </summary>
    /// <remarks>
    /// <b>这条不能省。</b> 少了它就会出现「图标显示静音、音量却在往上走」的鬼状态：
    /// 用户按 Ctrl+↑ 想调大声音，界面却还说自己是静音的。
    /// 改成 0 时不解除 —— 那本来就是静音的意思，留着标记反而省得用户再点一次。
    /// 返回标记是否变化。
    /// </remarks>
    public bool OnVolumeSet(double volume)
    {
        if (!Muted || volume <= 0)
        {
            return false;
        }

        Muted = false;
        return true;
    }

    /// <summary>真正该送给播放引擎的音量。</summary>
    public double EffectiveVolume(double volume) => Muted ? 0 : volume;
}
