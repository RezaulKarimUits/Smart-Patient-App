using System.Threading.Channels;
using InPatient.Api.Data;
using InPatient.Api.Data.Entities;
using InPatient.Api.Domain;
using InPatient.Api.Hubs;
using InPatient.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Services;

/// <summary>A coded-rule result. The LLM never decides alerts; it only explains them afterwards.</summary>
public record RuleHit(string AlertType, string RuleCode, string Severity, string Detail, DateTime TriggerAt);

public record AlertDto(int Id, int AdmissionId, string PatientName, string? BedNumber, string? WardName, string Severity, string AlertType, string RuleCode,
    string RuleDetail, string? AiExplanation, string Status, int EscalationLevel, DateTime CreatedAt, DateTime? AckAt, string? AckByName, int? DoseTaskId);

/// <summary>Alerts waiting for an AI explanation (processed in the background so saving stays fast).</summary>
public sealed class AlertExplanationQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();
    public void Enqueue(int alertId) => _channel.Writer.TryWrite(alertId);
    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

public sealed class AlertService(InPatientDbContext db, IClock clock, ICurrentUser me, INotifier notifier, CareTeamService team,
    AlertExplanationQueue explain, ILogger<AlertService> log)
{
    public static readonly TimeSpan CriticalEscalateAfter = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan WarningEscalateAfter = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Raises or upgrades alerts for rule hits. De-duplication: an active (Open/Acknowledged) alert with the same rule
    /// absorbs the hit (upgraded if more severe); an alert already raised after the triggering observation is not repeated.
    /// </summary>
    public async Task<List<Alert>> RaiseAsync(Admission admission, IEnumerable<RuleHit> hits, int? doseTaskId = null, CancellationToken ct = default)
    {
        var raised = new List<Alert>();
        var hitList = hits.ToList();
        if (hitList.Count == 0) return raised;
        var existing = await db.Alerts.Where(a => a.AdmissionId == admission.Id).ToListAsync(ct);
        var now = clock.UtcNow;

        foreach (var hit in hitList)
        {
            var sameRule = existing.Where(a => SameRule(a, hit)).ToList();
            var active = sameRule.FirstOrDefault(a => AlertStatus.Active.Contains(a.Status));
            if (active is not null)
            {
                if (AlertSeverity.Rank(hit.Severity) > AlertSeverity.Rank(active.Severity))
                {
                    active.Severity = hit.Severity;
                    active.RuleCode = hit.RuleCode;
                    active.RuleDetail = hit.Detail;
                    active.Status = AlertStatus.Open;          // needs a fresh acknowledgement
                    active.AckAt = null; active.AckBy = null;
                    active.CreatedAt = now; active.EscalationLevel = 0; active.LastEscalatedAt = null;
                    active.AiExplanation = null;
                    raised.Add(active);
                }
                else if (active.RuleDetail != hit.Detail && hit.TriggerAt > active.CreatedAt)
                {
                    active.RuleDetail = hit.Detail; // keep the numbers current without re-alerting
                }
                continue;
            }
            if (sameRule.Any(a => a.CreatedAt >= hit.TriggerAt)) continue; // already alerted on this observation

            var alert = new Alert
            {
                HospitalId = admission.HospitalId, AdmissionId = admission.Id, Severity = hit.Severity, AlertType = hit.AlertType, RuleCode = hit.RuleCode,
                RuleDetail = hit.Detail, Status = AlertStatus.Open, EscalationLevel = 0, DoseTaskId = doseTaskId, CreatedAt = now
            };
            db.Alerts.Add(alert);
            existing.Add(alert);
            raised.Add(alert);
        }
        await db.SaveChangesAsync(ct);

        foreach (var a in raised)
        {
            log.LogInformation("Alert {AlertId} {Type}/{Rule} {Severity} raised for admission {AdmissionId}", a.Id, a.AlertType, a.RuleCode, a.Severity, a.AdmissionId);
            explain.Enqueue(a.Id);
            await NotifyAsync(a, escalated: false, ct);
        }
        return raised;
    }

    private static bool SameRule(Alert a, RuleHit h)
        => a.AlertType == h.AlertType && (h.AlertType == AlertTypes.News2 || a.RuleCode == h.RuleCode);

    /// <summary>Resolves active NEWS2 alerts when the score has settled (coded rule, not AI).</summary>
    public async Task AutoResolveNews2Async(Admission admission, CancellationToken ct = default)
    {
        var open = await db.Alerts.Where(a => a.AdmissionId == admission.Id && a.AlertType == AlertTypes.News2 && AlertStatus.Active.Contains(a.Status)).ToListAsync(ct);
        foreach (var a in open) { a.Status = AlertStatus.Resolved; a.ResolvedAt = clock.UtcNow; }
        if (open.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            foreach (var a in open) await notifier.ToHospitalAsync(a.HospitalId, HubEvents.AlertUpdated, new { alertId = a.Id, admissionId = a.AdmissionId, status = a.Status });
        }
    }

    /// <summary>
    /// Level 0: ward nurses for the bed (and the consultant for Critical alerts, so they see it at once).
    /// Level 1: consultant (escalated). Level 2: duty doctor placeholder — hospital admins + consultant.
    /// </summary>
    public async Task NotifyAsync(Alert alert, bool escalated, CancellationToken ct = default)
    {
        var dto = await ToDtoAsync(alert.Id, ct);
        if (dto is null) return;
        var admission = await db.Admissions.Where(a => a.Id == alert.AdmissionId).Select(a => new { a.ConsultantUserId, a.CurrentBedId }).FirstAsync(ct);
        var targets = new List<int>();
        if (alert.EscalationLevel == 0)
        {
            if (admission.CurrentBedId is not null) targets.AddRange(await team.NursesForBedAsync(admission.CurrentBedId.Value, ct));
            if (alert.Severity == AlertSeverity.Critical || alert.AlertType == AlertTypes.MissedDose) targets.Add(admission.ConsultantUserId);
        }
        else
        {
            targets.Add(admission.ConsultantUserId);
            if (admission.CurrentBedId is not null) targets.AddRange(await team.NursesForBedAsync(admission.CurrentBedId.Value, ct));
            if (alert.EscalationLevel >= 2)
                targets.AddRange(await db.AppUsers.Where(u => u.Role == Roles.HospitalAdmin && u.IsActive).Select(u => u.Id).ToListAsync(ct));
        }
        await notifier.ToUsersAsync(targets, escalated ? HubEvents.AlertUpdated : HubEvents.AlertRaised, new { alert = dto, escalated });
        await notifier.ToHospitalAsync(alert.HospitalId, HubEvents.AdmissionUpdated, new { admissionId = alert.AdmissionId });
    }

    // ---------- Escalation (called by AlertEscalationJob) ----------

    public async Task<int> EscalateDueAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var open = await db.Alerts.Where(a => a.Status == AlertStatus.Open && a.EscalationLevel < 2 && a.Severity != AlertSeverity.Info).ToListAsync(ct);
        var changed = new List<Alert>();
        foreach (var a in open)
        {
            var wait = a.Severity == AlertSeverity.Critical ? CriticalEscalateAfter : WarningEscalateAfter;
            var since = a.LastEscalatedAt ?? a.CreatedAt;
            if (now - since < wait) continue;
            a.EscalationLevel++;
            a.LastEscalatedAt = now;
            changed.Add(a);
        }
        if (changed.Count == 0) return 0;
        await db.SaveChangesAsync(ct);
        foreach (var a in changed)
        {
            log.LogWarning("Alert {AlertId} escalated to level {Level}{Duty}", a.Id, a.EscalationLevel, a.EscalationLevel == 2 ? " (duty doctor placeholder paged)" : "");
            await NotifyAsync(a, escalated: true, ct);
        }
        return changed.Count;
    }

    // ---------- Queries & actions ----------

    public async Task<List<AlertDto>> ListForMeAsync(bool activeOnly, int? admissionId, CancellationToken ct)
    {
        var q = db.Alerts.AsQueryable();
        if (admissionId is not null) q = q.Where(a => a.AdmissionId == admissionId);
        else if (me.Role == Roles.Consultant)
        {
            var uid = me.RequireUserId();
            var wards = await team.ConsultantWardIdsAsync(uid, ct);
            q = q.Where(a => a.Admission.ConsultantUserId == uid || (a.Admission.CurrentBed != null && wards.Contains(a.Admission.CurrentBed.WardId)));
        }
        else if (me.Role == Roles.Nurse)
        {
            var beds = await team.NurseBedIdsAsync(me.RequireUserId(), ct);
            q = q.Where(a => a.Admission.CurrentBedId != null && beds.Contains(a.Admission.CurrentBedId.Value));
        }
        if (activeOnly) q = q.Where(a => AlertStatus.Active.Contains(a.Status) && a.Admission.Status == AdmissionStatus.Active);
        else q = q.Where(a => a.CreatedAt >= clock.UtcNow.AddDays(-7));
        var list = await Project(q).Take(300).ToListAsync(ct);
        return list.OrderByDescending(a => a.Status == AlertStatus.Open).ThenByDescending(a => AlertSeverity.Rank(a.Severity)).ThenByDescending(a => a.CreatedAt).ToList();
    }

    public async Task<AlertDto?> ToDtoAsync(int alertId, CancellationToken ct = default)
        => await Project(db.Alerts.Where(a => a.Id == alertId)).FirstOrDefaultAsync(ct);

    private static IQueryable<AlertDto> Project(IQueryable<Alert> q) => q.Select(a => new AlertDto(a.Id, a.AdmissionId, a.Admission.Patient.Name,
        a.Admission.CurrentBed != null ? a.Admission.CurrentBed.BedNumber : null, a.Admission.CurrentBed != null ? a.Admission.CurrentBed.Ward.Name : null,
        a.Severity, a.AlertType, a.RuleCode, a.RuleDetail, a.AiExplanation, a.Status, a.EscalationLevel, a.CreatedAt, a.AckAt,
        a.AckByNavigation != null ? a.AckByNavigation.Name : null, a.DoseTaskId));

    public async Task<AlertDto> AcknowledgeAsync(int alertId, CancellationToken ct)
    {
        var a = await db.Alerts.FirstOrDefaultAsync(x => x.Id == alertId, ct) ?? throw ApiException.NotFound("Alert not found");
        if (a.Status == AlertStatus.Resolved) throw ApiException.Conflict("Alert is already resolved");
        if (a.Status == AlertStatus.Open)
        {
            a.Status = AlertStatus.Acknowledged;
            a.AckAt = clock.UtcNow;
            a.AckBy = me.RequireUserId();
            await db.SaveChangesAsync(ct);
            await notifier.ToHospitalAsync(a.HospitalId, HubEvents.AlertUpdated, new { alertId = a.Id, admissionId = a.AdmissionId, status = a.Status });
        }
        return (await ToDtoAsync(a.Id, ct))!;
    }

    public async Task<AlertDto> ResolveAsync(int alertId, CancellationToken ct)
    {
        var a = await db.Alerts.FirstOrDefaultAsync(x => x.Id == alertId, ct) ?? throw ApiException.NotFound("Alert not found");
        if (a.Status != AlertStatus.Resolved)
        {
            if (a.AckAt is null) { a.AckAt = clock.UtcNow; a.AckBy = me.RequireUserId(); }
            a.Status = AlertStatus.Resolved;
            a.ResolvedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            await notifier.ToHospitalAsync(a.HospitalId, HubEvents.AlertUpdated, new { alertId = a.Id, admissionId = a.AdmissionId, status = a.Status });
        }
        return (await ToDtoAsync(a.Id, ct))!;
    }
}
