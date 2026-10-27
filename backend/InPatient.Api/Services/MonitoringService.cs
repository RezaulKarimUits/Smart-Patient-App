using InPatient.Api.Data;
using InPatient.Api.Data.Entities;
using InPatient.Api.Domain;
using InPatient.Api.Hubs;
using InPatient.Api.Infrastructure;
using InPatient.Api.Services.Rules;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Services;

public record MonitoringResult(int? News2Score, string? News2Band, string Flag, IReadOnlyList<AlertDto> NewAlerts, string? VitalsSuggestion);

/// <summary>Runs the coded monitoring rules for one admission after any clinical entry is confirmed.</summary>
public sealed class MonitoringService(InPatientDbContext db, IClock clock, AlertService alerts, PatientFlagService flags, CareTeamService team,
    INotifier notifier)
{
    public static readonly TimeSpan CriticalRecheck = TimeSpan.FromHours(1);
    public static readonly TimeSpan WatchRecheck = TimeSpan.FromHours(4);

    public async Task<MonitoringResult> RunAsync(int admissionId, CancellationToken ct = default)
    {
        var admission = await db.Admissions.Include(a => a.Patient).FirstAsync(a => a.Id == admissionId, ct);
        var now = clock.UtcNow;
        var since = now.AddHours(-96);
        var adult = News2Calculator.AppliesToAge(Dhaka.AgeYears(admission.Patient.DateOfBirth, now));

        var vitals = adult
            ? (await db.VitalSigns.Where(v => v.AdmissionId == admissionId && v.RecordedAt >= since).OrderBy(v => v.RecordedAt).ToListAsync(ct))
                .Select(v => new VitalPoint(v.RecordedAt, v.News2Score, v.News2Band,
                    News2Calculator.Calculate(new News2Input(v.RespRate, v.SpO2, v.OnOxygen, v.Systolic, v.Pulse, v.Consciousness, v.TemperatureC))))
                .ToList()
            : [];
        var labs = await db.LabResults.Where(l => l.AdmissionId == admissionId && l.ResultAt >= since && l.NumericValue != null)
            .OrderBy(l => l.ResultAt).Select(l => new LabPoint(l.ResultAt, l.TestName, l.NumericValue!.Value, l.Unit)).ToListAsync(ct);

        var hits = MonitoringRules.Evaluate(vitals, labs);
        var raised = admission.Status == AdmissionStatus.Active ? await alerts.RaiseAsync(admission, hits, null, ct) : [];

        // NEWS2 alert settles automatically once the latest score is no longer in an alerting band.
        var latest = vitals.LastOrDefault(v => v.Score is not null);
        if (latest is not null && !hits.Any(h => h.AlertType == AlertTypes.News2))
            await alerts.AutoResolveNews2Async(admission, ct);

        var status = (await flags.GetAsync([admissionId], ct))[admissionId];
        var suggestion = await EnsureVitalsRecheckAsync(admission, status.Flag, latest?.At ?? now, ct);

        var dtos = new List<AlertDto>();
        foreach (var a in raised) if (await alerts.ToDtoAsync(a.Id, ct) is { } d) dtos.Add(d);
        return new MonitoringResult(status.News2Score, status.News2Band, status.Flag, dtos, suggestion);
    }

    /// <summary>Critical → "re-check vitals in 1 hour"; Watch → 4 hours. Shown to the nurse as a task.</summary>
    private async Task<string?> EnsureVitalsRecheckAsync(Admission admission, string flag, DateTime from, CancellationToken ct)
    {
        if (admission.Status != AdmissionStatus.Active || flag == PatientFlag.Stable) return null;
        var interval = flag == PatientFlag.Critical ? CriticalRecheck : WatchRecheck;
        var text = flag == PatientFlag.Critical ? "Critical: re-check vitals in 1 hour" : "Watch: re-check vitals in 4 hours";
        var due = from + interval;
        var open = await db.CareTasks.Where(t => t.AdmissionId == admission.Id && t.TaskType == "VitalsRecheck" && t.Status == CareTaskStatus.Open).ToListAsync(ct);
        var existing = open.FirstOrDefault();
        if (existing is null)
        {
            db.CareTasks.Add(new CareTask
            {
                HospitalId = admission.HospitalId, AdmissionId = admission.Id, TaskType = "VitalsRecheck", Detail = text, DueAt = due,
                Status = CareTaskStatus.Open, CreatedAt = clock.UtcNow
            });
        }
        else if (due < existing.DueAt || existing.Detail != text)
        {
            existing.DueAt = due < existing.DueAt ? due : existing.DueAt;
            existing.Detail = text;
        }
        else return text;
        await db.SaveChangesAsync(ct);
        if (admission.CurrentBedId is not null)
            await notifier.ToUsersAsync(await team.NursesForBedAsync(admission.CurrentBedId.Value, ct), HubEvents.TasksChanged,
                new { admissionId = admission.Id, reason = "vitalsRecheck", message = text });
        return text;
    }

    /// <summary>Completes open vitals re-check tasks once a new set of vitals is recorded.</summary>
    public async Task CompleteVitalsRecheckAsync(int admissionId, int userId, CancellationToken ct = default)
    {
        var open = await db.CareTasks.Where(t => t.AdmissionId == admissionId && t.TaskType == "VitalsRecheck" && t.Status == CareTaskStatus.Open).ToListAsync(ct);
        foreach (var t in open) { t.Status = CareTaskStatus.Done; t.DoneAt = clock.UtcNow; t.DoneBy = userId; }
        if (open.Count > 0) await db.SaveChangesAsync(ct);
    }
}
