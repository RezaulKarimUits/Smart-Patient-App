using InPatient.Api.Data;
using InPatient.Api.Data.Entities;
using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Hubs;
using InPatient.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Services;

/// <summary>Writes bed status changes + BedEvents and pushes them to the live board.</summary>
public sealed class BedLifecycle(InPatientDbContext db, IClock clock, ICurrentUser me, INotifier notifier, IServiceScopeFactory scopes)
{
    public void Change(Bed bed, string newStatus, string eventType, int? admissionId, string? note = null, DateTime? at = null)
    {
        var when = at ?? clock.UtcNow;
        bed.Status = newStatus;
        bed.StatusChangedAt = when;
        db.BedEvents.Add(new BedEvent
        {
            HospitalId = bed.HospitalId, BedId = bed.Id, AdmissionId = admissionId, EventType = eventType,
            ByUserId = me.UserId, At = when, Note = note
        });
    }

    /// <summary>Broadcast fresh tiles for these beds (after SaveChanges).</summary>
    public async Task BroadcastAsync(params int[] bedIds)
    {
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<CurrentUser>().RunAsSystem();
        var board = scope.ServiceProvider.GetRequiredService<BedService>();
        foreach (var id in bedIds.Distinct())
        {
            var tile = await board.GetTileAsync(id);
            if (tile is null) continue;
            var hospitalId = await scope.ServiceProvider.GetRequiredService<InPatientDbContext>().Beds.Where(b => b.Id == id).Select(b => b.HospitalId).FirstAsync();
            await notifier.ToHospitalAsync(hospitalId, HubEvents.BedChanged, tile);
        }
    }
}

public sealed class AdmissionService(InPatientDbContext db, IClock clock, ICurrentUser me, PatientFlagService flags, BedLifecycle beds, INotifier notifier)
{
    // ---------- Access ----------

    /// <summary>Loads an admission in the caller's hospital (tenant filter) and enforces patient ownership.</summary>
    public async Task<Admission> GetAccessibleAsync(int admissionId, CancellationToken ct = default)
    {
        var a = await db.Admissions.FirstOrDefaultAsync(x => x.Id == admissionId, ct) ?? throw ApiException.NotFound("Admission not found");
        EnsureCanView(a, me);
        return a;
    }

    public static void EnsureCanView(Admission a, ICurrentUser user)
    {
        if (user.Role == Roles.Patient && a.PatientId != user.PatientId) throw ApiException.Forbidden("You can only view your own admission");
    }

    public async Task<Admission> GetActiveAsync(int admissionId, CancellationToken ct = default)
    {
        var a = await GetAccessibleAsync(admissionId, ct);
        if (a.Status != AdmissionStatus.Active) throw ApiException.Conflict("Admission is already discharged");
        return a;
    }

    // ---------- Patients ----------

