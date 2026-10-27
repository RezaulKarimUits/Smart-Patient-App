using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class CareTask
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int AdmissionId { get; set; }

    public string TaskType { get; set; } = null!;

    public string Detail { get; set; } = null!;

    public DateTime DueAt { get; set; }

    public string Status { get; set; } = null!;

    public int? DoneBy { get; set; }

    public DateTime? DoneAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Admission Admission { get; set; } = null!;

    public virtual AppUser? DoneByNavigation { get; set; }

    public virtual Hospital Hospital { get; set; } = null!;
}
