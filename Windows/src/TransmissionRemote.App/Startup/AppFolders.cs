namespace TransmissionRemote.App.Startup;

/// <summary>Where the app keeps its files: %LOCALAPPDATA%\TransmissionRemote unless told otherwise.</summary>
internal static class AppFolders
{
    /// <summary>
    /// Points the app at another data folder. Development and tests use it so the app never reads or changes the
    /// servers and settings of a real installation on the same PC.
    /// </summary>
    public const string DataDirectoryVariable = "TRANSMISSIONREMOTE_DATA_DIR";

    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string Logs => Path.Combine(DataDirectory, "Logs");

    private static string ResolveDataDirectory()
    {
        var custom = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return Path.GetFullPath(custom);
        }
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "TransmissionRemote");
    }
}
