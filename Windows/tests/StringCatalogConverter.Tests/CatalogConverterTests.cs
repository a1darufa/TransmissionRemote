namespace StringCatalogConverter.Tests;

/// <summary>Which strings go into which language's .resw file, under which names, with which text.</summary>
[TestClass]
public sealed class CatalogConverterTests
{
    private const string Catalog = """
        {
          "sourceLanguage" : "en",
          "strings" : {
            "%@ %@" : {
              "localizations" : {
                "en" : { "stringUnit" : { "state" : "new", "value" : "%1$@ %2$@" } }
              },
              "shouldTranslate" : false
            },
            "%lld files" : {
              "localizations" : {
                "en" : { "variations" : { "plural" : {
                  "one" : { "stringUnit" : { "state" : "translated", "value" : "%lld file" } },
                  "other" : { "stringUnit" : { "state" : "translated", "value" : "%lld files" } }
                } } },
                "de" : { "variations" : { "plural" : {
                  "one" : { "stringUnit" : { "state" : "translated", "value" : "Eine Datei" } },
                  "other" : { "stringUnit" : { "state" : "translated", "value" : "%lld Dateien" } }
                } } },
                "fr" : { "variations" : { "plural" : {
                  "one" : { "stringUnit" : { "state" : "translated", "value" : "%lld fichier" } },
                  "many" : { "stringUnit" : { "state" : "new", "value" : "%lld de fichiers" } },
                  "other" : { "stringUnit" : { "state" : "translated", "value" : "%lld fichiers" } }
                } } },
                "ru" : { "variations" : { "plural" : {
                  "one" : { "stringUnit" : { "state" : "translated", "value" : "%lld файл" } },
                  "few" : { "stringUnit" : { "state" : "translated", "value" : "%lld файла" } },
                  "many" : { "stringUnit" : { "state" : "translated", "value" : "%lld файлов" } },
                  "other" : { "stringUnit" : { "state" : "translated", "value" : "%lld файла" } }
                } } },
                "zh-Hans" : { "stringUnit" : { "state" : "translated", "value" : "%lld 个文件" } }
              }
            },
            "Cancel" : {
              "comment" : "Button in the Add Torrent sheet.",
              "localizations" : {
                "de" : { "stringUnit" : { "state" : "translated", "value" : "Abbrechen" } },
                "fr" : { "stringUnit" : { "state" : "needs_review", "value" : "Annuler" } },
                "ru" : { "stringUnit" : { "state" : "translated", "value" : "Отменить" } }
              }
            },
            "Connected to Transmission %@ using %@." : {
              "localizations" : {
                "en" : { "stringUnit" : { "state" : "new", "value" : "Connected to Transmission %1$@ using %2$@." } },
                "zh-Hans" : {
                  "stringUnit" : { "state" : "translated", "value" : "已使用 %2$@ 连接到 Transmission %1$@。" }
                }
              }
            },
            "Show {braces}" : {
              "localizations" : {
                "de" : { "stringUnit" : { "state" : "translated", "value" : "Zeige {Klammern}" } }
              }
            }
          },
          "version" : "1.0"
        }
        """;

    [TestMethod]
    public void EnglishGoesToEnUsWithEveryStringSortedByName()
    {
        var english = Convert(Catalog)["en-US"];

        CollectionAssert.AreEqual(
            new[]
            {
                "Cancel", "ConnectedToTransmissionUsing", "Files_Few", "Files_Many", "Files_One", "Files_Other",
                "ShowBraces", "Text",
            },
            english.Select(resource => resource.Name).ToList());
    }

    [TestMethod]
    public void EnglishHasEveryPluralFormOtherLanguagesUseSoMrtAlwaysFindsADefault()
    {
        var files = Convert(Catalog);

        // Russian's "few" and "many" forms exist in en-US with English's "other" text.
        var few = files["en-US"].Single(resource => resource.Name == "Files_Few");
        Assert.AreEqual("{0} files", few.Value);
        StringAssert.Contains(few.Comment, "Mac key: %lld files");
        StringAssert.Contains(few.Comment, "English never uses this plural form");
        Assert.AreEqual("{0} files", Value(files, "en-US", "Files_Many"));
        // Languages other than English don't get forms they lack.
        Assert.IsNull(Value(files, "de", "Files_Few"));
    }

    [TestMethod]
    public void EnglishTextComesFromTheEnglishLocalizationOrElseTheKey()
    {
        var files = Convert(Catalog);

        Assert.AreEqual("Cancel", Value(files, "en-US", "Cancel"));
        Assert.AreEqual("Connected to Transmission {0} using {1}.",
            Value(files, "en-US", "ConnectedToTransmissionUsing"));
        Assert.AreEqual("{0} {1}", Value(files, "en-US", "Text"));
    }

