using System.Security.Cryptography;

namespace Bodian.Probe;

/// <summary>
/// 设备标识。devid 与 qimei36 用同一个值，生成一次后必须稳定复用——
/// 频繁变更设备标识本身就是账号风控的异常信号。
/// </summary>
internal static class DeviceIdentity
{
    public static string StoragePath { get; } =
        Path.Combine(ProbePaths.LocalAppData, "devid.txt");

    public static bool IsNewlyCreated { get; private set; }

    /// <summary>读取已持久化的 devid；不存在或内容非法时重新生成并落盘。</summary>
    public static string GetOrCreate()
    {
        IsNewlyCreated = false;

        if (File.Exists(StoragePath))
        {
            var existing = File.ReadAllText(StoragePath).Trim();
            if (IsValid(existing))
            {
                return existing;
            }

            Console.Error.WriteLine($"警告：{StoragePath} 内容不是 32 位小写十六进制，将重新生成。");
        }

        var fresh = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
        File.WriteAllText(StoragePath, fresh);
        IsNewlyCreated = true;
        return fresh;
    }

    private static bool IsValid(string value)
        => value.Length == 32 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
