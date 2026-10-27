using System.Text;
using System.Text.Json;
using InPatient.Api.Data;
using InPatient.Api.Data.Entities;
using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Hubs;
using InPatient.Api.Infrastructure;
using InPatient.Api.Services.Ai;
using InPatient.Api.Services.Rules;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Services;

public record ParameterTrendDto(string Name, string Group, decimal? Previous, DateTime? PreviousAt, decimal? Latest, DateTime? LatestAt, string? Unit,
    decimal? Change, string Direction, string? Comment);

public record AiProgressDto(int AdmissionId, DateTime GeneratedAt, bool Cached, string OverallTrajectory, string Summary,
    IReadOnlyList<ParameterTrendDto> Parameters, IReadOnlyList<string> WatchFor, string Model, string Disclaimer);

public record HandoverFactsDto(int? News2First, int? News2Latest, string? LastVitals, DateTime? LastVitalsAt, IReadOnlyList<string> DosesGiven,
    IReadOnlyList<string> DosesMissedOrRefused, IReadOnlyList<string> NewResults, IReadOnlyList<string> OpenAlerts, IReadOnlyList<string> PendingTasks);

public record HandoverPatientDto(int AdmissionId, string PatientName, string? BedNumber, string Flag, HandoverFactsDto Facts, string Summary,
    IReadOnlyList<string> Priorities);

public record HandoverDto(string Shift, DateTime GeneratedAt, DateTime WindowStart, IReadOnlyList<HandoverPatientDto> Patients, string Model);

public record DischargeDraftDto(int AdmissionId, string SummaryText, DischargeDraft Sections, DateTime GeneratedAt, string Model);

public record DischargeRequest(string Summary);

/// <summary>Context builders shared by the AI features. Patient names are never sent to the model.</summary>
public sealed class ClinicalContext(InPatientDbContext db, IClock clock)
{
    public static readonly Dictionary<string, (decimal Low, decimal High, string Unit)> VitalNormal = new()
    {
        ["NEWS2"] = (0, 0, ""), ["Systolic BP"] = (111, 219, "mmHg"), ["Pulse"] = (51, 90, "/min"), ["Respiration rate"] = (12, 20, "/min"),
        ["Temperature"] = (36.1m, 38.0m, "°C"), ["SpO2"] = (96, 100, "%"),
    };

    public static string Direction(decimal? prev, decimal? latest, decimal low, decimal high)
    {
        if (prev is null || latest is null) return "stable";
        decimal Dist(decimal v) => v < low ? low - v : v > high ? v - high : 0;
        var eps = Math.Max(0.0001m, (high - low) * 0.02m);
        var dp = Dist(prev.Value); var dl = Dist(latest.Value);
        if (dl < dp - eps) return "improving";
        if (dl > dp + eps) return "worsening";
        return "stable";
    }

