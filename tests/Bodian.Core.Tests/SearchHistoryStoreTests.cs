using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class SearchHistoryStoreTests
{
    // 每个测试使用隔离的临时目录，不访问真实用户历史。
    private readonly string _path = Path.Combine(Path.GetTempPath(), "bodian-search-history-tests", Guid.NewGuid().ToString("N"), "search-history.json");

    [Fact]
    public void MissingFile_ReturnsEmptyHistory()
    {
        Assert.Empty(new JsonSearchHistoryStore(_path).Load());
    }

    [Fact]
    public async Task SaveThenReload_TrimsAndDeduplicatesPreservingRecentOrder()
    {
        var store = new JsonSearchHistoryStore(_path);
        await store.SaveAsync([" 晴天 ", "Jay", "jay", "", " ", "周杰伦"]);
        Assert.Equal(new[] { "晴天", "Jay", "周杰伦" }, new JsonSearchHistoryStore(_path).Load());
    }

    [Fact]
    public async Task History_KeepsOnlyTheMostRecentTwentyWords()
    {
        await new JsonSearchHistoryStore(_path).SaveAsync(Enumerable.Range(1, 30).Select(i => $"word-{i}").ToArray());
        var words = new JsonSearchHistoryStore(_path).Load();
        Assert.Equal(20, words.Count);
        Assert.Equal("word-1", words[0]);
        Assert.Equal("word-20", words[^1]);
    }

    [Fact]
    public async Task Clear_AfterPendingWrite_PersistsEmptyHistory()
    {
        var store = new JsonSearchHistoryStore(_path);
        var save = store.SaveAsync(["晴天"]);
        var clear = store.SaveAsync([]);
        await Task.WhenAll(save, clear);
        Assert.Empty(new JsonSearchHistoryStore(_path).Load());
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public async Task CorruptFile_ReturnsEmptyHistoryAndKeepsOriginalFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, "not-json", TestContext.Current.CancellationToken);
        Assert.Empty(new JsonSearchHistoryStore(_path).Load());
        Assert.Equal("not-json", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }
}
