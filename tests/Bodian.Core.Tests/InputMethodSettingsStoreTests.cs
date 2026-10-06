using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class InputMethodSettingsStoreTests
{
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "bodian-ime-settings-tests",
        Guid.NewGuid().ToString("N"), "input-method.json");

    [Fact]
    public void MissingFile_IsDisabled_WithoutCreatingAFile()
    {
        var path = NewPath();
        Assert.False(new JsonInputMethodSettingsStore(path).Load().CompatibilityEnabled);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void EnabledAndDisabled_RoundTrip()
    {
        var path = NewPath();
        var store = new JsonInputMethodSettingsStore(path);
        Assert.True(store.Save(new InputMethodSettings(true)));
        Assert.True(store.Load().CompatibilityEnabled);
        Assert.True(store.Save(InputMethodSettings.Default));
        Assert.False(store.Load().CompatibilityEnabled);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"CompatibilityEnabled\":\"yes\"}")]
    public void InvalidOrMissingPreference_IsDisabled_AndPreservesFile(string json)
    {
        var path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        Assert.False(new JsonInputMethodSettingsStore(path).Load().CompatibilityEnabled);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void SaveFailure_IsReported()
    {
        var path = NewPath();
        Directory.CreateDirectory(path);
        Assert.False(new JsonInputMethodSettingsStore(path).Save(new InputMethodSettings(true)));
    }
}