    public async Task<List<PatientDto>> SearchPatientsAsync(string? q, CancellationToken ct)
    {
        var query = db.Patients.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(p => p.Name.Contains(s) || p.Mrn.Contains(s) || (p.Phone != null && p.Phone.Contains(s)));
        }
        var now = clock.UtcNow;
        var rows = await query.OrderByDescending(p => p.CreatedAt).Take(30)
            .Select(p => new { p, Active = p.Admissions.Where(a => a.Status == AdmissionStatus.Active).Select(a => (int?)a.Id).FirstOrDefault() })
            .ToListAsync(ct);
        return rows.Select(r => ToDto(r.p, r.Active, now)).ToList();
    }

    public async Task<PatientDto> RegisterPatientAsync(RegisterPatientRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) throw ApiException.BadRequest("Name is required");
        if (req.Sex is not ("M" or "F" or "O")) throw ApiException.BadRequest("Sex must be M, F or O");
        var now = clock.UtcNow;
        if (req.DateOfBirth > Dhaka.Today(now) || req.DateOfBirth < Dhaka.Today(now).AddYears(-120)) throw ApiException.BadRequest("Date of birth is not valid");
        string[] groups = ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"];
        var blood = string.IsNullOrWhiteSpace(req.BloodGroup) ? null : req.BloodGroup.Trim().ToUpperInvariant();
        if (blood is not null && !groups.Contains(blood)) throw ApiException.BadRequest("Unknown blood group");

        var hospitalId = me.RequireHospitalId();
        var code = await db.Hospitals.Where(h => h.Id == hospitalId).Select(h => h.Code).FirstAsync(ct);
        var prefix = (code.Length > 3 ? code[..3] : code) + "-";
        var existing = await db.Patients.Where(p => p.Mrn.StartsWith(prefix)).Select(p => p.Mrn).ToListAsync(ct);
        var next = existing.Select(m => int.TryParse(m[prefix.Length..], out var n) ? n : 0).DefaultIfEmpty(24000).Max() + 1;

        var p = new Patient
        {
            Mrn = prefix + next, Name = req.Name.Trim(), DateOfBirth = req.DateOfBirth, Sex = req.Sex,
            Phone = req.Phone?.Trim(), Guardian = req.Guardian?.Trim(), BloodGroup = blood, CreatedAt = now, CreatedBy = me.UserId
        };
        db.Patients.Add(p);
        await db.SaveChangesAsync(ct);
        return ToDto(p, null, now);
    }

    private static PatientDto ToDto(Patient p, int? activeAdmissionId, DateTime now)
        => new(p.Id, p.Mrn, p.Name, p.DateOfBirth, Dhaka.AgeYears(p.DateOfBirth, now), p.Sex, p.Phone, p.Guardian, p.BloodGroup, activeAdmissionId);

    // ---------- Admit ----------

    public async Task<AdmitResponse> AdmitAsync(AdmitRequest req, CancellationToken ct)
    {
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == req.PatientId, ct) ?? throw ApiException.NotFound("Patient not found");
        if (await db.Admissions.AnyAsync(a => a.PatientId == patient.Id && a.Status == AdmissionStatus.Active, ct))
            throw ApiException.Conflict("Patient already has an active admission");

        var bed = await db.Beds.Include(b => b.Ward).FirstOrDefaultAsync(b => b.Id == req.BedId, ct) ?? throw ApiException.NotFound("Bed not found");
        if (bed.Status == BedStatus.Reserved && await db.Admissions.AnyAsync(a => a.CurrentBedId == bed.Id && a.Status == AdmissionStatus.Active, ct))
            throw ApiException.Conflict($"Bed {bed.BedNumber} is reserved for another patient");
        if (bed.Status is not (BedStatus.Free or BedStatus.Reserved))
            throw ApiException.Conflict($"Bed {bed.BedNumber} is {bed.Status}, choose a free bed");

        var consultant = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == req.ConsultantUserId && u.Role == Roles.Consultant && u.IsActive, ct)
            ?? throw ApiException.BadRequest("Consultant not found");

        var now = clock.UtcNow;
        var admission = new Admission
        {
            PatientId = patient.Id, CurrentBedId = bed.Id, ConsultantUserId = consultant.Id, WristbandToken = Tokens.NewQrToken(),
            AdmittedAt = now, ProvisionalDiagnosis = req.ProvisionalDiagnosis?.Trim(), Status = AdmissionStatus.Active,
            CreatedAt = now, CreatedBy = me.UserId
        };
        db.Admissions.Add(admission);
        await db.SaveChangesAsync(ct);

        db.AdmissionWardStays.Add(new AdmissionWardStay
        {
            AdmissionId = admission.Id, WardId = bed.WardId, BedId = bed.Id, StartAt = now, Reason = StayReason.Admitted
        });
        beds.Change(bed, BedStatus.Reserved, BedEventType.Reserved, admission.Id, "Reserved at admission - awaiting bed & wristband scan");
        await db.SaveChangesAsync(ct);
        await beds.BroadcastAsync(bed.Id);

        return new AdmitResponse(await GetHeaderAsync(admission.Id, ct), await GetWristbandAsync(admission.Id, ct));
    }

    // ---------- Read models ----------

    public async Task<AdmissionHeaderDto> GetHeaderAsync(int admissionId, CancellationToken ct = default)
    {
        var list = await GetHeadersAsync(db.Admissions.Where(a => a.Id == admissionId), ct);
        var h = list.FirstOrDefault() ?? throw ApiException.NotFound("Admission not found");
        if (me.Role == Roles.Patient && h.PatientId != me.PatientId) throw ApiException.Forbidden("You can only view your own admission");
        return h;
    }

    public async Task<List<AdmissionHeaderDto>> GetHeadersAsync(IQueryable<Admission> query, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var rows = await query.Select(a => new
        {
            a.Id, a.PatientId, PatientName = a.Patient.Name, a.Patient.Mrn, a.Patient.DateOfBirth, a.Patient.Sex, a.Patient.BloodGroup,
            a.CurrentBedId, BedNumber = a.CurrentBed != null ? a.CurrentBed.BedNumber : null,
            WardId = a.CurrentBed != null ? (int?)a.CurrentBed.WardId : null,
            WardName = a.CurrentBed != null ? a.CurrentBed.Ward.Name : null,
            WardType = a.CurrentBed != null ? a.CurrentBed.Ward.Type : null,
            a.ConsultantUserId, ConsultantName = a.ConsultantUser.Name, a.AdmittedAt, a.ProvisionalDiagnosis, a.Status, a.BedConfirmedAt, a.DischargedAt,
            LastWard = a.AdmissionWardStays.OrderByDescending(s => s.StartAt).Select(s => new { s.WardId, s.Ward.Name, s.Ward.Type, s.Bed.BedNumber }).FirstOrDefault()
        }).ToListAsync(ct);

        var status = await flags.GetAsync(rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(r =>
        {
            var st = status[r.Id];
            var age = Dhaka.AgeYears(r.DateOfBirth, now);
            var end = r.DischargedAt ?? now;
            return new AdmissionHeaderDto(r.Id, r.PatientId, r.PatientName, r.Mrn, age, r.Sex, r.BloodGroup,
                r.CurrentBedId, r.BedNumber ?? r.LastWard?.BedNumber, r.WardId ?? r.LastWard?.WardId, r.WardName ?? r.LastWard?.Name, r.WardType ?? r.LastWard?.Type,
                r.ConsultantUserId, r.ConsultantName, r.AdmittedAt, r.ProvisionalDiagnosis, r.Status, r.BedConfirmedAt != null, r.DischargedAt,
                st.News2Score, st.News2Band, age >= 16, st.Flag, st.ActiveAlerts, (int)Math.Floor((end - r.AdmittedAt).TotalDays) + 1);
        }).ToList();
    }

    public async Task<WristbandDto> GetWristbandAsync(int admissionId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var a = await db.Admissions.Where(x => x.Id == admissionId).Select(x => new
        {
            x.Id, x.Patient.Name, x.Patient.Mrn, x.Patient.DateOfBirth, x.Patient.Sex, x.Patient.BloodGroup, Hospital = x.Hospital.Name,
            Ward = x.CurrentBed != null ? x.CurrentBed.Ward.Name : null, Bed = x.CurrentBed != null ? x.CurrentBed.BedNumber : null,
            Consultant = x.ConsultantUser.Name, x.AdmittedAt, x.WristbandToken
        }).FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound("Admission not found");
        return new WristbandDto(a.Id, a.Name, a.Mrn, a.DateOfBirth, Dhaka.AgeYears(a.DateOfBirth, now), a.Sex, a.BloodGroup, a.Hospital, a.Ward, a.Bed,
            a.Consultant, a.AdmittedAt, a.WristbandToken, Qr.PngBase64(a.WristbandToken));
    }

    public async Task<List<AdmissionHeaderDto>> ListActiveAsync(int? wardId, CancellationToken ct)
    {
        var q = db.Admissions.Where(a => a.Status == AdmissionStatus.Active);
        if (wardId is not null) q = q.Where(a => a.CurrentBed != null && a.CurrentBed.WardId == wardId);
        var list = await GetHeadersAsync(q, ct);
        return list.OrderBy(h => h.WardName).ThenBy(h => h.BedNumber).ToList();
    }

    public Task NotifyAdmissionUpdatedAsync(Admission a)
        => notifier.ToHospitalAsync(a.HospitalId, HubEvents.AdmissionUpdated, new { admissionId = a.Id });
}