    /// <summary>Latest vs the reading ~24 h earlier (vitals) and latest vs previous result (labs), with coded direction.</summary>
    public async Task<List<ParameterTrendDto>> TrendsAsync(int admissionId, bool adult, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var vitals = await db.VitalSigns.Where(v => v.AdmissionId == admissionId && v.RecordedAt >= now.AddHours(-72)).OrderBy(v => v.RecordedAt).ToListAsync(ct);
        var list = new List<ParameterTrendDto>();
        if (vitals.Count > 0)
        {
            var latest = vitals[^1];
            var baseline = vitals.LastOrDefault(v => v.RecordedAt <= latest.RecordedAt.AddHours(-20)) ?? (vitals.Count > 1 ? vitals[0] : null);
            void Add(string name, Func<VitalSign, decimal?> f)
            {
                var n = VitalNormal[name];
                var l = f(latest); var p = baseline is null ? null : f(baseline);
                if (l is null) return;
                list.Add(new ParameterTrendDto(name, "vitals", p, baseline?.RecordedAt, l, latest.RecordedAt, n.Unit, p is null ? null : l - p, Direction(p, l, n.Low, n.High), null));
            }
            if (adult) Add("NEWS2", v => v.News2Score);
            Add("Systolic BP", v => v.Systolic);
            Add("Pulse", v => v.Pulse);
            Add("Respiration rate", v => v.RespRate);
            Add("Temperature", v => v.TemperatureC);
            Add("SpO2", v => v.SpO2);
        }
        var labs = await db.LabResults.Where(l => l.AdmissionId == admissionId && l.NumericValue != null).OrderBy(l => l.ResultAt).ToListAsync(ct);
        foreach (var g in labs.GroupBy(l => l.TestName))
        {
            var s = g.ToList();
            var l = s[^1]; var p = s.Count > 1 ? s[^2] : null;
            var def = LabCatalog.Find(l.TestName);
            var low = l.RefLow ?? def?.RefLow ?? l.NumericValue!.Value; var high = l.RefHigh ?? def?.RefHigh ?? l.NumericValue!.Value;
            list.Add(new ParameterTrendDto(l.TestName, "labs", p?.NumericValue, p?.ResultAt, l.NumericValue, l.ResultAt, l.Unit,
                p is null ? null : l.NumericValue - p.NumericValue, Direction(p?.NumericValue, l.NumericValue, low, high), null));
        }
        return list;
    }

    public async Task<(Admission Admission, int Age, string Sex, string? Bed, string? Ward)> PatientAsync(int admissionId, CancellationToken ct)
    {
        var a = await db.Admissions.Include(x => x.Patient).Include(x => x.CurrentBed).ThenInclude(b => b!.Ward).FirstAsync(x => x.Id == admissionId, ct);
        return (a, Dhaka.AgeYears(a.Patient.DateOfBirth, clock.UtcNow), a.Patient.Sex, a.CurrentBed?.BedNumber, a.CurrentBed?.Ward.Name);
    }

    public static string FormatTrends(IEnumerable<ParameterTrendDto> trends)
        => string.Join("\n", trends.Select(t => $"- {t.Name}: {Fmt(t.Name, t.Previous)} → {Fmt(t.Name, t.Latest)} {t.Unit} ({t.Direction}" +
                                               (t.PreviousAt is null ? "" : $", previous {Dhaka.FormatDateTime(t.PreviousAt.Value)}") + $", latest {Dhaka.FormatDateTime(t.LatestAt!.Value)})"));

    private static string Fmt(string name, decimal? v) => v is null ? "n/a" : LabCatalog.Format(name, v.Value);
}

/// <summary>Writes a 1–2 sentence AI explanation for each new alert, in the background. Rules already decided the alert.</summary>
public sealed class AlertExplanationWorker(AlertExplanationQueue queue, IServiceScopeFactory scopes, ILogger<AlertExplanationWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var alertId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                scope.ServiceProvider.GetRequiredService<CurrentUser>().RunAsSystem();
                var ai = scope.ServiceProvider.GetRequiredService<IStructuredAi>();
                if (!ai.IsConfigured) continue;
                await ExplainAsync(scope.ServiceProvider, ai, alertId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogWarning("Alert {AlertId} explanation failed: {Message}", alertId, ex.Message); }
        }
    }

    private static async Task ExplainAsync(IServiceProvider sp, IStructuredAi ai, int alertId, CancellationToken ct)
    {
        var db = sp.GetRequiredService<InPatientDbContext>();
        var ctx = sp.GetRequiredService<ClinicalContext>();
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == alertId, ct);
        if (alert is null || alert.AiExplanation is not null) return;
        var (adm, age, sex, bed, ward) = await ctx.PatientAsync(alert.AdmissionId, ct);
        var trends = await ctx.TrendsAsync(adm.Id, News2Calculator.AppliesToAge(age), ct);

        var user = $"""
            ALERT (raised by coded rules): {alert.Severity} · {alert.AlertType} · {alert.RuleCode}
            RULE DETAIL: {alert.RuleDetail}
            PATIENT: {age} y {sex}, bed {bed}, {ward}. Provisional diagnosis: {adm.ProvisionalDiagnosis ?? "not recorded"}.
            RECENT TRENDS:
            {ClinicalContext.FormatTrends(trends)}
            Write 1–2 sentences for the consultant explaining why this alert fired, quoting the actual numbers and how they changed. No diagnosis, no treatment advice.
            """;
        var res = await ai.GenerateAsync<AlertExplanationDraft>(new AiJsonRequest("alert_explanation", AiSchemas.AlertExplanation,
            "You explain early-warning alerts on a hospital ward in plain clinical English. " + AiPrompts.Safety, user), ct);
        alert.AiExplanation = res.Explanation.Length > 1000 ? res.Explanation[..1000] : res.Explanation;
        await db.SaveChangesAsync(ct);
        var dto = await sp.GetRequiredService<AlertService>().ToDtoAsync(alert.Id, ct);
        await sp.GetRequiredService<INotifier>().ToHospitalAsync(alert.HospitalId, HubEvents.AlertUpdated, new { alertId = alert.Id, admissionId = alert.AdmissionId, alert = dto });
    }
}

