using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransmissionRemote.ViewModels;

/// <summary>The JSON files the app saves, read and written without reflection so the app can be trimmed.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    IgnoreReadOnlyProperties = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(ServerProfile))]
[JsonSerializable(typeof(StoredServers))]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;

/// <summary>The servers file: the servers and which one is active.</summary>
internal sealed record StoredServers(IReadOnlyList<ServerProfile> Servers, Guid? ActiveServerId);

/// <summary>Small file helpers shared by the stores.</summary>
internal static class StorageFiles
{
    /// <summary>
    /// Writes through a temporary file, so a crash or full disk midway never leaves a half-written file
    /// in place of the old one.
    /// </summary>
    public static void WriteAtomically(string path, byte[] contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, contents);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Keeps a copy of a file that couldn't be read, before the next save replaces it, so nothing is lost
    /// for good (for example after going back to an older version; transgui #1344). Returns its path.
    /// </summary>
    public static string KeepUnreadableCopy(string path, byte[] contents, DateTimeOffset now)
    {
        var stamp = now.ToString("yyyy-MM-dd'T'HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);
        var name = $"{Path.GetFileNameWithoutExtension(path)}-unreadable-{stamp}{Path.GetExtension(path)}";
        var copy = Path.Combine(Path.GetDirectoryName(path)!, name);
        File.WriteAllBytes(copy, contents);
        return copy;
    }

    /// <summary>A path with the user's folder written as %USERPROFILE%, so shared logs don't reveal the user's name.</summary>
    public static string DisplayPath(string path) => LogPath.Display(path);

    public static JsonException? TryParse(byte[] contents, out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(contents);
            return null;
        }
        catch (JsonException error)
        {
            document = null;
            return error;
        }
    }
}
