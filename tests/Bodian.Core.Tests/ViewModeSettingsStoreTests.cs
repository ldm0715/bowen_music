using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 显示方式偏好（行列表 / 封面卡片）的本地存储。**零网络、零真实用户目录** ——
/// 每个测试用自己那份临时文件。
/// </summary>
/// <remarks>
/// 与 <c>ThemeSettingsStoreTests</c> 同一套形态：重点是坏文件不能让页面起不来，
/// 以及写不进去时不能把异常扔回调用方（开关本身在本次会话里已经生效了）。
/// </remarks>
public sealed class ViewModeSettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bodian-view-mode-tests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "view-mode.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonViewModeSettingsStore NewStore() => new(SettingsPath);

    // ── 基本读写 ────────────────────────────────────────────────────────────

    [Fact]
    public void NoFile_FallsBackToList()
    {
        // 默认是**行列表**：卡片是后加的形态，没有记录时停在原来那一套上。
        Assert.False(ViewModeSettings.Default.UseGrid);
        Assert.False(NewStore().Load().UseGrid);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Save_ThenLoad_RoundTrips(bool useGrid)
    {
        NewStore().Save(new ViewModeSettings { UseGrid = useGrid });

        Assert.Equal(useGrid, NewStore().Load().UseGrid);
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var store = NewStore();

        // _directory 此时还不存在 —— 构造函数不该建目录，只有写的时候才建。
        Assert.False(Directory.Exists(_directory));

        store.Save(new ViewModeSettings { UseGrid = true });

        Assert.True(File.Exists(SettingsPath));
        Assert.True(store.Load().UseGrid);
    }

    [Fact]
    public void Save_Twice_LastWriteWins()
    {
        var store = NewStore();

        store.Save(new ViewModeSettings { UseGrid = true });
        store.Save(new ViewModeSettings { UseGrid = false });

        Assert.False(NewStore().Load().UseGrid);
    }

    // ── 坏文件 ──────────────────────────────────────────────────────────────

    [Fact]
    public void CorruptJson_FallsBackToList_ButFileIsKept()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ this is not json");

        Assert.False(NewStore().Load().UseGrid);

        // 坏文件**不删** —— 留着还能人工看一眼出了什么事。
        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public void EmptyFile_FallsBackToList()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, string.Empty);

        Assert.False(NewStore().Load().UseGrid);
    }

    [Fact]
    public void NullInJson_FallsBackToList()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "null");

        Assert.False(NewStore().Load().UseGrid);
    }

    [Fact]
    public void Save_WritesPascalCaseFieldName()
    {
        NewStore().Save(new ViewModeSettings { UseGrid = true });

        var text = File.ReadAllText(SettingsPath);

        // 文件是我们自己写、自己读的，形状要在这里钉住：以后动了 JsonSourceGenerationOptions
        // 的命名策略，旧文件会被静默读回默认值（行列表），而那是看不出来的。
        Assert.Contains("\"UseGrid\": true", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownField_IsIgnored_AndKnownFieldStillReads()
    {
        Directory.CreateDirectory(_directory);

        // 单独包一层对象而不是裸写一个 bool，是为了以后加字段不用换格式：
        // 多出来的键必须被忽略，不能把整份读坏。
        File.WriteAllText(SettingsPath, """{ "UseGrid": true, "FutureField": 42 }""");

        Assert.True(NewStore().Load().UseGrid);
    }

    // ── 写失败不抛 ──────────────────────────────────────────────────────────

    [Fact]
    public void Save_ToUnwritablePath_DoesNotThrow()
    {
        // 目标「文件」的那一层被占成目录，写必然失败。设置项写不进去只该记日志，
        // 不该把异常扔回调用方 —— 本次会话的选择已经生效了。
        Directory.CreateDirectory(SettingsPath);

        NewStore().Save(new ViewModeSettings { UseGrid = true });
    }
}
