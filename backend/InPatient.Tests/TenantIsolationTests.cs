using InPatient.Api.Data;
using InPatient.Api.Data.Entities;
using InPatient.Api.Domain;
using InPatient.Api.Infrastructure;
using InPatient.Api.Services.Rules;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Tests;

public class TenantIsolationTests
{
    private static DbContextOptions<InPatientDbContext> Options(string name)
        => new DbContextOptionsBuilder<InPatientDbContext>().UseInMemoryDatabase(name).Options;

    private static InPatientDbContext Context(DbContextOptions<InPatientDbContext> o, ICurrentUser user) => new(o, user);

    private static readonly ICurrentUser System = new FixedCurrentUser(null, null, "System");
    private static readonly ICurrentUser NurseA = new FixedCurrentUser(10, 1, Roles.Nurse);
    private static readonly ICurrentUser NurseB = new FixedCurrentUser(20, 2, Roles.Nurse);

    private static async Task<DbContextOptions<InPatientDbContext>> SeedAsync()
    {
        var o = Options(Guid.NewGuid().ToString());
        await using var db = Context(o, System);
        db.Hospitals.AddRange(new Hospital { Id = 1, Name = "A", Code = "A" }, new Hospital { Id = 2, Name = "B", Code = "B" });
        db.Wards.AddRange(new Ward { Id = 1, HospitalId = 1, Name = "Medicine A", Code = "M", Type = "Medicine" },
                          new Ward { Id = 2, HospitalId = 2, Name = "Medicine B", Code = "M", Type = "Medicine" });
        db.Patients.AddRange(new Patient { Id = 1, HospitalId = 1, Mrn = "A-1", Name = "Patient A", Sex = "F", DateOfBirth = new DateOnly(1990, 1, 1) },
                             new Patient { Id = 2, HospitalId = 2, Mrn = "B-1", Name = "Patient B", Sex = "M", DateOfBirth = new DateOnly(1980, 1, 1) });
        db.AppUsers.AddRange(new AppUser { Id = 10, HospitalId = 1, Name = "Nurse A", Phone = "1", PasswordHash = "x", Role = Roles.Nurse },
                             new AppUser { Id = 20, HospitalId = 2, Name = "Nurse B", Phone = "2", PasswordHash = "x", Role = Roles.Nurse });
        db.Admissions.AddRange(new Admission { Id = 1, HospitalId = 1, PatientId = 1, ConsultantUserId = 10, WristbandToken = "wa", Status = "Active" },
                               new Admission { Id = 2, HospitalId = 2, PatientId = 2, ConsultantUserId = 20, WristbandToken = "wb", Status = "Active" });
        await db.SaveChangesAsync();
        return o;
    }

    [Fact]
    public async Task Hospital_A_cannot_read_hospital_B_data()
    {
        var o = await SeedAsync();
        await using var db = Context(o, NurseA);

        Assert.Equal(["Medicine A"], await db.Wards.Select(w => w.Name).ToListAsync());
        Assert.Equal(["Patient A"], await db.Patients.Select(p => p.Name).ToListAsync());
        Assert.Single(await db.Admissions.ToListAsync());
        Assert.Single(await db.Hospitals.ToListAsync());
        Assert.Equal(["Nurse A"], await db.AppUsers.Select(u => u.Name).ToListAsync());

        // Direct lookups by id / token of the other hospital find nothing.
        Assert.Null(await db.Patients.FirstOrDefaultAsync(p => p.Id == 2));
        Assert.Null(await db.Admissions.FirstOrDefaultAsync(a => a.WristbandToken == "wb"));
    }

    [Fact]
    public async Task Hospital_B_sees_only_its_own()
    {
        var o = await SeedAsync();
        await using var db = Context(o, NurseB);
        Assert.Equal(["Patient B"], await db.Patients.Select(p => p.Name).ToListAsync());
    }

    [Fact]
    public async Task SuperAdmin_and_system_see_all_hospitals()
    {
        var o = await SeedAsync();
        await using var super = Context(o, new FixedCurrentUser(1, null, Roles.SuperAdmin));
        Assert.Equal(2, await super.Patients.CountAsync());
        await using var sys = Context(o, System);
        Assert.Equal(2, await sys.Admissions.CountAsync());
    }

    [Fact]
    public async Task Inserts_get_the_callers_hospital_automatically()
    {
        var o = await SeedAsync();
        await using (var db = Context(o, NurseA))
        {
            db.Wards.Add(new Ward { Name = "New ward", Code = "N", Type = "Other" });
            await db.SaveChangesAsync();
        }
        await using var sys = Context(o, System);
        Assert.Equal(1, (await sys.Wards.SingleAsync(w => w.Code == "N")).HospitalId);
    }

    [Fact]
    public async Task Cross_hospital_write_is_blocked()
    {
        var o = await SeedAsync();
        await using var db = Context(o, NurseA);
        db.Wards.Add(new Ward { HospitalId = 2, Name = "Sneaky", Code = "X", Type = "Other" });
        var ex = await Assert.ThrowsAsync<ApiException>(() => db.SaveChangesAsync());
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task No_user_context_sees_nothing()
    {
        var o = await SeedAsync();
        await using var db = Context(o, new FixedCurrentUser(null, null, "Anonymous"));
        Assert.Empty(await db.Patients.ToListAsync());
    }
}

public class WristbandGuardTests
{
    [Fact]
    public void Matching_wristband_passes() => WristbandGuard.EnsureMatches("abc123", "abc123");

    [Fact]
    public void Matching_wristband_with_prefix_or_case_passes() => WristbandGuard.EnsureMatches("abc123", "inpatient://scan/ABC123");

    [Fact]
    public void Wrong_wristband_is_rejected_as_wrong_patient()
    {
        var ex = Assert.Throws<ApiException>(() => WristbandGuard.EnsureMatches("abc123", "zzz999"));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("Wrong patient", ex.Message);
    }

    [Fact]
    public void Missing_scan_is_rejected()
    {
        var ex = Assert.Throws<ApiException>(() => WristbandGuard.EnsureMatches("abc123", null));
        Assert.Equal(400, ex.StatusCode);
    }
}
