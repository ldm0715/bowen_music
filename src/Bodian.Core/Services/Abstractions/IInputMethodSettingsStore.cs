using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>启动前同步读取输入法兼容偏好；保存失败时返回 false。</summary>
public interface IInputMethodSettingsStore
{
    InputMethodSettings Load();
    bool Save(InputMethodSettings settings);
}
