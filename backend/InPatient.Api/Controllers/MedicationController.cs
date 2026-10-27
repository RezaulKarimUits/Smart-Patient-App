using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InPatient.Api.Controllers;

[ApiController]
[Route("api")]
public class PrescriptionsController(PrescriptionService prescriptions) : ControllerBase
{
    [Authorize(Roles = "Nurse,Consultant,HospitalAdmin,Patient")]
    [HttpGet("admissions/{admissionId:int}/prescriptions")]
    public Task<List<PrescriptionDto>> List(int admissionId, CancellationToken ct) => prescriptions.ListAsync(admissionId, ct);

    /// <summary>Form prescription, saved as Draft. Sign to activate.</summary>
    [Authorize(Roles = Roles.Consultant)]
    [HttpPost("admissions/{admissionId:int}/prescriptions")]
    public Task<PrescriptionDto> Create(int admissionId, SavePrescriptionRequest req, CancellationToken ct)
        => prescriptions.CreateDraftAsync(admissionId, req, Sources.Form, ct);

    [Authorize(Roles = "Nurse,Consultant,HospitalAdmin")]
    [HttpGet("prescriptions/{id:int}")]
    public Task<PrescriptionDto> Get(int id, CancellationToken ct) => prescriptions.GetAsync(id, ct);

    [Authorize(Roles = Roles.Consultant)]
    [HttpPut("prescriptions/{id:int}")]
    public Task<PrescriptionDto> Update(int id, SavePrescriptionRequest req, CancellationToken ct) => prescriptions.UpdateDraftAsync(id, req, ct);

    [Authorize(Roles = Roles.Consultant)]
    [HttpDelete("prescriptions/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) { await prescriptions.DeleteDraftAsync(id, ct); return NoContent(); }

    /// <summary>Sign → dose tasks are generated in Asia/Dhaka time.</summary>
    [Authorize(Roles = Roles.Consultant)]
    [HttpPost("prescriptions/{id:int}/sign")]
    public Task<PrescriptionDto> Sign(int id, CancellationToken ct) => prescriptions.SignAsync(id, ct);

    /// <summary>Attach an image (prescription photo, report or progress photo) to the prescription.</summary>
    [Authorize(Roles = Roles.Consultant)]
    [HttpPost("prescriptions/{id:int}/files")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public Task<FileDto> Attach(int id, IFormFile file, [FromForm] string? kind, [FromForm] string? caption, CancellationToken ct)
        => prescriptions.AttachAsync(id, file, kind, caption, ct);

    [Authorize(Roles = Roles.Consultant)]
    [HttpPost("prescription-items/{itemId:int}/stop")]
    public Task<PrescriptionItemDto> Stop(int itemId, CancellationToken ct) => prescriptions.StopItemAsync(itemId, ct);
}

[ApiController]
[Route("api")]
public class DosesController(MedicationService meds) : ControllerBase
{
    [Authorize(Roles = "Nurse,Consultant,HospitalAdmin,Patient")]
    [HttpGet("admissions/{admissionId:int}/doses")]
    public Task<DoseScheduleDto> Schedule(int admissionId, [FromQuery] DateOnly? date, CancellationToken ct) => meds.GetScheduleAsync(admissionId, date, ct);

    /// <summary>Nurse medication round: due + overdue doses for my beds this shift.</summary>
    [Authorize(Roles = Roles.Nurse)]
    [HttpGet("nurse/medication-round")]
    public Task<List<RoundPatientDto>> Round(CancellationToken ct) => meds.GetRoundAsync(ct);

    /// <summary>Record a dose. Requires the scanned wristband token; mismatch → 409 "Wrong patient".</summary>
    [Authorize(Roles = Roles.Nurse)]
    [HttpPost("doses/{taskId:int}/record")]
    public Task<DoseTaskDto> Record(int taskId, RecordDoseRequest req, CancellationToken ct) => meds.RecordAsync(taskId, req, ct);

    [Authorize(Roles = Roles.Nurse)]
    [HttpPost("prescription-items/{itemId:int}/prn-dose")]
    public Task<DoseTaskDto> Prn(int itemId, RecordPrnRequest req, CancellationToken ct) => meds.RecordPrnAsync(itemId, req, ct);
}

[ApiController]
[Route("api/alerts")]
public class AlertsController(AlertService alerts) : ControllerBase
{
    /// <summary>Alerts for my wards (consultant), my beds (nurse) or one admission.</summary>
    [Authorize(Roles = "Nurse,Consultant,HospitalAdmin")]
    [HttpGet]
    public Task<List<AlertDto>> List([FromQuery] bool active = true, [FromQuery] int? admissionId = null, CancellationToken ct = default)
        => alerts.ListForMeAsync(active, admissionId, ct);

    [Authorize(Roles = "Nurse,Consultant")]
    [HttpPost("{id:int}/ack")]
    public Task<AlertDto> Ack(int id, CancellationToken ct) => alerts.AcknowledgeAsync(id, ct);

    [Authorize(Roles = Roles.Consultant)]
    [HttpPost("{id:int}/resolve")]
    public Task<AlertDto> Resolve(int id, CancellationToken ct) => alerts.ResolveAsync(id, ct);
}
