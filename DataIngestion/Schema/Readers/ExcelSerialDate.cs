using System.Globalization;

namespace DataIngestion.Schema.Readers;

/// <summary>
/// Excel's date serials as text a date column can be inferred from.
/// </summary>
/// <remarks>
/// Excel stores 15 March 2026 as the number 46096. Imported raw, every date column in every
/// workbook types as <c>int</c> and the user's dates become five-digit numbers — the silent
/// corruption <see cref="Schema.ColumnTypeInference"/> exists to prevent. The formats emitted here
/// are all in that class's <c>DateFormats</c> list, so a converted column types as
/// <c>DateTime</c> and round-trips.
/// </remarks>
public static class ExcelSerialDate
{
    private const double SecondsPerDay = 86_400d;

    /// <summary>
    /// The serial as <c>yyyy-MM-dd</c>, or <c>yyyy-MM-dd HH:mm:ss</c> when it carries a time, or
    /// <c>HH:mm:ss</c> when it carries only a time. Null when no date can hold it, which tells the
    /// caller to keep the number exactly as the file stored it.
    /// </summary>
    public static string? ToText(double serial, bool date1904)
    {
        // Below 1 in the 1900 system there is no date part at all: the value is a time of day.
        // Emitted as a time it stays a string, which the review loop can retype with `field`;
        // emitted as a date it would claim a day the file never mentioned.
        if (!date1904 && serial > 0 && serial < 1)
            return TimeSpan.FromSeconds(Math.Round(serial * SecondsPerDay))
                .ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

        var date = ToDateTime(serial, date1904);

        if (date is null)
            return null;

        return date.Value.TimeOfDay == TimeSpan.Zero
            ? date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : date.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A duration in days as total hours, <c>h:mm:ss</c>, the way an elapsed-time format
    /// (<c>[h]:mm</c>) shows it: 1.5 is "36:00:00", 0.5 is "12:00:00". Never a date. Null when
    /// the number is too large to be a duration, which keeps the stored number.
    /// </summary>
    public static string? ToElapsedText(double days)
    {
        if (double.IsNaN(days) || double.IsInfinity(days) || Math.Abs(days) > 1e9)
            return null;

        var totalSeconds = (long)Math.Round(Math.Abs(days) * SecondsPerDay);
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds / 60 % 60;
        var seconds = totalSeconds % 60;
        var sign = days < 0 && totalSeconds > 0 ? "-" : "";

        return string.Create(CultureInfo.InvariantCulture, $"{sign}{hours}:{minutes:00}:{seconds:00}");
    }

    /// <summary>The serial as a <see cref="DateTime"/>, or null when it names no real date.</summary>
    public static DateTime? ToDateTime(double serial, bool date1904)
    {
        if (double.IsNaN(serial) || double.IsInfinity(serial))
            return null;

        // The 1904 system counts from 1904-01-01 as serial 0. The 1900 system counts from serial 1
        // as 1900-01-01 AND contains a 29 February 1900 that never existed, so every serial from
        // 60 up is one day ahead of the real calendar and 60 itself is the phantom day.
        var (epoch, days) = date1904
            ? (new DateTime(1904, 1, 1), serial)
            : (new DateTime(1899, 12, 31), serial >= 60 ? serial - 1 : serial);

        if (days < 0)
            return null;

        var whole = Math.Floor(days);
        var seconds = (int)Math.Round((days - whole) * SecondsPerDay);

        // Rounding up through midnight rolls into the next day rather than reading 24:00:00.
        if (seconds >= SecondsPerDay)
        {
            whole++;
            seconds -= (int)SecondsPerDay;
        }

        try
        {
            return epoch.AddDays(whole).AddSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Past DateTime.MaxValue. Bounding by a magic serial would restate the same limit in a
            // second place, where it could disagree.
            return null;
        }
    }
}
