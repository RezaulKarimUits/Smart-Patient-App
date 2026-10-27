using InPatient.Api.Data;
using InPatient.Api.Data.Entities;
using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Services;

public record AdminWardDto(int Id, string Name, string Code, string Type, string? Floor, bool IsActive, int Beds, WardOccupancyDto Occupancy,
    IReadOnlyList<StaffBriefDto> Consultants);
public record SaveWardRequest(string Name, string Code, string Type, string? Floor, bool? IsActive);
public record AddBedsRequest(int? Count, string? BedNumber);
public record AdminBedDto(int Id, int WardId, string BedNumber, string Status);
public record StaffDto(int Id, string Name, string Phone, string Role, string? Designation, bool IsActive, int? PatientId, IReadOnlyList<int> WardIds);
public record CreateStaffRequest(string Name, string Phone, string Role, string? Designation, string Password, int? PatientId);
public record UpdateStaffRequest(string Name, string? Designation, bool IsActive);
public record ResetPasswordRequest(string Password);
public record ConsultantWardsRequest(IReadOnlyList<int> WardIds);
public record ShiftDto(int Id, int NurseUserId, string NurseName, int WardId, string WardName, DateOnly ShiftDate, string ShiftType, IReadOnlyList<int> BedIds, IReadOnlyList<string> BedNumbers);
public record SaveShiftRequest(int NurseUserId, int WardId, DateOnly ShiftDate, string ShiftType, IReadOnlyList<int> BedIds);
public record HospitalDto(int Id, string Name, string Code, string? Address, string? Phone, bool IsActive, int Wards, int Beds, int Users, int ActiveAdmissions);
public record SaveHospitalRequest(string Name, string Code, string? Address, string? Phone, bool? IsActive);
public record CreateAdminRequest(string Name, string Phone, string Password);

/// <summary>HospitalAdmin management (own hospital via tenant filter) and SuperAdmin hospital provisioning.</summary>
public sealed class AdminService(InPatientDbContext db, IClock clock, ICurrentUser me, BedService beds)
{
    // ---------- Wards & beds ----------

    public async Task<List<AdminWardDto>> WardsAsync(CancellationToken ct)
    {
        var wards = await db.Wards.OrderBy(w => w.Id).Select(w => new
        {
            w.Id, w.Name, w.Code, w.Type, w.Floor, w.IsActive, Statuses = w.Beds.Select(b => b.Status).ToList(),
            Consultants = w.ConsultantWards.Select(c => new StaffBriefDto(c.ConsultantUser.Id, c.ConsultantUser.Name, c.ConsultantUser.Designation)).ToList()
        }).ToListAsync(ct);
        return wards.Select(w => new AdminWardDto(w.Id, w.Name, w.Code, w.Type, w.Floor, w.IsActive, w.Statuses.Count, BedService.Occupancy(w.Statuses), w.Consultants)).ToList();
    }

