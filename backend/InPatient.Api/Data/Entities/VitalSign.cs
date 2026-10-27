using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class VitalSign
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int AdmissionId { get; set; }

    public DateTime RecordedAt { get; set; }

    public int? RecordedBy { get; set; }

    public string Source { get; set; } = null!;

    public int? Systolic { get; set; }

    public int? Diastolic { get; set; }

    public int? Pulse { get; set; }

    public int? RespRate { get; set; }

    public decimal? TemperatureC { get; set; }

    public int? SpO2 { get; set; }

    public bool OnOxygen { get; set; }

    public string Consciousness { get; set; } = null!;

    public int? News2Score { get; set; }

    public string? News2Band { get; set; }

    public int? SignedOffBy { get; set; }

    public DateTime? SignedOffAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Admission Admission { get; set; } = null!;

    public virtual Hospital Hospital { get; set; } = null!;

    public virtual AppUser? RecordedByNavigation { get; set; }

    public virtual AppUser? SignedOffByNavigation { get; set; }
}
