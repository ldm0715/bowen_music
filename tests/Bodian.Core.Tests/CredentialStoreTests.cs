using System.Runtime.Versioning;
using System.Text;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 凭据落盘与设备标识。需要真实的 DPAPI，所以整类标 Windows-only。
/// </summary>
/// <remarks>
/// 测试项目是平台中立的 <c>net10.0</c>，而 <see cref="DpapiCredentialStore"/> 标了
/// <c>[SupportedOSPlatform("windows")]</c>，所以这里必须跟着标，否则触发 CA1416。
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class CredentialStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bodian-tests-" + Guid.NewGuid().ToString("N"));

    private string TempFile(string name) => Path.Combine(_dir, name);

    public CredentialStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // 只删自己建的临时目录，绝不碰 %LOCALAPPDATA%\Bodian
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static BodianCredential Sample() => new("50303440", "tok_abcdef0123456789", "测试小号");

    // ── 往返 ────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var store = new DpapiCredentialStore(TempFile("session.dat"));

        Assert.Null(store.Load());                       // 还没有

        store.Save(Sample());
        var loaded = Assert.IsType<BodianCredential>(store.Load());

        Assert.Equal("50303440", loaded.Uid);
        Assert.Equal("tok_abcdef0123456789", loaded.Token);
        Assert.Equal("测试小号", loaded.Nickname);
        Assert.True(loaded.IsAuthenticated);
    }

    /// <summary>落盘的是密文。这条守的是「绝不写明文 token」那条红线。</summary>
    [Fact]
    public void SavedFile_DoesNotContainPlaintext()
    {
        var path = TempFile("session.dat");
        var store = new DpapiCredentialStore(path);
        store.Save(Sample());

        var bytes = File.ReadAllBytes(path);
        var asText = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("tok_abcdef0123456789", asText);
        Assert.DoesNotContain("50303440", asText);
        Assert.DoesNotContain("测试小号", asText);
        // 连字段名都不该出现——说明整个 JSON 都被加密了，不是逐字段加密
        Assert.DoesNotContain("Token", asText);

        // 但确实有内容
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Clear_RemovesFile_AndReportsWhetherItDid()
    {
        var store = new DpapiCredentialStore(TempFile("session.dat"));

        Assert.False(store.Clear());                     // 本来就没有

        store.Save(Sample());
        Assert.True(store.Clear());
        Assert.Null(store.Load());
        Assert.False(store.Clear());
    }

    /// <summary>换了 Windows 账号、或文件被外部改动过时应当返回 null，而不是抛异常让应用起不来。</summary>
    [Fact]
    public void Load_ReturnsNull_OnGarbageFile()
    {
        var path = TempFile("session.dat");
        File.WriteAllBytes(path, "this is not a DPAPI blob"u8.ToArray());

        Assert.Null(new DpapiCredentialStore(path).Load());
    }

    // ── 与 P0 探针的兼容性 ─────────────────────────────────────────────────

    /// <summary>
    /// **这条是本组最重要的测试：新实现必须读得动 P0 探针写下的会话文件。**
    /// </summary>
    /// <remarks>
    /// 兼容性取决于三样东西同时正确：文件路径、DPAPI 的附加 entropy、
    /// 以及 JSON 字段名（<c>Uid</c> / <c>Token</c> / <c>Nickname</c>）。
    /// 任何一样改动都会让已登录的会话读不出来，而那种错**不会报错，只会表现为「登录态丢了」**。
    /// <para>
    /// 只读真实文件、不写不删。文件不存在时跳过（换个没跑过探针的机器是正常情况）。
    /// </para>
    /// </remarks>
    [Fact]
    public void ReadsProbeSessionFile_WrittenByP0Tool()
    {
        if (!File.Exists(AppPaths.CredentialFile))
        {
            Assert.Skip($"没有 {AppPaths.CredentialFile}（本机还没跑过探针的 login），跳过兼容性检查。");
        }

        var credential = new DpapiCredentialStore().Load();

        Assert.NotNull(credential);
        Assert.False(string.IsNullOrWhiteSpace(credential.Uid));
        Assert.True(credential.IsAuthenticated, $"uid 是 {credential.Uid}，不像已登录会话");
    }

    /// <summary>
    /// 路径常量必须与探针一致 —— 改了路径就等于换了设备与账号。
    /// </summary>
    /// <remarks>
    /// 这里是**逐字**断言，所以改目录名时它一定会红 —— 这正是它的用处：
    /// 提醒你探针那份副本（<c>tools/Bodian.Probe/ProbePaths.cs</c>）要一起改。
    /// <b>但更该做的是不改</b>：目录里的 <c>devid.txt</c> 是设备标识，换路径等于换设备，
    /// 那是账号风控的异常信号。见 <c>AppPaths</c> 的注释。
    /// </remarks>
    [Fact]
    public void Paths_MatchTheProbeConventions()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(localAppData, "Bowen"), AppPaths.LocalAppData);
        Assert.Equal(Path.Combine(localAppData, "Bowen", "devid.txt"), AppPaths.DeviceIdFile);
        Assert.Equal(Path.Combine(localAppData, "Bowen", "session.dat"), AppPaths.CredentialFile);
    }
}

