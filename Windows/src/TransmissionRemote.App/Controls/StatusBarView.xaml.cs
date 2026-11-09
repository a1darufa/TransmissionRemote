using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TransmissionKit;
using TransmissionRemote.App.Presentation;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Controls;

/// <summary>
/// Shows <see cref="StatusBarViewModel"/>. It's updated from code on every change: several values are missing until
/// the server reports them, and x:Bind skips functions whose arguments are null.
/// </summary>
public sealed partial class StatusBarView : UserControl
{
    private MainViewModel? _viewModel;
    private DisplayFormatter? _format;

    public StatusBarView() => InitializeComponent();

    private StatusBarViewModel Model => _viewModel!.StatusBar;

    /// <summary>Set once, before the view loads.</summary>
    internal void Bind(MainViewModel viewModel, DisplayFormatter format)
    {
        _viewModel = viewModel;
        _format = format;
        viewModel.StatusBar.PropertyChanged += OnModelChanged;
        Refresh();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (_viewModel is null || _format is null)
        {
            return;
        }
        var model = Model;
        var count = model.TorrentCount;
        TorrentCountText.Text = StringResources.Format(Strings.TorrentsFor(count), count.ToString("N0", CultureInfo.CurrentCulture));
        if (model.Security is { } security)
        {
            SecurityGlyph.Glyph = StatusVisuals.Glyph(security);
            SecurityGlyph.Foreground = StatusVisuals.Brush(security);
            ToolTipService.SetToolTip(ConnectionInfo, ConnectionToolTip(model, security));
        }

        ProblemPanel.Visibility = model.PollingProblem is null ? Visibility.Collapsed : Visibility.Visible;
        ProblemText.Text = ErrorMessages.For(model.PollingProblem);
        UpdatedText.Text = model.DataAge > TimeSpan.Zero ? StringResources.Format(Strings.Updated, _format.Ago(model.DataAge)) : "";

        DownloadText.Text = _format.Speed(model.DownloadSpeed);
        DownloadLimitText.Text = LimitText(model.DownloadLimit);
        UploadText.Text = _format.Speed(model.UploadSpeed);
        UploadLimitText.Text = LimitText(model.UploadLimit);

        FreeSpacePanel.Visibility = model.FreeSpace is null ? Visibility.Collapsed : Visibility.Visible;
        if (model.FreeSpace is { } free)
        {
            FreeSpaceText.Text = StringResources.Format(Strings.Free, _format.Bytes(free.SizeBytes));
            ToolTipService.SetToolTip(FreeSpacePanel, free.Path);
        }

        TurtleButton.IsChecked = model.IsAlternativeSpeedEnabled;
        ToolTipService.SetToolTip(
            TurtleButton,
            model.IsAlternativeSpeedEnabled ? Strings.AlternativeSpeedLimitsAreOn : Strings.TurnOnAlternativeSpeedLimits);
    }

    /// <summary>"(500 KB/s)" after the speed, the limit in force; nothing without one.</summary>
    private string LimitText(int? limit) =>
        limit is { } kilobytes ? StringResources.Format(Strings.SpeedLimitInForce, _format!.SpeedLimit(kilobytes)) : "";

    /// <summary>The security summary, then the server: "Transmission 4.0.5 · legacy RPC", the Mac's subtitle.</summary>
    private static string ConnectionToolTip(StatusBarViewModel model, TransportSecurity security)
    {
        var summary = StatusVisuals.Summary(security);
        if (model.ServerVersion.Length == 0 || model.Dialect is not { } dialect)
        {
            return summary;
        }
        var protocol = dialect == RpcDialect.JsonRpc ? "JSON-RPC" : Strings.LegacyRPC_FB73DE;
        return $"{summary}\nTransmission {model.ServerVersion} · {protocol}";
    }

    private void OnTryNow(object sender, RoutedEventArgs e) => _viewModel?.RetryNowCommand.Execute(null);

    private void OnTurtleClick(object sender, RoutedEventArgs e) => _viewModel?.ToggleAlternativeSpeedCommand.Execute(null);
}
