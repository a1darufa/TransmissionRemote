namespace TransmissionKit.Tests;

/// <summary>Known folders tree.</summary>
[TestClass]
public sealed class RemoteFolderTreeTests
{
    [TestMethod]
    public void CollapsesTheSharedTopChainIntoOneNode()
    {
        var tree = RemoteFolderTree.Build(new Dictionary<string, int>
        {
            ["/mnt/HD/HD_a2/Movies"] = 3,
            ["/mnt/HD/HD_a2/TV/Shows"] = 2,
            ["/mnt/HD/HD_a2/TV"] = 0,
        });

        var root = Assert.ContainsSingle(tree);
        Assert.AreEqual("/mnt/HD/HD_a2", root.Path);
        Assert.AreEqual("/mnt/HD/HD_a2", root.Name, "a top-level node shows its full path");

        Assert.IsNotNull(root.Children);
        SequenceAssert.AreEqual(root.Children.Select(child => child.Name), "Movies", "TV");
        SequenceAssert.AreEqual(root.Children.Select(child => child.TorrentCount), 3, 0);
        Assert.IsNull(root.Children[0].Children, "a folder with no known subfolders has no expander");
        var tv = root.Children[1];
        Assert.IsNotNull(tv.Children);
        SequenceAssert.AreEqual(tv.Children.Select(child => child.Path), "/mnt/HD/HD_a2/TV/Shows");
    }

    [TestMethod]
    public void StopsCollapsingAtAFolderThatHoldsTorrents()
    {
        var tree = RemoteFolderTree.Build(new Dictionary<string, int> { ["/data"] = 1, ["/data/done/music"] = 4 });

        var root = tree[0];
        Assert.AreEqual("/data", root.Path);
        Assert.AreEqual(1, root.TorrentCount);
        Assert.IsNotNull(root.Children);
        Assert.AreEqual("done", root.Children[0].Name);
    }

    [TestMethod]
    public void KeepsSeparateRootsApart()
    {
        var tree = RemoteFolderTree.Build(new Dictionary<string, int> { ["/srv/a"] = 1, ["/home/me/b"] = 1 });

        SequenceAssert.AreEqual(tree.Select(folder => folder.Path), "/home/me/b", "/srv/a");
    }

    [TestMethod]
    public void MergesTrailingSlashesAndIgnoresEmptyPaths()
    {
        var tree = RemoteFolderTree.Build(new Dictionary<string, int>
        {
            ["/downloads/"] = 2,
            ["/downloads"] = 1,
            [""] = 5,
            ["/"] = 1,
        });

        var root = Assert.ContainsSingle(tree);
        Assert.AreEqual("/downloads", root.Path);
        Assert.AreEqual(3, root.TorrentCount);
    }

    [TestMethod]
    public void HandlesWindowsPaths()
    {
        var tree = RemoteFolderTree.Build(new Dictionary<string, int>
        {
            [@"C:\Torrents\Done"] = 1,
            [@"C:\Torrents\Temp"] = 1,
        });

        var root = tree[0];
        Assert.AreEqual(@"C:\Torrents", root.Path);
        Assert.IsNotNull(root.Children);
        SequenceAssert.AreEqual(root.Children.Select(child => child.Path), @"C:\Torrents\Done", @"C:\Torrents\Temp");
    }

    [TestMethod]
    public void GivesADriveItsRootPath()
    {
        var tree = RemoteFolderTree.Build(new Dictionary<string, int>
        {
            [@"D:\Movies"] = 1,
            [@"D:\Music"] = 2,
            [@"D:\"] = 1,
        });

        SequenceAssert.AreEqual(tree.Select(folder => folder.Path), @"D:\");
    }

    [TestMethod]
    public void SortsNamesLikeAFileBrowser()
    {
        var tree = RemoteFolderTree.Build(new Dictionary<string, int>
        {
            ["/tv/Season 10"] = 1,
            ["/tv/Season 2"] = 1,
            ["/tv/extras"] = 1,
        });

        var tv = tree[0];
        Assert.IsNotNull(tv.Children);
        SequenceAssert.AreEqual(tv.Children.Select(child => child.Name), "extras", "Season 2", "Season 10");
    }

    [TestMethod]
    public void ComparesTreesByTheirFolders()
    {
        var counts = new Dictionary<string, int> { ["/data/a"] = 1, ["/data/b"] = 2 };

        Assert.AreEqual(RemoteFolderTree.Build(counts)[0], RemoteFolderTree.Build(counts)[0]);
    }
}
