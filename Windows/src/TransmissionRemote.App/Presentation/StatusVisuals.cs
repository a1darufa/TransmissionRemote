using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TransmissionKit;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Presentation;

/// <summary>Glyphs and theme brushes for torrent states and connection security (brief 4.3, 5.2, 6.3, 6.4).</summary>
internal static class StatusVisuals
{
    public static string Glyph(TorrentStatusIcon icon) => icon switch
    {
        TorrentStatusIcon.Warning => "",
        TorrentStatusIcon.Finished => "",
        TorrentStatusIcon.Paused => "",
        TorrentStatusIcon.Queued => "",
        TorrentStatusIcon.Verifying => "",
        TorrentStatusIcon.Downloading => "",
        _ => "",
    };

    /// <summary>Theme brushes, so contrast themes and light/dark follow; never a fixed colour.</summary>
    public static Brush Brush(TorrentStatusTone tone) => Resource(tone switch
    {
        TorrentStatusTone.Warning => "SystemFillColorCautionBrush",
        TorrentStatusTone.Downloading => "StatusDownloadingBrush",
        TorrentStatusTone.Seeding => "SystemFillColorSuccessBrush",
        TorrentStatusTone.Verifying => "StatusVerifyingBrush",
        _ => "TextFillColorSecondaryBrush",
    });

    public static string Glyph(TransportSecurity security) => security switch
    {
        TransportSecurity.Tls => "",
        TransportSecurity.Tailscale => "",
        TransportSecurity.UnencryptedInternet => "",
        _ => "",
    };

    public static Brush Brush(TransportSecurity security) => Resource(security switch
    {
        TransportSecurity.Tls or TransportSecurity.Tailscale => "SystemFillColorSuccessBrush",
        TransportSecurity.UnencryptedInternet => "SystemFillColorCriticalBrush",
        _ => "TextFillColorSecondaryBrush",
    });

    /// <summary>The security summaries, the Mac's exact sentences with "this PC" for this computer.</summary>
    public static string Summary(TransportSecurity security) => security switch
    {
        TransportSecurity.Tls => Strings.EncryptedWithHTTPS,
        TransportSecurity.Tailscale => Strings.PlainHTTPInsideATailscaleTunnelWhich,
        TransportSecurity.Loopback => Strings.ConnectionToThisPC,
        TransportSecurity.LocalNetwork => Strings.PlainHTTPOnAPrivateNetworkDevicesOnThe,
        _ => Strings.PlainHTTPOverTheInternetThePasswordAnd,
    };

    private static Brush Resource(string key) => (Brush)Application.Current.Resources[key];
}
