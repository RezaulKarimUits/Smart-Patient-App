using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class NurseShiftBed
{
    public int NurseShiftId { get; set; }

    public int BedId { get; set; }

    public int HospitalId { get; set; }

    public virtual Bed Bed { get; set; } = null!;

    public virtual Hospital Hospital { get; set; } = null!;

    public virtual NurseShift NurseShift { get; set; } = null!;
}
