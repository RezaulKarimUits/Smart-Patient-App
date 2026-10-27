using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class NurseShift
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int NurseUserId { get; set; }

    public int WardId { get; set; }

    public DateOnly ShiftDate { get; set; }

    public string ShiftType { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Hospital Hospital { get; set; } = null!;

    public virtual ICollection<NurseShiftBed> NurseShiftBeds { get; set; } = new List<NurseShiftBed>();

    public virtual AppUser NurseUser { get; set; } = null!;

    public virtual Ward Ward { get; set; } = null!;
}
