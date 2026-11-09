using System.Diagnostics.CodeAnalysis;

namespace StringCatalogConverter;

/// <summary>
/// The converter's arguments: one or more catalogs, the Strings folder to write into, and optionally the C# file
/// that names every string for the app's code and a catalog of English texts worded for Windows.
/// </summary>
internal sealed record CommandLineOptions(
    IReadOnlyList<string> CatalogPaths, string OutputFolder, string? CodePath = null, string? EnglishOverridesPath = null)
{
    public const string Usage =
        "Usage: dotnet run --project tools/StringCatalogConverter -- " +
        "--catalog <Localizable.xcstrings> [--catalog <path>...] --output <Strings folder> [--code <Strings.g.cs>] " +
        "[--english-overrides <EnglishOverrides.xcstrings>]";

    private const string Catalog = "--catalog";
    private const string Output = "--output";
    private const string Code = "--code";
    private const string EnglishOverrides = "--english-overrides";

    public static bool TryParse(
        IReadOnlyList<string> arguments,
        [NotNullWhen(true)] out CommandLineOptions? options,
        [NotNullWhen(false)] out string? error)
    {
        options = null;
        var catalogs = new List<string>();
        // The options other than --catalog take one path each.
        var single = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index++)
        {
            var name = arguments[index];
            if (name is not (Catalog or Output or Code or EnglishOverrides))
            {
                error = $"Unknown argument \"{name}\".";
                return false;
            }
            if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"{name} needs a path.";
                return false;
            }

            var path = arguments[++index];
            if (name == Catalog)
            {
                catalogs.Add(path);
            }
            else if (!single.TryAdd(name, path))
            {
                error = $"{name} can only be given once.";
                return false;
            }
        }

        if (catalogs.Count == 0)
        {
            error = "Give at least one --catalog.";
            return false;
        }
        if (!single.TryGetValue(Output, out var output))
        {
            error = "Give the --output folder.";
            return false;
        }
        options = new CommandLineOptions(
            catalogs, output, single.GetValueOrDefault(Code), single.GetValueOrDefault(EnglishOverrides));
        error = null;
        return true;
    }
}