/// <summary>
/// 设备标识。它不需要 DPAPI，但要在 Windows 上跑文件系统。
/// </summary>
public sealed class DeviceIdentityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bodian-tests-" + Guid.NewGuid().ToString("N"));

    private string TempFile(string name) => Path.Combine(_dir, name);

    public DeviceIdentityTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void CreatesAndPersists_WhenMissing()
    {
        var path = TempFile("devid.txt");
        var identity = new FileDeviceIdentity(path);

        var value = identity.Value;

        Assert.True(FileDeviceIdentity.IsValid(value));
        Assert.True(identity.IsNewlyCreated);
        Assert.Equal(value, File.ReadAllText(path).Trim());
    }

    /// <summary>
    /// **已有的标识必须原样返回。** 重新生成设备标识是账号风控的异常信号。
    /// </summary>
    [Fact]
    public void ReusesExistingValue_Verbatim()
    {
        var path = TempFile("devid.txt");
        const string existing = "0123456789abcdef0123456789abcdef";
        File.WriteAllText(path, existing);

        var identity = new FileDeviceIdentity(path);

        Assert.Equal(existing, identity.Value);
        Assert.False(identity.IsNewlyCreated);
        Assert.Equal(existing, File.ReadAllText(path).Trim());   // 一个字都没改
    }

    [Fact]
    public void Regenerates_WhenContentIsInvalid()
    {
        var path = TempFile("devid.txt");
        File.WriteAllText(path, "不是设备标识");

        var identity = new FileDeviceIdentity(path);

        Assert.True(FileDeviceIdentity.IsValid(identity.Value));
        Assert.NotEqual("不是设备标识", identity.Value);
        Assert.True(identity.IsNewlyCreated);
    }

    /// <summary>尾随换行不该被当成内容差异。</summary>
    [Fact]
    public void TrimsTrailingWhitespace()
    {
        var path = TempFile("devid.txt");
        File.WriteAllText(path, "0123456789abcdef0123456789abcdef\r\n");

        var identity = new FileDeviceIdentity(path);

        Assert.Equal("0123456789abcdef0123456789abcdef", identity.Value);
        Assert.False(identity.IsNewlyCreated);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef", true)]    // 32 位小写十六进制
    [InlineData("0123456789ABCDEF0123456789ABCDEF", false)]   // 大写不行
    [InlineData("0123456789abcdef0123456789abcde", false)]    // 31 位
    [InlineData("0123456789abcdef0123456789abcdef0", false)]  // 33 位
    [InlineData("", false)]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz", false)]
    public void IsValid_EnforcesLowercaseHex32(string value, bool expected)
        => Assert.Equal(expected, FileDeviceIdentity.IsValid(value));

    /// <summary>
    /// 默认路径必须与 P0 探针相同，且**不能重新生成**——否则客户端会换一个设备标识。
    /// </summary>
    [Fact]
    public void DefaultPath_SharesTheProbesDeviceId()
    {
        Assert.Equal(AppPaths.DeviceIdFile, Path.Combine(AppPaths.LocalAppData, "devid.txt"));

        if (!File.Exists(AppPaths.DeviceIdFile))
        {
            Assert.Skip($"没有 {AppPaths.DeviceIdFile}（本机还没跑过探针的 devid 命令），跳过。");
        }

        var onDisk = File.ReadAllText(AppPaths.DeviceIdFile).Trim();

        Assert.True(FileDeviceIdentity.IsValid(onDisk));
        Assert.Equal(onDisk, new FileDeviceIdentity().Value);
    }
}
