using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class AiSummary
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int AdmissionId { get; set; }

    public string Kind { get; set; } = null!;

    public string Content { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Admission Admission { get; set; } = null!;

    public virtual Hospital Hospital { get; set; } = null!;
}
