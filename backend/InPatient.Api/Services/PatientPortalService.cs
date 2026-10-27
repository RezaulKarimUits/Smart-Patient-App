using InPatient.Api.Data;
using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Services;

public record PatientMedicineDto(int DoseId, string Medicine, string Dose, string Route, DateTime DueAt, string Status, DateTime? GivenAt);

public record PatientStayDto(AdmissionHeaderDto Admission, string? ConsultantDesignation, string HospitalName, IReadOnlyList<PatientMedicineDto> TodaysMedicines,
    IReadOnlyList<LabDto> SignedReports, IReadOnlyList<FileDto> Files, string? DischargeSummary, IReadOnlyList<JourneyStayDto> Journey);

/// <summary>Read-only view for the Patient role: only their own admissions, only signed results.</summary>
public sealed class PatientPortalService(InPatientDbContext db, IClock clock, ICurrentUser me, AdmissionService admissions, ClinicalService clinical,
    FileService files, WardHistoryService history)
{
    private int PatientId => me.PatientId ?? throw ApiException.Forbidden("Patient login required");

    public async Task<List<AdmissionHeaderDto>> MyAdmissionsAsync(CancellationToken ct)
    {
        var pid = PatientId;
        var list = await admissions.GetHeadersAsync(db.Admissions.Where(a => a.PatientId == pid), ct);
        return list.OrderByDescending(a => a.Status == AdmissionStatus.Active).ThenByDescending(a => a.AdmittedAt).ToList();
    }

    public async Task<PatientStayDto> StayAsync(int admissionId, CancellationToken ct)
    {
        var pid = PatientId;
        var adm = await db.Admissions.Include(a => a.ConsultantUser).Include(a => a.Hospital).FirstOrDefaultAsync(a => a.Id == admissionId && a.PatientId == pid, ct)
            ?? throw ApiException.NotFound("Admission not found");
        var header = await admissions.GetHeaderAsync(adm.Id, ct);

        var today = Dhaka.Today(clock.UtcNow);
        var from = Dhaka.ToUtc(today.ToDateTime(TimeOnly.MinValue));
        var doses = await db.DoseTasks.Where(t => t.AdmissionId == adm.Id && t.DueAt >= from && t.DueAt < from.AddDays(1)).OrderBy(t => t.DueAt)
            .Select(t => new PatientMedicineDto(t.Id, (t.PrescriptionItem.Form != null ? t.PrescriptionItem.Form + ". " : "") + t.PrescriptionItem.DrugName,
                t.PrescriptionItem.Dose, t.PrescriptionItem.Route, t.DueAt,
                t.Status == DoseStatus.Given ? "Given" : t.Status == DoseStatus.Scheduled || t.Status == DoseStatus.Due ? "Pending" : t.Status, t.DoneAt))
            .ToListAsync(ct);
        var labs = (await clinical.GetLabsAsync(adm.Id, ct)).Results;          // signed only for patients
        var fileList = await files.ListAsync(adm.Id, null, ct);                 // signed reports + images
        var journey = (await history.GetJourneyAsync(adm.Id, ct)).Stays;
        return new PatientStayDto(header, adm.ConsultantUser.Designation, adm.Hospital.Name, doses, labs, fileList,
            adm.Status == AdmissionStatus.Discharged ? adm.DischargeSummary : null, journey);
    }
}
