using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InPatient.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = Roles.HospitalAdmin)]
public class AdminController(AdminService admin) : ControllerBase
{
    [HttpGet("wards")] public Task<List<AdminWardDto>> Wards(CancellationToken ct) => admin.WardsAsync(ct);
    [HttpPost("wards")] public Task<AdminWardDto> CreateWard(SaveWardRequest req, CancellationToken ct) => admin.SaveWardAsync(null, req, ct);
    [HttpPut("wards/{id:int}")] public Task<AdminWardDto> UpdateWard(int id, SaveWardRequest req, CancellationToken ct) => admin.SaveWardAsync(id, req, ct);
    [HttpGet("wards/{id:int}/beds")] public Task<List<AdminBedDto>> Beds(int id, CancellationToken ct) => admin.WardBedsAsync(id, ct);
    [HttpPost("wards/{id:int}/beds")] public Task<List<AdminBedDto>> AddBeds(int id, AddBedsRequest req, CancellationToken ct) => admin.AddBedsAsync(id, req, ct);
    [HttpPost("beds/{id:int}/regenerate-qr")] public Task<QrDto> RegenerateQr(int id, CancellationToken ct) => admin.RegenerateQrAsync(id, ct);

    [HttpGet("staff")] public Task<List<StaffDto>> Staff([FromQuery] string? role, CancellationToken ct) => admin.StaffAsync(role, ct);
    [HttpPost("staff")] public Task<StaffDto> CreateStaff(CreateStaffRequest req, CancellationToken ct) => admin.CreateStaffAsync(req, ct);
    [HttpPut("staff/{id:int}")] public Task<StaffDto> UpdateStaff(int id, UpdateStaffRequest req, CancellationToken ct) => admin.UpdateStaffAsync(id, req, ct);
    [HttpPost("staff/{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest req, CancellationToken ct) { await admin.ResetPasswordAsync(id, req, ct); return NoContent(); }

    /// <summary>Consultant–ward mapping (replaces the consultant's set of wards).</summary>
    [HttpPut("consultants/{id:int}/wards")]
    public Task<StaffDto> ConsultantWards(int id, ConsultantWardsRequest req, CancellationToken ct) => admin.SetConsultantWardsAsync(id, req, ct);

    [HttpGet("shifts")] public Task<List<ShiftDto>> Shifts([FromQuery] DateOnly? date, [FromQuery] int? wardId, CancellationToken ct) => admin.ShiftsAsync(date, wardId, ct);
    [HttpPost("shifts")] public Task<ShiftDto> SaveShift(SaveShiftRequest req, CancellationToken ct) => admin.SaveShiftAsync(req, ct);
    [HttpDelete("shifts/{id:int}")]
    public async Task<IActionResult> DeleteShift(int id, CancellationToken ct) { await admin.DeleteShiftAsync(id, ct); return NoContent(); }
}

[ApiController]
[Route("api/super")]
[Authorize(Roles = Roles.SuperAdmin)]
public class SuperAdminController(AdminService admin) : ControllerBase
{
    [HttpGet("hospitals")] public Task<List<HospitalDto>> Hospitals(CancellationToken ct) => admin.HospitalsAsync(ct);
    [HttpPost("hospitals")] public Task<HospitalDto> Create(SaveHospitalRequest req, CancellationToken ct) => admin.SaveHospitalAsync(null, req, ct);
    [HttpPut("hospitals/{id:int}")] public Task<HospitalDto> Update(int id, SaveHospitalRequest req, CancellationToken ct) => admin.SaveHospitalAsync(id, req, ct);
    [HttpGet("hospitals/{id:int}/admins")] public Task<List<StaffDto>> Admins(int id, CancellationToken ct) => admin.HospitalAdminsAsync(id, ct);
    [HttpPost("hospitals/{id:int}/admins")] public Task<StaffDto> CreateAdmin(int id, CreateAdminRequest req, CancellationToken ct) => admin.CreateHospitalAdminAsync(id, req, ct);
}
