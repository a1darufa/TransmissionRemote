using System.Globalization;
using System.Text;

namespace TransmissionKit.Tests;

/// <summary>Looking up peer countries.</summary>
[TestClass]
public sealed class CountryDatabaseTests
{
    /// <summary>A few lines in DB-IP's format, including a gap (1.0.4.0 – 1.0.7.255) and IPv6.</summary>
    private const string Csv = """
        0.0.0.0,0.255.255.255,ZZ
        1.0.0.0,1.0.0.255,AU
        1.0.1.0,1.0.3.255,CN
        1.0.8.0,1.0.15.255,DE
        2001:200::,2001:200:ffff:ffff:ffff:ffff:ffff:ffff,JP
        2a00:1450::,2a00:1450:ffff:ffff:ffff:ffff:ffff:ffff,IE
        """;

    /// <summary>The first lines of Csv, gzipped with a file name in the header.</summary>
    private const string GzippedSample =
        "H4sICAAAAAAC/2RiaXAuY3N2ADPQMwBBHQM9I1NTGNaJiuIyhEpAaJCgYyhY0BAqaAwWdPbjMjIwMLQCElZWOnBm"
        + "GhDgJHS8ArgAO5oVIngAAAA=";

    private static readonly CountryDatabase Database = new(Encoding.UTF8.GetBytes(Csv));

    [TestMethod]
    [DataRow("1.0.0.1", "AU")]
    [DataRow("1.0.2.200", "CN")]
    [DataRow("1.0.15.255", "DE")]
    [DataRow("2001:200::1", "JP")]
    [DataRow("2a00:1450:4001:80b::200e", "IE")]
    public void FindsTheCountry(string address, string country)
    {
        Assert.AreEqual(country, Database.CountryCodeFor(address));
    }

    [TestMethod]
    [DataRow("0.0.0.1")]
    [DataRow("1.0.5.1")]
    [DataRow("1.0.16.0")]
    [DataRow("255.255.255.255")]
    [DataRow("2001:100::1")]
    [DataRow("not an address")]
    [DataRow("")]
    public void ReturnsNullForUnknownAddresses(string address)
    {
        Assert.IsNull(Database.CountryCodeFor(address));
    }

    [TestMethod]
    [DataRow("1.0.1", DisplayName = "three parts")]
    [DataRow("01.0.0.1", DisplayName = "leading zero")]
    [DataRow("[2001:200::1]", DisplayName = "brackets")]
    [DataRow("2001:200::1%4", DisplayName = "zone ID")]
    public void ParsesAddressesAsStrictlyAsTheMacApp(string address)
    {
        Assert.IsNull(Database.CountryCodeFor(address));
    }

    [TestMethod]
    public void SkipsMalformedLines()
    {
        var database = new CountryDatabase("garbage\n1.0.0.0,1.0.0.255,AU\r\n1.0.1.0,1.0.1.255,toolong\n,,\n"u8);

        Assert.AreEqual("AU", database.CountryCodeFor("1.0.0.9"));
        Assert.IsNull(database.CountryCodeFor("1.0.1.9"));
    }

    [TestMethod]
    public void HandlesARangeEndingAtTheLastAddress()
    {
        var database = new CountryDatabase("0.0.0.0,255.255.255.255,US\n"u8);

        Assert.AreEqual("US", database.CountryCodeFor("255.255.255.255"));
    }

    [TestMethod]
    public void DrawsFlagsFromCountryCodes()
    {
        Assert.AreEqual("🇩🇪", CountryDisplay.Flag("DE"));
        Assert.AreEqual("🇯🇵", CountryDisplay.Flag("jp"));
    }

    [TestMethod]
    public void NamesCountriesAndFallsBackToTheCode()
    {
        // The language follows the operating system, so the name itself depends on the machine.
        Assert.AreEqual(new RegionInfo("DE").DisplayName, CountryDisplay.Name("DE"));
        Assert.AreNotEqual("DE", CountryDisplay.Name("DE"));
        Assert.AreEqual("ZZ", CountryDisplay.Name("ZZ"));
    }

    [TestMethod]
    public void UnpacksGzipWithAFileNameHeader()
    {
        var csv = Gzip.Decompress(Convert.FromBase64String(GzippedSample));
        var database = new CountryDatabase(csv);

        Assert.AreEqual("AU", database.CountryCodeFor("1.0.0.7"));
        Assert.AreEqual("JP", database.CountryCodeFor("2001:200::5"));
    }

    [TestMethod]
    public void RejectsHeadersThatPointPastTheEnd()
    {
        // FEXTRA claims 65,535 bytes of extras in a short file, followed by the FNAME flag.
        byte[] header =
            [0x1F, 0x8B, 8, 0x04 | 0x08, 0, 0, 0, 0, 0, 3, 0xFF, 0xFF, .. Enumerable.Repeat((byte)0x41, 12)];

        var error = Assert.ThrowsExactly<GzipException>(() => Gzip.Decompress(header));

        Assert.AreEqual(GzipError.Truncated, error.Error);
    }

    [TestMethod]
    public void RejectsAFileCutShort()
    {
        var sample = Convert.FromBase64String(GzippedSample);

        var error = Assert.ThrowsExactly<GzipException>(() => Gzip.Decompress(sample[..^8]));

        Assert.AreEqual(GzipError.Truncated, error.Error);
    }

    [TestMethod]
    public void RejectsDamagedData()
    {
        var sample = Convert.FromBase64String(GzippedSample);
        sample[^6] ^= 0xFF; // inside the CRC-32

        var error = Assert.ThrowsExactly<GzipException>(() => Gzip.Decompress(sample));

        Assert.AreEqual(GzipError.Corrupt, error.Error);
    }

    [TestMethod]
    public void RejectsDataThatIsNotGzip()
    {
        var error = Assert.ThrowsExactly<GzipException>(() => Gzip.Decompress(new byte[40]));

        Assert.AreEqual(GzipError.NotGzip, error.Error);
    }
}
