using System.Text.Json;
using InPatient.Api.Data;
using InPatient.Api.Data.Entities;
using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Infrastructure;
using InPatient.Api.Services.Ai;
using InPatient.Api.Services.Rules;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Services;

public record VoiceDraftResponse(int CaptureId, string Purpose, string Transcript, object Draft, IReadOnlyList<Issue> Warnings, string? ClarificationQuestion,
    bool RequiresWristband, IReadOnlyList<ActiveItem> ActiveItems, int? AudioFileId);

public record ConfirmVoiceRequest(JsonElement Draft, string? WristbandToken);

public record NurseSavedDto(int? VitalId, int? News2Score, string? News2Band, IReadOnlyList<DoseTaskDto> Doses, IReadOnlyList<int> LabIds, int? NoteId);

public record VoiceConfirmResponse(int CaptureId, string Purpose, NurseSavedDto? Nurse, PrescriptionDto? Prescription, NoteDto? Note, MonitoringDto? Monitoring);

/// <summary>
/// Voice pipeline: validate file → store audio → hint → transcribe → structure (strict schema) → validate in code → Drafted capture.
/// Only <see cref="ConfirmAsync"/> writes clinical records.
/// </summary>
public sealed class VoiceService(InPatientDbContext db, IClock clock, ICurrentUser me, AdmissionService admissions, FileService files,
    ISpeechToText stt, IStructuredAi ai, ClinicalService clinical, MedicationService meds, PrescriptionService prescriptions, ILogger<VoiceService> log)
{
    public static readonly string[] WardVocabulary = ["BP", "pulse", "SpO2", "saline", "injection", "nebulization", "CBC", "haemoglobin", "platelet", "creatinine"];

    public async Task<VoiceDraftResponse> CaptureAsync(string purpose, int admissionId, IFormFile? audio, string? typedTranscript, CancellationToken ct)
    {
        if (!ai.IsConfigured || (audio is not null && !stt.IsConfigured)) throw new AiNotConfiguredException();
        if (purpose is VoicePurpose.Prescription or VoicePurpose.RoundNote && me.Role != Roles.Consultant) throw ApiException.Forbidden("Consultants only");
        if (purpose == VoicePurpose.NurseEntry && me.Role is not (Roles.Nurse or Roles.Consultant)) throw ApiException.Forbidden();
        if (audio is null && string.IsNullOrWhiteSpace(typedTranscript)) throw ApiException.BadRequest("Send an audio recording");

        var admission = await admissions.GetActiveAsync(admissionId, ct);
        var ctx = await BuildContextAsync(admission, ct);

        int? audioId = null;
        string transcript;
        if (audio is not null)
        {
            var (file, bytes, name) = await files.UploadAudioAsync(admission, audio, ct);  // validated + kept for audit
            audioId = file.Id;
            transcript = await stt.TranscribeAsync(bytes, name, BuildHint(ctx), ct);
        }
        else transcript = typedTranscript!.Trim();
        if (string.IsNullOrWhiteSpace(transcript)) throw ApiException.Unprocessable("No speech was recognised — please record again");

        object draft;
        IReadOnlyList<Issue> issues;
        string? question;
        var requiresWristband = false;
        switch (purpose)
        {
            case VoicePurpose.NurseEntry:
            {
                var raw = await StructureNurseAsync(transcript, ctx, ct);
                var v = VoiceDraftValidator.ValidateNurse(raw, ctx.Items);
                (draft, issues, question, requiresWristband) = (v.Draft, v.Issues, v.Draft.ClarificationQuestion, v.RequiresWristband);
                break;
            }
            case VoicePurpose.Prescription:
            {
                var raw = await StructurePrescriptionAsync(transcript, ctx, ct);
                var v = VoiceDraftValidator.ValidatePrescription(raw);
                (draft, issues, question) = (v.Draft, v.Issues, v.Draft.ClarificationQuestion);
                break;
            }
            default:
            {
                var raw = await ai.GenerateAsync<RoundNoteDraft>(new AiJsonRequest("round_note", AiSchemas.RoundNote, AiPrompts.RoundNote + "\n" + AiPrompts.Safety,
                    ctx.Prompt + "\n\nTRANSCRIPT:\n" + transcript), ct);
                var list = new List<Issue>();
                if (string.IsNullOrWhiteSpace(raw.Note)) list.Add(Issue.Block("note", "No note content recognised"));
                (draft, issues, question) = (raw, list, raw.ClarificationQuestion);
                break;
            }
        }

        var capture = new VoiceCapture
        {
            HospitalId = admission.HospitalId, AdmissionId = admission.Id, UserId = me.RequireUserId(), Purpose = purpose, AudioFileId = audioId,
            Transcript = transcript, DraftJson = JsonSerializer.Serialize(draft, Json.Options), Status = VoiceStatus.Drafted, CreatedAt = clock.UtcNow
        };
        db.VoiceCaptures.Add(capture);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Voice capture {CaptureId} {Purpose} drafted for admission {AdmissionId} with {Issues} issue(s)", capture.Id, purpose, admission.Id, issues.Count);
        
        return new VoiceDraftResponse(capture.Id, purpose, transcript, draft, issues, question, requiresWristband, ctx.Items, audioId);
    }

    public Task<NurseDraft> StructureNurseAsync(string transcript, VoiceContext ctx, CancellationToken ct)
        => ai.GenerateAsync<NurseDraft>(new AiJsonRequest("nurse_entry", AiSchemas.NurseEntry, AiPrompts.NurseEntry + "\n" + AiPrompts.Safety,
            ctx.Prompt + "\n\nTRANSCRIPT:\n" + transcript), ct);

    public Task<PrescriptionDraft> StructurePrescriptionAsync(string transcript, VoiceContext ctx, CancellationToken ct)
        => ai.GenerateAsync<PrescriptionDraft>(new AiJsonRequest("prescription", AiSchemas.Prescription, AiPrompts.Prescription + "\n" + AiPrompts.Safety,
            ctx.Prompt + "\n\nTRANSCRIPT:\n" + transcript), ct);

    public record VoiceContext(IReadOnlyList<ActiveItem> Items, string Prompt);

    /// <summary>Context for the model: no patient name, only bed/age/sex, Dhaka time and the active items with ids.</summary>
    public async Task<VoiceContext> BuildContextAsync(Admission admission, CancellationToken ct)
    {
        var info = await db.Admissions.Where(a => a.Id == admission.Id).Select(a => new
        {
            a.Patient.DateOfBirth, a.Patient.Sex, Bed = a.CurrentBed != null ? a.CurrentBed.BedNumber : null, Ward = a.CurrentBed != null ? a.CurrentBed.Ward.Name : null
        }).FirstAsync(ct);
        var items = await ActiveItemsAsync(admission.Id, ct);
        return new VoiceContext(items, BuildPrompt(clock.UtcNow, Dhaka.AgeYears(info.DateOfBirth, clock.UtcNow), info.Sex, info.Bed, info.Ward, items));
    }

    public static string BuildPrompt(DateTime nowUtc, int age, string sex, string? bed, string? ward, IReadOnlyList<ActiveItem> items)
    {
        var list = items.Count == 0 ? "(none)" : string.Join("\n", items.Select(i => $"- id {i.Id}: {i.Form} {i.DrugName} {i.Dose} {i.Route} {i.Frequency}".Replace("  ", " ")));
        return $"CURRENT TIME (Asia/Dhaka): {Dhaka.ToLocal(nowUtc):yyyy-MM-dd HH:mm}\nPATIENT: {age} y, {sex}, bed {bed ?? "-"}, {ward ?? ""}\nACTIVE PRESCRIPTION ITEMS:\n{list}";
    }

    public static string BuildHint(VoiceContext ctx)
        => "Hospital ward report in English. Medicines: " + string.Join(", ", ctx.Items.Select(i => i.DrugName).Distinct())
           + ". Words: " + string.Join(", ", WardVocabulary) + ".";

    public async Task<List<ActiveItem>> ActiveItemsAsync(int admissionId, CancellationToken ct)
        => await db.PrescriptionItems.Where(i => i.Prescription.AdmissionId == admissionId && i.Prescription.Status == PrescriptionStatus.Signed && i.IsActive)
            .OrderBy(i => i.Id).Select(i => new ActiveItem(i.Id, i.DrugName, i.Form, i.Strength, i.Dose, i.Route, i.Frequency)).ToListAsync(ct);

    // ---------- Confirm / discard ----------

    public async Task<VoiceConfirmResponse> ConfirmAsync(int captureId, ConfirmVoiceRequest req, CancellationToken ct)
    {
        var capture = await LoadOwnDraftAsync(captureId, ct);
        var admission = await admissions.GetActiveAsync(capture.AdmissionId, ct);
        VoiceConfirmResponse result;
        switch (capture.Purpose)
        {
            case VoicePurpose.NurseEntry:
                result = await ConfirmNurseAsync(capture, admission, Parse<NurseDraft>(req.Draft), req.WristbandToken, ct);
                break;
            case VoicePurpose.Prescription:
            {
                var v = VoiceDraftValidator.ValidatePrescription(Parse<PrescriptionDraft>(req.Draft));
                ThrowIfBlocked(v.Issues);
                var rx = await prescriptions.CreateDraftAsync(admission.Id, new SavePrescriptionRequest(v.Draft.GeneralInstructions,
                    v.Draft.Items.Select(i => new PrescriptionItemInput(i.DrugName, i.Form, i.Strength, i.Dose!, i.Route!, i.Frequency, i.DurationDays, i.Instructions, null)).ToList()),
                    Sources.Voice, ct);
                capture.FinalJson = JsonSerializer.Serialize(v.Draft, Json.Options);
                result = new VoiceConfirmResponse(capture.Id, capture.Purpose, null, rx, null, null);
                break;
            }
            default:
            {
                var d = Parse<RoundNoteDraft>(req.Draft);
                if (string.IsNullOrWhiteSpace(d.Note)) throw ApiException.Unprocessable("Note is empty");
                var text = d.Note.Trim() + (string.IsNullOrWhiteSpace(d.Plan) ? "" : $"\nPlan: {d.Plan.Trim()}");
                var note = await clinical.AddNoteAsync(admission.Id, new NoteInput(NoteTypes.ConsultantRound, text), Sources.Voice, capture.AudioFileId, ct);
                capture.FinalJson = JsonSerializer.Serialize(d, Json.Options);
                result = new VoiceConfirmResponse(capture.Id, capture.Purpose, null, null, note, null);
                break;
            }
        }
        capture.Status = VoiceStatus.Confirmed;
        capture.ConfirmedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<VoiceConfirmResponse> ConfirmNurseAsync(VoiceCapture capture, Admission admission, NurseDraft edited, string? wristband, CancellationToken ct)
    {
        var items = await ActiveItemsAsync(admission.Id, ct);
        var v = VoiceDraftValidator.ValidateNurse(edited, items);
        ThrowIfBlocked(v.Issues);
        var d = v.Draft;
        if (d.Medications.Count > 0) WristbandGuard.EnsureMatches(admission.WristbandToken, wristband);

        var now = clock.UtcNow;
        var uid = me.RequireUserId();
        var dob = await db.Patients.Where(p => p.Id == admission.PatientId).Select(p => p.DateOfBirth).FirstAsync(ct);

        VitalSign? vital = null;
        var vi = d.Vitals;
        if (vi.Systolic != null || vi.Diastolic != null || vi.Pulse != null || vi.RespRate != null || vi.Temperature != null || vi.Spo2 != null)
        {
            var (entity, issues) = clinical.BuildVital(admission, dob,
                new VitalsInput(vi.Systolic, vi.Diastolic, vi.Pulse, vi.RespRate, vi.Temperature, vi.TemperatureUnit ?? "C", vi.Spo2, vi.OnOxygen, vi.Consciousness, null), Sources.Voice);
            ThrowIfBlocked(issues);
            vital = entity;
            db.VitalSigns.Add(entity);
        }

        var doseTasks = new List<DoseTask>();
        foreach (var m in d.Medications)
        {
            var item = items.First(i => i.Id == m.PrescriptionItemId);
            var target = ResolveTime(m.Time, now);
            var status = char.ToUpperInvariant(m.Status[0]) + m.Status[1..];
            DoseTask task;
            if (item.Frequency == Frequencies.PRN)
            {
                task = new DoseTask { HospitalId = admission.HospitalId, PrescriptionItemId = item.Id, AdmissionId = admission.Id, DueAt = target, Status = DoseStatus.Due, CreatedAt = now };
                db.DoseTasks.Add(task);
            }
            else
            {
                var candidates = await db.DoseTasks.Where(t => t.PrescriptionItemId == item.Id && (DoseStatus.Open.Contains(t.Status) || t.Status == DoseStatus.Missed)
                        && t.DueAt >= target.AddHours(-3) && t.DueAt <= target.AddHours(3)).ToListAsync(ct);
                task = candidates.OrderBy(t => Math.Abs((t.DueAt - target).TotalMinutes)).FirstOrDefault()
                    ?? throw ApiException.Unprocessable($"No scheduled dose of {item.DrugName} near {Dhaka.FormatTime(target)} — record it from the medication round");
            }
            MedicationService.ApplyRecord(task, status, m.Reason, now, uid);
            doseTasks.Add(task);
        }

        var (labs, labIssues, _) = d.LabResults.Count == 0 ? ([], [], []) :
            clinical.BuildLabs(admission, d.LabResults.Select(l => new LabInput(l.TestName, l.Value, null, l.Unit, null, null, null)).ToList(), Sources.Voice, null);
        ThrowIfBlocked(labIssues);
        db.LabResults.AddRange(labs);

        ClinicalNote? note = null;
        if (!string.IsNullOrWhiteSpace(d.Note))
        {
            note = new ClinicalNote
            {
                HospitalId = admission.HospitalId, AdmissionId = admission.Id, AuthorUserId = uid, NoteType = NoteTypes.Nursing, Text = d.Note!,
                Source = Sources.Voice, AudioFileId = capture.AudioFileId, CreatedAt = now
            };
            db.ClinicalNotes.Add(note);
        }

        capture.FinalJson = JsonSerializer.Serialize(d, Json.Options);
        await db.SaveChangesAsync(ct); // one transaction for everything confirmed

        var doseDtos = new List<DoseTaskDto>();
        foreach (var t in doseTasks)
        {
            await db.Entry(t).Reference(x => x.PrescriptionItem).LoadAsync(ct);
            await meds.AfterRecordAsync(t, ct);
            doseDtos.Add(meds.ToDto(t));
        }
        var monitoring = await clinical.AfterEntryAsync(admission, vitalsRecorded: vital is not null, ct);
        return new VoiceConfirmResponse(capture.Id, capture.Purpose,
            new NurseSavedDto(vital?.Id, vital?.News2Score, vital?.News2Band, doseDtos, labs.Select(l => l.Id).ToList(), note?.Id),
            null, null, ClinicalService.ToDto(monitoring));
    }

    /// <summary>"HH:mm" Dhaka → the matching instant nearest to now (handles "at 10" around midnight).</summary>
    public static DateTime ResolveTime(string? hhmm, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(hhmm) || !TimeOnly.TryParse(hhmm, out var t)) return nowUtc;
        var localNow = Dhaka.ToLocal(nowUtc);
        var candidates = new[] { -1, 0, 1 }.Select(d => Dhaka.ToUtc(localNow.Date.AddDays(d).Add(t.ToTimeSpan())));
        return candidates.OrderBy(c => Math.Abs((c - nowUtc).TotalMinutes)).First();
    }

    public async Task DiscardAsync(int captureId, CancellationToken ct)
    {
        var c = await LoadOwnDraftAsync(captureId, ct);
        c.Status = VoiceStatus.Discarded; // audio is kept for audit
        await db.SaveChangesAsync(ct);
    }

    private async Task<VoiceCapture> LoadOwnDraftAsync(int captureId, CancellationToken ct)
    {
        var c = await db.VoiceCaptures.FirstOrDefaultAsync(x => x.Id == captureId, ct) ?? throw ApiException.NotFound("Voice capture not found");
        if (c.UserId != me.UserId) throw ApiException.Forbidden("Only the person who recorded this can confirm it");
        if (c.Status != VoiceStatus.Drafted) throw ApiException.Conflict($"This capture is already {c.Status.ToLowerInvariant()}");
        return c;
    }

    private static T Parse<T>(JsonElement el)
    {
        try { return el.Deserialize<T>(Json.Options) ?? throw ApiException.BadRequest("Draft is empty"); }
        catch (JsonException ex) { throw ApiException.BadRequest("Draft is not valid: " + ex.Message); }
    }

    private static void ThrowIfBlocked(IEnumerable<Issue> issues)
    {
        var blockers = issues.Where(i => i.Level == Issue.Blocker).ToList();
        if (blockers.Count > 0) throw ApiException.Unprocessable(string.Join("; ", blockers.Select(b => b.Message)));
    }
}
