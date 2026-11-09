using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using TransmissionRemote.App.Controls;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Views;

/// <summary>The page shown instead of the torrent list while not connected.</summary>
public sealed partial class ConnectionStateView : UserControl
{
    private MainViewModel? _viewModel;

    public ConnectionStateView() => InitializeComponent();

    /// <summary>Set once, before the view loads.</summary>
    public MainViewModel ViewModel
    {
        get => _viewModel ?? throw new InvalidOperationException("Set ViewModel before the view loads.");
        set
        {
            _viewModel = value;
            value.ConnectionState.PropertyChanged += OnConnectionStateChanged;
            ShowPage(value.ConnectionState.Page);
        }
    }

    private void OnConnectionStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionStateViewModel.Page))
        {
            ShowPage(ViewModel.ConnectionState.Page);
        }
    }

    private void ShowPage(ConnectionPage page)
    {
        UIElement? visible = page switch
        {
            ConnectionPage.Connecting => ConnectingPage,
            ConnectionPage.Reconnecting => ReconnectingPage,
            ConnectionPage.UntrustedCertificate => UntrustedCertificatePage,
            ConnectionPage.UnencryptedConnectionBlocked => UnencryptedConnectionBlockedPage,
            ConnectionPage.CouldNotConnect => CouldNotConnectPage,
            ConnectionPage.NoServer => NoServerPage,
            ConnectionPage.NotConnected => NotConnectedPage,
            _ => null,
        };
        UIElement[] pages =
        [
            ConnectingPage, ReconnectingPage, UntrustedCertificatePage, UnencryptedConnectionBlockedPage,
            CouldNotConnectPage, NoServerPage, NotConnectedPage,
        ];
        foreach (var candidate in pages)
        {
            candidate.Visibility = candidate == visible ? Visibility.Visible : Visibility.Collapsed;
        }
        Announce(visible);
    }

    /// <summary>Narrator reads the new page's title, since the page changes without the user doing anything.</summary>
    private void Announce(UIElement? page)
    {
        switch (page)
        {
            case EmptyStateView emptyState:
                emptyState.AnnounceTitle();
                break;
            case not null when page == ConnectingPage:
                var peer = FrameworkElementAutomationPeer.FromElement(ConnectingText)
                    ?? FrameworkElementAutomationPeer.CreatePeerForElement(ConnectingText);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                break;
        }
    }

    private void OnTryNow(object sender, RoutedEventArgs e) => ViewModel.RetryNowCommand.Execute(null);
}