    [TestMethod]
    public void UnfinishedTranslationsAreLeftOutForMrtToFallBackToEnglish()
    {
        var files = Convert(Catalog);

        // French has only a string that needs review and a plural string with an unfinished form.
        Assert.IsFalse(files.ContainsKey("fr"));
        // German has no translation of "Connected to ...".
        Assert.IsNull(Value(files, "de", "ConnectedToTransmissionUsing"));
        Assert.AreEqual("Abbrechen", Value(files, "de", "Cancel"));
    }

    [TestMethod]
    public void StringsMarkedNotForTranslationAreOnlyInEnglish()
    {
        var files = Convert(Catalog);

        Assert.AreEqual("{0} {1}", Value(files, "en-US", "Text"));
        Assert.IsNull(Value(files, "ru", "Text"));
        Assert.IsNull(Value(files, "zh-Hans", "Text"));
    }

    [TestMethod]
    public void PluralStringsGetOneResourcePerCategory()
    {
        var files = Convert(Catalog);

        Assert.AreEqual("{0} file", Value(files, "en-US", "Files_One"));
        Assert.AreEqual("{0} files", Value(files, "en-US", "Files_Other"));
        CollectionAssert.AreEqual(
            new[] { "{0} файла", "{0} файлов", "{0} файл", "{0} файла" },
            new[] { "Files_Few", "Files_Many", "Files_One", "Files_Other" }.Select(name => Value(files, "ru", name))
                .ToList());
    }

    [TestMethod]
    public void APluralFormMayLeaveOutTheNumber() =>
        Assert.AreEqual("Eine Datei", Value(Convert(Catalog), "de", "Files_One"));

    [TestMethod]
    public void ASingleValueForAPluralStringIsItsOtherForm()
    {
        var chinese = Convert(Catalog)["zh-Hans"];

        var forms = chinese.Where(resource => resource.Name.StartsWith("Files_", StringComparison.Ordinal)).ToList();
        Assert.HasCount(1, forms);
        Assert.AreEqual(new ResourceString("Files_Other", "{0} 个文件", "Mac key: %lld files"), forms[0]);
    }

    [TestMethod]
    public void NumberedArgumentsKeepTheirOrder() =>
        Assert.AreEqual("已使用 {1} 连接到 Transmission {0}。",
            Value(Convert(Catalog), "zh-Hans", "ConnectedToTransmissionUsing"));

    [TestMethod]
    public void BracesAreDoubledOnlyInStringsWithPlaceholders()
    {
        var files = Convert(Catalog);

        Assert.AreEqual("Show {braces}", Value(files, "en-US", "ShowBraces"));
        Assert.AreEqual("Zeige {Klammern}", Value(files, "de", "ShowBraces"));
    }

    [TestMethod]
    public void CommentsCarryTheMacKeyInEveryLanguage()
    {
        var files = Convert(Catalog);
        const string expected = "Mac key: Cancel\nMac comment: Button in the Add Torrent sheet.";

        Assert.AreEqual(expected, Resource(files, "en-US", "Cancel")!.Comment);
        Assert.AreEqual(expected, Resource(files, "ru", "Cancel")!.Comment);
        Assert.AreEqual("Mac key: %@ %@\nNot translated: the Mac catalog marks it shouldTranslate = false.",
            Resource(files, "en-US", "Text")!.Comment);
    }

    [TestMethod]
    public void TheSameKeyInTwoCatalogsIsWrittenOnce()
    {
        var once = Convert(Catalog);
        var twice = Convert(Catalog, Catalog);

        CollectionAssert.AreEqual(once.Keys.ToList(), twice.Keys.ToList());
        foreach (var language in once.Keys)
        {
            CollectionAssert.AreEqual(once[language].ToList(), twice[language].ToList());
        }
    }

    [TestMethod]
    public void TheSameKeyWithDifferentTranslationsIsAnError()
    {
        var other = SingleEntry("Cancel", "de", "Abbruch");

        Assert.ThrowsExactly<InvalidDataException>(() => Convert(Catalog, other));
    }

    [TestMethod]
    public void ATranslationWithAnArgumentEnglishLacksIsAnError()
    {
        var catalog = SingleEntry("Version %@", "ru", "Версия %1$@ (%2$@)");

        var error = Assert.ThrowsExactly<InvalidDataException>(() => Convert(catalog));
        StringAssert.Contains(error.Message, "\"Version %@\" (ru)");
    }

    [TestMethod]
    public void AMalformedSpecifierNamesTheKeyAndLanguage()
    {
        var catalog = SingleEntry("Done: %@", "de", "Fertig: 100%");

        var error = Assert.ThrowsExactly<InvalidDataException>(() => Convert(catalog));
        StringAssert.Contains(error.Message, "\"Done: %@\" (de)");
    }

