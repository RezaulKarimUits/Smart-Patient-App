using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class VwWardHistory
{
    public int HospitalId { get; set; }

    public int WardId { get; set; }

    public string WardName { get; set; } = null!;

    public DateOnly? Day { get; set; }

    public int? AdmissionsIn { get; set; }

    public int? TransfersIn { get; set; }

    public int? TransfersOut { get; set; }

    public int? Discharges { get; set; }

    public decimal? OccupiedBedDays { get; set; }

    public decimal? AvgLengthOfStayDays { get; set; }
}
