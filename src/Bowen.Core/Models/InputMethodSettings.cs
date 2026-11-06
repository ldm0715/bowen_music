namespace Bowen.Core.Models;

/// <summary>输入法兼容偏好。只有下次进程启动时应用，不改变当前输入框。</summary>
public sealed record InputMethodSettings
{
    /// <summary>是否启用输入法兼容模式。</summary>
    public bool CompatibilityEnabled { get; set; }

    public static InputMethodSettings Default { get; } = new();

    /// <summary>保留旧的构造签名，供现有调用点使用。</summary>
    public InputMethodSettings()
    {
    }

    /// <summary>保留旧的构造签名，供现有调用点使用。</summary>
    public InputMethodSettings(bool CompatibilityEnabled)
    {
        this.CompatibilityEnabled = CompatibilityEnabled;
    }
}
