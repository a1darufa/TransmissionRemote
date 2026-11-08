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
    }

    [TestMethod]
    [DataRow(new string[0], "--catalog")]
    [DataRow(new[] { "--output", "Strings" }, "--catalog")]
    [DataRow(new[] { "--catalog", "App.xcstrings" }, "--output")]
    [DataRow(new[] { "--catalog", "--output", "Strings" }, "--catalog needs a path")]
    [DataRow(new[] { "--catalog", "App.xcstrings", "--output" }, "--output needs a path")]
    [DataRow(new[] { "--catalog", "App.xcstrings", "--output", "A", "--output", "B" }, "once")]
    [DataRow(new[] { "App.xcstrings" }, "Unknown argument")]
    public void MistakesAreExplained(string[] arguments, string expectedError)
    {
        var parsed = CommandLineOptions.TryParse(arguments, out var options, out var error);

        Assert.IsFalse(parsed);
        Assert.IsNull(options);
        StringAssert.Contains(error, expectedError);
    }
}
