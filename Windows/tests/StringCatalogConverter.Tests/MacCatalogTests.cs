using TransmissionKit;

namespace StringCatalogConverter.Tests;

/// <summary>
/// The Mac app's real catalogs. They must convert, so a shape the converter can't handle fails here rather than
/// when someone regenerates the Windows strings, and their plural forms must cover every category PluralRules
/// asks for, which checks the rules against Xcode's own CLDR data.
/// </summary>
[TestClass]
public sealed class MacCatalogTests
{
    private const string LanguageWithAllStrings = CatalogConverter.DefaultLanguage;

    private static readonly string[] CatalogPaths =
    [
        "macOS/TransmissionRemote/Localizable.xcstrings",
        "macOS/Packages/TransmissionKit/Sources/TransmissionKit/Resources/Localizable.xcstrings",
    ];

    [TestMethod]
    public void EveryLanguageConverts()
    {
        var files = ConvertMacCatalogs();

        Assert.HasCount(28, files);
        Assert.AreEqual("Show Log Files",
            files[LanguageWithAllStrings].Single(resource => resource.Name == "ShowLogFiles").Value);
    }

    [TestMethod]
    public void TranslationsOnlyUseNamesEnglishHas()
    {
        var files = ConvertMacCatalogs();
        var englishNames = files[LanguageWithAllStrings].Select(resource => resource.Name).ToHashSet();

        foreach (var (language, resources) in files)
        {
            foreach (var resource in resources)
            {
                // Plural forms English doesn't need (Files_Few) belong to a family whose other form it has.
                var name = PluralFamily(resource.Name) is { } family
                    ? PluralRules.ResourceName(family, PluralCategory.Other)
                    : resource.Name;
                Assert.Contains(name, englishNames, $"{language}: {resource.Name}");
            }
        }
    }

    [TestMethod]
    public void PluralFormsCoverEveryCategoryTheRulesAskFor()
    {
        var files = ConvertMacCatalogs();
        long[] counts = [.. Enumerable.Range(0, 201).Select(count => (long)count), 1_000_000, 2_000_000];

        foreach (var (language, resources) in files)
        {
            var names = resources.Select(resource => resource.Name).ToHashSet();
            var families = names.Select(PluralFamily).OfType<string>().Distinct();
            foreach (var family in families)
            {
                foreach (var count in counts)
                {
                    var name = PluralRules.ResourceName(family, PluralRules.CategoryFor(language, count));
                    Assert.Contains(name, names, $"{language}: {name} for {count}");
                }
            }
        }
    }

    /// <summary>"Files" for "Files_Few"; null for a name that isn't a plural form.</summary>
    private static string? PluralFamily(string name) => Enum.GetValues<PluralCategory>()
        .Select(category => $"_{category}")
        .Where(suffix => name.EndsWith(suffix, StringComparison.Ordinal))
        .Select(suffix => name[..^suffix.Length])
        .FirstOrDefault();

    private static IReadOnlyDictionary<string, IReadOnlyList<ResourceString>> ConvertMacCatalogs()
    {
        var root = RepositoryRoot()
            ?? throw new AssertInconclusiveException("The Mac app's catalogs aren't above this build's folder.");
        return CatalogConverter.Convert(CatalogPaths.Select(path =>
            StringCatalogReader.Parse(File.ReadAllText(Path.Combine(root, path)), path)));
    }

    private static string? RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, CatalogPaths[0])))
            {
                return folder.FullName;
            }
        }
        return null;
    }
}
