using System.Text.Json;
using StringCatalogConverter;

if (!CommandLineOptions.TryParse(args, out var options, out var error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(CommandLineOptions.Usage);
    return 2;
}

try
{
    ReswExport.Run(options, Console.Out);
    return 0;
}
catch (Exception exception) when (exception is InvalidDataException or JsonException or IOException
    or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Conversion failed: {exception.Message}");
    return 1;
}
