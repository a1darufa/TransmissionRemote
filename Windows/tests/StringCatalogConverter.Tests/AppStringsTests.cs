namespace StringCatalogConverter.Tests;

/// <summary>
/// The Windows app's generated strings must match the catalogs, so a catalog changed without rerunning the
/// converter, or a hand-edited .resw file, fails here.
/// </summary>
[TestClass]
public sealed class AppStringsTests
{
    private const string StringsFolder = "Windows/src/TransmissionRemote.App/Strings";
    private const string WindowsCatalog = StringsFolder + "/Windows.xcstrings";
    private const string EnglishOverrides = StringsFolder + "/EnglishOverrides.xcstrings";
    private const string CodeFile = StringsFolder + "/Strings.g.cs";

    private const string HowToFix =
        "Run the converter as Windows/README.md (Strings) shows, and commit what it writes.";

    private static readonly string[] CatalogPaths =
    [
        "macOS/TransmissionRemote/Localizable.xcstrings",
        "macOS/Packages/TransmissionKit/Sources/TransmissionKit/Resources/Localizable.xcstrings",
        WindowsCatalog,
    ];

    [TestMethod]
    public void EveryLanguageFileMatchesTheCatalogs()
    {
        var root = RepositoryRoot();
        var files = Convert(root);

        Assert.HasCount(28, files);
        foreach (var (language, resources) in files)
        {
            var path = Path.Combine(root, StringsFolder, language, ReswExport.FileName);
            Assert.IsTrue(File.Exists(path), $"{language} has no Resources.resw. {HowToFix}");
            Assert.AreEqual(ReswWriter.Write(resources), ReadNormalized(path), $"{language} is stale. {HowToFix}");
        }
    }

    [TestMethod]
    public void NoLanguageFolderIsLeftOver()
    {
        var root = RepositoryRoot();
        var expected = Convert(root).Keys.ToHashSet(StringComparer.Ordinal);

        var folders = Directory.GetDirectories(Path.Combine(root, StringsFolder)).Select(Path.GetFileName);
        foreach (var folder in folders)
        {
            Assert.Contains(folder!, expected, $"Strings/{folder} isn't a language the catalogs have. {HowToFix}");
        }
    }

    [TestMethod]
    public void TheNamesFileMatchesTheCatalogs()
    {
        var root = RepositoryRoot();
        var english = Convert(root)[CatalogConverter.DefaultLanguage];

        Assert.AreEqual(CodeWriter.Write(english), ReadNormalized(Path.Combine(root, CodeFile)),
            $"Strings.g.cs is stale. {HowToFix}");
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ResourceString>> Convert(string root)
    {
        StringCatalog Read(string path) => StringCatalogReader.Parse(File.ReadAllText(Path.Combine(root, path)), path);
        return CatalogConverter.Convert(CatalogPaths.Select(Read), Read(EnglishOverrides));
    }

    /// <summary>Git may check text files out with CRLF on Windows; the converter writes LF.</summary>
    private static string ReadNormalized(string path) => File.ReadAllText(path).ReplaceLineEndings("\n");

    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, WindowsCatalog)))
            {
                return folder.FullName;
            }
        }
        throw new AssertInconclusiveException("The app's string catalogs aren't above this build's folder.");
    }
}
