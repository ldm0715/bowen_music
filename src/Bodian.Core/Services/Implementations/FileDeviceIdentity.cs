using System.Security.Cryptography;
using Bodian.Core.Services.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <summary>
/// 把设备标识落在文件里，一次生成、长期复用。
/// </summary>
/// <remarks>
/// 移植自 P0 探针的 <c>DeviceIdentity</c>，**行为与存储位置都保持一致**，
/// 这样客户端与探针看到的是同一个 devid。
/// </remarks>
public sealed class FileDeviceIdentity : IDeviceIdentity
{
    private readonly string _path;
    private readonly Lazy<string> _value;

    /// <param name="path">默认 <see cref="AppPaths.DeviceIdFile"/>（与探针共用）。测试可注入临时路径。</param>
    public FileDeviceIdentity(string? path = null)
    {
        _path = path ?? AppPaths.DeviceIdFile;
        _value = new Lazy<string>(ReadOrCreate);
    }

    public string Value => _value.Value;

    /// <summary>本次是否重新生成了标识（内容非法或文件不存在时为 <c>true</c>）。</summary>
    public bool IsNewlyCreated { get; private set; }

    private string ReadOrCreate()
    {
        IsNewlyCreated = false;

        if (File.Exists(_path))
        {
            var existing = File.ReadAllText(_path).Trim();
            if (IsValid(existing))
            {
                return existing;
            }
        }

        var fresh = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, fresh);
        IsNewlyCreated = true;
        return fresh;
    }

    /// <summary>必须是 32 位小写十六进制，否则视为非法并重新生成。</summary>
    public static bool IsValid(string value)
        => value.Length == 32 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
