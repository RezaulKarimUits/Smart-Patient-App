using InPatient.Api.Data;
using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Controllers;

[ApiController]
[Route("api")]
public class BedsController(BedService beds, WardHistoryService history, InPatientDbContext db) : ControllerBase
{
    /// <summary>Live bed board, all wards or one ward.</summary>
    [Authorize(Roles = Roles.BedBoard)]
    [HttpGet("board")]
    public Task<BoardDto> Board([FromQuery] int? wardId, CancellationToken ct) => beds.GetBoardAsync(wardId, ct);

    [Authorize(Roles = Roles.BedBoard)]
    [HttpGet("wards")]
    public Task<List<WardSummaryDto>> Wards(CancellationToken ct) => beds.GetWardSummariesAsync(ct);

    [Authorize(Roles = Roles.BedBoard)]
    [HttpGet("wards/{wardId:int}/consultants")]
    public async Task<List<StaffBriefDto>> WardConsultants(int wardId, CancellationToken ct)
    {
        var mapped = await db.ConsultantWards.Where(c => c.WardId == wardId)
            .Select(c => new StaffBriefDto(c.ConsultantUser.Id, c.ConsultantUser.Name, c.ConsultantUser.Designation)).ToListAsync(ct);
        var others = await db.AppUsers.Where(u => u.Role == Roles.Consultant && u.IsActive)
            .Select(u => new StaffBriefDto(u.Id, u.Name, u.Designation)).ToListAsync(ct);
        return mapped.Concat(others.Where(o => mapped.All(m => m.Id != o.Id))).ToList();
    }

    [Authorize(Roles = Roles.Reviewers)]
    [HttpGet("wards/{wardId:int}/history")]
    public Task<WardHistoryDto> WardHistory(int wardId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => history.GetAsync(wardId, from, to, ct);

    [Authorize(Roles = Roles.BedBoard)]
    [HttpGet("beds/{id:int}")]
    public async Task<ActionResult<BedTileDto>> Bed(int id, CancellationToken ct)
        => await beds.GetTileAsync(id, ct) is { } t ? t : NotFound(new { error = "Bed not found" });

    [Authorize(Roles = Roles.FrontDesk)]
    [HttpPost("beds/{id:int}/reserve")]
    public Task<BedTileDto> Reserve(int id, ReserveBedRequest req, CancellationToken ct) => beds.ReserveAsync(id, req.Note, ct);

    /// <summary>Lifecycle changes: Cleaning→Free, Free→Blocked, Blocked→Free, Reserved(no patient)→Free.</summary>
    [Authorize(Roles = "HospitalAdmin,Receptionist,Nurse")]
    [HttpPost("beds/{id:int}/status")]
    public Task<BedTileDto> SetStatus(int id, BedStatusChangeRequest req, CancellationToken ct) => beds.SetStatusAsync(id, req, ct);

    [Authorize(Roles = Roles.FrontDesk)]
    [HttpGet("beds/{id:int}/qr")]
    public Task<QrDto> BedQr(int id, CancellationToken ct) => beds.GetBedQrAsync(id, ct);

    [Authorize(Roles = Roles.FrontDesk)]
    [HttpGet("wards/{wardId:int}/qr")]
    public Task<List<QrDto>> WardQrs(int wardId, CancellationToken ct) => beds.GetWardQrsAsync(wardId, ct);
}

[ApiController]
[Route("api/scan")]
public class ScanController(BedService beds) : ControllerBase
{
    /// <summary>Is this token a bed QR or a wristband?</summary>
    [Authorize(Roles = Roles.BedBoard)]
    [HttpGet("resolve")]
    public Task<ScanResolveDto> Resolve([FromQuery] string token, CancellationToken ct) => beds.ResolveAsync(token, ct);

    /// <summary>Nurse scans the bed QR then the wristband: Reserved → Occupied.</summary>
    [Authorize(Roles = "Nurse,HospitalAdmin")]
    [HttpPost("confirm-bed")]
    public Task<AdmissionHeaderDto> ConfirmBed(ConfirmBedRequest req, CancellationToken ct) => beds.ConfirmBedAsync(req, ct);

    /// <summary>Scan wristband then new bed QR.</summary>
    [Authorize(Roles = "Nurse,HospitalAdmin")]
    [HttpPost("transfer")]
    public Task<AdmissionHeaderDto> Transfer(ScanTransferRequest req, CancellationToken ct) => beds.TransferByScanAsync(req, ct);
}
