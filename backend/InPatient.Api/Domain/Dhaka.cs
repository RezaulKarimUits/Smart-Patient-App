namespace InPatient.Api.Domain;

/// <summary>
/// Asia/Dhaka time helpers. Bangladesh is UTC+6 with no daylight saving, so a fixed offset is exact
/// and avoids depending on the host's time-zone database.
/// All persisted timestamps are UTC; these helpers are for clinical schedules and display.
/// </summary>
public static class Dhaka
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(6);

    public static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc + Offset, DateTimeKind.Unspecified);
    public static DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local - Offset, DateTimeKind.Utc);
    public static DateOnly Today(DateTime utcNow) => DateOnly.FromDateTime(ToLocal(utcNow));

    /// <summary>UTC start of the Dhaka calendar day containing <paramref name="utc"/>.</summary>
    public static DateTime StartOfDayUtc(DateTime utc) => ToUtc(ToLocal(utc).Date);

    public static string FormatTime(DateTime utc) => ToLocal(utc).ToString("HH:mm");
    public static string FormatDateTime(DateTime utc) => ToLocal(utc).ToString("dd MMM HH:mm");

    /// <summary>Morning 08-14, Evening 14-20, Night 20-08 (a night shift belongs to the date it starts on).</summary>
    public static (DateOnly Date, string Shift) CurrentShift(DateTime utcNow)
    {
        var local = ToLocal(utcNow);
        var date = DateOnly.FromDateTime(local);
        return local.Hour switch
        {
            < 8 => (date.AddDays(-1), ShiftTypes.Night),
            < 14 => (date, ShiftTypes.Morning),
            < 20 => (date, ShiftTypes.Evening),
            _ => (date, ShiftTypes.Night)
        };
    }

    /// <summary>UTC start/end of a given shift.</summary>
    public static (DateTime StartUtc, DateTime EndUtc) ShiftWindow(DateOnly date, string shift)
    {
        var day = date.ToDateTime(TimeOnly.MinValue);
        var (start, hours) = shift switch
        {
            ShiftTypes.Morning => (day.AddHours(8), 6),
            ShiftTypes.Evening => (day.AddHours(14), 6),
            _ => (day.AddHours(20), 12)
        };
        return (ToUtc(start), ToUtc(start.AddHours(hours)));
    }

    public static int AgeYears(DateOnly dob, DateTime atUtc)
    {
        var today = DateOnly.FromDateTime(ToLocal(atUtc));
        var age = today.Year - dob.Year;
        if (dob > today.AddYears(-age)) age--;
        return age;
    }
}
