using System.Text.Json;
using TransmissionKit;

namespace StringCatalogConverter.Tests;

/// <summary>Reading .xcstrings files in the shapes Xcode writes.</summary>
[TestClass]
public sealed class StringCatalogReaderTests
{
    private const string Catalog = """
        {
          "sourceLanguage" : "en",
          "strings" : {
            " " : {
              "shouldTranslate" : false
            },
            "%lld files" : {
              "comment" : "Under the file list.",
              "localizations" : {
                "en" : {
                  "variations" : {
                    "plural" : {
                      "one" : { "stringUnit" : { "state" : "translated", "value" : "%lld file" } },
                      "other" : { "stringUnit" : { "state" : "translated", "value" : "%lld files" } }
                    }
                  }
                }
              }
            },
            "Cancel" : {
              "localizations" : {
                "de" : { "stringUnit" : { "state" : "translated", "value" : "Abbrechen" } },
                "fr" : { "stringUnit" : { "state" : "needs_review", "value" : "Annuler" } }
              }
            }
          },
          "version" : "1.0"
        }
        """;

    [TestMethod]
    public void ReadsEntriesWithTheirLocalizations()
    {
        var catalog = StringCatalogReader.Parse(Catalog, "Test.xcstrings");

        Assert.AreEqual("en", catalog.SourceLanguage);
        CollectionAssert.AreEqual(
            new[] { " ", "%lld files", "Cancel" }, catalog.Entries.Select(entry => entry.Key).ToList());
        var cancel = catalog.Entries[2];
        Assert.IsTrue(cancel.ShouldTranslate);
        Assert.IsFalse(cancel.IsPlural);
        Assert.AreEqual(new CatalogText("Abbrechen", IsTranslated: true), cancel.Localizations["de"].Value);
        Assert.AreEqual(new CatalogText("Annuler", IsTranslated: false), cancel.Localizations["fr"].Value);
    }

    [TestMethod]
    public void ReadsShouldTranslateCommentsAndPluralForms()
    {
        var catalog = StringCatalogReader.Parse(Catalog, "Test.xcstrings");

        Assert.IsFalse(catalog.Entries[0].ShouldTranslate);
        Assert.IsEmpty(catalog.Entries[0].Localizations);
        var files = catalog.Entries[1];
        Assert.IsTrue(files.IsPlural);
        Assert.AreEqual("Under the file list.", files.Comment);
        var forms = files.Localizations["en"].PluralForms!;
        Assert.AreEqual("%lld file", forms[PluralCategory.One].Value);
        Assert.AreEqual("%lld files", forms[PluralCategory.Other].Value);
    }

    [TestMethod]
    [DataRow("""{ "stringUnit": { "state": "translated", "value": "%#@files@" }, "substitutions": {} }""")]
    [DataRow("""{ "variations": { "device": { "mac": { "stringUnit": { "state": "new", "value": "x" } } } } }""")]
    [DataRow("""{ "variations": { "plural": { "other": { "variations": { "device": {} } } } } }""")]
    [DataRow("""{ "variations": { "plural": { "lots": { "stringUnit": { "state": "new", "value": "x" } } } } }""")]
    [DataRow("""{ "variations": { "plural": { "one": { "stringUnit": { "state": "new", "value": "x" } } } } }""")]
    [DataRow("""{ "stringUnit": { "state": "new", "value": "x" }, "variations": { "plural": {} } }""")]
    [DataRow("""{ }""")]
    [DataRow("""{ "stringUnit": { "state": "translated" } }""")]
    public void ShapesResWCantExpressAreRejected(string localization) =>
        Assert.ThrowsExactly<InvalidDataException>(
            () => StringCatalogReader.Parse(SingleEntry("de", localization), "Test.xcstrings"));

    [TestMethod]
    [DataRow("../../outside")]
    [DataRow("de/x")]
    [DataRow(@"de\n")] // A line break, escaped for JSON.
    [DataRow("")]
    public void LanguageCodesThatArentTagsAreRejected(string language) =>
        Assert.ThrowsExactly<InvalidDataException>(() => StringCatalogReader.Parse(
            SingleEntry(language, """{ "stringUnit": { "state": "translated", "value": "x" } }"""), "Test.xcstrings"));

    [TestMethod]
    [DataRow("""[]""")]
    [DataRow("""{ "strings": {} }""")]
    [DataRow("""{ "sourceLanguage": "en" }""")]
    [DataRow("""{ "sourceLanguage": "en", "strings": { "Key": { "shouldTranslate": "no" } } }""")]
    [DataRow("""{ "sourceLanguage": "en", "strings": { "Key": { "comment": 5 } } }""")]
    public void MalformedCatalogsAreRejected(string json) =>
        Assert.ThrowsExactly<InvalidDataException>(() => StringCatalogReader.Parse(json, "Test.xcstrings"));

    [TestMethod]
    public void ErrorsNameTheCatalogKeyAndLanguage()
    {
        var error = Assert.ThrowsExactly<InvalidDataException>(() => StringCatalogReader.Parse(
            SingleEntry("de", """{ "stringUnit": { "state": "translated", "value": 1 } }"""), "Test.xcstrings"));

        StringAssert.Contains(error.Message, "Test.xcstrings, \"Key\", de");
    }

    [TestMethod]
    public void TextThatIsntJsonIsAJsonError() =>
        Assert.Throws<JsonException>(() => StringCatalogReader.Parse("not json", "Test.xcstrings"));

    private static string SingleEntry(string language, string localization) => $$"""
        { "sourceLanguage": "en", "strings": { "Key": { "localizations": { "{{language}}": {{localization}} } } } }
        """;
}
