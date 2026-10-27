using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class Prescription
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int AdmissionId { get; set; }

    public int ConsultantUserId { get; set; }

    public string Source { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime? SignedAt { get; set; }

    public string? GeneralInstructions { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Admission Admission { get; set; } = null!;

    public virtual AppUser ConsultantUser { get; set; } = null!;

    public virtual Hospital Hospital { get; set; } = null!;

    public virtual ICollection<PrescriptionItem> PrescriptionItems { get; set; } = new List<PrescriptionItem>();

    public virtual ICollection<StoredFile> StoredFiles { get; set; } = new List<StoredFile>();
}
