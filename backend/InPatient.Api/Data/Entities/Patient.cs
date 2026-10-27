using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class Patient
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public string Mrn { get; set; } = null!;

    public string Name { get; set; } = null!;

    public DateOnly DateOfBirth { get; set; }

    public string Sex { get; set; } = null!;

    public string? Phone { get; set; }

    public string? Guardian { get; set; }

    public string? BloodGroup { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    public virtual ICollection<Admission> Admissions { get; set; } = new List<Admission>();

    public virtual ICollection<AppUser> AppUsers { get; set; } = new List<AppUser>();

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual Hospital Hospital { get; set; } = null!;
}
