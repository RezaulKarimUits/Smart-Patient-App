using InPatient.Api.Data;
using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Services;

/// <summary>Ward history (bed events + daily stats from vw_WardHistory) and the per-admission ward journey.</summary>
public sealed class WardHistoryService(InPatientDbContext db, IClock clock, AdmissionService admissions)
{
    public async Task<WardHistoryDto> GetAsync(int wardId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var ward = await db.Wards.FirstOrDefaultAsync(w => w.Id == wardId, ct) ?? throw ApiException.NotFound("Ward not found");
        var today = Dhaka.Today(clock.UtcNow);
        var end = to ?? today;
        var start = from ?? end.AddDays(-6);
        if (start > end) throw ApiException.BadRequest("'from' must be before 'to'");
        if (end.DayNumber - start.DayNumber > 366) throw ApiException.BadRequest("Range is limited to one year");

        var days = await db.VwWardHistories.Where(h => h.WardId == wardId && h.Day >= start && h.Day <= end)
            .OrderBy(h => h.Day).ToListAsync(ct);
        var dayDtos = days.Select(d => new WardHistoryDayDto(d.Day!.Value, d.AdmissionsIn ?? 0, d.TransfersIn ?? 0, d.TransfersOut ?? 0, d.Discharges ?? 0,
            d.OccupiedBedDays ?? 0, d.AvgLengthOfStayDays)).ToList();

        var fromUtc = Dhaka.ToUtc(start.ToDateTime(TimeOnly.MinValue));
        var toUtc = Dhaka.ToUtc(end.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var events = await db.BedEvents.Where(e => e.Bed.WardId == wardId && e.At >= fromUtc && e.At < toUtc)
            .OrderByDescending(e => e.At).Take(500)
            .Select(e => new BedEventDto(e.Id, e.BedId, e.Bed.BedNumber, e.Bed.WardId, e.Bed.Ward.Name, e.EventType, e.At, e.Note,
                e.AdmissionId, e.Admission != null ? e.Admission.Patient.Name : null, e.ByUser != null ? e.ByUser.Name : null))
            .ToListAsync(ct);

        var occ = await db.VwWardOccupancyNows.FirstOrDefaultAsync(o => o.WardId == wardId, ct);
        var occNow = occ is null ? new WardOccupancyDto(0, 0, 0, 0, 0, 0, 0)
            : new WardOccupancyDto(occ.TotalBeds ?? 0, occ.FreeBeds ?? 0, occ.ReservedBeds ?? 0, occ.OccupiedBeds ?? 0, occ.CleaningBeds ?? 0, occ.BlockedBeds ?? 0, occ.OccupancyPct ?? 0);

        var los = dayDtos.Where(d => d.AvgLengthOfStayDays != null).ToList();
        var totalBedDays = dayDtos.Sum(d => d.OccupiedBedDays);
        var nDays = Math.Max(1, end.DayNumber - start.DayNumber + 1);
        var totals = new WardHistoryTotalsDto(
            dayDtos.Sum(d => d.AdmissionsIn), dayDtos.Sum(d => d.TransfersIn), dayDtos.Sum(d => d.TransfersOut), dayDtos.Sum(d => d.Discharges),
            totalBedDays,
            los.Count == 0 ? null : Math.Round(los.Sum(d => d.AvgLengthOfStayDays!.Value * d.Discharges) / Math.Max(1, los.Sum(d => d.Discharges)), 2),
            occNow.TotalBeds == 0 ? 0 : Math.Round(100m * totalBedDays / (occNow.TotalBeds * nDays), 1));

        return new WardHistoryDto(ward.Id, ward.Name, start, end, occNow.TotalBeds, occNow, totals, dayDtos, events);
    }

    public async Task<PatientJourneyDto> GetJourneyAsync(int admissionId, CancellationToken ct)
    {
        var a = await admissions.GetAccessibleAsync(admissionId, ct);
        var stays = await db.VwPatientWardJourneys.Where(j => j.AdmissionId == admissionId).OrderBy(j => j.StartAt).ToListAsync(ct);
        var patient = await db.Patients.Where(p => p.Id == a.PatientId).Select(p => new { p.Name, p.Mrn }).FirstAsync(ct);
        return new PatientJourneyDto(a.Id, patient.Name, patient.Mrn, a.Status,
            stays.Select(s => new JourneyStayDto(s.StayId, (int)(s.Sequence ?? 0), s.WardId, s.WardName, s.BedId, s.BedNumber, s.Reason, s.StartAt, s.EndAt, s.DurationHours ?? 0)).ToList());
    }
}
