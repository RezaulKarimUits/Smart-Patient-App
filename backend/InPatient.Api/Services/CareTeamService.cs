using InPatient.Api.Data;
using InPatient.Api.Domain;
using InPatient.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace InPatient.Api.Services;

/// <summary>Who looks after which bed right now (nurse shifts, consultant–ward mapping).</summary>
public sealed class CareTeamService(InPatientDbContext db, IClock clock)
{
    public (DateOnly Date, string Shift) CurrentShift() => Dhaka.CurrentShift(clock.UtcNow);

    /// <summary>Beds assigned to the nurse for the current shift.</summary>
    public async Task<List<int>> NurseBedIdsAsync(int nurseUserId, CancellationToken ct = default)
    {
        var (date, shift) = CurrentShift();
        return await db.NurseShiftBeds
            .Where(x => x.NurseShift.NurseUserId == nurseUserId && x.NurseShift.ShiftDate == date && x.NurseShift.ShiftType == shift)
            .Select(x => x.BedId).Distinct().ToListAsync(ct);
    }

    public async Task<List<int>> NurseWardIdsAsync(int nurseUserId, CancellationToken ct = default)
    {
        var (date, shift) = CurrentShift();
        return await db.NurseShifts.Where(s => s.NurseUserId == nurseUserId && s.ShiftDate == date && s.ShiftType == shift)
            .Select(s => s.WardId).Distinct().ToListAsync(ct);
    }

    /// <summary>Nurses on the current shift covering this bed (falls back to all nurses on the ward's shift).</summary>
    public async Task<List<int>> NursesForBedAsync(int bedId, CancellationToken ct = default)
    {
        var (date, shift) = CurrentShift();
        var ids = await db.NurseShiftBeds
            .Where(x => x.BedId == bedId && x.NurseShift.ShiftDate == date && x.NurseShift.ShiftType == shift)
            .Select(x => x.NurseShift.NurseUserId).Distinct().ToListAsync(ct);
        if (ids.Count > 0) return ids;
        var wardId = await db.Beds.Where(b => b.Id == bedId).Select(b => b.WardId).FirstOrDefaultAsync(ct);
        return await NursesForWardAsync(wardId, ct);
    }

    public async Task<List<int>> NursesForWardAsync(int wardId, CancellationToken ct = default)
    {
        var (date, shift) = CurrentShift();
        return await db.NurseShifts.Where(s => s.WardId == wardId && s.ShiftDate == date && s.ShiftType == shift)
            .Select(s => s.NurseUserId).Distinct().ToListAsync(ct);
    }

    public Task<List<int>> ConsultantWardIdsAsync(int consultantUserId, CancellationToken ct = default)
        => db.ConsultantWards.Where(c => c.ConsultantUserId == consultantUserId).Select(c => c.WardId).ToListAsync(ct);

    public Task<List<int>> ConsultantsForWardAsync(int wardId, CancellationToken ct = default)
        => db.ConsultantWards.Where(c => c.WardId == wardId).Select(c => c.ConsultantUserId).ToListAsync(ct);
}

public static class Qr
{
    public static string PngBase64(string content, int pixelsPerModule = 8)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);
        return Convert.ToBase64String(png);
    }
}
