using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class Bed
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int WardId { get; set; }

    public string BedNumber { get; set; } = null!;

    public string QrToken { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime StatusChangedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Admission? Admission { get; set; }

    public virtual ICollection<AdmissionWardStay> AdmissionWardStays { get; set; } = new List<AdmissionWardStay>();

    public virtual ICollection<BedEvent> BedEvents { get; set; } = new List<BedEvent>();

    public virtual Hospital Hospital { get; set; } = null!;

    public virtual ICollection<NurseShiftBed> NurseShiftBeds { get; set; } = new List<NurseShiftBed>();

    public virtual Ward Ward { get; set; } = null!;
}
