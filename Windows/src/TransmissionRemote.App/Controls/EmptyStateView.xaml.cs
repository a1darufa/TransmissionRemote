using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace TransmissionRemote.App.Controls;

/// <summary>A centred message in place of content: icon, title, description, details and buttons.</summary>
public sealed partial class EmptyStateView : UserControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(object), typeof(EmptyStateView), new PropertyMetadata(null));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyStateView), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(EmptyStateView), new PropertyMetadata(""));

    public static readonly DependencyProperty DetailsProperty = DependencyProperty.Register(
        nameof(Details), typeof(object), typeof(EmptyStateView), new PropertyMetadata(null));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(EmptyStateView), new PropertyMetadata(null));

    public EmptyStateView() => InitializeComponent();

    /// <summary>A 48 epx icon, secondary colour unless the icon sets its own.</summary>
    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Anything between the description and the buttons, such as a certificate's fingerprint.</summary>
    public object? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

    /// <summary>The buttons, usually a horizontal StackPanel with the main one in the accent style first.</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    /// <summary>Focuses the first enabled button, so the keyboard focus has somewhere to go when a page is replaced.</summary>
    public void FocusFirstAction(FocusState state)
    {
        var buttons = Actions switch
        {
            Button button => [button],
            Panel panel => panel.Children.OfType<Button>(),
            _ => [],
        };
        buttons.FirstOrDefault(button => button.IsEnabled)?.Focus(state);
    }

    /// <summary>Has Narrator read the title, for a message that just appeared in place of another.</summary>
    public void AnnounceTitle()
    {
        var peer = FrameworkElementAutomationPeer.FromElement(TitleText)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(TitleText);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
