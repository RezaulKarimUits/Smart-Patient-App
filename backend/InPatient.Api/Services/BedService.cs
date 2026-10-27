using InPatient.Api.Data;
using InPatient.Api.Data.Entities;
using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Services;

public sealed class BedService(InPatientDbContext db, IClock clock, ICurrentUser me, PatientFlagService flags, BedLifecycle lifecycle,
    AdmissionService admissions, CareTeamService team)
{
    // ---------- Board ----------

    public async Task<BoardDto> GetBoardAsync(int? wardId, CancellationToken ct)
    {
        var hospitalId = me.RequireHospitalId();
        var hospital = await db.Hospitals.Where(h => h.Id == hospitalId).Select(h => h.Name).FirstAsync(ct);
        var wards = await db.Wards.Where(w => w.IsActive && (wardId == null || w.Id == wardId)).OrderBy(w => w.Id)
            .Select(w => new { w.Id, w.Name, w.Code, w.Type }).ToListAsync(ct);
        var tiles = await GetTilesAsync(db.Beds.Where(b => wardId == null || b.WardId == wardId), ct);

        var result = wards.Select(w =>
        {
            var wb = tiles.Where(t => t.WardId == w.Id).OrderBy(t => t.BedNumber).ToList();
            return new BoardWardDto(w.Id, w.Name, w.Code, w.Type, Occupancy(wb.Select(t => t.Status)), wb);
        }).ToList();
        return new BoardDto(hospitalId, hospital, clock.UtcNow, result);
    }

    public static WardOccupancyDto Occupancy(IEnumerable<string> statuses)
    {
        var s = statuses.ToList();
        int c(string x) => s.Count(v => v == x);
        var total = s.Count;
        var pct = total == 0 ? 0 : Math.Round(100m * (c(BedStatus.Occupied) + c(BedStatus.Reserved)) / total, 1);
        return new WardOccupancyDto(total, c(BedStatus.Free), c(BedStatus.Reserved), c(BedStatus.Occupied), c(BedStatus.Cleaning), c(BedStatus.Blocked), pct);
    }

    public async Task<BedTileDto?> GetTileAsync(int bedId, CancellationToken ct = default)
        => (await GetTilesAsync(db.Beds.Where(b => b.Id == bedId), ct)).FirstOrDefault();

    public async Task<List<BedTileDto>> GetTilesAsync(IQueryable<Bed> beds, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var rows = await beds.Select(b => new
        {
            b.Id, b.WardId, WardName = b.Ward.Name, b.BedNumber, b.Status, b.StatusChangedAt,
            LastNote = b.BedEvents.OrderByDescending(e => e.At).Select(e => e.Note).FirstOrDefault(),
            Adm = b.Admission != null && b.Admission.Status == AdmissionStatus.Active
                ? new
                {
                    b.Admission.Id, b.Admission.PatientId, b.Admission.Patient.Name, b.Admission.Patient.Mrn, b.Admission.Patient.DateOfBirth,
                    b.Admission.Patient.Sex, b.Admission.ProvisionalDiagnosis, Consultant = b.Admission.ConsultantUser.Name, b.Admission.BedConfirmedAt
                }
                : null
        }).ToListAsync(ct);

        var status = await flags.GetAsync(rows.Where(r => r.Adm != null).Select(r => r.Adm!.Id).ToList(), ct);
        return rows.Select(r =>
        {
            BedOccupantDto? occ = null;
            if (r.Adm is not null)
            {
                var st = status[r.Adm.Id];
                var age = Dhaka.AgeYears(r.Adm.DateOfBirth, now);
                occ = new BedOccupantDto(r.Adm.Id, r.Adm.PatientId, r.Adm.Name, r.Adm.Mrn, age, r.Adm.Sex, r.Adm.ProvisionalDiagnosis, r.Adm.Consultant,
                    age >= 16 ? st.News2Score : null, age >= 16 ? st.News2Band : null, st.Flag, st.ActiveAlerts, r.Adm.BedConfirmedAt == null);
            }
            return new BedTileDto(r.Id, r.WardId, r.WardName, r.BedNumber, r.Status, r.StatusChangedAt, r.LastNote, occ);
        }).ToList();
    }

    public async Task<List<WardSummaryDto>> GetWardSummariesAsync(CancellationToken ct)
    {
        var board = await GetBoardAsync(null, ct);
        var consultants = await db.ConsultantWards.Select(c => new { c.WardId, c.ConsultantUser.Id, c.ConsultantUser.Name, c.ConsultantUser.Designation }).ToListAsync(ct);
        var wards = await db.Wards.Where(w => w.IsActive).ToDictionaryAsync(w => w.Id, ct);
        return board.Wards.Select(w => new WardSummaryDto(w.Id, w.Name, w.Code, w.Type, wards[w.Id].Floor, w.Occupancy,
            w.Beds.Count(b => b.Occupant?.Flag == PatientFlag.Watch), w.Beds.Count(b => b.Occupant?.Flag == PatientFlag.Critical),
            consultants.Where(c => c.WardId == w.Id).Select(c => new StaffBriefDto(c.Id, c.Name, c.Designation)).ToList())).ToList();
    }

    // ---------- Lifecycle: Free → Reserved → Occupied → Cleaning → Free, plus Blocked ----------

    public async Task<BedTileDto> ReserveAsync(int bedId, string? note, CancellationToken ct)
    {
        var bed = await LoadBedAsync(bedId, ct);
        if (bed.Status != BedStatus.Free) throw ApiException.Conflict($"Only a free bed can be reserved (bed is {bed.Status})");
        lifecycle.Change(bed, BedStatus.Reserved, BedEventType.Reserved, null, string.IsNullOrWhiteSpace(note) ? "Reserved" : note.Trim());
        return await SaveAndTileAsync(bed, ct);
    }

    public async Task<BedTileDto> SetStatusAsync(int bedId, BedStatusChangeRequest req, CancellationToken ct)
    {
        var bed = await LoadBedAsync(bedId, ct);
        var hasActive = await db.Admissions.AnyAsync(a => a.CurrentBedId == bedId && a.Status == AdmissionStatus.Active, ct);
        var note = req.Note?.Trim();
        switch (bed.Status, req.Status)
        {
            case (BedStatus.Cleaning, BedStatus.Free):
                lifecycle.Change(bed, BedStatus.Free, BedEventType.Cleaned, null, note ?? "Cleaned and ready"); break;
            case (BedStatus.Free, BedStatus.Blocked):
            case (BedStatus.Cleaning, BedStatus.Blocked):
                if (string.IsNullOrWhiteSpace(note)) throw ApiException.BadRequest("Give a reason for blocking the bed");
                lifecycle.Change(bed, BedStatus.Blocked, BedEventType.Blocked, null, note); break;
            case (BedStatus.Blocked, BedStatus.Free):
                lifecycle.Change(bed, BedStatus.Free, BedEventType.Unblocked, null, note ?? "Unblocked"); break;
            case (BedStatus.Reserved, BedStatus.Free) when !hasActive:
                lifecycle.Change(bed, BedStatus.Free, BedEventType.Released, null, note ?? "Reservation released"); break;
            case (BedStatus.Reserved, BedStatus.Free):
                throw ApiException.Conflict("This bed is reserved for an admitted patient - transfer or discharge the patient instead");
            case (BedStatus.Free, BedStatus.Reserved):
                lifecycle.Change(bed, BedStatus.Reserved, BedEventType.Reserved, null, note ?? "Reserved"); break;
            case (BedStatus.Occupied, _):
                throw ApiException.Conflict("An occupied bed changes only through transfer or discharge");
            default:
                throw ApiException.Conflict($"Cannot change bed from {bed.Status} to {req.Status}");
        }
        return await SaveAndTileAsync(bed, ct);
    }

    private async Task<Bed> LoadBedAsync(int bedId, CancellationToken ct)
        => await db.Beds.FirstOrDefaultAsync(b => b.Id == bedId, ct) ?? throw ApiException.NotFound("Bed not found");

    private async Task<BedTileDto> SaveAndTileAsync(Bed bed, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        await lifecycle.BroadcastAsync(bed.Id);
        return (await GetTileAsync(bed.Id, ct))!;
    }

    public async Task<QrDto> GetBedQrAsync(int bedId, CancellationToken ct)
    {
        var b = await db.Beds.Where(x => x.Id == bedId).Select(x => new { x.QrToken, x.BedNumber, Ward = x.Ward.Name }).FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Bed not found");
        return new QrDto(b.QrToken, Qr.PngBase64(b.QrToken), $"{b.Ward} · {b.BedNumber}");
    }

    public async Task<List<QrDto>> GetWardQrsAsync(int wardId, CancellationToken ct)
    {
        var beds = await db.Beds.Where(x => x.WardId == wardId).OrderBy(x => x.BedNumber)
            .Select(x => new { x.QrToken, x.BedNumber, Ward = x.Ward.Name }).ToListAsync(ct);
        return beds.Select(b => new QrDto(b.QrToken, Qr.PngBase64(b.QrToken), $"{b.Ward} · {b.BedNumber}")).ToList();
    }

    // ---------- Scanning ----------

    public async Task<ScanResolveDto> ResolveAsync(string token, CancellationToken ct)
    {
        token = NormalizeToken(token);
        var bed = await db.Beds.Where(b => b.QrToken == token).Select(b => (int?)b.Id).FirstOrDefaultAsync(ct);
        if (bed is not null)
        {
            var tile = await GetTileAsync(bed.Value, ct);
            var admission = tile?.Occupant is null ? null : await admissions.GetHeaderAsync(tile.Occupant.AdmissionId, ct);
            return new ScanResolveDto("bed", tile, admission);
        }
        var adm = await db.Admissions.Where(a => a.WristbandToken == token).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
        if (adm is not null)
            return new ScanResolveDto("admission", null, await admissions.GetHeaderAsync(adm.Value, ct));
        throw await UnknownCodeAsync(token, "This QR code is not a bed or wristband of this hospital", ct);
    }

    /// <summary>
    /// Error for a code not found in this hospital. Says plainly when it belongs to another hospital
    /// (without revealing which hospital or whose bed/wristband it is).
    /// </summary>
    private async Task<ApiException> UnknownCodeAsync(string token, string notFoundMessage, CancellationToken ct)
    {
        var elsewhere = await db.Beds.IgnoreQueryFilters().AnyAsync(b => b.QrToken == token, ct)
            || await db.Admissions.IgnoreQueryFilters().AnyAsync(a => a.WristbandToken == token, ct);
        return elsewhere
            ? ApiException.Forbidden("This QR code belongs to a different hospital. Only beds and wristbands of your hospital can be scanned.")
            : ApiException.NotFound(notFoundMessage);
    }

    /// <summary>Accepts a raw token or a URL/prefixed value ending in the token.</summary>
    public static string NormalizeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) throw ApiException.BadRequest("Scan code is empty");
        var t = token.Trim();
        var slash = t.LastIndexOfAny(['/', ':', '=']);
        if (slash >= 0) t = t[(slash + 1)..];
        return t.ToLowerInvariant();
    }

    /// <summary>Nurse scans bed QR then wristband: Reserved bed becomes Occupied.</summary>
    public async Task<AdmissionHeaderDto> ConfirmBedAsync(ConfirmBedRequest req, CancellationToken ct)
    {
        var bedToken = NormalizeToken(req.BedToken);
        var bed = await db.Beds.Include(b => b.Ward).FirstOrDefaultAsync(b => b.QrToken == bedToken, ct)
            ?? throw await UnknownCodeAsync(bedToken, "Bed QR code not recognised", ct);
        var admission = await FindByWristbandAsync(req.WristbandToken, ct);

        if (admission.CurrentBedId != bed.Id)
        {
            var assigned = admission.CurrentBedId is null ? "no bed" : await db.Beds.Where(b => b.Id == admission.CurrentBedId).Select(b => b.BedNumber).FirstAsync(ct);
            throw ApiException.Conflict($"Wrong bed: this patient is assigned to {assigned}, not {bed.BedNumber}");
        }
        if (bed.Status == BedStatus.Occupied && admission.BedConfirmedAt is not null)
            return await admissions.GetHeaderAsync(admission.Id, ct); // idempotent re-scan
        if (bed.Status != BedStatus.Reserved) throw ApiException.Conflict($"Bed {bed.BedNumber} is {bed.Status}");

        admission.BedConfirmedAt = clock.UtcNow;
        lifecycle.Change(bed, BedStatus.Occupied, BedEventType.Admitted, admission.Id, "Bed and wristband scanned");
        await db.SaveChangesAsync(ct);
        await lifecycle.BroadcastAsync(bed.Id);
        await admissions.NotifyAdmissionUpdatedAsync(admission);
        return await admissions.GetHeaderAsync(admission.Id, ct);
    }

    public async Task<Admission> FindByWristbandAsync(string wristbandToken, CancellationToken ct)
    {
        var token = NormalizeToken(wristbandToken);
        var a = await db.Admissions.FirstOrDefaultAsync(x => x.WristbandToken == token, ct) ?? throw await UnknownCodeAsync(token, "Wristband not recognised", ct);
        if (a.Status != AdmissionStatus.Active) throw ApiException.Conflict("This wristband belongs to a discharged admission");
        return a;
    }

    public async Task<AdmissionHeaderDto> TransferByScanAsync(ScanTransferRequest req, CancellationToken ct)
    {
        var admission = await FindByWristbandAsync(req.WristbandToken, ct);
        var bedToken = NormalizeToken(req.BedToken);
        var bedId = await db.Beds.Where(b => b.QrToken == bedToken).Select(b => (int?)b.Id).FirstOrDefaultAsync(ct)
            ?? throw await UnknownCodeAsync(bedToken, "Bed QR code not recognised", ct);
        return await TransferAsync(admission.Id, new TransferRequest(bedId, req.Reason), ct);
    }

    /// <summary>
    /// Old bed → Cleaning, ward stay closed and a new one opened, new bed → Occupied.
    /// If the ward changes and the current consultant does not cover it, the new ward's consultant takes over.
    /// </summary>
    public async Task<AdmissionHeaderDto> TransferAsync(int admissionId, TransferRequest req, CancellationToken ct)
    {
        var admission = await admissions.GetActiveAsync(admissionId, ct);
        var newBed = await db.Beds.Include(b => b.Ward).FirstOrDefaultAsync(b => b.Id == req.BedId, ct) ?? throw ApiException.NotFound("Bed not found");
        if (newBed.Id == admission.CurrentBedId) throw ApiException.Conflict("Patient is already in this bed");
        var newBedTaken = await db.Admissions.AnyAsync(a => a.CurrentBedId == newBed.Id && a.Status == AdmissionStatus.Active, ct);
        if (newBedTaken || newBed.Status is not (BedStatus.Free or BedStatus.Reserved))
            throw ApiException.Conflict($"Bed {newBed.BedNumber} is not available ({newBed.Status})");

        var now = clock.UtcNow;
        var oldBed = admission.CurrentBedId is null ? null : await db.Beds.FirstAsync(b => b.Id == admission.CurrentBedId, ct);
        var openStay = await db.AdmissionWardStays.Where(s => s.AdmissionId == admission.Id && s.EndAt == null).OrderByDescending(s => s.StartAt).FirstOrDefaultAsync(ct);
        if (openStay is not null) openStay.EndAt = now;

        var reason = string.IsNullOrWhiteSpace(req.Reason) ? $"Transfer to {newBed.Ward.Name} {newBed.BedNumber}" : req.Reason.Trim();
        if (oldBed is not null)
            lifecycle.Change(oldBed, BedStatus.Cleaning, BedEventType.TransferredOut, admission.Id, reason, now);

        admission.CurrentBedId = newBed.Id;
        admission.BedConfirmedAt = now;
        db.AdmissionWardStays.Add(new AdmissionWardStay { AdmissionId = admission.Id, WardId = newBed.WardId, BedId = newBed.Id, StartAt = now, Reason = StayReason.Transfer });
        lifecycle.Change(newBed, BedStatus.Occupied, BedEventType.TransferredIn, admission.Id, reason, now);

        if (oldBed is null || oldBed.WardId != newBed.WardId)
        {
            var wardConsultants = await team.ConsultantsForWardAsync(newBed.WardId, ct);
            if (wardConsultants.Count > 0 && !wardConsultants.Contains(admission.ConsultantUserId))
                admission.ConsultantUserId = wardConsultants[0];
        }
        await db.SaveChangesAsync(ct);
        await lifecycle.BroadcastAsync(newBed.Id, oldBed?.Id ?? newBed.Id);
        await admissions.NotifyAdmissionUpdatedAsync(admission);
        return await admissions.GetHeaderAsync(admission.Id, ct);
    }
}
