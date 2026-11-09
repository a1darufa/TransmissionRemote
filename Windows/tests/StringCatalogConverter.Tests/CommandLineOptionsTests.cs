namespace StringCatalogConverter.Tests;

/// <summary>The converter's command line.</summary>
[TestClass]
public sealed class CommandLineOptionsTests
{
    [TestMethod]
    public void ReadsEveryCatalogAndTheOutputFolder()
    {
        var parsed = CommandLineOptions.TryParse(
            ["--catalog", "App.xcstrings", "--output", "Strings", "--catalog", "Kit.xcstrings"],
            out var options,
            out var error);

        Assert.IsTrue(parsed, error);
        CollectionAssert.AreEqual(new[] { "App.xcstrings", "Kit.xcstrings" }, options!.CatalogPaths.ToList());
        Assert.AreEqual("Strings", options.OutputFolder);
        Assert.IsNull(options.CodePath);
    }

    [TestMethod]
    public void ReadsTheOptionalCodeFile()
    {
        var parsed = CommandLineOptions.TryParse(
            ["--catalog", "App.xcstrings", "--output", "Strings", "--code", "Strings.g.cs"],
            out var options,
            out var error);

        Assert.IsTrue(parsed, error);
        Assert.AreEqual("Strings.g.cs", options!.CodePath);
        Assert.IsNull(options.EnglishOverridesPath);
    }

    [TestMethod]
    public void ReadsTheOptionalEnglishOverrides()
    {
        var parsed = CommandLineOptions.TryParse(
            ["--catalog", "App.xcstrings", "--english-overrides", "EnglishOverrides.xcstrings", "--output", "Strings"],
            out var options,
            out var error);

        Assert.IsTrue(parsed, error);
        Assert.AreEqual("EnglishOverrides.xcstrings", options!.EnglishOverridesPath);
        CollectionAssert.AreEqual(new[] { "App.xcstrings" }, options.CatalogPaths.ToList(), "the overrides aren't a catalog");
    }

    [TestMethod]
    [DataRow(new string[0], "--catalog")]
    [DataRow(new[] { "--output", "Strings" }, "--catalog")]
    [DataRow(new[] { "--catalog", "App.xcstrings" }, "--output")]
    [DataRow(new[] { "--catalog", "--output", "Strings" }, "--catalog needs a path")]
    [DataRow(new[] { "--catalog", "App.xcstrings", "--output" }, "--output needs a path")]
    [DataRow(new[] { "--catalog", "App.xcstrings", "--output", "A", "--output", "B" }, "once")]
    [DataRow(new[] { "--catalog", "App.xcstrings", "--output", "A", "--code", "B", "--code", "C" }, "--code can only")]
    [DataRow(new[] { "--catalog", "App.xcstrings", "--output", "A", "--code" }, "--code needs a path")]
    [DataRow(new[] { "--catalog", "App.xcstrings", "--output", "A", "--english-overrides", "B", "--english-overrides", "C" }, "--english-overrides can only")]
    [DataRow(new[] { "App.xcstrings" }, "Unknown argument")]
    public void MistakesAreExplained(string[] arguments, string expectedError)
    {
        var parsed = CommandLineOptions.TryParse(arguments, out var options, out var error);

        Assert.IsFalse(parsed);
        Assert.IsNull(options);
        StringAssert.Contains(error, expectedError);
    }
}
