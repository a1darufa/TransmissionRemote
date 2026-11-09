using System.Globalization;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Presentation;

/// <summary>The app's formatter: the user's regional format with unit names in the display language.</summary>
internal static class AppFormatting
{
    public static DisplayFormatter CreateFormatter() => new(
        CultureInfo.CurrentCulture,
        new ByteUnitLabels
        {
            Bytes = Strings.BytesSizeUnit,
            Kilobytes = Strings.KBSizeUnit,
            Megabytes = Strings.MBSizeUnit,
            Gigabytes = Strings.GBSizeUnit,
            Terabytes = Strings.TBSizeUnit,
            PerSecond = Strings.PerSecondSpeedUnitSuffix,
        },
        new DurationUnitLabels
        {
            Days = Strings.DaysNarrow,
            Hours = Strings.HoursNarrow,
            Minutes = Strings.MinutesNarrow,
            Seconds = Strings.SecondsNarrow,
            Separator = Strings.BetweenDurationUnits,
            Ago = Strings.TimeAgo,
        });
}
