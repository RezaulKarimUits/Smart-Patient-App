using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class VwPatientWardJourney
{
    public int HospitalId { get; set; }

    public int StayId { get; set; }

    public int AdmissionId { get; set; }

    public int PatientId { get; set; }

    public string Mrn { get; set; } = null!;

    public string PatientName { get; set; } = null!;

    public int WardId { get; set; }

    public string WardName { get; set; } = null!;

    public int BedId { get; set; }

    public string BedNumber { get; set; } = null!;

    public string Reason { get; set; } = null!;

    public DateTime StartAt { get; set; }

    public DateTime? EndAt { get; set; }

    public decimal? DurationHours { get; set; }

    public long? Sequence { get; set; }

    public string AdmissionStatus { get; set; } = null!;
}
