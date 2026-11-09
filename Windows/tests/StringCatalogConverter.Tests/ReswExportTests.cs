namespace StringCatalogConverter.Tests;

/// <summary>Reading catalogs from disk and writing a Resources.resw per language folder.</summary>
[TestClass]
public sealed class ReswExportTests
{
    private const string Catalog = """
        {
          "sourceLanguage" : "en",
          "strings" : {
            "Cancel" : {
              "localizations" : {
                "de" : { "stringUnit" : { "state" : "translated", "value" : "Abbrechen" } },
                "zh-Hans" : { "stringUnit" : { "state" : "translated", "value" : "取消" } }
              }
            }
          }
        }
        """;

    [TestMethod]
    public void WritesAResourcesFilePerLanguageFolder()
    {
        var folder = Directory.CreateTempSubdirectory("StringCatalogConverterTests");
        try
        {
            var catalog = Path.Combine(folder.FullName, "Localizable.xcstrings");
            File.WriteAllText(catalog, Catalog);
            var output = Path.Combine(folder.FullName, "Strings");
            using var log = new StringWriter();

            ReswExport.Run(new CommandLineOptions([catalog], output), log);

            CollectionAssert.AreEquivalent(
                new[] { "de", "en-US", "zh-Hans" },
                Directory.GetDirectories(output).Select(Path.GetFileName).ToList());
            StringAssert.Contains(File.ReadAllText(Path.Combine(output, "zh-Hans", "Resources.resw")),
                "<value>取消</value>");
            StringAssert.Contains(log.ToString(), $"{Path.Combine(output, "de", "Resources.resw")}: 1 strings");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void WritesTheNamesFileWhenAskedTo()
    {
        var folder = Directory.CreateTempSubdirectory("StringCatalogConverterTests");
        try
        {
            var catalog = Path.Combine(folder.FullName, "Localizable.xcstrings");
            File.WriteAllText(catalog, Catalog);
            var code = Path.Combine(folder.FullName, "Strings.g.cs");

            ReswExport.Run(
                new CommandLineOptions([catalog], Path.Combine(folder.FullName, "Strings"), code), TextWriter.Null);

            StringAssert.Contains(File.ReadAllText(code), "public static string Cancel =>");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ACatalogThatCantBeConvertedWritesNothing()
    {
        var folder = Directory.CreateTempSubdirectory("StringCatalogConverterTests");
        try
        {
            var good = Path.Combine(folder.FullName, "Good.xcstrings");
            var bad = Path.Combine(folder.FullName, "Bad.xcstrings");
            File.WriteAllText(good, Catalog);
            File.WriteAllText(bad, Catalog.Replace("Abbrechen", "100%", StringComparison.Ordinal));
            var output = Path.Combine(folder.FullName, "Strings");

            Assert.ThrowsExactly<InvalidDataException>(
                () => ReswExport.Run(new CommandLineOptions([good, bad], output), TextWriter.Null));
            Assert.IsFalse(Directory.Exists(output));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
