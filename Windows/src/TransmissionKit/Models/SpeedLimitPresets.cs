using System.Globalization;
using System.Text.RegularExpressions;

namespace TransmissionKit;

/// <summary>The speeds offered in speed-limit menus, like Transmission Remote GUI's "Speed limit menu items".</summary>
public static partial class SpeedLimitPresets
{
    /// <summary>Transmission Remote GUI's default list, in kB/s. 0 stands for unlimited.</summary>
    public const string DefaultList = "0,10,25,50,100,250,500,750,1000,2500,5000,7000";

    /// <summary>
    /// The speeds in a comma-separated list, smallest first. Duplicates and anything that isn't a whole,
    /// non-negative number are dropped, so a typo never breaks the menu.
    /// </summary>
    public static IReadOnlyList<int> Parse(string text)
    {
        var speeds = new SortedSet<int>();
        foreach (var item in Separators().Split(text))
        {
            if (int.TryParse(item, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var speed)
                && speed >= 0)
            {
                speeds.Add(speed);
            }
        }
        return [.. speeds];
    }

    [GeneratedRegex(@"[,;\s]+")]
    private static partial Regex Separators();
}
