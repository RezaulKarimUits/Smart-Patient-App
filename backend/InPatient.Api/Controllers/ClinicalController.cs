using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InPatient.Api.Controllers;

[ApiController]
[Route("api")]
public class ClinicalController(ClinicalService clinical) : ControllerBase
{
    [Authorize(Roles = Roles.Clinical)]
    [HttpPost("admissions/{id:int}/vitals")]
    public Task<VitalsSaveResponse> RecordVitals(int id, VitalsInput input, CancellationToken ct) => clinical.RecordVitalsAsync(id, input, ct);

    [Authorize(Roles = "Nurse,Consultant,HospitalAdmin")]
    [HttpGet("admissions/{id:int}/vitals")]
    public Task<List<VitalDto>> Vitals(int id, [FromQuery] int hours = 120, CancellationToken ct = default) => clinical.GetVitalsAsync(id, hours, ct);

    [Authorize(Roles = Roles.Clinical)]
    [HttpPost("admissions/{id:int}/labs")]
    public Task<LabsSaveResponse> RecordLabs(int id, List<LabInput> input, CancellationToken ct) => clinical.RecordLabsAsync(id, input, Sources.Form, null, ct);

    /// <summary>Lab results + comparison table (tests × dates). Patients only see signed-off results.</summary>
    [Authorize(Roles = "Nurse,Consultant,HospitalAdmin,Patient")]
    [HttpGet("admissions/{id:int}/labs")]
    public Task<LabsResponse> Labs(int id, CancellationToken ct) => clinical.GetLabsAsync(id, ct);

    [Authorize(Roles = Roles.Clinical)]
    [HttpPost("admissions/{id:int}/notes")]
    public Task<NoteDto> AddNote(int id, NoteInput input, CancellationToken ct) => clinical.AddNoteAsync(id, input, Sources.Form, null, ct);

    [Authorize(Roles = "Nurse,Consultant,HospitalAdmin")]
    [HttpGet("admissions/{id:int}/notes")]
    public Task<List<NoteDto>> Notes(int id, CancellationToken ct) => clinical.GetNotesAsync(id, ct);

    [Authorize(Roles = "Nurse,Consultant,HospitalAdmin,Patient")]
    [HttpGet("admissions/{id:int}/timeline")]
    public Task<List<TimelineItemDto>> Timeline(int id, [FromQuery] DateTime? before, [FromQuery] int limit = 150, CancellationToken ct = default)
        => clinical.GetTimelineAsync(id, before, limit, ct);

    /// <summary>Entries waiting for consultant review (vitals, labs, notes), grouped by patient.</summary>
    [Authorize(Roles = Roles.Consultant)]
    [HttpGet("signoff/pending")]
    public Task<List<PendingPatientDto>> Pending([FromQuery] int? admissionId, CancellationToken ct) => clinical.GetPendingAsync(admissionId, ct);

    [Authorize(Roles = Roles.Consultant)]
    [HttpPost("signoff")]
    public Task<SignOffResponse> SignOff(SignOffRequest req, CancellationToken ct) => clinical.SignOffAsync(req, ct);
}

[ApiController]
[Route("api")]
public class WorklistController(WorklistService worklist) : ControllerBase
{
    /// <summary>Nurse home: beds assigned this shift with due-dose, alert and task badges.</summary>
    [Authorize(Roles = Roles.Nurse)]
    [HttpGet("nurse/beds")]
    public Task<NurseHomeDto> NurseBeds(CancellationToken ct) => worklist.NurseHomeAsync(ct);

    [Authorize(Roles = Roles.Nurse)]
    [HttpPost("care-tasks/{id:int}/done")]
    public Task<CareTaskDto> DoneTask(int id, CancellationToken ct) => worklist.CompleteCareTaskAsync(id, ct);

    /// <summary>Consultant priority list across their wards.</summary>
    [Authorize(Roles = Roles.Consultant)]
    [HttpGet("consultant/patients")]
    public Task<List<PriorityPatientDto>> Priority(CancellationToken ct) => worklist.ConsultantPriorityAsync(ct);
}
