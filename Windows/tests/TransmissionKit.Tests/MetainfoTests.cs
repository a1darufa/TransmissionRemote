using System.Text;

namespace TransmissionKit.Tests;

/// <summary>Bencode and .torrent parsing.</summary>
[TestClass]
public sealed class MetainfoTests
{
    private const string InfoDictionary =
        "d5:filesl"
        + "d6:lengthi1000e4:pathl6:Season7:e01.mkveed4:attr1:p6:lengthi24e4:pathl4:.pad2:24ee"
        + "d6:lengthi20e4:pathl8:info.nfoee"
        + "e4:name4:Show12:piece lengthi16384e6:pieces0:7:privatei1ee";

    private static readonly byte[] MultiFileTorrent =
        Bytes("d8:announce23:http://tracker/announce7:comment5:hello4:info" + InfoDictionary + "e");

    [TestMethod]
    public void ParsesMultiFileTorrentAndKeepsPaddingFilesNumbered()
    {
        var metainfo = TorrentMetainfo.Parse(MultiFileTorrent);

        Assert.AreEqual("Show", metainfo.Name);
        SequenceAssert.AreEqual(
            metainfo.Files.Select(file => file.Path), "Show/Season/e01.mkv", "Show/.pad/24", "Show/info.nfo");
        // Transmission 3.00–4.1 ignore BEP 47's attr flag and number padding files like any other.
        SequenceAssert.AreEqual(metainfo.Files.Select(file => file.Id), 0, 1, 2);
        SequenceAssert.AreEqual(metainfo.Files.Select(file => file.IsPadding), false, true, false);
        Assert.AreEqual(1020, metainfo.TotalSize);
        Assert.AreEqual("hello", metainfo.Comment);
        Assert.IsTrue(metainfo.IsPrivate);
    }

    [TestMethod]
    public void InfoHashCoversExactInfoBytes()
    {
        var metainfo = TorrentMetainfo.Parse(MultiFileTorrent);

        // shasum -a 1 over InfoDictionary's bytes.
        Assert.AreEqual("6db4036891c208f7b94c84e97990df6c2834a79e", metainfo.InfoHash);
    }

    [TestMethod]
    public void RecognizesPaddingFilesByNameWhenTheDaemonListsThem()
    {
        Assert.IsTrue(new FileEntry(0, "Show/.pad/16384", 16_384).IsPadding);
        Assert.IsTrue(new FileEntry(1, "Show/_____padding_file_0_if you see this", 9).IsPadding);
        Assert.IsFalse(new FileEntry(2, "Show/.pad", 9).IsPadding);
        Assert.IsFalse(new FileEntry(3, "Show/notes.pad", 9).IsPadding);
    }

    [TestMethod]
    public void PrefersTheFileListOverASingleLength()
    {
        var metainfo = TorrentMetainfo.Parse(Bytes("d4:infod5:filesld6:lengthi5e4:pathl1:aeee6:lengthi9e4:name1:Xee"));

        SequenceAssert.AreEqual(metainfo.Files.Select(file => file.Path), "X/a");
    }

    [TestMethod]
    public void ParsesSingleFileTorrent()
    {
        var metainfo = TorrentMetainfo.Parse(Bytes("d4:infod6:lengthi42e4:name8:file.isoee"));

        SequenceAssert.AreEqual(metainfo.Files, new FileEntry(0, "file.iso", 42));
    }

    [TestMethod]
    [DataRow("d4:infod", DisplayName = "truncated")]
    [DataRow("d4:info99999:x", DisplayName = "length beyond buffer")]
    [DataRow("i12", DisplayName = "unterminated integer")]
    [DataRow("d4:infod4:name3:abcee trailing", DisplayName = "trailing garbage")]
    [DataRow("x", DisplayName = "unknown marker")]
    [DataRow("i-e", DisplayName = "sign without digits")]
    public void RejectsMalformedInput(string input)
    {
        var error = Assert.ThrowsExactly<TorrentParseException>(() => TorrentMetainfo.Parse(Bytes(input)));

        Assert.AreEqual(TorrentParseError.Malformed, error.Error);
        Assert.IsInstanceOfType<BencodeException>(error.InnerException);
    }

    [TestMethod]
    public void RejectsExcessiveNesting()
    {
        var nested = new string('l', 200) + new string('e', 200);

        var error = Assert.ThrowsExactly<BencodeException>(() => BencodeParser.Parse(Bytes(nested)));

        Assert.AreEqual(BencodeError.NestingTooDeep, error.Error);
    }

    [TestMethod]
    public void RequiresInfoDictionary()
    {
        var error = Assert.ThrowsExactly<TorrentParseException>(
            () => TorrentMetainfo.Parse(Bytes("d8:announce3:urle")));

        Assert.AreEqual(TorrentParseError.MissingInfoDictionary, error.Error);
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
}

/// <summary>Magnet links.</summary>
[TestClass]
public sealed class MagnetLinkTests
{
    [TestMethod]
    public void ParsesMagnet()
    {
        const string text =
            "magnet:?xt=urn:btih:C12FE1C06BBA254A9DC9F519B335AA7C1367A88A&dn=Some%20Show&tr=udp%3A%2F%2Ftracker%3A80";

        Assert.IsTrue(MagnetLink.TryParse(text, out var link));
        Assert.AreEqual("c12fe1c06bba254a9dc9f519b335aa7c1367a88a", link.InfoHash);
        Assert.AreEqual("Some Show", link.DisplayName);
        SequenceAssert.AreEqual(link.Trackers, "udp://tracker:80");
    }

    [TestMethod]
    [DataRow("https://example.com")]
    [DataRow("magnet:?dn=nohash")]
    [DataRow("not a link")]
    [DataRow("")]
    public void RejectsNonMagnets(string text)
    {
        Assert.IsFalse(MagnetLink.TryParse(text, out _));
    }

    [TestMethod]
    public void RecognizesDownloadUrls()
    {
        Assert.IsTrue("https://example.com/file.torrent".IsTorrentDownloadUrl);
        Assert.IsFalse("ftp://example.com/file.torrent".IsTorrentDownloadUrl);
        Assert.IsFalse("magnet:?xt=urn:btih:abc".IsTorrentDownloadUrl);
    }
}
