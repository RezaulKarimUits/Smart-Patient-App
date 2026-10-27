using InPatient.Api.Data;
using InPatient.Api.Data.Entities;
using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Hubs;
using InPatient.Api.Infrastructure;
using InPatient.Api.Services.Rules;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Services;

public sealed class PrescriptionService(InPatientDbContext db, IClock clock, ICurrentUser me, AdmissionService admissions, FileService files,
    CareTeamService team, INotifier notifier)
{
    public static readonly string[] Routes = ["PO", "IV", "IM", "SC", "Neb", "PR", "SL", "Topical", "Inhaled", "Other"];

    public static List<string> Validate(IReadOnlyList<PrescriptionItemInput> items)
    {
        var errors = new List<string>();
        if (items.Count == 0) errors.Add("Add at least one medicine");
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var n = string.IsNullOrWhiteSpace(it.DrugName) ? $"Item {i + 1}" : it.DrugName;
            if (string.IsNullOrWhiteSpace(it.DrugName)) errors.Add($"Item {i + 1}: drug name is required");
            if (string.IsNullOrWhiteSpace(it.Dose)) errors.Add($"{n}: dose is required (never assumed)");
            if (string.IsNullOrWhiteSpace(it.Route)) errors.Add($"{n}: route is required");
            if (!Frequencies.All.Contains(it.Frequency)) errors.Add($"{n}: frequency must be one of {string.Join(", ", Frequencies.All)}");
            if (it.DurationDays is not null and (< 1 or > 90)) errors.Add($"{n}: duration must be 1-90 days");
        }
        return errors;
    }

    public async Task<PrescriptionDto> CreateDraftAsync(int admissionId, SavePrescriptionRequest req, string source, CancellationToken ct)
    {
        var admission = await admissions.GetActiveAsync(admissionId, ct);
        var errors = Validate(req.Items);
        if (errors.Count > 0) throw ApiException.Unprocessable(string.Join("; ", errors));
        var now = clock.UtcNow;
        var p = new Prescription
        {
            HospitalId = admission.HospitalId, AdmissionId = admission.Id, ConsultantUserId = me.RequireUserId(), Source = source,
            Status = PrescriptionStatus.Draft, GeneralInstructions = req.GeneralInstructions?.Trim(), CreatedAt = now
        };
        AddItems(p, req.Items, now);
        db.Prescriptions.Add(p);
        await db.SaveChangesAsync(ct);
        return await GetAsync(p.Id, ct);
    }

    private static void AddItems(Prescription p, IEnumerable<PrescriptionItemInput> items, DateTime now)
    {
        foreach (var it in items)
        {
            p.PrescriptionItems.Add(new PrescriptionItem
            {
                HospitalId = p.HospitalId, DrugName = it.DrugName.Trim(), Form = Clean(it.Form), Strength = Clean(it.Strength), Dose = it.Dose.Trim(),
                Route = it.Route.Trim(), Frequency = it.Frequency, DurationDays = it.DurationDays, Instructions = Clean(it.Instructions),
                StartAt = it.StartAt is { } s && s > now ? DateTime.SpecifyKind(s, DateTimeKind.Utc) : now, IsActive = true, CreatedAt = now
            });
        }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public async Task<PrescriptionDto> UpdateDraftAsync(int prescriptionId, SavePrescriptionRequest req, CancellationToken ct)
    {
        var p = await LoadOwnDraftAsync(prescriptionId, ct);
        var errors = Validate(req.Items);
        if (errors.Count > 0) throw ApiException.Unprocessable(string.Join("; ", errors));
        db.PrescriptionItems.RemoveRange(p.PrescriptionItems);
        p.PrescriptionItems.Clear();
        p.GeneralInstructions = req.GeneralInstructions?.Trim();
        AddItems(p, req.Items, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return await GetAsync(p.Id, ct);
    }

    public async Task DeleteDraftAsync(int prescriptionId, CancellationToken ct)
    {
        var p = await LoadOwnDraftAsync(prescriptionId, ct);
        var attached = await db.StoredFiles.Where(f => f.PrescriptionId == p.Id).ToListAsync(ct);
        foreach (var f in attached) f.PrescriptionId = null; // keep images on the admission
        db.Prescriptions.Remove(p);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Prescription> LoadOwnDraftAsync(int prescriptionId, CancellationToken ct)
    {
        var p = await db.Prescriptions.Include(x => x.PrescriptionItems).FirstOrDefaultAsync(x => x.Id == prescriptionId, ct)
            ?? throw ApiException.NotFound("Prescription not found");
        if (p.Status != PrescriptionStatus.Draft) throw ApiException.Conflict("A signed prescription cannot be edited - stop items or write a new one");
        if (p.ConsultantUserId != me.UserId) throw ApiException.Forbidden("Only the prescribing consultant can change this draft");
        return p;
    }

    /// <summary>Signing makes the prescription active and generates DoseTasks (Asia/Dhaka schedule).</summary>
    public async Task<PrescriptionDto> SignAsync(int prescriptionId, CancellationToken ct)
    {
        var p = await LoadOwnDraftAsync(prescriptionId, ct);
        var admission = await admissions.GetActiveAsync(p.AdmissionId, ct);
        var now = clock.UtcNow;
        p.Status = PrescriptionStatus.Signed;
        p.SignedAt = now;

        var tasks = new List<DoseTask>();
        foreach (var item in p.PrescriptionItems)
        {
            if (item.StartAt < now) item.StartAt = now;
            foreach (var due in DoseScheduler.Schedule(item.Frequency, item.StartAt, item.DurationDays, item.Instructions))
            {
                var t = new DoseTask
                {
                    HospitalId = p.HospitalId, PrescriptionItem = item, AdmissionId = p.AdmissionId, DueAt = due, CreatedAt = now,
                    Status = due <= now.AddMinutes(15) ? DoseStatus.Due : DoseStatus.Scheduled
                };
                tasks.Add(t);
                db.DoseTasks.Add(t);
            }
        }
        await db.SaveChangesAsync(ct);

        // Nurses caring for this bed see the new tasks immediately.
        if (admission.CurrentBedId is not null)
        {
            var nurses = await team.NursesForBedAsync(admission.CurrentBedId.Value, ct);
            await notifier.ToUsersAsync(nurses, HubEvents.TasksChanged, new { admissionId = admission.Id, reason = "prescriptionSigned", newTasks = tasks.Count });
            var dueNow = tasks.Where(t => t.Status == DoseStatus.Due).ToList();
            if (dueNow.Count > 0)
                await notifier.ToUsersAsync(nurses, HubEvents.DoseDue, new
                {
                    admissionId = admission.Id, taskIds = dueNow.Select(t => t.Id), count = dueNow.Count,
                    message = $"{dueNow.Count} new dose(s) due now"
                });
        }
        await admissions.NotifyAdmissionUpdatedAsync(admission);
        var dto = await GetAsync(p.Id, ct);
        return dto with { DoseTasksCreated = tasks.Count };
    }

    /// <summary>Stop an active item: future doses are removed.</summary>
    public async Task<PrescriptionItemDto> StopItemAsync(int itemId, CancellationToken ct)
    {
        var item = await db.PrescriptionItems.Include(i => i.Prescription).FirstOrDefaultAsync(i => i.Id == itemId, ct) ?? throw ApiException.NotFound("Item not found");
        await admissions.GetActiveAsync(item.Prescription.AdmissionId, ct);
        if (!item.IsActive) return ToItemDto(item);
        var now = clock.UtcNow;
        item.IsActive = false;
        item.StoppedAt = now;
        var future = await db.DoseTasks.Where(t => t.PrescriptionItemId == itemId && (t.Status == DoseStatus.Scheduled || t.Status == DoseStatus.Due) && t.DueAt > now).ToListAsync(ct);
        db.DoseTasks.RemoveRange(future);
        await db.SaveChangesAsync(ct);
        return ToItemDto(item);
    }

    public async Task<List<PrescriptionDto>> ListAsync(int admissionId, CancellationToken ct)
    {
        await admissions.GetAccessibleAsync(admissionId, ct);
        var ids = await db.Prescriptions.Where(p => p.AdmissionId == admissionId)
            .Where(p => me.Role != Roles.Patient || p.Status == PrescriptionStatus.Signed)
            .OrderByDescending(p => p.CreatedAt).Select(p => p.Id).ToListAsync(ct);
        var list = new List<PrescriptionDto>();
        foreach (var id in ids) list.Add(await GetAsync(id, ct));
        return list;
    }

    public async Task<PrescriptionDto> GetAsync(int prescriptionId, CancellationToken ct)
    {
        var p = await db.Prescriptions.Include(x => x.PrescriptionItems).Include(x => x.ConsultantUser)
            .Include(x => x.StoredFiles).ThenInclude(f => f.UploadedByNavigation)
            .FirstOrDefaultAsync(x => x.Id == prescriptionId, ct) ?? throw ApiException.NotFound("Prescription not found");
        await admissions.GetAccessibleAsync(p.AdmissionId, ct);
        return new PrescriptionDto(p.Id, p.AdmissionId, p.ConsultantUserId, p.ConsultantUser.Name, p.Source, p.Status, p.CreatedAt, p.SignedAt, p.GeneralInstructions,
            p.PrescriptionItems.OrderBy(i => i.Id).Select(ToItemDto).ToList(), p.StoredFiles.OrderBy(f => f.UploadedAt).Select(files.ToDto).ToList(), 0);
    }

    public async Task<FileDto> AttachAsync(int prescriptionId, IFormFile file, string? kind, string? caption, CancellationToken ct)
    {
        var p = await db.Prescriptions.FirstOrDefaultAsync(x => x.Id == prescriptionId, ct) ?? throw ApiException.NotFound("Prescription not found");
        var f = await files.UploadAsync(p.AdmissionId, file, string.IsNullOrWhiteSpace(kind) ? FileKinds.Prescription : kind, caption, p.Id, ct);
        return files.ToDto(f);
    }

    public static string Summarize(PrescriptionItem i)
        => string.Join(" ", new[] { i.Form, i.DrugName, i.Strength != i.Dose ? i.Strength : null, i.Dose, i.Route, i.Frequency,
            i.DurationDays is { } d ? $"× {d} days" : null, string.IsNullOrWhiteSpace(i.Instructions) ? null : $"({i.Instructions})" }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    public static PrescriptionItemDto ToItemDto(PrescriptionItem i) => new(i.Id, i.DrugName, i.Form, i.Strength, i.Dose, i.Route, i.Frequency, i.StartAt,
        i.DurationDays, i.Instructions, i.IsActive, i.StoppedAt, Summarize(i));
}