public sealed class AiProgressService(InPatientDbContext db, IClock clock, IStructuredAi ai, ClinicalContext ctx, AdmissionService admissions)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);
    public const string Disclaimer = "AI comparison to support clinical judgement — not a diagnosis.";

    public async Task<AiProgressDto> GetAsync(int admissionId, bool refresh, CancellationToken ct)
    {
        if (!ai.IsConfigured) throw new AiNotConfiguredException();
        var admission = await admissions.GetAccessibleAsync(admissionId, ct);
        var now = clock.UtcNow;
        if (!refresh)
        {
            var cached = await db.AiSummaries.Where(s => s.AdmissionId == admissionId && s.Kind == AiSummaryKind.Progress24h && s.CreatedAt >= now - CacheFor)
                .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct);
            if (cached is not null && JsonSerializer.Deserialize<AiProgressDto>(cached.Content, Json.Options) is { } c) return c with { Cached = true };
        }

        var (adm, age, sex, bed, ward) = await ctx.PatientAsync(admissionId, ct);
        var trends = await ctx.TrendsAsync(admissionId, News2Calculator.AppliesToAge(age), ct);
        if (trends.Count == 0) throw ApiException.Conflict("Not enough results yet to compare");
        var alerts = await db.Alerts.Where(a => a.AdmissionId == admissionId && AlertStatus.Active.Contains(a.Status)).Select(a => a.RuleDetail).ToListAsync(ct);

        var user = $"""
            PATIENT: {age} y {sex}, bed {bed}, {ward}, day {(int)(now - adm.AdmittedAt).TotalDays + 1} of admission. Provisional diagnosis: {adm.ProvisionalDiagnosis ?? "not recorded"}.
            PARAMETERS (latest vs previous, direction already computed by coded rules relative to normal ranges):
            {ClinicalContext.FormatTrends(trends)}
            ACTIVE ALERTS: {(alerts.Count == 0 ? "none" : string.Join(" | ", alerts))}
            Give an overall trajectory, a 2–3 sentence summary quoting the numbers, one short comment per parameter (same names), and up to 3 things to watch. Never a diagnosis.
            """;
        var res = await ai.GenerateAsync<ProgressDraft>(new AiJsonRequest("progress", AiSchemas.Progress,
            "You compare an in-patient's serial results for the consultant. " + AiPrompts.Safety, user), ct);

        var comments = res.ParameterComments.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Comment, StringComparer.OrdinalIgnoreCase);
        var dto = new AiProgressDto(admissionId, now, false, res.OverallTrajectory, res.Summary,
            trends.Select(t => t with { Comment = comments.GetValueOrDefault(t.Name) }).ToList(), res.WatchFor, ai.Model, Disclaimer);
        db.AiSummaries.Add(new AiSummary { HospitalId = admission.HospitalId, AdmissionId = admissionId, Kind = AiSummaryKind.Progress24h,
            Content = JsonSerializer.Serialize(dto, Json.Options), CreatedAt = now });
        await db.SaveChangesAsync(ct);
        return dto;
    }
}

