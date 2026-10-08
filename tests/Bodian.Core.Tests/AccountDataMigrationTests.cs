using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Services.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 旧布局（三份数据平铺在根目录）搬进账号作用域。
/// </summary>
/// <remarks>
/// 根目录指向临时目录 —— 否则这些用例会搬走本机真实数据。
/// </remarks>
public sealed class AccountDataMigrationTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "bodian-migration-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteLegacy(string fileName, string content)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, fileName), content);
    }

    private static InMemoryCredentialStore SignedIn(string uid) =>
        new(new BodianCredential(uid, "token", "昵称"));

    private void Run(ICredentialStore credentials) =>
        AccountDataMigration.Run(credentials, NullLogger.Instance, _root);

    private string Legacy(string fileName) => Path.Combine(_root, fileName);

    private string Scoped(string scope, string fileName) =>
        AppPaths.AccountFilePath(scope, fileName, _root);

    [Fact]
    public void WithASession_TheDataLandsInTheAccountsBucket()
    {
        WriteLegacy(AppPaths.PlayQueueFileName, "queue");
        WriteLegacy(AppPaths.PlayHistoryFileName, "history");
        WriteLegacy(AppPaths.SearchHistoryFileName, "search");

        Run(SignedIn("111"));

        Assert.False(File.Exists(Legacy(AppPaths.PlayHistoryFileName)));
        Assert.Equal("history", File.ReadAllText(Scoped("111", AppPaths.PlayHistoryFileName)));
        Assert.Equal("queue", File.ReadAllText(Scoped("111", AppPaths.PlayQueueFileName)));
        Assert.Equal("search", File.ReadAllText(Scoped("111", AppPaths.SearchHistoryFileName)));
    }

    [Fact]
    public void WithoutASession_TheDataLandsInTheAnonymousBucket()
    {
        WriteLegacy(AppPaths.PlayHistoryFileName, "history");

        Run(new InMemoryCredentialStore());

        Assert.Equal("history", File.ReadAllText(Scoped(AppPaths.AnonymousScope, AppPaths.PlayHistoryFileName)));
    }

    [Fact]
    public void ExistingTargetIsNotOverwritten_AndTheLegacyFileIsKept()
    {
        WriteLegacy(AppPaths.PlayHistoryFileName, "old");
        var target = Scoped("111", AppPaths.PlayHistoryFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "new");

        Run(SignedIn("111"));

        // 宁可有重复文件，也不删用户数据、不覆盖已有数据。
        Assert.Equal("new", File.ReadAllText(target));
        Assert.Equal("old", File.ReadAllText(Legacy(AppPaths.PlayHistoryFileName)));
    }

    [Fact]
    public void SecondRunIsANoOp()
    {
        WriteLegacy(AppPaths.PlayHistoryFileName, "history");

        Run(SignedIn("111"));
        Run(SignedIn("111"));

        Assert.False(File.Exists(Legacy(AppPaths.PlayHistoryFileName)));
        Assert.Equal("history", File.ReadAllText(Scoped("111", AppPaths.PlayHistoryFileName)));
    }

    [Fact]
    public void OnlyTheThreeAccountLevelFilesAreTouched()
    {
        // 个人级与设备级的文件不许被搬走：换账号不该换主题、快捷键、设备标识。
        WriteLegacy("settings.json", "theme");
        WriteLegacy("devid.txt", "devid");
        WriteLegacy(AppPaths.PlayHistoryFileName, "history");

        Run(SignedIn("111"));

        Assert.True(File.Exists(Legacy("settings.json")));
        Assert.True(File.Exists(Legacy("devid.txt")));
        Assert.False(File.Exists(Legacy(AppPaths.PlayHistoryFileName)));
    }

    [Fact]
    public void NothingToMigrate_IsNotAnError()
    {
        Directory.CreateDirectory(_root);

        Run(SignedIn("111"));
    }
}
