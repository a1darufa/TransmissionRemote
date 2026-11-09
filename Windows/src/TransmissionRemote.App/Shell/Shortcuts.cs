using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TransmissionRemote.ViewModels;
using Windows.System;

namespace TransmissionRemote.App.Shell;

/// <summary>
/// Turns the shortcut table (<see cref="KeyboardShortcuts"/>) into WinUI keyboard accelerators. Each command's keys
/// are declared once, on its menu item, so they work while the menu is closed and the menu shows the first one
/// in the user's language.
/// </summary>
internal static class Shortcuts
{
    /// <summary>
    /// Gives a menu item or button all of its command's keys. Keys a text field uses (Delete, Ctrl+C…) only work
    /// while the focus is inside <paramref name="listScope"/>, the torrent list.
    /// </summary>
    public static void Attach(UIElement element, AppCommand command, UIElement listScope)
    {
        foreach (var chord in KeyboardShortcuts.For(command).All)
        {
            element.KeyboardAccelerators.Add(Accelerator(chord, listScope));
        }
        // A menu item shows its key next to its name; without this, WinUI repeats it in a tooltip.
        if (element is MenuFlyoutItemBase)
        {
            element.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        }
    }

    /// <summary>Keys with no menu item to show them, such as Ctrl+F for the filter box, on an element of the window.</summary>
    public static void AttachHidden(UIElement owner, AppCommand command, Action action)
    {
        foreach (var chord in KeyboardShortcuts.For(command).All)
        {
            var accelerator = Accelerator(chord, listScope: null);
            accelerator.Invoked += (_, args) =>
            {
                action();
                args.Handled = true;
            };
            owner.KeyboardAccelerators.Add(accelerator);
        }
    }

    private static KeyboardAccelerator Accelerator(KeyChord chord, UIElement? listScope) => new()
    {
        // The table uses Windows' own key and modifier values.
        Key = (VirtualKey)(int)chord.Key,
        Modifiers = (VirtualKeyModifiers)(int)chord.Modifiers,
        ScopeOwner = chord.IsListOnly ? listScope : null,
    };
}
