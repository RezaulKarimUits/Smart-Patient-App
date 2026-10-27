using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InPatient.Api.Controllers;

/// <summary>Voice capture → draft → confirm. Nothing is saved to the clinical record until confirm.</summary>
[ApiController]
[Route("api/voice")]
public class VoiceController(VoiceService voice) : ControllerBase
{
    public const long MaxUpload = 21 * 1024 * 1024;

    /// <summary>Nurse bedside entry (vitals, medicines given, results, note). Multipart: audio (m4a/mp3/mp4/wav/webm/ogg ≤ 20 MB), admissionId.</summary>
    [Authorize(Roles = Roles.Clinical)]
    [HttpPost("nurse-entry")]
    [RequestSizeLimit(MaxUpload)]
    public Task<VoiceDraftResponse> NurseEntry([FromForm] int admissionId, IFormFile? audio, [FromForm] string? transcript, CancellationToken ct)
        => voice.CaptureAsync(VoicePurpose.NurseEntry, admissionId, audio, transcript, ct);

    [Authorize(Roles = Roles.Consultant)]
    [HttpPost("prescription")]
    [RequestSizeLimit(MaxUpload)]
    public Task<VoiceDraftResponse> Prescription([FromForm] int admissionId, IFormFile? audio, [FromForm] string? transcript, CancellationToken ct)
        => voice.CaptureAsync(VoicePurpose.Prescription, admissionId, audio, transcript, ct);

    [Authorize(Roles = Roles.Consultant)]
    [HttpPost("round-note")]
    [RequestSizeLimit(MaxUpload)]
    public Task<VoiceDraftResponse> RoundNote([FromForm] int admissionId, IFormFile? audio, [FromForm] string? transcript, CancellationToken ct)
        => voice.CaptureAsync(VoicePurpose.RoundNote, admissionId, audio, transcript, ct);

    /// <summary>Confirm the (possibly edited) draft. Nurse entries with medicines need the scanned wristband token.</summary>
    [Authorize(Roles = Roles.Clinical)]
    [HttpPost("{captureId:int}/confirm")]
    public Task<VoiceConfirmResponse> Confirm(int captureId, ConfirmVoiceRequest req, CancellationToken ct) => voice.ConfirmAsync(captureId, req, ct);

    [Authorize(Roles = Roles.Clinical)]
    [HttpPost("{captureId:int}/discard")]
    public async Task<IActionResult> Discard(int captureId, CancellationToken ct) { await voice.DiscardAsync(captureId, ct); return NoContent(); }
}

[ApiController]
[Route("api")]
public class FilesController(FileService files, ReportService reports) : ControllerBase
{
    /// <summary>Upload an image (progress photo, X-ray photo, report, prescription) with a caption.</summary>
    [Authorize(Roles = Roles.Clinical)]
    [HttpPost("admissions/{id:int}/files")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<FileDto> Upload(int id, IFormFile file, [FromForm] string kind, [FromForm] string? caption, CancellationToken ct)
        => files.ToDto(await files.UploadAsync(id, file, kind, caption, null, ct));

    /// <summary>Gallery. Each item carries a read-only SAS URL valid ≤ 10 minutes.</summary>
    [Authorize(Roles = "Nurse,Consultant,HospitalAdmin,Patient")]
    [HttpGet("admissions/{id:int}/files")]
    public Task<List<FileDto>> List(int id, [FromQuery] string? kind, CancellationToken ct) => files.ListAsync(id, kind, ct);

    [Authorize(Roles = "Nurse,Consultant,HospitalAdmin,Patient")]
    [HttpGet("files/{fileId:int}")]
    public Task<FileDto> Get(int fileId, CancellationToken ct) => files.GetAsync(fileId, ct);

    /// <summary>File bytes for a short-lived signed link taken from a FileDto url (expires within 10 minutes).</summary>
    [AllowAnonymous]
    [HttpGet("files/{fileId:int}/content")]
    public async Task<IActionResult> Content(int fileId, [FromQuery] long exp, [FromQuery] string? sig, CancellationToken ct)
    {
        var (bytes, contentType) = await files.ReadSignedAsync(fileId, exp, sig, ct);
        Response.Headers.CacheControl = "private, max-age=300";
        return File(bytes.ToArray(), contentType);
    }

    /// <summary>Photo of a lab report → stored → AI extraction → values for review (nothing saved to results yet).</summary>
    [Authorize(Roles = Roles.Clinical)]
    [HttpPost("admissions/{id:int}/reports/extract")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public Task<ReportExtractResponse> Extract(int id, IFormFile image, CancellationToken ct) => reports.ExtractAsync(id, image, ct);

    [Authorize(Roles = Roles.Clinical)]
    [HttpPost("admissions/{id:int}/reports/confirm")]
    public Task<LabsSaveResponse> ConfirmReport(int id, ReportConfirmRequest req, CancellationToken ct) => reports.ConfirmAsync(id, req, ct);
}

[ApiController]
[Route("api")]
public class InsightsController(AiProgressService progress, HandoverService handover, DischargeService discharge) : ControllerBase
{
    /// <summary>AI comparison of latest vs previous results with coded direction per parameter. Cached 15 minutes.</summary>
    [Authorize(Roles = Roles.Clinical)]
    [HttpPost("admissions/{id:int}/ai-progress")]
    public Task<AiProgressDto> Progress(int id, [FromQuery] bool refresh = false, CancellationToken ct = default) => progress.GetAsync(id, refresh, ct);

    [Authorize(Roles = Roles.Nurse)]
    [HttpGet("nurse/handover")]
    public Task<HandoverDto> Handover(CancellationToken ct) => handover.GetAsync(ct);

    [Authorize(Roles = Roles.Consultant)]
    [HttpPost("admissions/{id:int}/discharge/draft")]
    public Task<DischargeDraftDto> DischargeDraft(int id, CancellationToken ct) => discharge.DraftAsync(id, ct);

    /// <summary>Confirm discharge with the (edited) summary. Bed → Cleaning; summary visible to the patient.</summary>
    [Authorize(Roles = Roles.Consultant)]
    [HttpPost("admissions/{id:int}/discharge")]
    public Task<AdmissionHeaderDto> Discharge(int id, DischargeRequest req, CancellationToken ct) => discharge.DischargeAsync(id, req, ct);
}

[ApiController]
[Route("api/me")]
public class PatientPortalController(PatientPortalService portal) : ControllerBase
{
    [Authorize(Roles = Roles.Patient)]
    [HttpGet("admissions")]
    public Task<List<AdmissionHeaderDto>> Admissions(CancellationToken ct) => portal.MyAdmissionsAsync(ct);

    [Authorize(Roles = Roles.Patient)]
    [HttpGet("admissions/{id:int}")]
    public Task<PatientStayDto> Stay(int id, CancellationToken ct) => portal.StayAsync(id, ct);
}

public partial class DevController
{
    /// <summary>Runs the 15 sample transcripts through the structuring step and reports field-level accuracy.</summary>
    [HttpPost("voice-eval")]
    public Task<VoiceEvalReport> VoiceEval([FromServices] VoiceEvalService eval, CancellationToken ct) => eval.RunAsync(ct);
}
