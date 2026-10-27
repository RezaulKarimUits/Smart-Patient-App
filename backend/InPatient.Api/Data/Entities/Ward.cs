using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class Ward
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public string Name { get; set; } = null!;

    public string Code { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string? Floor { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<AdmissionWardStay> AdmissionWardStays { get; set; } = new List<AdmissionWardStay>();

    public virtual ICollection<Bed> Beds { get; set; } = new List<Bed>();

    public virtual ICollection<ConsultantWard> ConsultantWards { get; set; } = new List<ConsultantWard>();

    public virtual Hospital Hospital { get; set; } = null!;

    public virtual ICollection<NurseShift> NurseShifts { get; set; } = new List<NurseShift>();
}
