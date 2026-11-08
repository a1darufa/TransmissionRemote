using System.Text;

namespace StringCatalogConverter;

/// <summary>Reads the catalogs and writes &lt;output&gt;/&lt;language&gt;/Resources.resw for every language.</summary>
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
        var catalogs = options.CatalogPaths
            .Select(path => StringCatalogReader.Parse(File.ReadAllText(path), path))
            .ToList();
        // Converting everything before writing anything means a bad catalog leaves the old files untouched.
        var files = CatalogConverter.Convert(catalogs);

        foreach (var (language, resources) in files)
        {
            var folder = Path.Combine(options.OutputFolder, language);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, FileName);
            File.WriteAllText(path, ReswWriter.Write(resources), Utf8WithoutBom);
            log.WriteLine($"{path}: {resources.Count} strings");
        }
    }
}
