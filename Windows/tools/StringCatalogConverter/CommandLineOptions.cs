using System.Diagnostics.CodeAnalysis;

namespace StringCatalogConverter;

/// <summary>The converter's arguments: one or more catalogs and the Strings folder to write into.</summary>
internal sealed record CommandLineOptions(IReadOnlyList<string> CatalogPaths, string OutputFolder)
{
    public const string Usage =
        "Usage: dotnet run --project tools/StringCatalogConverter -- " +
        "--catalog <Localizable.xcstrings> [--catalog <path>...] --output <Strings folder>";

    public static bool TryParse(
        IReadOnlyList<string> arguments,
        [NotNullWhen(true)] out CommandLineOptions? options,
        [NotNullWhen(false)] out string? error)
    {
        options = null;
        var catalogs = new List<string>();
        string? output = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            var name = arguments[index];
            if (name is not ("--catalog" or "--output"))
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
            }
            else if (output is null)
            {
                output = path;
            }
            else
            {
                error = "--output can only be given once.";
                return false;
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
        options = new CommandLineOptions(catalogs, output);
        error = null;
        return true;
    }
}
