using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class DoseTask
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int PrescriptionItemId { get; set; }

    public int AdmissionId { get; set; }

    public DateTime DueAt { get; set; }

    public string Status { get; set; } = null!;

    public int? DoneByUserId { get; set; }

    public DateTime? DoneAt { get; set; }

    public string? Reason { get; set; }

    public DateTime? EscalatedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Admission Admission { get; set; } = null!;

    public virtual ICollection<Alert> Alerts { get; set; } = new List<Alert>();

    public virtual AppUser? DoneByUser { get; set; }

    public virtual Hospital Hospital { get; set; } = null!;

    public virtual PrescriptionItem PrescriptionItem { get; set; } = null!;
}
