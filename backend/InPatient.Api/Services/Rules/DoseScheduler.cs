using InPatient.Api.Domain;
using InPatient.Api.Infrastructure;

namespace InPatient.Api.Services.Rules;

/// <summary>
/// Pure dose-time generation in Asia/Dhaka time (same rules as the seed in 002_seed.sql):
/// OD 08:00 (20:00 if the instructions say "night"); BD 08:00, 20:00; TDS 08:00, 14:00, 20:00;
/// QID 06:00, 12:00, 18:00, 24:00; QnH every n hours from start; STAT one dose at start; PRN no schedule.
/// Only times in [start, start + duration) are produced; open-ended items are scheduled for 7 days.
/// </summary>
public static class DoseScheduler
{
    public const int DefaultDurationDays = 7;

    public static IReadOnlyList<DateTime> Schedule(string frequency, DateTime startUtc, int? durationDays, string? instructions = null, DateTime? notAfterUtc = null)
    {
        var days = durationDays is > 0 ? durationDays.Value : DefaultDurationDays;
        var endUtc = startUtc.AddDays(days);
        if (notAfterUtc is not null && notAfterUtc < endUtc) endUtc = notAfterUtc.Value;

        var times = new List<DateTime>();
        switch (frequency)
        {
            case Frequencies.PRN:
                break;
            case Frequencies.STAT:
                times.Add(startUtc);
                break;
            case Frequencies.Q4H or Frequencies.Q6H or Frequencies.Q8H or Frequencies.Q12H:
                var step = IntervalHours(frequency);
                for (var t = startUtc; t < endUtc; t = t.AddHours(step)) times.Add(t);
                break;
            default:
                var clock = ClockTimes(frequency, instructions);
                var firstLocalDay = Dhaka.ToLocal(startUtc).Date;
                for (var d = 0; d <= days + 1; d++)
                {
                    foreach (var minutes in clock)
                    {
                        var due = Dhaka.ToUtc(firstLocalDay.AddDays(d).AddMinutes(minutes));
                        if (due >= startUtc && due < endUtc) times.Add(due);
                    }
                }
                break;
        }
        return times.Select(t => DateTime.SpecifyKind(t, DateTimeKind.Utc)).OrderBy(t => t).ToList();
    }

    public static int IntervalHours(string frequency) => frequency switch
    {
        Frequencies.Q4H => 4,
        Frequencies.Q6H => 6,
        Frequencies.Q8H => 8,
        Frequencies.Q12H => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(frequency))
    };

    /// <summary>Fixed clock times as minutes after Dhaka midnight (1440 = 24:00, i.e. next midnight).</summary>
    public static int[] ClockTimes(string frequency, string? instructions = null) => frequency switch
    {
        Frequencies.OD when instructions?.Contains("night", StringComparison.OrdinalIgnoreCase) == true => [1200],
        Frequencies.OD => [480],
        Frequencies.BD => [480, 1200],
        Frequencies.TDS => [480, 840, 1200],
        Frequencies.QID => [360, 720, 1080, 1440],
        _ => throw ApiException.BadRequest($"Unknown frequency '{frequency}'")
    };
}

public static class WristbandGuard
{
    /// <summary>Recording a dose requires scanning the patient's wristband; any other token is "Wrong patient".</summary>
    public static void EnsureMatches(string expectedToken, string? scannedToken)
    {
        if (string.IsNullOrWhiteSpace(scannedToken)) throw ApiException.BadRequest("Scan the patient's wristband first");
        var scanned = BedService.NormalizeToken(scannedToken);
        if (!string.Equals(expectedToken, scanned, StringComparison.OrdinalIgnoreCase))
            throw new ApiException(StatusCodes.Status409Conflict, "Wrong patient");
    }
}