public sealed class HandoverService(InPatientDbContext db, IClock clock, ICurrentUser me, IStructuredAi ai, CareTeamService team, PatientFlagService flags)
{
    public async Task<HandoverDto> GetAsync(CancellationToken ct)
    {
        if (!ai.IsConfigured) throw new AiNotConfiguredException();
        var now = clock.UtcNow;
        var since = now.AddHours(-12);
        var (date, shift) = team.CurrentShift();
        var bedIds = await team.NurseBedIdsAsync(me.RequireUserId(), ct);
        var admissions = await db.Admissions.Where(a => a.Status == AdmissionStatus.Active && a.CurrentBedId != null && bedIds.Contains(a.CurrentBedId.Value))
            .Select(a => new { a.Id, a.Patient.Name, a.Patient.DateOfBirth, a.Patient.Sex, Bed = a.CurrentBed!.BedNumber, a.ProvisionalDiagnosis })
            .OrderBy(a => a.Bed).ToListAsync(ct);
        if (admissions.Count == 0) return new HandoverDto(shift, now, since, [], ai.Model);
        var ids = admissions.Select(a => a.Id).ToList();
        var status = await flags.GetAsync(ids, ct);

        var facts = new Dictionary<int, HandoverFactsDto>();
        foreach (var a in admissions)
        {
            var vitals = await db.VitalSigns.Where(v => v.AdmissionId == a.Id && v.RecordedAt >= since).OrderBy(v => v.RecordedAt).ToListAsync(ct);
            var last = vitals.LastOrDefault() ?? await db.VitalSigns.Where(v => v.AdmissionId == a.Id).OrderByDescending(v => v.RecordedAt).FirstOrDefaultAsync(ct);
            var doses = await db.DoseTasks.Include(t => t.PrescriptionItem).Where(t => t.AdmissionId == a.Id && t.DueAt >= since && t.DueAt <= now).OrderBy(t => t.DueAt).ToListAsync(ct);
            var labs = await db.LabResults.Where(l => l.AdmissionId == a.Id && l.ResultAt >= since).OrderBy(l => l.ResultAt).ToListAsync(ct);
            var alertTexts = await db.Alerts.Where(x => x.AdmissionId == a.Id && AlertStatus.Active.Contains(x.Status)).Select(x => x.Severity + ": " + x.RuleDetail).ToListAsync(ct);
            var upcoming = await db.DoseTasks.Include(t => t.PrescriptionItem).Where(t => t.AdmissionId == a.Id && DoseStatus.Open.Contains(t.Status) && t.DueAt > now && t.DueAt <= now.AddHours(4))
                .OrderBy(t => t.DueAt).ToListAsync(ct);
            var care = await db.CareTasks.Where(t => t.AdmissionId == a.Id && t.Status == CareTaskStatus.Open).ToListAsync(ct);

            string D(DoseTask t) => $"{t.PrescriptionItem.DrugName} {t.PrescriptionItem.Dose} {Dhaka.FormatTime(t.DueAt)}";
            facts[a.Id] = new HandoverFactsDto(
                vitals.FirstOrDefault(v => v.News2Score != null)?.News2Score, vitals.LastOrDefault(v => v.News2Score != null)?.News2Score ?? last?.News2Score,
                last is null ? null : ClinicalService.VitalsSummary(new VitalDto(last.Id, last.RecordedAt, last.Systolic, last.Diastolic, last.Pulse, last.RespRate, last.TemperatureC,
                    last.SpO2, last.OnOxygen, last.Consciousness, last.News2Score, last.News2Band, last.Source, null, null, null)),
                last?.RecordedAt,
                doses.Where(t => t.Status == DoseStatus.Given).Select(D).ToList(),
                doses.Where(t => t.Status is DoseStatus.Missed or DoseStatus.Refused or DoseStatus.Withheld or DoseStatus.Delayed).Select(t => $"{D(t)} ({t.Status}{(t.Reason is null ? "" : ": " + t.Reason)})").ToList(),
                labs.Select(l => $"{l.TestName} {(l.NumericValue is { } v ? LabCatalog.Format(l.TestName, v) : l.TextValue)} {l.Unit} ({l.Flag})").ToList(),
                alertTexts,
                upcoming.Select(t => "Dose " + D(t)).Concat(care.Select(c => $"{c.Detail} (due {Dhaka.FormatTime(c.DueAt)})")).ToList());
        }

        // Cached per admission for 15 minutes.
        var cached = await db.AiSummaries.Where(s => ids.Contains(s.AdmissionId) && s.Kind == AiSummaryKind.Handover && s.CreatedAt >= now.AddMinutes(-15))
            .GroupBy(s => s.AdmissionId).Select(g => g.OrderByDescending(s => s.CreatedAt).First()).ToListAsync(ct);
        var summaries = cached.ToDictionary(s => s.AdmissionId, s => JsonSerializer.Deserialize<HandoverPatientDraft>(s.Content, Json.Options)!);
        var missing = admissions.Where(a => !summaries.ContainsKey(a.Id)).ToList();
        if (missing.Count > 0)
        {
            var sb = new StringBuilder($"SHIFT HANDOVER at {Dhaka.FormatDateTime(now)} (Asia/Dhaka), covering the last 12 hours.\n");
            foreach (var a in missing)
            {
                var f = facts[a.Id];
                sb.AppendLine($"""
                    --- admissionId {a.Id}: bed {a.Bed}, {Dhaka.AgeYears(a.DateOfBirth, now)} y {a.Sex}, {a.ProvisionalDiagnosis}. Status flag: {status[a.Id].Flag}.
                    NEWS2 first→latest: {f.News2First?.ToString() ?? "n/a"} → {f.News2Latest?.ToString() ?? "n/a"}. Last vitals: {f.LastVitals ?? "none"}.
                    Doses given: {Join(f.DosesGiven)}. Missed/refused/held: {Join(f.DosesMissedOrRefused)}. New results: {Join(f.NewResults)}.
                    Open alerts: {Join(f.OpenAlerts)}. Pending next 4 h: {Join(f.PendingTasks)}.
                    """);
            }
            sb.AppendLine("For each patient write a 2–3 sentence handover summary (vitals trend, doses, results, alerts) and up to 3 short priorities for the next shift. Use the numbers given; no diagnosis.");
            var res = await ai.GenerateAsync<HandoverDraft>(new AiJsonRequest("handover", AiSchemas.Handover,
                "You write concise nursing shift handovers (SBAR style) for a Bangladeshi hospital ward. " + AiPrompts.Safety, sb.ToString()), ct);
            foreach (var p in res.Patients.Where(p => missing.Any(m => m.Id == p.AdmissionId)))
            {
                summaries[p.AdmissionId] = p;
                db.AiSummaries.Add(new AiSummary { HospitalId = me.RequireHospitalId(), AdmissionId = p.AdmissionId, Kind = AiSummaryKind.Handover,
                    Content = JsonSerializer.Serialize(p, Json.Options), CreatedAt = now });
            }
            await db.SaveChangesAsync(ct);
        }

        var patients = admissions.Select(a => new HandoverPatientDto(a.Id, a.Name, a.Bed, status[a.Id].Flag, facts[a.Id],
            summaries.TryGetValue(a.Id, out var s) ? s.Summary : "Summary unavailable — see facts.", s?.Priorities ?? []))
            .OrderBy(p => p.Flag switch { PatientFlag.Critical => 0, PatientFlag.Watch => 1, _ => 2 }).ThenBy(p => p.BedNumber).ToList();
        return new HandoverDto(shift, now, since, patients, ai.Model);
    }

