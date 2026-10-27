using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class AdmissionWardStay
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int AdmissionId { get; set; }

    public int WardId { get; set; }

    public int BedId { get; set; }

    public DateTime StartAt { get; set; }

    public DateTime? EndAt { get; set; }

    public string Reason { get; set; } = null!;

    public virtual Admission Admission { get; set; } = null!;

    public virtual Bed Bed { get; set; } = null!;

    public virtual Hospital Hospital { get; set; } = null!;

    public virtual Ward Ward { get; set; } = null!;
}
