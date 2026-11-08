namespace StringCatalogConverter.Tests;

/// <summary>Apple format specifiers rewritten as .NET composite format placeholders.</summary>
[TestClass]
public sealed class AppleFormatTests
{
    [TestMethod]
    [DataRow("Version %@", "Version {0}")]
    [DataRow("Every %lld s", "Every {0} s")]
    [DataRow("%ld of %d, %u left, %i more", "{0} of {1}, {2} left, {3} more")]
    [DataRow("%1$@ %2$@", "{0} {1}")]
    [DataRow("%2$@ from %1$@", "{1} from {0}")]
    [DataRow("Active: %1$lld of %2$lld", "Active: {0} of {1}")]
    [DataRow("%@ is 100%% done", "{0} is 100% done")]
    public void SpecifiersBecomePlaceholdersInArgumentOrder(string apple, string expected) =>
        Assert.AreEqual(expected, AppleFormat.Parse(apple).ToDotNet(asFormatString: true));

    [TestMethod]
    [DataRow("Ratio %.1f", "Ratio {0:F1}")]
    [DataRow("%.2f%%", "{0:F2}%")]
    [DataRow("%f", "{0:F6}")]
    public void FixedDecimalsKeepTheirPrecision(string apple, string expected) =>
        Assert.AreEqual(expected, AppleFormat.Parse(apple).ToDotNet(asFormatString: true));

    [TestMethod]
    public void BracesAreDoubledOnlyInFormatStrings()
    {
        Assert.AreEqual("{{{0}}}", AppleFormat.Parse("{%@}").ToDotNet(asFormatString: true));

        var plain = AppleFormat.Parse("Use {braces} at 100%%");
        Assert.AreEqual("Use {{braces}} at 100%", plain.ToDotNet(asFormatString: true));
        Assert.AreEqual("Use {braces} at 100%", plain.ToDotNet(asFormatString: false));
    }

    [TestMethod]
    public void PlaceholderIndicesAreReported()
    {
        CollectionAssert.AreEquivalent(new[] { 0, 1 }, AppleFormat.Parse("%2$@ and %1$@").PlaceholderIndices.ToList());
        CollectionAssert.AreEquivalent(new[] { 1 }, AppleFormat.Parse("only %2$@").PlaceholderIndices.ToList());
        Assert.IsEmpty(AppleFormat.Parse("No arguments").PlaceholderIndices);
    }

    [TestMethod]
    [DataRow("%1$@ and %@")]
    [DataRow("%@ and %2$@")]
    [DataRow("%5d")]
    [DataRow("%-d")]
    [DataRow("50% off")]
    [DataRow("%x")]
    [DataRow("%s")]
    [DataRow("%.2d")]
    [DataRow("%1$%")]
    [DataRow("100%")]
    [DataRow("%!")]
    public void SpecifiersWithoutAFaithfulEquivalentAreRejected(string apple) =>
        Assert.ThrowsExactly<FormatException>(() => AppleFormat.Parse(apple));

    [TestMethod]
    public void TextWithPlaceholdersMustBeAFormatString() =>
        Assert.ThrowsExactly<InvalidOperationException>(() => AppleFormat.Parse("%@").ToDotNet(asFormatString: false));
}
