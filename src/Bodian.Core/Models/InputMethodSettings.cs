namespace Bodian.Core.Models;

/// <summary>输入法兼容偏好。只有下次进程启动时应用，不改变当前输入框。</summary>
public sealed record InputMethodSettings(bool CompatibilityEnabled = false)
{
    public static InputMethodSettings Default { get; } = new();
}
