using InPatient.Api.Domain;
using InPatient.Api.Services.Rules;

namespace InPatient.Tests;

public class DoseSchedulerTests
{
    // 2026-10-26 07:00 Asia/Dhaka (= 01:00 UTC): before the 08:00 slot.
    private static readonly DateTime Start = Dhaka.ToUtc(new DateTime(2026, 10, 26, 7, 0, 0));

    private static string[] Local(IEnumerable<DateTime> utc) => utc.Select(t => Dhaka.ToLocal(t).ToString("dd HH:mm")).ToArray();

    [Fact]
    public void OD_is_0800_daily()
    {
        var t = DoseScheduler.Schedule(Frequencies.OD, Start, 3);
        Assert.Equal(["26 08:00", "27 08:00", "28 08:00"], Local(t));
    }

    [Fact]
    public void OD_at_night_is_2000()
    {
        var t = DoseScheduler.Schedule(Frequencies.OD, Start, 2, "at night");
        Assert.Equal(["26 20:00", "27 20:00"], Local(t));
    }

    [Fact]
    public void BD_is_0800_and_2000()
    {
        var t = DoseScheduler.Schedule(Frequencies.BD, Start, 2);
        Assert.Equal(["26 08:00", "26 20:00", "27 08:00", "27 20:00"], Local(t));
    }

    [Fact]
    public void TDS_is_0800_1400_2000()
    {
        var t = DoseScheduler.Schedule(Frequencies.TDS, Start, 1);
        Assert.Equal(["26 08:00", "26 14:00", "26 20:00"], Local(t));
    }

    [Fact]
    public void QID_is_0600_1200_1800_2400()
    {
        var t = DoseScheduler.Schedule(Frequencies.QID, Dhaka.ToUtc(new DateTime(2026, 10, 26, 5, 0, 0)), 1);
        Assert.Equal(["26 06:00", "26 12:00", "26 18:00", "27 00:00"], Local(t));
    }

    [Fact]
    public void Fixed_times_skip_slots_before_start()
    {
        // Signed at 15:30 → first TDS dose is 20:00 today
        var t = DoseScheduler.Schedule(Frequencies.TDS, Dhaka.ToUtc(new DateTime(2026, 10, 26, 15, 30, 0)), 1);
        Assert.Equal(["26 20:00", "27 08:00", "27 14:00"], Local(t));
    }

    [Theory]
    [InlineData("Q4H", 4, 6)] [InlineData("Q6H", 6, 4)] [InlineData("Q8H", 8, 3)] [InlineData("Q12H", 12, 2)]
    public void QnH_every_n_hours_from_start(string freq, int hours, int perDay)
    {
        var start = Dhaka.ToUtc(new DateTime(2026, 10, 26, 15, 40, 0));
        var t = DoseScheduler.Schedule(freq, start, 1);
        Assert.Equal(perDay, t.Count);
        Assert.Equal(start, t[0]);
        for (var i = 1; i < t.Count; i++) Assert.Equal(TimeSpan.FromHours(hours), t[i] - t[i - 1]);
    }

    [Fact]
    public void Q12H_for_five_days_is_ten_doses()
        => Assert.Equal(10, DoseScheduler.Schedule(Frequencies.Q12H, Start, 5).Count);

    [Fact]
    public void STAT_is_one_dose_now()
    {
        var t = DoseScheduler.Schedule(Frequencies.STAT, Start, null);
        Assert.Equal([Start], t);
    }

    [Fact]
    public void PRN_has_no_schedule() => Assert.Empty(DoseScheduler.Schedule(Frequencies.PRN, Start, 5));

    [Fact]
    public void Open_ended_items_use_default_seven_days()
        => Assert.Equal(DoseScheduler.DefaultDurationDays, DoseScheduler.Schedule(Frequencies.OD, Start, null).Count);

    [Fact]
    public void Times_are_utc() => Assert.All(DoseScheduler.Schedule(Frequencies.BD, Start, 2), t => Assert.Equal(DateTimeKind.Utc, t.Kind));
}
