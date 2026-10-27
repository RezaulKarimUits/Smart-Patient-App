using InPatient.Api.Data;
using InPatient.Api.Data.Entities;
using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Hubs;
using InPatient.Api.Infrastructure;
using InPatient.Api.Services.Rules;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Services;

/// <summary>Vitals, labs, notes, timeline and consultant sign-off.</summary>
public sealed class ClinicalService(InPatientDbContext db, IClock clock, ICurrentUser me, AdmissionService admissions, MonitoringService monitoring,
    CareTeamService team, INotifier notifier)
{
    public static MonitoringDto ToDto(MonitoringResult r) => new(r.News2Score, r.News2Band, r.Flag, r.NewAlerts, r.VitalsSuggestion);

    // ---------- Vitals ----------

    /// <summary>Validates, converts °F→°C, scores NEWS2 (16+ only). Does not save.</summary>
    public (VitalSign Entity, List<Issue> Issues) BuildVital(Admission admission, DateOnly dob, VitalsInput v, string source)
    {
        var issues = ClinicalValidation.CheckVitals(v.Systolic, v.Diastolic, v.Pulse, v.RespRate, v.SpO2, v.Consciousness);
        var (tempC, tIssue) = ClinicalValidation.NormalizeTemperature(v.Temperature, v.TemperatureUnit);
        if (tIssue is not null) issues.Add(tIssue);
        if (v.Systolic is null && v.Diastolic is null && v.Pulse is null && v.RespRate is null && v.Temperature is null && v.SpO2 is null)
            issues.Add(Issue.Block("vitals", "Enter at least one vital sign"));

        var now = clock.UtcNow;
        var at = v.RecordedAt is { } r && r <= now.AddMinutes(5) && r >= now.AddDays(-2) ? DateTime.SpecifyKind(r, DateTimeKind.Utc) : now;
        var consciousness = string.IsNullOrWhiteSpace(v.Consciousness) ? Consciousness.Alert : v.Consciousness;
        var entity = new VitalSign
        {
            HospitalId = admission.HospitalId, AdmissionId = admission.Id, RecordedAt = at, RecordedBy = me.UserId, Source = source,
            Systolic = v.Systolic, Diastolic = v.Diastolic, Pulse = v.Pulse, RespRate = v.RespRate, TemperatureC = tempC, SpO2 = v.SpO2,
            OnOxygen = v.OnOxygen ?? false, Consciousness = consciousness, CreatedAt = now
        };
        if (News2Calculator.AppliesToAge(Dhaka.AgeYears(dob, at)))
        {
            var score = News2Calculator.Calculate(new News2Input(entity.RespRate, entity.SpO2, entity.OnOxygen, entity.Systolic, entity.Pulse, entity.Consciousness, entity.TemperatureC));
            if (score is not null) { entity.News2Score = score.Score; entity.News2Band = score.Band; }
            else issues.Add(Issue.Warn("news2", "NEWS2 needs RR, SpO2, BP, pulse and temperature — not scored"));
        }
        return (entity, issues);
    }

    public async Task<VitalsSaveResponse> RecordVitalsAsync(int admissionId, VitalsInput input, CancellationToken ct)
    {
        var admission = await admissions.GetActiveAsync(admissionId, ct);
        var dob = await db.Patients.Where(p => p.Id == admission.PatientId).Select(p => p.DateOfBirth).FirstAsync(ct);
        var (vital, issues) = BuildVital(admission, dob, input, Sources.Form);
        var blockers = issues.Where(i => i.Level == Issue.Blocker).ToList();
        if (blockers.Count > 0) throw ApiException.Unprocessable(string.Join("; ", blockers.Select(b => b.Message)));
        db.VitalSigns.Add(vital);
        await db.SaveChangesAsync(ct);
        var result = await AfterEntryAsync(admission, vitalsRecorded: true, ct);
        return new VitalsSaveResponse(await GetVitalAsync(vital.Id, ct), ToDto(result));
    }

    /// <summary>NEWS2/monitoring after any confirmed entry + realtime refresh.</summary>
    public async Task<MonitoringResult> AfterEntryAsync(Admission admission, bool vitalsRecorded, CancellationToken ct)
    {
        if (vitalsRecorded) await monitoring.CompleteVitalsRecheckAsync(admission.Id, me.RequireUserId(), ct);
        var result = await monitoring.RunAsync(admission.Id, ct);
        await admissions.NotifyAdmissionUpdatedAsync(admission);
        if (admission.CurrentBedId is not null)
        {
            var tile = new { admissionId = admission.Id };
            await notifier.ToHospitalAsync(admission.HospitalId, HubEvents.TasksChanged, tile);
        }
        return result;
    }

    public async Task<VitalDto> GetVitalAsync(int id, CancellationToken ct)
        => await ProjectVitals(db.VitalSigns.Where(v => v.Id == id)).FirstAsync(ct);

    public async Task<List<VitalDto>> GetVitalsAsync(int admissionId, int hours, CancellationToken ct)
    {
        await admissions.GetAccessibleAsync(admissionId, ct);
        var since = clock.UtcNow.AddHours(-Math.Clamp(hours, 1, 24 * 60));
        return await ProjectVitals(db.VitalSigns.Where(v => v.AdmissionId == admissionId && v.RecordedAt >= since).OrderBy(v => v.RecordedAt)).ToListAsync(ct);
    }

    private static IQueryable<VitalDto> ProjectVitals(IQueryable<VitalSign> q) => q.Select(v => new VitalDto(v.Id, v.RecordedAt, v.Systolic, v.Diastolic, v.Pulse,
        v.RespRate, v.TemperatureC, v.SpO2, v.OnOxygen, v.Consciousness, v.News2Score, v.News2Band, v.Source,
        v.RecordedByNavigation != null ? v.RecordedByNavigation.Name : null, v.SignedOffByNavigation != null ? v.SignedOffByNavigation.Name : null, v.SignedOffAt));

    // ---------- Labs ----------

    public (List<LabResult> Entities, List<Issue> Issues, List<string> Notes) BuildLabs(Admission admission, IReadOnlyList<LabInput> inputs, string source, int? fileId)
    {
        var issues = new List<Issue>();
        var notes = new List<string>();
        var list = new List<LabResult>();
        var now = clock.UtcNow;
        if (inputs.Count == 0) issues.Add(Issue.Block("labResults", "Add at least one result"));
        for (var i = 0; i < inputs.Count; i++)
        {
            var x = inputs[i];
            if (string.IsNullOrWhiteSpace(x.TestName)) { issues.Add(Issue.Block($"labResults[{i}]", "Test name is required")); continue; }
            if (x.Value is null && string.IsNullOrWhiteSpace(x.TextValue)) { issues.Add(Issue.Block($"labResults[{i}]", $"{x.TestName}: value is required")); continue; }
            var n = LabCatalog.Normalize(x.TestName, x.Value, x.Unit, x.RefLow, x.RefHigh, x.TextValue);
            if (n.Note is not null) notes.Add($"{n.TestName}: {n.Note}");
            if (ClinicalValidation.CheckLab($"labResults[{i}]", n.TestName, n.Value) is { } issue) issues.Add(issue);
            var at = x.ResultAt is { } r && r <= now.AddMinutes(5) && r >= now.AddDays(-14) ? DateTime.SpecifyKind(r, DateTimeKind.Utc) : now;
            list.Add(new LabResult
            {
                HospitalId = admission.HospitalId, AdmissionId = admission.Id, StoredFileId = fileId, TestName = n.TestName, NumericValue = n.Value,
                TextValue = string.IsNullOrWhiteSpace(n.TextValue) ? null : n.TextValue.Trim(), Unit = n.Unit, RefLow = n.RefLow, RefHigh = n.RefHigh,
                Flag = n.Flag, ResultAt = at, Source = source, RecordedBy = me.UserId, CreatedAt = now
            });
        }
        return (list, issues, notes);
    }

    public async Task<LabsSaveResponse> RecordLabsAsync(int admissionId, IReadOnlyList<LabInput> inputs, string source, int? fileId, CancellationToken ct)
    {
        var admission = await admissions.GetActiveAsync(admissionId, ct);
        var (labs, issues, notes) = BuildLabs(admission, inputs, source, fileId);
        var blockers = issues.Where(i => i.Level == Issue.Blocker).ToList();
        if (blockers.Count > 0) throw ApiException.Unprocessable(string.Join("; ", blockers.Select(b => b.Message)));
        db.LabResults.AddRange(labs);
        await db.SaveChangesAsync(ct);
        var result = await AfterEntryAsync(admission, vitalsRecorded: false, ct);
        var ids = labs.Select(l => l.Id).ToList();
        return new LabsSaveResponse(await ProjectLabs(db.LabResults.Where(l => ids.Contains(l.Id))).ToListAsync(ct), notes, ToDto(result));
    }

    public async Task<LabsResponse> GetLabsAsync(int admissionId, CancellationToken ct)
    {
        await admissions.GetAccessibleAsync(admissionId, ct);
        var q = db.LabResults.Where(l => l.AdmissionId == admissionId);
        if (me.Role == Roles.Patient) q = q.Where(l => l.SignedOffAt != null); // patients see signed reports only
        var results = await ProjectLabs(q.OrderByDescending(l => l.ResultAt)).ToListAsync(ct);
        return new LabsResponse(results, BuildTable(results));
    }

    /// <summary>Rows = tests, columns = Dhaka dates (latest value per day) for side-by-side comparison.</summary>
    public static LabTableDto BuildTable(IReadOnlyList<LabDto> results)
    {
        var dates = results.Select(r => DateOnly.FromDateTime(Dhaka.ToLocal(r.ResultAt))).Distinct().OrderBy(d => d).ToList();
        var order = LabCatalog.Definitions.Select(d => d.Name).ToList();
        var rows = results.GroupBy(r => r.TestName)
            .OrderBy(g => order.IndexOf(g.Key) is var i && i >= 0 ? i : 99).ThenBy(g => g.Key)
            .Select(g =>
            {
                var first = g.OrderByDescending(r => r.ResultAt).First();
                var cells = dates.Select(d => g.Where(r => DateOnly.FromDateTime(Dhaka.ToLocal(r.ResultAt)) == d).OrderByDescending(r => r.ResultAt)
                    .Select(r => new LabCellDto(r.Id, r.Value, r.TextValue, r.Flag, r.ResultAt)).FirstOrDefault()).ToList();
                var numeric = g.Where(r => r.Value != null).OrderBy(r => r.ResultAt).ToList();
                string? trend = null;
                if (numeric.Count >= 2)
                {
                    // Direction over the last 48 h (latest vs the earliest value in that window).
                    var latest = numeric[^1];
                    var baseline = numeric.First(r => r.ResultAt >= latest.ResultAt.AddHours(-48));
                    if (baseline.Id == latest.Id) baseline = numeric[^2];
                    var delta = latest.Value!.Value - baseline.Value!.Value;
                    var rel = baseline.Value != 0 ? Math.Abs(delta / baseline.Value!.Value) : 1;
                    trend = rel < 0.05m ? "stable" : delta > 0 ? "up" : "down";
                }
                return new LabRowDto(g.Key, first.Unit, first.RefLow, first.RefHigh, cells, trend);
            }).ToList();
        return new LabTableDto(dates, rows);
    }

    private static IQueryable<LabDto> ProjectLabs(IQueryable<LabResult> q) => q.Select(l => new LabDto(l.Id, l.TestName, l.NumericValue, l.TextValue, l.Unit,
        l.RefLow, l.RefHigh, l.Flag, l.ResultAt, l.Source, l.StoredFileId,
        l.RecordedByNavigation != null ? l.RecordedByNavigation.Name : null, l.SignedOffByNavigation != null ? l.SignedOffByNavigation.Name : null, l.SignedOffAt));

    // ---------- Notes ----------

    public async Task<NoteDto> AddNoteAsync(int admissionId, NoteInput input, string source, int? audioFileId, CancellationToken ct)
    {
        var admission = await admissions.GetActiveAsync(admissionId, ct);
        if (!NoteTypes.All.Contains(input.NoteType)) throw ApiException.BadRequest("Note type must be Nursing, ConsultantRound or Handover");
        if (input.NoteType == NoteTypes.ConsultantRound && me.Role != Roles.Consultant) throw ApiException.Forbidden("Only consultants write round notes");
        if (string.IsNullOrWhiteSpace(input.Text)) throw ApiException.BadRequest("Note text is required");
        var now = clock.UtcNow;
        var note = new ClinicalNote
        {
            HospitalId = admission.HospitalId, AdmissionId = admission.Id, AuthorUserId = me.RequireUserId(), NoteType = input.NoteType,
            Text = input.Text.Trim(), Source = source, AudioFileId = audioFileId, CreatedAt = now,
            // A consultant's own round note needs no separate review.
            SignedOffBy = input.NoteType == NoteTypes.ConsultantRound ? me.UserId : null,
            SignedOffAt = input.NoteType == NoteTypes.ConsultantRound ? now : null
        };
        db.ClinicalNotes.Add(note);
        await db.SaveChangesAsync(ct);
        await admissions.NotifyAdmissionUpdatedAsync(admission);
        return (await GetNotesQuery(db.ClinicalNotes.Where(n => n.Id == note.Id)).FirstAsync(ct));
    }

    public async Task<List<NoteDto>> GetNotesAsync(int admissionId, CancellationToken ct)
    {
        await admissions.GetAccessibleAsync(admissionId, ct);
        return await GetNotesQuery(db.ClinicalNotes.Where(n => n.AdmissionId == admissionId).OrderByDescending(n => n.CreatedAt)).ToListAsync(ct);
    }

    private static IQueryable<NoteDto> GetNotesQuery(IQueryable<ClinicalNote> q) => q.Select(n => new NoteDto(n.Id, n.NoteType, n.Text, n.Source, n.CreatedAt,
        n.AuthorUserId, n.AuthorUser.Name, n.AuthorUser.Role, n.SignedOffByNavigation != null ? n.SignedOffByNavigation.Name : null, n.SignedOffAt, n.AudioFileId));

    // ---------- Timeline (everything merged, newest first) ----------

    public async Task<List<TimelineItemDto>> GetTimelineAsync(int admissionId, DateTime? before, int limit, CancellationToken ct)
    {
        await admissions.GetAccessibleAsync(admissionId, ct);
        var end = before ?? clock.UtcNow.AddMinutes(5);
        limit = Math.Clamp(limit, 10, 300);
        var items = new List<TimelineItemDto>();

        var vitals = await ProjectVitals(db.VitalSigns.Where(v => v.AdmissionId == admissionId && v.RecordedAt < end).OrderByDescending(v => v.RecordedAt).Take(limit)).ToListAsync(ct);
        items.AddRange(vitals.Select(v => new TimelineItemDto("vitals", v.RecordedAt, v.News2Score is null ? "Vitals" : $"Vitals · NEWS2 {v.News2Score}",
            VitalsSummary(v), v.RecordedByName, v.Source, v.News2Band, v.Id, v.SignedOffAt != null, v.SignedOffByName)));

        var labs = await ProjectLabs(db.LabResults.Where(l => l.AdmissionId == admissionId && l.ResultAt < end).OrderByDescending(l => l.ResultAt).Take(limit)).ToListAsync(ct);
        items.AddRange(labs.Select(l => new TimelineItemDto("lab", l.ResultAt, $"{l.TestName} {FormatLab(l)}",
            $"{l.Flag}{(l.RefLow != null || l.RefHigh != null ? $" · ref {l.RefLow:0.##}–{l.RefHigh:0.##}" : "")}", l.RecordedByName, l.Source,
            l.Flag == LabFlags.Normal ? null : l.Flag, l.Id, l.SignedOffAt != null, l.SignedOffByName)));

        var notes = await GetNotesQuery(db.ClinicalNotes.Where(n => n.AdmissionId == admissionId && n.CreatedAt < end).OrderByDescending(n => n.CreatedAt).Take(limit)).ToListAsync(ct);
        items.AddRange(notes.Select(n => new TimelineItemDto("note", n.CreatedAt, n.NoteType switch
        {
            NoteTypes.ConsultantRound => "Consultant round", NoteTypes.Handover => "Handover note", _ => "Nursing note"
        }, n.Text, n.AuthorName, n.Source, null, n.Id, n.SignedOffAt != null, n.SignedOffByName)));

        var doses = await db.DoseTasks.Where(t => t.AdmissionId == admissionId && t.DoneAt != null && t.DoneAt < end || (t.AdmissionId == admissionId && t.Status == DoseStatus.Missed && t.DueAt < end))
            .OrderByDescending(t => t.DoneAt ?? t.DueAt).Take(limit)
            .Select(t => new { t.Id, t.Status, t.DoneAt, t.DueAt, t.Reason, t.PrescriptionItem.DrugName, t.PrescriptionItem.Dose, t.PrescriptionItem.Route, By = t.DoneByUser != null ? t.DoneByUser.Name : null })
            .ToListAsync(ct);
        items.AddRange(doses.Select(d => new TimelineItemDto("dose", d.DoneAt ?? d.DueAt, $"{d.DrugName} {d.Dose} {d.Route} · {d.Status}",
            $"Due {Dhaka.FormatTime(d.DueAt)}{(d.Reason is null ? "" : " · " + d.Reason)}", d.By, null, d.Status is DoseStatus.Missed ? AlertSeverity.Warning : null, d.Id, null, null)));

        var rx = await db.Prescriptions.Where(p => p.AdmissionId == admissionId && p.CreatedAt < end).OrderByDescending(p => p.CreatedAt).Take(50)
            .Select(p => new { p.Id, p.Status, p.Source, p.CreatedAt, p.SignedAt, Cons = p.ConsultantUser.Name, Items = p.PrescriptionItems.Select(i => i.DrugName).ToList() }).ToListAsync(ct);
        items.AddRange(rx.Select(p => new TimelineItemDto("prescription", p.SignedAt ?? p.CreatedAt, p.Status == PrescriptionStatus.Signed ? "Prescription signed" : "Prescription draft",
            string.Join(", ", p.Items), p.Cons, p.Source, null, p.Id, null, null)));

        var files = await db.StoredFiles.Where(f => f.AdmissionId == admissionId && f.Kind != FileKinds.Audio && f.UploadedAt < end).OrderByDescending(f => f.UploadedAt).Take(50)
            .Select(f => new { f.Id, f.Kind, f.Caption, f.UploadedAt, By = f.UploadedByNavigation != null ? f.UploadedByNavigation.Name : null }).ToListAsync(ct);
        items.AddRange(files.Select(f => new TimelineItemDto("file", f.UploadedAt, f.Kind switch
        {
            FileKinds.TestReport => "Report photo", FileKinds.Prescription => "Prescription image", FileKinds.Progress => "Progress image", _ => "File"
        }, f.Caption, f.By, null, null, f.Id, null, null)));

        var alertRows = await db.Alerts.Where(a => a.AdmissionId == admissionId && a.CreatedAt < end).OrderByDescending(a => a.CreatedAt).Take(50).ToListAsync(ct);
        items.AddRange(alertRows.Select(a => new TimelineItemDto("alert", a.CreatedAt, $"{a.Severity} alert · {a.AlertType}", a.AiExplanation ?? a.RuleDetail, null, null,
            a.Severity, a.Id, null, null)));

        var beds = await db.BedEvents.Where(e => e.AdmissionId == admissionId && e.At < end).OrderByDescending(e => e.At).Take(50)
            .Select(e => new { e.Id, e.EventType, e.At, e.Note, e.Bed.BedNumber, Ward = e.Bed.Ward.Name, By = e.ByUser != null ? e.ByUser.Name : null }).ToListAsync(ct);
        items.AddRange(beds.Select(e => new TimelineItemDto("bed", e.At, $"{e.EventType} · {e.Ward} {e.BedNumber}", e.Note, e.By, null, null, e.Id, null, null)));

        if (me.Role == Roles.Patient) items = items.Where(i => i.Type is "bed" or "prescription" || i.SignedOff == true).ToList();
        return items.OrderByDescending(i => i.At).Take(limit).ToList();
    }

    public static string VitalsSummary(VitalDto v) => string.Join(" · ", new[]
    {
        v.Systolic != null ? $"BP {v.Systolic}/{v.Diastolic?.ToString() ?? "?"}" : null,
        v.Pulse != null ? $"P {v.Pulse}" : null,
        v.RespRate != null ? $"RR {v.RespRate}" : null,
        v.TemperatureC != null ? $"T {v.TemperatureC:0.0}°C" : null,
        v.SpO2 != null ? $"SpO2 {v.SpO2}%{(v.OnOxygen ? " on O2" : "")}" : null,
        v.Consciousness != Consciousness.Alert ? v.Consciousness : null
    }.Where(s => s != null));

    public static string FormatLab(LabDto l) => l.Value is { } v ? $"{LabCatalog.Format(l.TestName, v)} {l.Unit}".Trim() : l.TextValue ?? "";

    // ---------- Consultant sign-off ----------

    private async Task<IQueryable<Admission>> ReviewableAdmissionsAsync(CancellationToken ct)
    {
        var q = db.Admissions.Where(a => a.Status == AdmissionStatus.Active);
        if (me.Role == Roles.Consultant)
        {
            var uid = me.RequireUserId();
            var wards = await team.ConsultantWardIdsAsync(uid, ct);
            q = q.Where(a => a.ConsultantUserId == uid || (a.CurrentBed != null && wards.Contains(a.CurrentBed.WardId)));
        }
        return q;
    }

    public async Task<List<PendingPatientDto>> GetPendingAsync(int? admissionId, CancellationToken ct)
    {
        var adm = await ReviewableAdmissionsAsync(ct);
        if (admissionId is not null) adm = adm.Where(a => a.Id == admissionId);
        var ids = await adm.Select(a => a.Id).ToListAsync(ct);

        var vitals = await ProjectVitals(db.VitalSigns.Where(v => ids.Contains(v.AdmissionId) && v.SignedOffAt == null)).Select(v => new { v, v.Id }).ToListAsync(ct);
        var vitalAdm = await db.VitalSigns.Where(v => ids.Contains(v.AdmissionId) && v.SignedOffAt == null).Select(v => new { v.Id, v.AdmissionId }).ToDictionaryAsync(x => x.Id, x => x.AdmissionId, ct);
        var labs = await db.LabResults.Where(l => ids.Contains(l.AdmissionId) && l.SignedOffAt == null)
            .Select(l => new { l.Id, l.AdmissionId, l.TestName, l.NumericValue, l.TextValue, l.Unit, l.Flag, l.ResultAt, l.Source, By = l.RecordedByNavigation != null ? l.RecordedByNavigation.Name : null })
            .ToListAsync(ct);
        var notes = await db.ClinicalNotes.Where(n => ids.Contains(n.AdmissionId) && n.SignedOffAt == null)
            .Select(n => new { n.Id, n.AdmissionId, n.NoteType, n.Text, n.CreatedAt, n.Source, By = n.AuthorUser.Name }).ToListAsync(ct);

        var entries = new List<(int AdmissionId, PendingEntryDto Entry)>();
        entries.AddRange(vitals.Select(x => (vitalAdm[x.Id], new PendingEntryDto("vital", x.v.Id, x.v.RecordedAt,
            (x.v.News2Score is null ? "" : $"NEWS2 {x.v.News2Score} · ") + VitalsSummary(x.v), x.v.RecordedByName, x.v.Source, x.v.News2Band))));
        entries.AddRange(labs.Select(l => (l.AdmissionId, new PendingEntryDto("lab", l.Id, l.ResultAt,
            $"{l.TestName} {(l.NumericValue is { } v ? LabCatalog.Format(l.TestName, v) : l.TextValue)} {l.Unit}".Trim(), l.By, l.Source, l.Flag))));
        entries.AddRange(notes.Select(n => (n.AdmissionId, new PendingEntryDto("note", n.Id, n.CreatedAt,
            (n.NoteType == NoteTypes.Handover ? "Handover: " : "") + (n.Text.Length > 160 ? n.Text[..157] + "…" : n.Text), n.By, n.Source, null))));

        var headers = (await admissions.GetHeadersAsync(db.Admissions.Where(a => ids.Contains(a.Id)), ct)).ToDictionary(h => h.Id);
        return entries.GroupBy(e => e.AdmissionId)
            .Select(g => new PendingPatientDto(g.Key, headers[g.Key].PatientName, headers[g.Key].BedNumber, headers[g.Key].WardName, headers[g.Key].Flag,
                g.Select(e => e.Entry).OrderByDescending(e => e.At).ToList()))
            .OrderByDescending(p => p.Flag == PatientFlag.Critical).ThenByDescending(p => p.Flag == PatientFlag.Watch).ThenBy(p => p.BedNumber)
            .ToList();
    }

    public async Task<SignOffResponse> SignOffAsync(SignOffRequest req, CancellationToken ct)
    {
        var uid = me.RequireUserId();
        var now = clock.UtcNow;
        var ids = await (await ReviewableAdmissionsAsync(ct)).Select(a => a.Id).ToListAsync(ct);
        var vIds = req.VitalIds ?? []; var lIds = req.LabIds ?? []; var nIds = req.NoteIds ?? [];
        if (vIds.Count + lIds.Count + nIds.Count == 0) throw ApiException.BadRequest("Nothing selected to sign off");

        var vitals = await db.VitalSigns.Where(v => vIds.Contains(v.Id) && ids.Contains(v.AdmissionId) && v.SignedOffAt == null).ToListAsync(ct);
        var labs = await db.LabResults.Where(l => lIds.Contains(l.Id) && ids.Contains(l.AdmissionId) && l.SignedOffAt == null).ToListAsync(ct);
        var notes = await db.ClinicalNotes.Where(n => nIds.Contains(n.Id) && ids.Contains(n.AdmissionId) && n.SignedOffAt == null).ToListAsync(ct);
        foreach (var v in vitals) { v.SignedOffBy = uid; v.SignedOffAt = now; }
        foreach (var l in labs) { l.SignedOffBy = uid; l.SignedOffAt = now; }
        foreach (var n in notes) { n.SignedOffBy = uid; n.SignedOffAt = now; }
        await db.SaveChangesAsync(ct);
        var name = await db.AppUsers.Where(u => u.Id == uid).Select(u => u.Name).FirstAsync(ct);
        return new SignOffResponse(vitals.Count, labs.Count, notes.Count, name);
    }
}
