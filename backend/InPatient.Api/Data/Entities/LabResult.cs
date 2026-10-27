using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class LabResult
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int AdmissionId { get; set; }

    public int? StoredFileId { get; set; }

    public string TestName { get; set; } = null!;

    public decimal? NumericValue { get; set; }

    public string? TextValue { get; set; }

    public string? Unit { get; set; }

    public decimal? RefLow { get; set; }

    public decimal? RefHigh { get; set; }

    public string Flag { get; set; } = null!;

    public DateTime ResultAt { get; set; }

    public string Source { get; set; } = null!;

    public int? RecordedBy { get; set; }

    public int? SignedOffBy { get; set; }

    public DateTime? SignedOffAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Admission Admission { get; set; } = null!;

    public virtual Hospital Hospital { get; set; } = null!;

    public virtual AppUser? RecordedByNavigation { get; set; }

    public virtual AppUser? SignedOffByNavigation { get; set; }

    public virtual StoredFile? StoredFile { get; set; }
}
