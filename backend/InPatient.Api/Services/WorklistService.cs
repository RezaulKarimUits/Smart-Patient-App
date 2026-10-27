using InPatient.Api.Data;
using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Services;

/// <summary>Role home screens: nurse "My beds" and the consultant priority list.</summary>
public sealed class WorklistService(InPatientDbContext db, IClock clock, ICurrentUser me, CareTeamService team, BedService beds,
    AdmissionService admissions, AlertService alerts)
{
    public async Task<NurseHomeDto> NurseHomeAsync(CancellationToken ct)
    {
        var uid = me.RequireUserId();
        var (date, shift) = team.CurrentShift();
        var (start, end) = Dhaka.ShiftWindow(date, shift);
        var bedIds = await team.NurseBedIdsAsync(uid, ct);
        var tiles = await beds.GetTilesAsync(db.Beds.Where(b => bedIds.Contains(b.Id)), ct);
        var now = clock.UtcNow;
        var admIds = tiles.Where(t => t.Occupant != null).Select(t => t.Occupant!.AdmissionId).ToList();

        var doses = await db.DoseTasks.Where(t => admIds.Contains(t.AdmissionId) && (DoseStatus.Open.Contains(t.Status)) && t.DueAt <= now.AddHours(12))
            .Select(t => new { t.AdmissionId, t.DueAt, t.Status }).ToListAsync(ct);
        var careTasks = await db.CareTasks.Where(t => admIds.Contains(t.AdmissionId) && t.Status == CareTaskStatus.Open).ToListAsync(ct);
        var lastVitals = await db.VitalSigns.Where(v => admIds.Contains(v.AdmissionId)).GroupBy(v => v.AdmissionId)
            .Select(g => new { AdmissionId = g.Key, At = g.Max(v => v.RecordedAt) }).ToDictionaryAsync(x => x.AdmissionId, x => x.At, ct);

        var list = tiles.Select(t =>
        {
            var aid = t.Occupant?.AdmissionId;
            var d = aid is null ? [] : doses.Where(x => x.AdmissionId == aid).ToList();
            return new NurseBedDto(t.Id, t.BedNumber, t.WardId, t.WardName, t.Status, t.Occupant,
                d.Count(x => x.DueAt <= now.AddMinutes(15)), d.Count(x => x.DueAt < now), d.Where(x => x.DueAt >= now).Select(x => (DateTime?)x.DueAt).Min(),
                t.Occupant?.ActiveAlerts ?? 0,
                careTasks.Where(c => c.AdmissionId == aid).Select(c => new CareTaskDto(c.Id, c.AdmissionId, c.TaskType, c.Detail, c.DueAt, c.Status, c.DueAt < now)).ToList(),
                aid is not null && lastVitals.TryGetValue(aid.Value, out var lv) ? lv : null);
        })
        // Critical first, then Watch, then beds needing action, then bed number.
        .OrderBy(b => b.Occupant?.Flag switch { PatientFlag.Critical => 0, PatientFlag.Watch => 1, null => 3, _ => 2 })
        .ThenByDescending(b => b.OverdueDoses + b.DueDoses)
        .ThenBy(b => b.BedNumber)
        .ToList();
        return new NurseHomeDto(date.ToString("yyyy-MM-dd"), shift, start, end, list);
    }

    /// <summary>Open critical alerts first, then NEWS2 (highest first), then trend alerts.</summary>
    public async Task<List<PriorityPatientDto>> ConsultantPriorityAsync(CancellationToken ct)
    {
        var uid = me.RequireUserId();
        var wards = await team.ConsultantWardIdsAsync(uid, ct);
        var q = db.Admissions.Where(a => a.Status == AdmissionStatus.Active &&
                                         (a.ConsultantUserId == uid || (a.CurrentBed != null && wards.Contains(a.CurrentBed.WardId))));
        var headers = await admissions.GetHeadersAsync(q, ct);
        var ids = headers.Select(h => h.Id).ToList();
        var active = (await alerts.ListForMeAsync(true, null, ct)).Where(a => ids.Contains(a.AdmissionId)).ToList();
        var pendingCounts = await PendingCountsAsync(ids, ct);

        var ranked = headers.Select(h =>
        {
            var al = active.Where(a => a.AdmissionId == h.Id).ToList();
            var openCritical = al.Any(a => a.Severity == AlertSeverity.Critical && a.Status == AlertStatus.Open);
            var anyCritical = al.Any(a => a.Severity == AlertSeverity.Critical);
            var trend = al.Count(a => a.AlertType == AlertTypes.Trend);
            var reason = openCritical ? "Open critical alert"
                : h.News2Score >= 5 ? $"NEWS2 {h.News2Score}"
                : trend > 0 ? "Worsening trend"
                : al.Count > 0 ? al[0].AlertType == AlertTypes.MissedDose ? "Missed dose" : "Alert"
                : "Stable";
            return new { h, al, openCritical, anyCritical, trend, reason, mine = h.ConsultantUserId == uid };
        })
        .OrderByDescending(x => x.openCritical)
        .ThenByDescending(x => x.anyCritical)
        // then alerting NEWS2 (≥5 or a single 3), highest first
        .ThenByDescending(x => x.h.News2Score >= 5 || x.h.News2Band == News2Bands.LowMedium ? x.h.News2Score ?? 0 : -1)
        .ThenByDescending(x => x.trend)
        .ThenByDescending(x => x.al.Count)
        .ThenByDescending(x => x.h.News2Score ?? -1)
        .ThenByDescending(x => x.mine)
        .ThenBy(x => x.h.BedNumber)
        .ToList();
        return ranked.Select((x, i) => new PriorityPatientDto(x.h, x.al, pendingCounts.GetValueOrDefault(x.h.Id), i + 1, x.reason)).ToList();
    }

    private async Task<Dictionary<int, int>> PendingCountsAsync(List<int> ids, CancellationToken ct)
    {
        var v = await db.VitalSigns.Where(x => ids.Contains(x.AdmissionId) && x.SignedOffAt == null).GroupBy(x => x.AdmissionId).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct);
        var l = await db.LabResults.Where(x => ids.Contains(x.AdmissionId) && x.SignedOffAt == null).GroupBy(x => x.AdmissionId).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct);
        var n = await db.ClinicalNotes.Where(x => ids.Contains(x.AdmissionId) && x.SignedOffAt == null).GroupBy(x => x.AdmissionId).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct);
        return v.Concat(l).Concat(n).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Sum(x => x.C));
    }

    public async Task<CareTaskDto> CompleteCareTaskAsync(int id, CancellationToken ct)
    {
        var t = await db.CareTasks.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiException.NotFound("Task not found");
        if (t.Status == CareTaskStatus.Open) { t.Status = CareTaskStatus.Done; t.DoneAt = clock.UtcNow; t.DoneBy = me.RequireUserId(); await db.SaveChangesAsync(ct); }
        return new CareTaskDto(t.Id, t.AdmissionId, t.TaskType, t.Detail, t.DueAt, t.Status, false);
    }
}
