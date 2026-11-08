using System.Collections.Frozen;

namespace TransmissionRemote.ViewModels;

/// <summary>
/// Recognizes programs, scripts and installers, so the app asks before opening one from a torrent.
/// </summary>
/// <remarks>
/// Windows checks files downloaded from the internet before they run because the browser marks them (the
/// Zone.Identifier "Mark of the Web" that SmartScreen and Office's Protected View look for). Files the
/// Transmission daemon wrote to a network share carry no such mark, so double-clicking a program or script inside a
/// torrent would run it with no warning at all (transgui pull request #1583). The dialog is the app's: it offers
/// Show in folder, Open anyway and Cancel, like the Mac app.
/// </remarks>
public static class RunnableFile
{
    /// <summary>Kinds of files that run something, or install something, as soon as they're opened.</summary>
    private static readonly FrozenSet<string> RunnableExtensions = new[]
    {
        // Programs: an executable, a screen saver, an old DOS shortcut, a Control Panel applet, an MMC console.
        "exe", "com", "scr", "pif", "cpl", "msc",
        // Installers and app packages, often run with administrator rights, including ClickOnce.
        "msi", "msp", "msix", "msixbundle", "appx", "appxbundle", "appinstaller", "application", "appref-ms",
        // Scripts Windows runs on double-click: Command Prompt, Windows Script Host and HTML applications.
        "bat", "cmd", "vbs", "vbe", "js", "jse", "wsf", "wsh", "hta",
        // PowerShell scripts open in Notepad by default, but run when that was changed, and "Run with PowerShell"
        // is one click away.
        "ps1",
        // Shortcuts that start any program with any arguments, such as "powershell -c …".
        "lnk", "url", "scf",
        // Registry files change system settings, behind a prompt people tend to accept.
        "reg",
        // Java programs, run by an installed Java.
        "jar", "jnlp",
        // Help files and troubleshooting packages that run scripts, both used to spread malware.
        "chm", "diagcab",
        // Excel add-ins, which Excel loads as native code.
        "xll",
        // Not listed: disk images (.iso, .vhdx) only mount; Office documents ask before enabling macros; .inf and
        // other text formats open in Notepad.
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether opening the file would run or install something. Works on the name alone.</summary>
    public static bool IsRunnable(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var name = path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
        // Windows ignores trailing dots and spaces, so "setup.exe. " opens as setup.exe.
        name = name.TrimEnd('.', ' ');
        // A name that is only an extension, such as ".exe", still opens by that extension.
        var extensionStart = name.LastIndexOf('.');
        return extensionStart >= 0 && RunnableExtensions.Contains(name[(extensionStart + 1)..]);
    }

    /// <summary>
    /// Whether the app should ask before opening this local item: it's runnable and not a folder (a folder
    /// named like a program just opens in File Explorer).
    /// </summary>
    public static bool NeedsConfirmation(string localPath) => IsRunnable(localPath) && !Directory.Exists(localPath);
}
