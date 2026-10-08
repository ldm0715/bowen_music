using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bodian.Probe;

internal sealed record BodianSession(string Uid, string Token, string? Nickname)
{
    public string Describe() => $"{Nickname ?? "(无昵称)"} / uid={Uid}";
}

/// <summary>
/// 会话凭据落盘。用 DPAPI 加密，绝不写明文 token——
/// 这是 roadmap 里「凭据」那条红线的第一条，从第一天就要做。
/// </summary>
internal static class SessionStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Bodian.Session.v1");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string StoragePath { get; } =
        Path.Combine(ProbePaths.LocalAppData, "session.dat");

    public static BodianSession? Load()
    {
        if (!File.Exists(StoragePath))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(StoragePath), Entropy, DataProtectionScope.CurrentUser);

            return JsonSerializer.Deserialize<BodianSession>(plain, Json);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            // 换了 Windows 账号、或文件被外部改动过——当作没有会话，别让探针起不来。
            Console.Error.WriteLine($"警告：{StoragePath} 无法解密，已忽略（{ex.GetType().Name}）。");
            return null;
        }
    }

    public static void Save(BodianSession session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);

        var plain = JsonSerializer.SerializeToUtf8Bytes(session, Json);
        var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

        File.WriteAllBytes(StoragePath, encrypted);
    }

    /// <summary>返回是否真的删掉了东西。</summary>
    public static bool Clear()
    {
        if (!File.Exists(StoragePath))
        {
            return false;
        }

        File.Delete(StoragePath);
        return true;
    }
}
