using System.Text;

namespace StringCatalogConverter;

/// <summary>
/// Reads the catalogs and writes &lt;output&gt;/&lt;language&gt;/Resources.resw for every language, plus the C# names
/// file when one is asked for.
/// </summary>
internal static class ReswExport
{
    public const string FileName = "Resources.resw";

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Converts and writes the files, reporting each file's string count to <paramref name="log"/>.</summary>
    /// <exception cref="InvalidDataException">A catalog can't be converted faithfully.</exception>
    /// <exception cref="System.Text.Json.JsonException">A catalog isn't JSON.</exception>
    /// <exception cref="IOException">A file can't be read or written.</exception>
    public static void Run(CommandLineOptions options, TextWriter log)
    {
        var catalogs = options.CatalogPaths.Select(Read).ToList();
        var englishOverrides = options.EnglishOverridesPath is { } overridesPath ? Read(overridesPath) : null;
        // Converting everything before writing anything means a bad catalog leaves the old files untouched.
        var files = CatalogConverter.Convert(catalogs, englishOverrides);

        var code = options.CodePath is null ? null : CodeWriter.Write(files[CatalogConverter.DefaultLanguage]);

        foreach (var (language, resources) in files)
        {
            var folder = Path.Combine(options.OutputFolder, language);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, FileName);
            File.WriteAllText(path, ReswWriter.Write(resources), Utf8WithoutBom);
            log.WriteLine($"{path}: {resources.Count} strings");
        }
        if (options.CodePath is not null)
        {
            File.WriteAllText(options.CodePath, code, Utf8WithoutBom);
            log.WriteLine($"{options.CodePath}: names for the app's code");
        }
    }

    private static StringCatalog Read(string path) => StringCatalogReader.Parse(File.ReadAllText(path), path);
}
