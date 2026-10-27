using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class PrescriptionItem
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int PrescriptionId { get; set; }

    public string DrugName { get; set; } = null!;

    public string? Form { get; set; }

    public string? Strength { get; set; }

    public string Dose { get; set; } = null!;

    public string Route { get; set; } = null!;

    public string Frequency { get; set; } = null!;

    public DateTime StartAt { get; set; }

    public int? DurationDays { get; set; }

    public string? Instructions { get; set; }

    public bool IsActive { get; set; }

    public DateTime? StoppedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<DoseTask> DoseTasks { get; set; } = new List<DoseTask>();

    public virtual Hospital Hospital { get; set; } = null!;

    public virtual Prescription Prescription { get; set; } = null!;
}