    public async Task<AdminWardDto> SaveWardAsync(int? id, SaveWardRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Code)) throw ApiException.BadRequest("Name and code are required");
        if (!WardTypes.All.Contains(req.Type)) throw ApiException.BadRequest("Type must be " + string.Join(", ", WardTypes.All));
        var code = req.Code.Trim().ToUpperInvariant();
        if (code.Length > 10) throw ApiException.BadRequest("Code is at most 10 characters");
        if (await db.Wards.AnyAsync(w => w.Code == code && w.Id != id, ct)) throw ApiException.Conflict($"Ward code {code} is already used");
        Ward ward;
        if (id is null)
        {
            ward = new Ward { Name = req.Name.Trim(), Code = code, Type = req.Type, Floor = req.Floor?.Trim(), IsActive = true, CreatedAt = clock.UtcNow };
            db.Wards.Add(ward);
        }
        else
        {
            ward = await db.Wards.FirstOrDefaultAsync(w => w.Id == id, ct) ?? throw ApiException.NotFound("Ward not found");
            if (ward.Code != code && await db.Beds.AnyAsync(b => b.WardId == ward.Id, ct)) throw ApiException.Conflict("Code cannot change once beds exist");
            ward.Name = req.Name.Trim(); ward.Code = code; ward.Type = req.Type; ward.Floor = req.Floor?.Trim();
            if (req.IsActive is false && await db.Admissions.AnyAsync(a => a.Status == AdmissionStatus.Active && a.CurrentBed!.WardId == ward.Id, ct))
                throw ApiException.Conflict("Ward has admitted patients - transfer them first");
            if (req.IsActive is not null) ward.IsActive = req.IsActive.Value;
        }
        await db.SaveChangesAsync(ct);
        return (await WardsAsync(ct)).First(w => w.Id == ward.Id);
    }

    public async Task<List<AdminBedDto>> AddBedsAsync(int wardId, AddBedsRequest req, CancellationToken ct)
    {
        var ward = await db.Wards.FirstOrDefaultAsync(w => w.Id == wardId, ct) ?? throw ApiException.NotFound("Ward not found");
        var existing = await db.Beds.Where(b => b.WardId == wardId).Select(b => b.BedNumber).ToListAsync(ct);
        var numbers = new List<string>();
        if (!string.IsNullOrWhiteSpace(req.BedNumber)) numbers.Add(req.BedNumber.Trim().ToUpperInvariant());
        else
        {
            var count = Math.Clamp(req.Count ?? 1, 1, 50);
            var next = existing.Select(n => int.TryParse(n.Split('-').Last(), out var x) ? x : 0).DefaultIfEmpty(0).Max() + 1;
            for (var i = 0; i < count; i++) numbers.Add($"{ward.Code}-{next + i:00}");
        }
        var dup = numbers.FirstOrDefault(existing.Contains);
        if (dup is not null) throw ApiException.Conflict($"Bed {dup} already exists");
        var now = clock.UtcNow;
        var created = numbers.Select(n => new Bed { WardId = wardId, BedNumber = n, QrToken = Tokens.NewQrToken(), Status = BedStatus.Free, StatusChangedAt = now, CreatedAt = now }).ToList();
        db.Beds.AddRange(created);
        await db.SaveChangesAsync(ct);
        return created.Select(b => new AdminBedDto(b.Id, b.WardId, b.BedNumber, b.Status)).ToList();
    }

    public async Task<List<AdminBedDto>> WardBedsAsync(int wardId, CancellationToken ct)
        => await db.Beds.Where(b => b.WardId == wardId).OrderBy(b => b.BedNumber).Select(b => new AdminBedDto(b.Id, b.WardId, b.BedNumber, b.Status)).ToListAsync(ct);

    /// <summary>Re-issues a bed's QR token (e.g. a label was lost). The old label stops working.</summary>
    public async Task<QrDto> RegenerateQrAsync(int bedId, CancellationToken ct)
    {
        var bed = await db.Beds.FirstOrDefaultAsync(b => b.Id == bedId, ct) ?? throw ApiException.NotFound("Bed not found");
        bed.QrToken = Tokens.NewQrToken();
        await db.SaveChangesAsync(ct);
        return await beds.GetBedQrAsync(bedId, ct);
    }

    // ---------- Staff ----------

    public async Task<List<StaffDto>> StaffAsync(string? role, CancellationToken ct)
    {
        var q = db.AppUsers.Where(u => u.Role != Roles.SuperAdmin);
        if (!string.IsNullOrWhiteSpace(role)) q = q.Where(u => u.Role == role);
        var users = await q.OrderBy(u => u.Role).ThenBy(u => u.Name)
            .Select(u => new { u.Id, u.Name, u.Phone, u.Role, u.Designation, u.IsActive, u.PatientId, Wards = u.ConsultantWards.Select(c => c.WardId).ToList() })
            .ToListAsync(ct);
        return users.Select(u => new StaffDto(u.Id, u.Name, u.Phone, u.Role, u.Designation, u.IsActive, u.PatientId, u.Wards)).ToList();
    }

    public async Task<StaffDto> CreateStaffAsync(CreateStaffRequest req, CancellationToken ct)
    {
        string[] allowed = [Roles.HospitalAdmin, Roles.Receptionist, Roles.Nurse, Roles.Consultant, Roles.Patient];
        if (!allowed.Contains(req.Role)) throw ApiException.BadRequest("Role must be HospitalAdmin, Receptionist, Nurse, Consultant or Patient");
        ValidateNew(req.Name, req.Phone, req.Password);
        if (req.Role == Roles.Patient)
        {
            if (req.PatientId is null || !await db.Patients.AnyAsync(p => p.Id == req.PatientId, ct)) throw ApiException.BadRequest("Choose the patient for this login");
        }
        var phone = req.Phone.Trim();
        if (await db.AppUsers.AnyAsync(u => u.Phone == phone, ct)) throw ApiException.Conflict("Phone is already registered in this hospital");
        var u = new AppUser
        {
            HospitalId = me.RequireHospitalId(), Name = req.Name.Trim(), Phone = phone, PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password, 11),
            Role = req.Role, Designation = req.Designation?.Trim(), IsActive = true, PatientId = req.Role == Roles.Patient ? req.PatientId : null, CreatedAt = clock.UtcNow
        };
        db.AppUsers.Add(u);
        await db.SaveChangesAsync(ct);
        return (await StaffAsync(null, ct)).First(s => s.Id == u.Id);
    }

    public async Task<StaffDto> UpdateStaffAsync(int id, UpdateStaffRequest req, CancellationToken ct)
    {
        var u = await db.AppUsers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiException.NotFound("User not found");
        if (string.IsNullOrWhiteSpace(req.Name)) throw ApiException.BadRequest("Name is required");
        if (u.Id == me.UserId && !req.IsActive) throw ApiException.Conflict("You cannot deactivate yourself");
        u.Name = req.Name.Trim(); u.Designation = req.Designation?.Trim(); u.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);
        return (await StaffAsync(null, ct)).First(s => s.Id == u.Id);
    }

    public async Task ResetPasswordAsync(int id, ResetPasswordRequest req, CancellationToken ct)
    {
        var u = await db.AppUsers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiException.NotFound("User not found");
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6) throw ApiException.BadRequest("Password must be at least 6 characters");
        u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password, 11);
        await db.SaveChangesAsync(ct);
    }

    public async Task<StaffDto> SetConsultantWardsAsync(int consultantId, ConsultantWardsRequest req, CancellationToken ct)
    {
        var u = await db.AppUsers.FirstOrDefaultAsync(x => x.Id == consultantId && x.Role == Roles.Consultant, ct) ?? throw ApiException.NotFound("Consultant not found");
        var wardIds = req.WardIds.Distinct().ToList();
        if (await db.Wards.CountAsync(w => wardIds.Contains(w.Id), ct) != wardIds.Count) throw ApiException.BadRequest("Unknown ward");
        var current = await db.ConsultantWards.Where(c => c.ConsultantUserId == u.Id).ToListAsync(ct);
        db.ConsultantWards.RemoveRange(current.Where(c => !wardIds.Contains(c.WardId)));
        foreach (var w in wardIds.Where(w => current.All(c => c.WardId != w)))
            db.ConsultantWards.Add(new ConsultantWard { ConsultantUserId = u.Id, WardId = w, CreatedAt = clock.UtcNow });
        await db.SaveChangesAsync(ct);
        return (await StaffAsync(null, ct)).First(s => s.Id == u.Id);
    }

    // ---------- Nurse shifts ----------

    public async Task<List<ShiftDto>> ShiftsAsync(DateOnly? date, int? wardId, CancellationToken ct)
    {
        var d = date ?? Dhaka.Today(clock.UtcNow);
        var q = db.NurseShifts.Where(s => s.ShiftDate == d);
        if (wardId is not null) q = q.Where(s => s.WardId == wardId);
        var rows = await q.OrderBy(s => s.WardId).ThenBy(s => s.ShiftType).ThenBy(s => s.NurseUser.Name)
            .Select(s => new { s.Id, s.NurseUserId, Nurse = s.NurseUser.Name, s.WardId, Ward = s.Ward.Name, s.ShiftDate, s.ShiftType,
                Beds = s.NurseShiftBeds.OrderBy(b => b.Bed.BedNumber).Select(b => new { b.BedId, b.Bed.BedNumber }).ToList() })
            .ToListAsync(ct);
        return rows.Select(r => new ShiftDto(r.Id, r.NurseUserId, r.Nurse, r.WardId, r.Ward, r.ShiftDate, r.ShiftType,
            r.Beds.Select(b => b.BedId).ToList(), r.Beds.Select(b => b.BedNumber).ToList())).ToList();
    }

    /// <summary>Creates or replaces a nurse's shift (one per nurse/date/shift) with its bed assignments.</summary>
    public async Task<ShiftDto> SaveShiftAsync(SaveShiftRequest req, CancellationToken ct)
    {
        if (!ShiftTypes.All.Contains(req.ShiftType)) throw ApiException.BadRequest("Shift must be Morning, Evening or Night");
        if (!await db.AppUsers.AnyAsync(u => u.Id == req.NurseUserId && u.Role == Roles.Nurse && u.IsActive, ct)) throw ApiException.BadRequest("Nurse not found");
        if (!await db.Wards.AnyAsync(w => w.Id == req.WardId, ct)) throw ApiException.BadRequest("Ward not found");
        var bedIds = req.BedIds.Distinct().ToList();
        if (await db.Beds.CountAsync(b => bedIds.Contains(b.Id) && b.WardId == req.WardId, ct) != bedIds.Count) throw ApiException.BadRequest("Beds must belong to the ward");

        var shift = await db.NurseShifts.Include(s => s.NurseShiftBeds)
            .FirstOrDefaultAsync(s => s.NurseUserId == req.NurseUserId && s.ShiftDate == req.ShiftDate && s.ShiftType == req.ShiftType, ct);
        if (shift is null)
        {
            shift = new NurseShift { NurseUserId = req.NurseUserId, WardId = req.WardId, ShiftDate = req.ShiftDate, ShiftType = req.ShiftType, CreatedAt = clock.UtcNow };
            db.NurseShifts.Add(shift);
        }
        else
        {
            shift.WardId = req.WardId;
            db.NurseShiftBeds.RemoveRange(shift.NurseShiftBeds);
            shift.NurseShiftBeds.Clear();
        }
        await db.SaveChangesAsync(ct);
        foreach (var b in bedIds) db.NurseShiftBeds.Add(new NurseShiftBed { NurseShiftId = shift.Id, BedId = b });
        await db.SaveChangesAsync(ct);
        return (await ShiftsAsync(req.ShiftDate, null, ct)).First(s => s.Id == shift.Id);
    }

    public async Task DeleteShiftAsync(int id, CancellationToken ct)
    {
        var s = await db.NurseShifts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiException.NotFound("Shift not found");
        db.NurseShifts.Remove(s); // NurseShiftBed cascades
        await db.SaveChangesAsync(ct);
    }

    // ---------- SuperAdmin ----------

    public async Task<List<HospitalDto>> HospitalsAsync(CancellationToken ct)
    {
        EnsureSuper();
        return await db.Hospitals.OrderBy(h => h.Id).Select(h => new HospitalDto(h.Id, h.Name, h.Code, h.Address, h.Phone, h.IsActive,
            h.Wards.Count, h.Beds.Count, h.AppUsers.Count, h.Admissions.Count(a => a.Status == AdmissionStatus.Active))).ToListAsync(ct);
    }

    public async Task<HospitalDto> SaveHospitalAsync(int? id, SaveHospitalRequest req, CancellationToken ct)
    {
        EnsureSuper();
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Code)) throw ApiException.BadRequest("Name and code are required");
        var code = req.Code.Trim().ToUpperInvariant();
        if (await db.Hospitals.AnyAsync(h => h.Code == code && h.Id != id, ct)) throw ApiException.Conflict($"Hospital code {code} is already used");
        Hospital h;
        if (id is null) { h = new Hospital { CreatedAt = clock.UtcNow, IsActive = true }; db.Hospitals.Add(h); }
        else h = await db.Hospitals.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiException.NotFound("Hospital not found");
        h.Name = req.Name.Trim(); h.Code = code; h.Address = req.Address?.Trim(); h.Phone = req.Phone?.Trim();
        if (req.IsActive is not null) h.IsActive = req.IsActive.Value;
        await db.SaveChangesAsync(ct);
        return (await HospitalsAsync(ct)).First(x => x.Id == h.Id);
    }

    public async Task<List<StaffDto>> HospitalAdminsAsync(int hospitalId, CancellationToken ct)
    {
        EnsureSuper();
        return await db.AppUsers.Where(u => u.HospitalId == hospitalId && u.Role == Roles.HospitalAdmin)
            .Select(u => new StaffDto(u.Id, u.Name, u.Phone, u.Role, u.Designation, u.IsActive, null, new List<int>())).ToListAsync(ct);
    }

    public async Task<StaffDto> CreateHospitalAdminAsync(int hospitalId, CreateAdminRequest req, CancellationToken ct)
    {
        EnsureSuper();
        if (!await db.Hospitals.AnyAsync(h => h.Id == hospitalId, ct)) throw ApiException.NotFound("Hospital not found");
        ValidateNew(req.Name, req.Phone, req.Password);
        var phone = req.Phone.Trim();
        if (await db.AppUsers.AnyAsync(u => u.HospitalId == hospitalId && u.Phone == phone, ct)) throw ApiException.Conflict("Phone already registered at this hospital");
        var u = new AppUser { HospitalId = hospitalId, Name = req.Name.Trim(), Phone = phone, PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password, 11),
            Role = Roles.HospitalAdmin, Designation = "Hospital administrator", IsActive = true, CreatedAt = clock.UtcNow };
        db.AppUsers.Add(u);
        await db.SaveChangesAsync(ct);
        return new StaffDto(u.Id, u.Name, u.Phone, u.Role, u.Designation, true, null, []);
    }

    private void EnsureSuper() { if (!me.IsSuperAdmin) throw ApiException.Forbidden(); }

    private static void ValidateNew(string name, string phone, string password)
    {
        if (string.IsNullOrWhiteSpace(name)) throw ApiException.BadRequest("Name is required");
        if (string.IsNullOrWhiteSpace(phone) || phone.Trim().Length < 6) throw ApiException.BadRequest("Enter a valid phone number");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6) throw ApiException.BadRequest("Password must be at least 6 characters");
    }
}
