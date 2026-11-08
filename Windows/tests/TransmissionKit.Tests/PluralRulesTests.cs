using static TransmissionKit.PluralCategory;

namespace TransmissionKit.Tests;

/// <summary>
/// CLDR cardinal plural rules for whole numbers, one test per rule family. Expected values are from the CLDR chart
/// (https://www.unicode.org/cldr/charts/latest/supplemental/language_plural_rules.html).
/// </summary>
[TestClass]
public sealed class PluralRulesTests
{
    [TestMethod]
    [DataRow("ru")]
    [DataRow("uk")]
    [DataRow("be")]
    public void EastSlavicLanguagesHaveOneFewAndMany(string language) =>
        AssertCategories(language,
            (0, Many), (1, One), (2, Few), (4, Few), (5, Many), (11, Many), (12, Many), (14, Many), (21, One),
            (22, Few), (25, Many), (101, One), (111, Many), (112, Many), (122, Few), (1_000_000, Many));

    [TestMethod]
    public void PolishTreatsOnlyOneAsOne() =>
        AssertCategories("pl",
            (0, Many), (1, One), (2, Few), (4, Few), (5, Many), (12, Many), (21, Many), (22, Few), (25, Many),
            (101, Many), (112, Many), (122, Few));

    [TestMethod]
    public void CzechHasFewForTwoToFourOnly() =>
        AssertCategories("cs", (0, Other), (1, One), (2, Few), (4, Few), (5, Other), (21, Other), (22, Other));

    [TestMethod]
    public void CroatianHasOneAndFewByLastDigits() =>
        AssertCategories("hr",
            (0, Other), (1, One), (2, Few), (5, Other), (11, Other), (12, Other), (21, One), (22, Few),
            (111, Other), (112, Other));

    [TestMethod]
    public void LithuanianExcludesTheTeens() =>
        AssertCategories("lt",
            (0, Other), (1, One), (2, Few), (9, Few), (10, Other), (11, Other), (19, Other), (20, Other), (21, One),
            (29, Few), (101, One), (111, Other));

    [TestMethod]
    public void LatvianHasZeroForTensAndTeens() =>
        AssertCategories("lv",
            (0, Zero), (1, One), (2, Other), (9, Other), (10, Zero), (11, Zero), (19, Zero), (20, Zero), (21, One),
            (22, Other), (101, One), (111, Zero));

    [TestMethod]
    public void RomanianHasFewForZeroAndEndingsOneToNineteen() =>
        AssertCategories("ro",
            (0, Few), (1, One), (2, Few), (19, Few), (20, Other), (100, Other), (101, Few), (119, Few), (120, Other));

    [TestMethod]
    [DataRow("fr")]
    [DataRow("pt")]
    [DataRow("pt-BR")]
    public void FrenchAndBrazilianPortugueseCountZeroAsOne(string language) =>
        AssertCategories(language,
            (0, One), (1, One), (2, Other), (1_000_000, Many), (2_000_000, Many), (1_000_001, Other));

    [TestMethod]
    [DataRow("ca")]
    [DataRow("es")]
    [DataRow("it")]
    [DataRow("pt-PT")]
    public void SomeRomanceLanguagesHaveManyForWholeMillions(string language) =>
        AssertCategories(language, (0, Other), (1, One), (2, Other), (1_000_000, Many), (1_500_000, Other));

    [TestMethod]
    [DataRow("da")]
    [DataRow("de")]
    [DataRow("el")]
    [DataRow("en")]
    [DataRow("fi")]
    [DataRow("hu")]
    [DataRow("nb")]
    [DataRow("nl")]
    [DataRow("sv")]
    [DataRow("tr")]
    public void EnglishLikeLanguagesHaveOneForOneOnly(string language) =>
        AssertCategories(language, (0, Other), (1, One), (2, Other), (21, Other), (1_000_000, Other));

    [TestMethod]
    [DataRow("ko")]
    [DataRow("zh-Hans")]
    [DataRow("zh-Hant")]
    public void KoreanAndChineseHaveNoPlurals(string language) =>
        AssertCategories(language, (0, Other), (1, Other), (2, Other), (1_000_000, Other));

    [TestMethod]
    public void TagsAreReadWithRegionScriptAndAnyCasing()
    {
        Assert.AreEqual(Few, PluralRules.CategoryFor("ru-RU", 3));
        Assert.AreEqual(Few, PluralRules.CategoryFor("RU", 3));
        Assert.AreEqual(Other, PluralRules.CategoryFor("zh-Hans-CN", 1));
        Assert.AreEqual(One, PluralRules.CategoryFor("fr-CA", 0));
        // Portugal counts 0 as other, Brazil as one.
        Assert.AreEqual(Other, PluralRules.CategoryFor("pt_PT", 0));
        Assert.AreEqual(Other, PluralRules.CategoryFor("PT-pt", 0));
        Assert.AreEqual(One, PluralRules.CategoryFor("pt-BR", 0));
    }

    [TestMethod]
    public void OtherLanguagesUseTheEnglishRules()
    {
        Assert.AreEqual(One, PluralRules.CategoryFor("ja", 1));
        Assert.AreEqual(Other, PluralRules.CategoryFor("ja", 2));
        Assert.AreEqual(One, PluralRules.CategoryFor("", 1));
    }

    [TestMethod]
    public void NegativeCountsUseTheirAbsoluteValue()
    {
        Assert.AreEqual(One, PluralRules.CategoryFor("ru", -1));
        Assert.AreEqual(Few, PluralRules.CategoryFor("ru", -2));
        Assert.AreEqual(Many, PluralRules.CategoryFor("ru", long.MinValue)); // 9223372036854775808
        Assert.AreEqual(Other, PluralRules.CategoryFor("en", long.MinValue));
    }

    [TestMethod]
    public void ResourceNamesAppendTheCategory()
    {
        Assert.AreEqual("Files_Few", PluralRules.ResourceName("Files", Few));
        Assert.AreEqual("TorrentsSelected_Other", PluralRules.ResourceName("TorrentsSelected", Other));
    }

    [TestMethod]
    public void MissingArgumentsAreRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => PluralRules.CategoryFor(null!, 1));
        Assert.ThrowsExactly<ArgumentException>(() => PluralRules.ResourceName("", One));
    }

    private static void AssertCategories(
        string language, params ReadOnlySpan<(long Count, PluralCategory Expected)> cases)
    {
        foreach (var (count, expected) in cases)
        {
            Assert.AreEqual(expected, PluralRules.CategoryFor(language, count), $"{language}, {count}");
        }
    }
}
