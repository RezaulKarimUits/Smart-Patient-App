using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class BedEvent
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int BedId { get; set; }

    public int? AdmissionId { get; set; }

    public string EventType { get; set; } = null!;

    public int? ByUserId { get; set; }

    public DateTime At { get; set; }

    public string? Note { get; set; }

    public virtual Admission? Admission { get; set; }

    public virtual Bed Bed { get; set; } = null!;

    public virtual AppUser? ByUser { get; set; }

    public virtual Hospital Hospital { get; set; } = null!;
}
