using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TransmissionRemote.App.Startup;

/// <summary>What the app and the PC are, for the startup log line and the update check.</summary>
internal static class SystemInfo
{
    private const string WindowsVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    /// <summary>The first Windows build that is Windows 11; both report themselves as version 10.0.</summary>
    private const int FirstWindows11Build = 22000;

    /// <summary>The app's version, such as "1.3", as the update check compares it with release tags.</summary>
    public static string AppVersion => InformationalVersion.Split('+')[0];

    /// <summary>The version with the commit it was built from, such as "1.3.0+f10b614…", for logs.</summary>
    public static string InformationalVersion { get; } =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    /// <summary>For example "Windows 11 25H2, build 26300.7171".</summary>
    public static string WindowsDescription()
    {
        var version = Environment.OSVersion.Version;
        var name = version.Build >= FirstWindows11Build ? "Windows 11" : "Windows 10";
        using var key = Registry.LocalMachine.OpenSubKey(WindowsVersionKey);
        var release = key?.GetValue("DisplayVersion") as string;
        var revision = key?.GetValue("UBR") is int ubr ? $".{ubr}" : string.Empty;
        var releasePart = string.IsNullOrEmpty(release) ? string.Empty : $" {release}";
        return $"{name}{releasePart}, build {version.Build}{revision}";
    }

    /// <summary>"X64", or "Arm64 (X64 process)" when an x64 build runs emulated on an ARM64 PC.</summary>
    public static string Architecture()
    {
        var os = RuntimeInformation.OSArchitecture;
        var process = RuntimeInformation.ProcessArchitecture;
        return os == process ? $"{os}" : $"{os} ({process} process)";
    }
}
