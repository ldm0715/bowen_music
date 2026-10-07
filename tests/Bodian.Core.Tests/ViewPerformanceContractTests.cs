using System.Xml.Linq;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>防止页面重设计重新引入让全部离屏条目实现的无界嵌套滚动。</summary>
public sealed class ViewPerformanceContractTests
{
    private static string UiRoot => Path.GetFullPath(Path.Combine(Fixtures.Root, "..", "src", "Bodian.WinUI"));

    private static bool IsScrollingList(XElement element)
        => element.Name.LocalName is "ListView" or "VirtualizedListView" or "GridView" or "TrackListView" or "AlbumListView";

    [Fact]
    public void SongRows_DoNotConstructHiddenActionMenus()
    {
        var document = XDocument.Load(Path.Combine(UiRoot, "Controls", "TrackMoreButton.xaml"));
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "TrackActionsMenu");
    }

    [Theory]
    [InlineData("BangListPage.xaml")]
    [InlineData("SearchPage.xaml")]
    [InlineData("DiscoverPage.xaml")]
    public void DenseFeeds_DoNotPutTheirListsInsideAnOuterScrollViewer(string file)
    {
        var document = XDocument.Load(Path.Combine(UiRoot, "Views", file));
        foreach (var list in document.Descendants().Where(IsScrollingList))
            Assert.DoesNotContain(list.Ancestors(), ancestor => ancestor.Name.LocalName == "ScrollViewer");
    }

    [Fact]
    public void DenseFeeds_DoNotRenderAnEntireGroupThroughItemsControl()
    {
        foreach (var file in new[] { "BangListPage.xaml", "SearchPage.xaml", "DiscoverPage.xaml" })
        {
            var document = XDocument.Load(Path.Combine(UiRoot, "Views", file));
            Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "ItemsControl");
        }
    }

    [Fact]
    public void VerticalLists_HaveAFiniteLayoutBeforeReachingAStackPanel()
    {
        foreach (var directory in new[] { "Views", "Controls" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(UiRoot, directory), "*.xaml"))
        {
            var document = XDocument.Load(file);
            foreach (var list in document.Descendants().Where(IsScrollingList))
            {
                if (list.Descendants().Any(element => element.Name.LocalName == "ItemsStackPanel"
                    && (string?)element.Attribute("Orientation") == "Horizontal")) continue;
                if (list.Attribute("Height") is not null || list.Attribute("MaxHeight") is not null) continue;
                foreach (var ancestor in list.Ancestors())
                {
                    if (ancestor.Attribute("Height") is not null || ancestor.Attribute("MaxHeight") is not null) break;
                    Assert.False(ancestor.Name.LocalName == "StackPanel"
                        && (string?)ancestor.Attribute("Orientation") != "Horizontal",
                        $"{Path.GetFileName(file)} puts a vertical list inside an unbounded StackPanel.");
                }
            }
        }
    }
}
