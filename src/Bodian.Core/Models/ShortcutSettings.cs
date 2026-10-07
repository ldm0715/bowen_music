namespace Bodian.Core.Models;

/// <summary>
/// 应用内快捷键的键位表。
/// </summary>
/// <remarks>
/// <para>
/// <b>用数组不用 <c>IReadOnlyList</c></b>：源生成 JSON 对数组的往返最直接，
/// 接口类型会走另一条反序列化路径，没必要为了一点点类型洁癖引入不确定性。
/// </para>
/// <para>
/// <b>只做应用内快捷键</b>，不注册全局热键：应用失去焦点时不响应，这正是应用内快捷键该有的语义，
/// 也避免了与其它软件抢占组合键。
/// </para>
/// </remarks>
public sealed record ShortcutSettings
{
    /// <summary>每个动作一条。缺项表示这个动作这一轮没有快捷键。</summary>
    public ShortcutBinding[] Bindings { get; init; } = [];

    /// <summary>出厂键位。</summary>
    public static ShortcutSettings Default { get; } = new()
    {
        Bindings =
        [
            new(ShortcutAction.TogglePlayPause, ShortcutKey.Space, ShortcutModifiers.None),
            new(ShortcutAction.PreviousTrack, ShortcutKey.Left, ShortcutModifiers.Control),
            new(ShortcutAction.NextTrack, ShortcutKey.Right, ShortcutModifiers.Control),
            new(ShortcutAction.VolumeUp, ShortcutKey.Up, ShortcutModifiers.Control),
            new(ShortcutAction.VolumeDown, ShortcutKey.Down, ShortcutModifiers.Control),
            new(ShortcutAction.ToggleMute, ShortcutKey.M, ShortcutModifiers.Control),
            new(ShortcutAction.ToggleFavorite, ShortcutKey.L, ShortcutModifiers.Control),
        ],
    };

    /// <summary>取某个动作当前的绑定；没有就是 <c>null</c>。</summary>
    public ShortcutBinding? For(ShortcutAction action)
    {
        foreach (var binding in Bindings)
        {
            if (binding.Action == action)
            {
                return binding;
            }
        }

        return null;
    }

    /// <summary>
    /// 逐项收口：补齐缺失的动作、把不合法的手势换回默认、把撞车的手势让给先来的。
    /// </summary>
    /// <remarks>
    /// <b>整份文件读不出来时用 <see cref="Default"/>，但文件读得出来、只是某一项坏了时逐项修。</b>
    /// 手工改坏一个键位不该让整套快捷键失灵；也不该出现两个动作抢同一个手势
    /// （那样先命中的那个永远赢，另一个等于被静默吃掉）。
    /// </remarks>
    public ShortcutSettings Normalized()
    {
        var result = new List<ShortcutBinding>(ShortcutActions.All.Length);
        var used = new HashSet<(ShortcutKey Key, ShortcutModifiers Modifiers)>();

        foreach (var action in ShortcutActions.All)
        {
            var fallback = Default.For(action)!;
            var candidate = For(action) ?? fallback;

            if (!IsUsable(candidate))
            {
                candidate = fallback;
            }

            // 撞车先让回默认位；默认位也被人占了，这个动作这一轮就没有快捷键。
            if (used.Contains(candidate.Gesture))
            {
                candidate = fallback;
            }

            if (!used.Add(candidate.Gesture))
            {
                continue;
            }

            result.Add(candidate);
        }

        return this with { Bindings = [.. result] };
    }

    /// <summary>这个手势触发哪个动作。没有就是 <c>null</c>（派发时每按一个键都要走这条）。</summary>
    public ShortcutAction? Find(ShortcutKey key, ShortcutModifiers modifiers)
    {
        foreach (var binding in Bindings)
        {
            if (binding.Gesture == (key, modifiers))
            {
                return binding.Action;
            }
        }

        return null;
    }

    /// <summary>这个手势被谁占了。<paramref name="except"/> 自己不参与（改键时用）。没有就是 <c>null</c>。</summary>
    public ShortcutAction? ConflictOf(ShortcutAction except, ShortcutKey key, ShortcutModifiers modifiers)
    {
        foreach (var binding in Bindings)
        {
            if (binding.Action != except && binding.Gesture == (key, modifiers))
            {
                return binding.Action;
            }
        }

        return null;
    }

    /// <summary>把手势换成另一个动作的绑定，返回新的一份（不改原对象）。</summary>
    public ShortcutSettings With(ShortcutBinding binding)
    {
        var result = new List<ShortcutBinding>(Bindings.Length + 1);
        var replaced = false;

        foreach (var existing in Bindings)
        {
            if (existing.Action == binding.Action)
            {
                result.Add(binding);
                replaced = true;
            }
            else
            {
                result.Add(existing);
            }
        }

        if (!replaced)
        {
            result.Add(binding);
        }

        return this with { Bindings = [.. result] };
    }

    /// <summary>键认识、修饰键没越界，而且「裸键」是该裸的那种。</summary>
    private static bool IsUsable(ShortcutBinding binding) =>
        ShortcutKeyRules.IsBindable(binding.Key)
        && ShortcutKeyRules.IsValidModifiers(binding.Modifiers)
        && (binding.Modifiers != ShortcutModifiers.None || ShortcutKeyRules.AllowWithoutModifier(binding.Key));
}