    private static string Join(IReadOnlyList<string> s) => s.Count == 0 ? "none" : string.Join("; ", s);
}

public sealed class DischargeService(InPatientDbContext db, IClock clock, ICurrentUser me, IStructuredAi ai, ClinicalContext ctx, AdmissionService admissions,
    BedLifecycle beds, INotifier notifier)
{
    public async Task<DischargeDraftDto> DraftAsync(int admissionId, CancellationToken ct)
    {
        if (!ai.IsConfigured) throw new AiNotConfiguredException();
        var admission = await admissions.GetActiveAsync(admissionId, ct);
        var (adm, age, sex, _, _) = await ctx.PatientAsync(admissionId, ct);
        var now = clock.UtcNow;
        var journey = (await db.AdmissionWardStays.Where(s => s.AdmissionId == admissionId).OrderBy(s => s.StartAt)
            .Select(s => new { Ward = s.Ward.Name, s.Bed.BedNumber, s.StartAt }).ToListAsync(ct))
            .Select(s => $"{s.Ward} {s.BedNumber} from {Dhaka.FormatDateTime(s.StartAt)}").ToList();
        var trends = await ctx.TrendsAsync(admissionId, News2Calculator.AppliesToAge(age), ct);
        var firstVitals = await db.VitalSigns.Where(v => v.AdmissionId == admissionId).OrderBy(v => v.RecordedAt).FirstOrDefaultAsync(ct);
        var meds = await db.PrescriptionItems.Where(i => i.Prescription.AdmissionId == admissionId && i.Prescription.Status == PrescriptionStatus.Signed)
            .Select(i => new { i.Form, i.DrugName, i.Dose, i.Route, i.Frequency, i.IsActive, i.DurationDays, i.Instructions }).ToListAsync(ct);
        var rounds = await db.ClinicalNotes.Where(n => n.AdmissionId == admissionId && n.NoteType == NoteTypes.ConsultantRound).OrderByDescending(n => n.CreatedAt).Take(4)
            .Select(n => n.Text).ToListAsync(ct);
        var alerts = await db.Alerts.Where(a => a.AdmissionId == admissionId).OrderByDescending(a => a.CreatedAt).Take(6).Select(a => a.Severity + ": " + a.RuleDetail).ToListAsync(ct);

        var user = $"""
            PATIENT: {age} y {sex}. Admitted {Dhaka.FormatDateTime(adm.AdmittedAt)}; length of stay {(now - adm.AdmittedAt).TotalDays:0.0} days. Provisional diagnosis: {adm.ProvisionalDiagnosis ?? "not recorded"}.
            WARD JOURNEY: {string.Join(" → ", journey)}
            FIRST VITALS: {(firstVitals is null ? "none" : $"BP {firstVitals.Systolic}/{firstVitals.Diastolic}, P {firstVitals.Pulse}, RR {firstVitals.RespRate}, T {firstVitals.TemperatureC}, SpO2 {firstVitals.SpO2}, NEWS2 {firstVitals.News2Score}")}
            LATEST vs PREVIOUS:
            {ClinicalContext.FormatTrends(trends)}
            MEDICINES (active = still prescribed): {string.Join("; ", meds.Select(m => $"{m.Form} {m.DrugName} {m.Dose} {m.Route} {m.Frequency}{(m.IsActive ? " (active)" : " (stopped)")}"))}
            RECENT CONSULTANT ROUNDS: {string.Join(" | ", rounds)}
            ALERTS DURING STAY: {(alerts.Count == 0 ? "none" : string.Join(" | ", alerts))}
            Draft a discharge summary for the consultant to edit. Use only the facts given; where something is unknown, say "to be completed by consultant".
            """;
        var res = await ai.GenerateAsync<DischargeDraft>(new AiJsonRequest("discharge_summary", AiSchemas.Discharge,
            "You draft hospital discharge summaries in clear English for a consultant to review and sign. " + AiPrompts.Safety, user), ct);
        var text = Compose(res);
        db.AiSummaries.Add(new AiSummary { HospitalId = admission.HospitalId, AdmissionId = admissionId, Kind = AiSummaryKind.Discharge, Content = text, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        return new DischargeDraftDto(admissionId, text, res, now, ai.Model);
    }

    public static string Compose(DischargeDraft d)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Presenting problem: " + d.PresentingProblem).AppendLine();
        sb.AppendLine("Hospital course: " + d.HospitalCourse).AppendLine();
        sb.AppendLine("Key results: " + d.KeyResults).AppendLine();
        sb.AppendLine("Condition at discharge: " + d.ConditionAtDischarge).AppendLine();
        sb.AppendLine("Discharge medicines:");
        foreach (var m in d.DischargeMedications) sb.AppendLine("• " + m);
        sb.AppendLine().AppendLine("Follow-up: " + d.FollowUp).AppendLine();
        sb.AppendLine("Return to hospital immediately if:");
        foreach (var r in d.ReturnPrecautions) sb.AppendLine("• " + r);
        return sb.ToString().Trim();
    }

    /// <summary>Consultant confirms (edited) summary: admission closes, bed → Cleaning, stay closed, future doses removed, summary visible to the patient.</summary>
    public async Task<AdmissionHeaderDto> DischargeAsync(int admissionId, DischargeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Summary) || req.Summary.Trim().Length < 20) throw ApiException.BadRequest("Write the discharge summary (at least a few sentences)");
        var admission = await admissions.GetActiveAsync(admissionId, ct);
        var now = clock.UtcNow;
        admission.Status = AdmissionStatus.Discharged;
        admission.DischargedAt = now;
        admission.DischargeSummary = req.Summary.Trim();

        var stay = await db.AdmissionWardStays.Where(s => s.AdmissionId == admissionId && s.EndAt == null).ToListAsync(ct);
        foreach (var s in stay) s.EndAt = now;
        int? bedId = admission.CurrentBedId;
        if (bedId is not null)
        {
            var bed = await db.Beds.FirstAsync(b => b.Id == bedId, ct);
            beds.Change(bed, BedStatus.Cleaning, BedEventType.Discharged, admission.Id, "Discharged");
        }
        admission.CurrentBedId = null;

        var items = await db.PrescriptionItems.Where(i => i.Prescription.AdmissionId == admissionId && i.IsActive).ToListAsync(ct);
        foreach (var i in items) { i.IsActive = false; i.StoppedAt = now; }
        var future = await db.DoseTasks.Where(t => t.AdmissionId == admissionId && (t.Status == DoseStatus.Scheduled || t.Status == DoseStatus.Due) && t.DueAt > now).ToListAsync(ct);
        db.DoseTasks.RemoveRange(future);
        var openAlerts = await db.Alerts.Where(a => a.AdmissionId == admissionId && AlertStatus.Active.Contains(a.Status)).ToListAsync(ct);
        foreach (var a in openAlerts) { a.Status = AlertStatus.Resolved; a.ResolvedAt = now; a.AckBy ??= me.UserId; a.AckAt ??= now; }
        var care = await db.CareTasks.Where(t => t.AdmissionId == admissionId && t.Status == CareTaskStatus.Open).ToListAsync(ct);
        foreach (var t in care) t.Status = CareTaskStatus.Cancelled;

        await db.SaveChangesAsync(ct);
        if (bedId is not null) await beds.BroadcastAsync(bedId.Value);
        await notifier.ToHospitalAsync(admission.HospitalId, HubEvents.AdmissionUpdated, new { admissionId, status = AdmissionStatus.Discharged });
        return await admissions.GetHeaderAsync(admissionId, ct);
    }
}
