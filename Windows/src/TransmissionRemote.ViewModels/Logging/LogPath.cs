namespace TransmissionRemote.ViewModels;

/// <summary>How paths are written into log files, by this library and by the app.</summary>
public static class LogPath
{
    /// <summary>
    /// The path with the user's folder written as %USERPROFILE%, so shared logs don't reveal the user's name.
    /// </summary>
    public static string Display(string path) =>
        Display(path, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    internal static string Display(string path, string userProfile)
    {
        ArgumentNullException.ThrowIfNull(path);
        var isInProfile = userProfile.Length > 0
            && path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase)
            && (path.Length == userProfile.Length || path[userProfile.Length] is '\\' or '/');
        return isInProfile ? "%USERPROFILE%" + path[userProfile.Length..] : path;
    }
}
