using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class VwWardOccupancyNow
{
    public int HospitalId { get; set; }

    public int WardId { get; set; }

    public string WardName { get; set; } = null!;

    public string WardCode { get; set; } = null!;

    public string WardType { get; set; } = null!;

    public int? TotalBeds { get; set; }

    public int? FreeBeds { get; set; }

    public int? ReservedBeds { get; set; }

    public int? OccupiedBeds { get; set; }

    public int? CleaningBeds { get; set; }

    public int? BlockedBeds { get; set; }

    public decimal? OccupancyPct { get; set; }
}
