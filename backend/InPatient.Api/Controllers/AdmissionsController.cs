using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InPatient.Api.Controllers;

[ApiController]
[Route("api")]
public class AdmissionsController(AdmissionService admissions, BedService beds, WardHistoryService history) : ControllerBase
{
    [Authorize(Roles = Roles.Staff)]
    [HttpGet("patients")]
    public Task<List<PatientDto>> SearchPatients([FromQuery] string? q, CancellationToken ct) => admissions.SearchPatientsAsync(q, ct);

    [Authorize(Roles = Roles.FrontDesk)]
    [HttpPost("patients")]
    public Task<PatientDto> RegisterPatient(RegisterPatientRequest req, CancellationToken ct) => admissions.RegisterPatientAsync(req, ct);

    /// <summary>Admit: the chosen free bed becomes Reserved until the nurse scans bed + wristband.</summary>
    [Authorize(Roles = Roles.FrontDesk)]
    [HttpPost("admissions")]
    public Task<AdmitResponse> Admit(AdmitRequest req, CancellationToken ct) => admissions.AdmitAsync(req, ct);

    [Authorize(Roles = Roles.Staff)]
    [HttpGet("admissions")]
    public Task<List<AdmissionHeaderDto>> ListActive([FromQuery] int? wardId, CancellationToken ct) => admissions.ListActiveAsync(wardId, ct);

    [Authorize(Roles = "HospitalAdmin,Receptionist,Nurse,Consultant,Patient")]
    [HttpGet("admissions/{id:int}")]
    public Task<AdmissionHeaderDto> Get(int id, CancellationToken ct) => admissions.GetHeaderAsync(id, ct);

    [Authorize(Roles = Roles.FrontDesk)]
    [HttpGet("admissions/{id:int}/wristband")]
    public Task<WristbandDto> Wristband(int id, CancellationToken ct) => admissions.GetWristbandAsync(id, ct);

    [Authorize(Roles = "HospitalAdmin,Receptionist,Consultant")]
    [HttpPost("admissions/{id:int}/transfer")]
    public Task<AdmissionHeaderDto> Transfer(int id, TransferRequest req, CancellationToken ct) => beds.TransferAsync(id, req, ct);

    [Authorize(Roles = "HospitalAdmin,Receptionist,Nurse,Consultant,Patient")]
    [HttpGet("admissions/{id:int}/journey")]
    public Task<PatientJourneyDto> Journey(int id, CancellationToken ct) => history.GetJourneyAsync(id, ct);
}
