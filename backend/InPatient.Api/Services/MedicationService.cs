using InPatient.Api.Data;
using InPatient.Api.Data.Entities;
using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Hubs;
using InPatient.Api.Infrastructure;
using InPatient.Api.Services.Rules;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Services;

/// <summary>Medication round, dose recording (wristband-verified), reminders and missed-dose escalation.</summary>
public sealed class MedicationService(InPatientDbContext db, IClock clock, ICurrentUser me, AdmissionService admissions, CareTeamService team,
    PatientFlagService flags, AlertService alerts, INotifier notifier, ILogger<MedicationService> log)
{
    public static readonly TimeSpan ReminderLead = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MissedAfter = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DelayedMissedAfter = TimeSpan.FromHours(2);

    private IQueryable<DoseTask> WithItem(IQueryable<DoseTask> q) => q.Include(t => t.PrescriptionItem).Include(t => t.DoneByUser);

    public DoseTaskDto ToDto(DoseTask t)
    {
        var now = clock.UtcNow;
        var i = t.PrescriptionItem;
        var open = DoseStatus.Open.Contains(t.Status) || t.Status == DoseStatus.Missed;
        return new DoseTaskDto(t.Id, t.AdmissionId, t.PrescriptionItemId, i.DrugName, i.Form, i.Strength, i.Dose, i.Route, i.Frequency, i.Instructions,
            t.DueAt, t.Status, t.DoneAt, t.DoneByUser?.Name, t.Reason, open && t.DueAt < now, (int)Math.Round((now - t.DueAt).TotalMinutes));
    }

    /// <summary>Doses for one Dhaka calendar day (default today) plus PRN items available.</summary>
    public async Task<DoseScheduleDto> GetScheduleAsync(int admissionId, DateOnly? date, CancellationToken ct)
    {
        await admissions.GetAccessibleAsync(admissionId, ct);
        var day = date ?? Dhaka.Today(clock.UtcNow);
        var from = Dhaka.ToUtc(day.ToDateTime(TimeOnly.MinValue));
        var to = from.AddDays(1);
        var tasks = await WithItem(db.DoseTasks.Where(t => t.AdmissionId == admissionId && t.DueAt >= from && t.DueAt < to)).OrderBy(t => t.DueAt).ToListAsync(ct);
        var prn = await db.PrescriptionItems.Where(i => i.Prescription.AdmissionId == admissionId && i.Prescription.Status == PrescriptionStatus.Signed
                                                         && i.IsActive && i.Frequency == Frequencies.PRN).ToListAsync(ct);
        return new DoseScheduleDto(admissionId, day, tasks.Select(ToDto).ToList(), prn.Select(PrescriptionService.ToItemDto).ToList());
    }

    /// <summary>Nurse's medication round: overdue and due-within-the-hour doses for the beds assigned this shift.</summary>
    public async Task<List<RoundPatientDto>> GetRoundAsync(CancellationToken ct)
    {
        var bedIds = await team.NurseBedIdsAsync(me.RequireUserId(), ct);
        var now = clock.UtcNow;
        var horizon = now.AddMinutes(60);
        var since = now.AddHours(-6);
        var tasks = await WithItem(db.DoseTasks.Where(t => t.Admission.Status == AdmissionStatus.Active && t.Admission.CurrentBedId != null
                && bedIds.Contains(t.Admission.CurrentBedId.Value)
                && t.DueAt <= horizon
                && (DoseStatus.Open.Contains(t.Status) || (t.Status == DoseStatus.Missed && t.DueAt >= since))))
            .OrderBy(t => t.DueAt).ToListAsync(ct);
        var admissionIds = tasks.Select(t => t.AdmissionId).Distinct().ToList();
        var info = await db.Admissions.Where(a => admissionIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Patient.Name, Bed = a.CurrentBed!.BedNumber, Ward = a.CurrentBed.Ward.Name }).ToDictionaryAsync(a => a.Id, ct);
        var status = await flags.GetAsync(admissionIds, ct);
        return tasks.GroupBy(t => t.AdmissionId)
            .Select(g => new RoundPatientDto(g.Key, info[g.Key].Name, info[g.Key].Bed, info[g.Key].Ward, status[g.Key].Flag, g.Select(ToDto).ToList()))
            .OrderBy(r => r.Doses.Min(d => d.DueAt)).ToList();
    }

    public async Task<DoseTaskDto> RecordAsync(int taskId, RecordDoseRequest req, CancellationToken ct)
    {
        var task = await WithItem(db.DoseTasks.Include(t => t.Admission)).FirstOrDefaultAsync(t => t.Id == taskId, ct) ?? throw ApiException.NotFound("Dose not found");
        if (task.Admission.Status != AdmissionStatus.Active) throw ApiException.Conflict("Patient is discharged");
        WristbandGuard.EnsureMatches(task.Admission.WristbandToken, req.WristbandToken);
        ApplyRecord(task, req.Status, req.Reason, clock.UtcNow, me.RequireUserId());
        await db.SaveChangesAsync(ct);
        await AfterRecordAsync(task, ct);
        return ToDto(task);
    }

    /// <summary>Shared by form and voice confirmation. Validates the transition; does not save.</summary>
    public static void ApplyRecord(DoseTask task, string status, string? reason, DateTime now, int userId)
    {
        if (!DoseStatus.Recordable.Contains(status)) throw ApiException.BadRequest("Status must be Given, Delayed, Refused or Withheld");
        var recordable = DoseStatus.Open.Contains(task.Status) || task.Status == DoseStatus.Missed;
        if (!recordable) throw ApiException.Conflict($"This dose is already recorded as {task.Status}");
        if (status != DoseStatus.Given && string.IsNullOrWhiteSpace(reason)) throw ApiException.BadRequest($"Give a reason for {status.ToLowerInvariant()}");
        if (task.DueAt > now.AddHours(1) && status == DoseStatus.Given) throw ApiException.Conflict("This dose is not due yet (more than 1 hour early)");
        task.Status = status;
        task.Reason = string.IsNullOrWhiteSpace(reason) ? (task.Status == DoseStatus.Given && task.DueAt < now - MissedAfter ? "Given late" : null) : reason.Trim();
        task.DoneAt = now;
        task.DoneByUserId = userId;
    }

    public async Task AfterRecordAsync(DoseTask task, CancellationToken ct)
    {
        await db.Entry(task).Reference(t => t.DoneByUser).LoadAsync(ct);
        if (task.Status == DoseStatus.Given)
        {
            // A late dose resolves its missed-dose alert.
            var missed = await db.Alerts.Where(a => a.DoseTaskId == task.Id && AlertStatus.Active.Contains(a.Status)).ToListAsync(ct);
            foreach (var a in missed) { a.Status = AlertStatus.Resolved; a.ResolvedAt = clock.UtcNow; }
            if (missed.Count > 0) await db.SaveChangesAsync(ct);
        }
        await notifier.ToHospitalAsync(task.HospitalId, HubEvents.TasksChanged, new { admissionId = task.AdmissionId, taskId = task.Id, status = task.Status });
    }

    /// <summary>PRN items have no schedule; each administration creates a Given task.</summary>
    public async Task<DoseTaskDto> RecordPrnAsync(int itemId, RecordPrnRequest req, CancellationToken ct)
    {
        var item = await db.PrescriptionItems.Include(i => i.Prescription).ThenInclude(p => p.Admission).FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw ApiException.NotFound("Medicine not found");
        var admission = item.Prescription.Admission;
        if (admission.Status != AdmissionStatus.Active) throw ApiException.Conflict("Patient is discharged");
        if (item.Frequency != Frequencies.PRN || !item.IsActive || item.Prescription.Status != PrescriptionStatus.Signed)
            throw ApiException.Conflict("Only active PRN medicines can be given on demand");
        WristbandGuard.EnsureMatches(admission.WristbandToken, req.WristbandToken);
        var now = clock.UtcNow;
        var task = new DoseTask
        {
            HospitalId = item.HospitalId, PrescriptionItemId = item.Id, AdmissionId = admission.Id, DueAt = now, Status = DoseStatus.Given,
            DoneAt = now, DoneByUserId = me.RequireUserId(), Reason = req.Reason?.Trim(), CreatedAt = now
        };
        db.DoseTasks.Add(task);
        await db.SaveChangesAsync(ct);
        task.PrescriptionItem = item;
        await AfterRecordAsync(task, ct);
        return ToDto(task);
    }

    // ---------- Jobs (run as system) ----------

    /// <summary>15 minutes before due: Scheduled → Due, remind the nurse(s) for that bed.</summary>
    public async Task<int> ProcessRemindersAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var due = await db.DoseTasks.Include(t => t.PrescriptionItem).Include(t => t.Admission).ThenInclude(a => a.CurrentBed)
            .Where(t => t.Status == DoseStatus.Scheduled && t.DueAt <= now + ReminderLead && t.Admission.Status == AdmissionStatus.Active)
            .ToListAsync(ct);
        if (due.Count == 0) return 0;
        foreach (var t in due) t.Status = DoseStatus.Due;
        await db.SaveChangesAsync(ct);

        foreach (var g in due.Where(t => t.Admission.CurrentBedId != null).GroupBy(t => t.Admission.CurrentBedId!.Value))
        {
            var nurses = await team.NursesForBedAsync(g.Key, ct);
            var bed = g.First().Admission.CurrentBed!.BedNumber;
            foreach (var t in g)
            {
                await notifier.ToUsersAsync(nurses, HubEvents.DoseDue, new
                {
                    taskId = t.Id, admissionId = t.AdmissionId, bedNumber = bed, drug = t.PrescriptionItem.DrugName, dose = t.PrescriptionItem.Dose,
                    route = t.PrescriptionItem.Route, dueAt = t.DueAt, message = $"{bed}: {t.PrescriptionItem.DrugName} {t.PrescriptionItem.Dose} {t.PrescriptionItem.Route} due at {Dhaka.FormatTime(t.DueAt)}"
                });
            }
        }
        log.LogInformation("Dose reminders: {Count} task(s) now due", due.Count);
        return due.Count;
    }

    /// <summary>
    /// Nothing recorded 30 min after due (2 h for Delayed): task → Missed, MissedDose alert,
    /// notify every nurse on the ward's shift and the consultant.
    /// </summary>
    public async Task<int> ProcessMissedAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var missed = await db.DoseTasks.Include(t => t.PrescriptionItem).Include(t => t.Admission).ThenInclude(a => a.CurrentBed)
            .Where(t => t.Admission.Status == AdmissionStatus.Active &&
                        (((t.Status == DoseStatus.Scheduled || t.Status == DoseStatus.Due) && t.DueAt <= now - MissedAfter)
                         || (t.Status == DoseStatus.Delayed && t.DueAt <= now - DelayedMissedAfter)))
            .ToListAsync(ct);
        foreach (var t in missed)
        {
            t.Status = DoseStatus.Missed;
            t.EscalatedAt = now;
            await db.SaveChangesAsync(ct);

            var i = t.PrescriptionItem;
            var hit = new RuleHit(AlertTypes.MissedDose, $"MISSED_DOSE_{t.Id}", AlertSeverity.Warning,
                $"Dose not recorded 30 min after due: {i.DrugName} {i.Dose} {i.Route} due {Dhaka.FormatTime(t.DueAt)} (Dhaka).", t.DueAt);
            var raised = await alerts.RaiseAsync(t.Admission, [hit], t.Id, ct);
            foreach (var a in raised) { a.EscalationLevel = 1; a.LastEscalatedAt = now; }
            await db.SaveChangesAsync(ct);

            var targets = new List<int> { t.Admission.ConsultantUserId };
            if (t.Admission.CurrentBed is not null) targets.AddRange(await team.NursesForWardAsync(t.Admission.CurrentBed.WardId, ct));
            await notifier.ToUsersAsync(targets, HubEvents.DoseMissed, new
            {
                taskId = t.Id, admissionId = t.AdmissionId, bedNumber = t.Admission.CurrentBed?.BedNumber, drug = i.DrugName, dueAt = t.DueAt,
                message = $"Missed dose: {t.Admission.CurrentBed?.BedNumber} {i.DrugName} {i.Dose} due {Dhaka.FormatTime(t.DueAt)}"
            });
        }
        if (missed.Count > 0) log.LogWarning("Missed-dose escalation: {Count} task(s)", missed.Count);
        return missed.Count;
    }
}
