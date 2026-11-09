using System.Diagnostics.CodeAnalysis;

namespace StringCatalogConverter;

/// <summary>
/// The converter's arguments: one or more catalogs, the Strings folder to write into, and optionally the C# file
/// that names every string for the app's code.
/// </summary>
internal sealed record CommandLineOptions(
    IReadOnlyList<string> CatalogPaths, string OutputFolder, string? CodePath = null)
{
    public const string Usage =
        "Usage: dotnet run --project tools/StringCatalogConverter -- " +
        "--catalog <Localizable.xcstrings> [--catalog <path>...] --output <Strings folder> [--code <Strings.g.cs>]";

    public static bool TryParse(
        IReadOnlyList<string> arguments,
        [NotNullWhen(true)] out CommandLineOptions? options,
        [NotNullWhen(false)] out string? error)
    {
        options = null;
        var catalogs = new List<string>();
        string? output = null;
        string? code = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            var name = arguments[index];
            if (name is not ("--catalog" or "--output" or "--code"))
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
            if (name == "--catalog")
            {
                catalogs.Add(path);
                continue;
            }
            if ((name == "--output" ? output : code) is not null)
            {
                error = $"{name} can only be given once.";
                return false;
            }
            if (name == "--output")
            {
                output = path;
            }
            else
            {
                code = path;
            }
        }

        if (catalogs.Count == 0)
        {
            error = "Give at least one --catalog.";
            return false;
        }
        if (output is null)
        {
            error = "Give the --output folder.";
            return false;
        }
        options = new CommandLineOptions(catalogs, output, code);
        error = null;
        return true;
    }
}
