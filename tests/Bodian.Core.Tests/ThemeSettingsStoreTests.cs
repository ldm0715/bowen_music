using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 外观设置的本地存储。**零网络、零真实用户目录** —— 每个测试用自己那份临时文件。
/// </summary>
/// <remarks>
/// 这里刻意覆盖「文件坏掉」的几种形态。外观设置是启动早期就要读的东西，
/// 它在坏文件上抛异常等于应用起不来 —— 那是这个测试类主要想挡住的事。
/// </remarks>
public sealed class ThemeSettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bodian-settings-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonThemeSettingsStore NewStore() => new(Path_);

    // ── 基本读写 ────────────────────────────────────────────────────────────

    [Fact]
    public void NoFile_FallsBackToSystem()
    {
        Assert.Equal(AppTheme.System, NewStore().Load().Theme);
    }

    [Theory]
    [InlineData(AppTheme.System)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void Save_ThenLoad_RoundTrips(AppTheme theme)
    {
        var store = NewStore();

        store.Save(new ThemeSettings(theme));

        Assert.Equal(theme, NewStore().Load().Theme);
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var store = NewStore();

        // _directory 此时还不存在 —— 构造函数不该建目录，只有写的时候才建。
        Assert.False(Directory.Exists(_directory));

        store.Save(new ThemeSettings(AppTheme.Dark));

        Assert.True(File.Exists(Path_));
        Assert.Equal(AppTheme.Dark, store.Load().Theme);
    }

    [Fact]
    public void Save_Twice_LastWriteWins()
    {
        var store = NewStore();

        store.Save(new ThemeSettings(AppTheme.Dark));
        store.Save(new ThemeSettings(AppTheme.Light));

        Assert.Equal(AppTheme.Light, NewStore().Load().Theme);
    }

    // ── 枚举写成字符串 ──────────────────────────────────────────────────────

    [Fact]
    public void Save_WritesEnumAsString_NotNumber()
    {
        NewStore().Save(new ThemeSettings(AppTheme.Dark));

        var text = File.ReadAllText(Path_);

        // 写成数字的话，AppTheme 的成员顺序一旦重排，旧文件会静默读成别的档位。
        Assert.Contains("Dark", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"theme\": 2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownEnumName_IsNotAccepted_ButFileIsKept()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, """{ "theme": "Midnight" }""");

        Assert.Equal(AppTheme.System, NewStore().Load().Theme);

        // 坏文件**不删** —— 留着还能人工看一眼出了什么事。
        Assert.True(File.Exists(Path_));
    }

    [Fact]
    public void OutOfRangeNumericValue_IsNotAccepted()
    {
        Directory.CreateDirectory(_directory);

        // 字符串枚举会挡掉未知名字，但**数字**仍能反序列化成未定义的枚举值，
        // 所以实现里额外有一道 Enum.IsDefined 检查。这条守的就是它。
        File.WriteAllText(Path_, """{ "theme": 99 }""");

        Assert.Equal(AppTheme.System, NewStore().Load().Theme);
    }

    [Fact]
    public void CorruptJson_FallsBackToSystem_ButFileIsKept()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, "{ this is not json");

        Assert.Equal(AppTheme.System, NewStore().Load().Theme);
        Assert.True(File.Exists(Path_));
    }

    [Fact]
    public void EmptyFile_FallsBackToSystem()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, string.Empty);

        Assert.Equal(AppTheme.System, NewStore().Load().Theme);
    }

    [Fact]
    public void NullThemeInJson_FallsBackToSystem()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, "null");

        Assert.Equal(AppTheme.System, NewStore().Load().Theme);
    }

    // ── 写失败不抛 ──────────────────────────────────────────────────────────

    [Fact]
    public void Save_ToUnwritablePath_DoesNotThrow()
    {
        // 目标「文件」的那一层被占成目录，写必然失败。设置项写不进去只该记日志，
        // 不该把异常扔回调用方 —— 本次会话的选择已经生效了。
        Directory.CreateDirectory(Path_);

        var store = NewStore();

        store.Save(new ThemeSettings(AppTheme.Dark));
    }
}
