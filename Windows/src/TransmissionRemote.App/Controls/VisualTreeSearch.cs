using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace TransmissionRemote.App.Controls;

/// <summary>Finding elements above or below another in the visual tree.</summary>
internal static class VisualTreeSearch
{
    /// <summary>The element itself or the nearest one above it of the type, such as the list item a cell is in.</summary>
    public static T? FindAscendantOrSelf<T>(this DependencyObject element)
        where T : DependencyObject
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }
        return null;
    }

    /// <summary>The first element of the type below this one, depth first, such as a control's own ScrollViewer.</summary>
    public static T? FindDescendant<T>(this DependencyObject element)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);
            if (child is T match)
            {
                return match;
            }
            if (child.FindDescendant<T>() is { } deeper)
            {
                return deeper;
            }
        }
        return null;
    }
}
