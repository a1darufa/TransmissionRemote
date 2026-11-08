namespace TransmissionKit.Tests;

/// <summary>Remote to local path mappings.</summary>
[TestClass]
public sealed class PathMappingTests
{
    private static readonly PathMapping Share = new("/mnt/HD/HD_a2", "/Volumes/HD_a2");

    [TestMethod]
    public void MappedFolderItselfBecomesTheLocalFolder()
    {
        Assert.AreEqual("/Volumes/HD_a2", Share.LocalPathFor("/mnt/HD/HD_a2"));
    }

    [TestMethod]
    public void PathsInsideKeepTheirSubfolders()
    {
        Assert.AreEqual("/Volumes/HD_a2/Movies/Film.mkv", Share.LocalPathFor("/mnt/HD/HD_a2/Movies/Film.mkv"));
    }

    [TestMethod]
    public void OnlyWholeFolderNamesMatch()
    {
        Assert.IsNull(Share.LocalPathFor("/mnt/HD/HD_a20/Movies"));
        Assert.IsNull(Share.LocalPathFor("/mnt/HD"));
    }

    [TestMethod]
    public void TrailingSlashesAndSpacesDontMatter()
    {
        var typed = new PathMapping(" /mnt/HD/HD_a2/ ", "/Volumes/HD_a2/");

        Assert.AreEqual("/Volumes/HD_a2/Movies", typed.LocalPathFor("/mnt/HD/HD_a2/Movies/"));
    }

    [TestMethod]
    public void RootMappingCoversEverything()
    {
        var root = new PathMapping("/", "/Volumes/NAS");

        Assert.AreEqual("/Volumes/NAS/downloads/file.iso", root.LocalPathFor("/downloads/file.iso"));
    }

    [TestMethod]
    public void UnfinishedMappingsAreIgnored()
    {
        Assert.IsNull(new PathMapping("/mnt", "").LocalPathFor("/mnt/a"));
        Assert.IsNull(new PathMapping("", "/Volumes/x").LocalPathFor("/mnt/a"));
    }

    [TestMethod]
    public void DeepestMappingWins()
    {
        var movies = new PathMapping("/mnt/HD/HD_a2/Movies", "/Volumes/Movies");
        PathMapping[] mappings = [Share, movies];

        Assert.AreEqual("/Volumes/Movies/Film.mkv", mappings.LocalPathFor("/mnt/HD/HD_a2/Movies/Film.mkv"));
        Assert.AreEqual("/Volumes/HD_a2/Music/Song.flac", mappings.LocalPathFor("/mnt/HD/HD_a2/Music/Song.flac"));
        Assert.IsNull(mappings.LocalPathFor("/srv/other"));
    }

    [TestMethod]
    public void WindowsDaemonPathsMapInsideTheirFolder()
    {
        var drive = new PathMapping(@"D:\Torrents", "/Volumes/Torrents");

        Assert.AreEqual("/Volumes/Torrents/TV/Show", drive.LocalPathFor(@"D:\Torrents\TV\Show"));
        // Joining a torrent's name onto its folder adds a forward slash.
        Assert.AreEqual("/Volumes/Torrents/Film.mkv", drive.LocalPathFor(@"D:\Torrents/Film.mkv"));
        // Windows ignores case.
        Assert.AreEqual("/Volumes/Torrents/tv", drive.LocalPathFor(@"d:\torrents\tv"));
        Assert.IsNull(drive.LocalPathFor(@"D:\TorrentsOld\x"));
    }

    [TestMethod]
    public void AWholeWindowsDriveCanBeMapped()
    {
        var drive = new PathMapping(@"E:\", "/Volumes/E");

        Assert.AreEqual("/Volumes/E/Downloads/a.iso", drive.LocalPathFor(@"E:\Downloads\a.iso"));
    }
}
