namespace StringCatalogConverter.Tests;

/// <summary>Resource names derived from the Mac app's English text.</summary>
[TestClass]
public sealed class ResourceNamerTests
{
    [TestMethod]
    [DataRow("Show Log Files", "ShowLogFiles")]
    [DataRow("Active: %lld of %lld", "ActiveOf")]
    [DataRow("Connected to Transmission %1$@ using %2$@.", "ConnectedToTransmissionUsing")]
    [DataRow("Add a .torrent file", "AddATorrentFile")]
    [DataRow("RPC version", "RPCVersion")]
    [DataRow("kB/s", "KBS")]
    public void WordsBecomePascalCaseWithoutPlaceholdersOrPunctuation(string english, string expected) =>
        Assert.AreEqual(expected, ResourceNamer.BaseName(english));

    [TestMethod]
    [DataRow("“%@” couldn't be opened as a torrent file.", "CouldNotBeOpenedAsATorrentFile")]
    [DataRow("Can't Reach the Server", "CannotReachTheServer")]
    [DataRow("Don't Download Selected", "DoNotDownloadSelected")]
    [DataRow("Won't start", "WillNotStart")]
    [DataRow("isn’t a magnet link", "IsNotAMagnetLink")]
    [DataRow("You're Up to Date", "YouAreUpToDate")]
    [DataRow("if they'd let us", "IfTheyWouldLetUs")]
    [DataRow("I'm here, I've seen it, I'll go", "IAmHereIHaveSeenItIWillGo")]
    [DataRow("Follow the server's global limits", "FollowTheServersGlobalLimits")]
    public void ContractionsAreSpelledOut(string english, string expected) =>
        Assert.AreEqual(expected, ResourceNamer.BaseName(english));

    [TestMethod]
    [DataRow("Settings…", "SettingsEllipsis")]
    [DataRow("Settings...", "SettingsEllipsis")]
    [DataRow("Connecting to %@…", "ConnectingToEllipsis")]
    [DataRow("Review and trust it (%@…).", "ReviewAndTrustIt")]
    public void OnlyATrailingEllipsisIsKeptAsAWord(string english, string expected) =>
        Assert.AreEqual(expected, ResourceNamer.BaseName(english));

    [TestMethod]
    [DataRow("↓ %@ kB/s", "DownKBS")]
    [DataRow("↑ unlimited", "UpUnlimited")]
    [DataRow("Enable µTP", "EnableUTP")]
    [DataRow("Café résumé", "CafeResume")]
    public void SymbolsAndAccentsBecomeAscii(string english, string expected) =>
        Assert.AreEqual(expected, ResourceNamer.BaseName(english));

    [TestMethod]
    public void MarkdownKeepsOnlyItsText()
    {
        Assert.AreEqual("RemoveTorrent", ResourceNamer.BaseName("Remove ^[%lld torrent](inflect: true)?"));
        Assert.AreEqual("DataIsIPToCountryLite",
            ResourceNamer.BaseName("Data is [IP to Country Lite](https://db-ip.com)."));
    }

    [TestMethod]
    public void LongTextsKeepTheirFirstWholeWords()
    {
        Assert.AreEqual("ThisServerUsesPlainHTTPOverTheInternetSo", ResourceNamer.BaseName(
            "This server uses plain HTTP over the internet, so your password would be sent unencrypted."));
        Assert.AreEqual("ImportFromTransmissionRemoteGUIEllipsis",
            ResourceNamer.BaseName("Import from Transmission Remote GUI…"));
        Assert.AreEqual(ResourceNamer.MaxLength, ResourceNamer.BaseName(new string('a', 100)).Length);
    }

    [TestMethod]
    public void NamesAreAlwaysIdentifiers()
    {
        Assert.AreEqual("Text", ResourceNamer.BaseName("%@ %@"));
        Assert.AreEqual("Text", ResourceNamer.BaseName(" "));
        Assert.AreEqual("Text19216811OrNasLocal", ResourceNamer.BaseName("192.168.1.1 or nas.local"));
    }

    [TestMethod]
    public void ClashingNamesAllGetAHashOfTheirKey()
    {
        var names = ResourceNamer.AssignNames([("Total Size", false), ("Total size", false), ("Size", false)]);

        Assert.AreEqual("TotalSize_9AC7DE", names["Total Size"]);
        Assert.AreEqual($"TotalSize_{ResourceNamer.HashSuffix("Total size")}", names["Total size"]);
        Assert.AreNotEqual(names["Total Size"], names["Total size"]);
        Assert.AreEqual("Size", names["Size"]);
    }

    [TestMethod]
    public void NamesThatDifferOnlyInCaseClash()
    {
        // MRT looks names up case-insensitively.
        var names = ResourceNamer.AssignNames([("Log In", false), ("Login", false)]);

        Assert.AreEqual($"LogIn_{ResourceNamer.HashSuffix("Log In")}", names["Log In"]);
        Assert.AreEqual($"Login_{ResourceNamer.HashSuffix("Login")}", names["Login"]);
    }

    [TestMethod]
    public void PluralKeysDontClashWithOtherKeys()
    {
        // "%lld files" becomes Files_One, Files_Other, ..., which can't be confused with Files.
        var names = ResourceNamer.AssignNames([("Files", false), ("%lld files", true)]);

        Assert.AreEqual("Files", names["Files"]);
        Assert.AreEqual("Files", names["%lld files"]);
    }

    [TestMethod]
    public void NamesDontDependOnOrder()
    {
        (string, bool)[] keys =
            [("Error", false), ("Error: %@", false), ("Files", false), ("%lld files", true), ("Verifying", false)];

        var forward = ResourceNamer.AssignNames(keys);
        var backward = ResourceNamer.AssignNames(keys.Reverse());

        CollectionAssert.AreEquivalent(forward.ToList(), backward.ToList());
    }

    [TestMethod]
    public void HashSuffixesAreSixHexDigitsOfTheKeysSha256()
    {
        Assert.AreEqual("9AC7DE", ResourceNamer.HashSuffix("Total Size"));
        Assert.AreEqual("51CDF6", ResourceNamer.HashSuffix("Total size"));
    }
}
