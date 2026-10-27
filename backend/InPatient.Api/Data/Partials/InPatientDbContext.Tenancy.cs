using InPatient.Api.Data.Entities;
using InPatient.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Data;

/// <summary>Implemented (via partial classes) by every entity that carries HospitalId.</summary>
public interface ITenantEntity
{
    int HospitalId { get; set; }
}

/// <summary>
/// Customisations of the scaffolded context (never edit InPatientDbContext.cs itself):
/// - constructor taking ICurrentUser (DI picks the longest constructor it can satisfy);
/// - global query filters limiting every query to the caller's hospital (SuperAdmin/system bypass);
/// - SaveChanges stamps HospitalId on inserts and refuses cross-tenant writes.
/// </summary>
public partial class InPatientDbContext
{
    private readonly ICurrentUser? _currentUser;

    public InPatientDbContext(DbContextOptions<InPatientDbContext> options, ICurrentUser currentUser) : this(options)
    {
        _currentUser = currentUser;
    }

    // Evaluated per query (EF parameterises context members used in filters).
    private bool BypassTenant => _currentUser?.BypassTenantFilter ?? false;
    private int CurrentHospitalId => _currentUser?.HospitalId ?? -1;

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Hospital>().HasQueryFilter(h => BypassTenant || h.Id == CurrentHospitalId);
        modelBuilder.Entity<AppUser>().HasQueryFilter(u => BypassTenant || u.HospitalId == CurrentHospitalId);

        Tenant<Admission>(modelBuilder);
        Tenant<AdmissionWardStay>(modelBuilder);
        Tenant<AiSummary>(modelBuilder);
        Tenant<Alert>(modelBuilder);
        Tenant<Bed>(modelBuilder);
        Tenant<BedEvent>(modelBuilder);
        Tenant<CareTask>(modelBuilder);
        Tenant<ClinicalNote>(modelBuilder);
        Tenant<ConsultantWard>(modelBuilder);
        Tenant<DoseTask>(modelBuilder);
        Tenant<LabResult>(modelBuilder);
        Tenant<NurseShift>(modelBuilder);
        Tenant<NurseShiftBed>(modelBuilder);
        Tenant<Patient>(modelBuilder);
        Tenant<Prescription>(modelBuilder);
        Tenant<PrescriptionItem>(modelBuilder);
        Tenant<StoredFile>(modelBuilder);
        Tenant<VitalSign>(modelBuilder);
        Tenant<VoiceCapture>(modelBuilder);
        Tenant<Ward>(modelBuilder);
        Tenant<VwPatientWardJourney>(modelBuilder);
        Tenant<VwWardHistory>(modelBuilder);
        Tenant<VwWardOccupancyNow>(modelBuilder);
    }

    private void Tenant<T>(ModelBuilder mb) where T : class, ITenantEntity
        => mb.Entity<T>().HasQueryFilter(e => BypassTenant || e.HospitalId == CurrentHospitalId);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampTenant()
    {
        var hospitalId = _currentUser?.HospitalId;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;

            if (entry.Entity is ITenantEntity t)
            {
                if (entry.State == EntityState.Added && t.HospitalId == 0)
                {
                    t.HospitalId = hospitalId ?? throw new InvalidOperationException(
                        $"Cannot insert {entry.Entity.GetType().Name} without a hospital context.");
                }
                if (!BypassTenant && t.HospitalId != hospitalId)
                    throw ApiException.Forbidden("Cross-hospital write blocked");
            }
            else if (entry.Entity is AppUser u && !BypassTenant)
            {
                if (entry.State == EntityState.Added && u.HospitalId is null) u.HospitalId = hospitalId;
                if (u.HospitalId != hospitalId) throw ApiException.Forbidden("Cross-hospital write blocked");
            }
        }
    }
}