    [TestMethod]
    public void GrammarAgreementWithoutPluralFormsIsAnError()
    {
        var catalog = SingleEntry("Remove ^[%lld torrent](inflect: true)?", "de", "%lld Torrents entfernen?");

        Assert.ThrowsExactly<InvalidDataException>(() => Convert(catalog));
    }

    [TestMethod]
    public void AnEnglishOverrideChangesOnlyTheEnglishText()
    {
        var overrides = SingleEntry("Connected to Transmission %@ using %@.", "en", "Connected to Transmission %1$@ over %2$@.");

        var files = ConvertWithOverrides(overrides, Catalog);

        Assert.AreEqual("Connected to Transmission {0} over {1}.", Value(files, "en-US", "ConnectedToTransmissionUsing"));
        Assert.AreEqual("已使用 {1} 连接到 Transmission {0}。", Value(files, "zh-Hans", "ConnectedToTransmissionUsing"));
        StringAssert.Contains(Resource(files, "en-US", "ConnectedToTransmissionUsing")!.Comment, "English overrides");
        Assert.DoesNotContain(
            "English overrides", Resource(files, "zh-Hans", "ConnectedToTransmissionUsing")!.Comment,
            "the translations still follow the Mac");
    }

    [TestMethod]
    public void AnEnglishOverrideCanChangeEachPluralForm()
    {
        const string Overrides = """
            {
              "sourceLanguage": "en",
              "strings": {
                "%lld files": { "localizations": { "en": { "variations": { "plural": {
                  "one": { "stringUnit": { "state": "translated", "value": "%lld item" } },
                  "other": { "stringUnit": { "state": "translated", "value": "%lld items" } }
                } } } } }
              }
            }
            """;

        var files = ConvertWithOverrides(Overrides, Catalog);

        Assert.AreEqual("{0} item", Value(files, "en-US", "Files_One"));
        Assert.AreEqual("{0} items", Value(files, "en-US", "Files_Few"), "the default for MRT follows the override");
        Assert.AreEqual("{0} файла", Value(files, "ru", "Files_Few"));
    }

    [TestMethod]
    [DataRow("Nowhere", "en", "Nowhere else", "isn't in any catalog")]
    [DataRow("Cancel", "de", "Abbruch", "may only have English text")]
    [DataRow("Connected to Transmission %@ using %@.", "en", "Connected to %1$@.", "placeholders")]
    [DataRow("Cancel", "en", "Cancel", "the same as the original")]
    [DataRow("%lld files", "en", "Files", "plural forms")]
    public void AnEnglishOverrideMustFitTheString(string key, string language, string value, string expectedError)
    {
        var overrides = SingleEntry(key, language, value);

        var error = Assert.ThrowsExactly<InvalidDataException>(() => ConvertWithOverrides(overrides, Catalog));
        StringAssert.Contains(error.Message, expectedError);
    }

    [TestMethod]
    public void CatalogsMustBeWrittenInEnglish() =>
        Assert.ThrowsExactly<InvalidDataException>(() => Convert(Catalog.Replace(
            "\"sourceLanguage\" : \"en\"", "\"sourceLanguage\" : \"de\"", StringComparison.Ordinal)));

    private static IReadOnlyDictionary<string, IReadOnlyList<ResourceString>> Convert(params string[] catalogs) =>
        CatalogConverter.Convert(catalogs.Select(json => StringCatalogReader.Parse(json, "Test.xcstrings")));

    private static IReadOnlyDictionary<string, IReadOnlyList<ResourceString>> ConvertWithOverrides(
        string overrides, params string[] catalogs) =>
        CatalogConverter.Convert(
            catalogs.Select(json => StringCatalogReader.Parse(json, "Test.xcstrings")),
            StringCatalogReader.Parse(overrides, "EnglishOverrides.xcstrings"));

    private static ResourceString? Resource(
        IReadOnlyDictionary<string, IReadOnlyList<ResourceString>> files, string language, string name) =>
        files.GetValueOrDefault(language)?.SingleOrDefault(resource => resource.Name == name);

    private static string? Value(
        IReadOnlyDictionary<string, IReadOnlyList<ResourceString>> files, string language, string name) =>
        Resource(files, language, name)?.Value;

    private static string SingleEntry(string key, string language, string value) => $$"""
        {
          "sourceLanguage": "en",
          "strings": {
            "{{key}}": { "localizations": { "{{language}}": {
              "stringUnit": { "state": "translated", "value": "{{value}}" } } } }
          }
        }
        """;
}
