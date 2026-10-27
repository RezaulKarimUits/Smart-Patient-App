using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class StoredFile
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int? AdmissionId { get; set; }

    public int? PrescriptionId { get; set; }

    public string Kind { get; set; } = null!;

    public string? Caption { get; set; }

    public string BlobPath { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public long SizeBytes { get; set; }

    public int? UploadedBy { get; set; }

    public DateTime UploadedAt { get; set; }

    public virtual Admission? Admission { get; set; }

    public virtual ICollection<ClinicalNote> ClinicalNotes { get; set; } = new List<ClinicalNote>();

    public virtual Hospital Hospital { get; set; } = null!;

    public virtual ICollection<LabResult> LabResults { get; set; } = new List<LabResult>();

    public virtual Prescription? Prescription { get; set; }

    public virtual AppUser? UploadedByNavigation { get; set; }

    public virtual ICollection<VoiceCapture> VoiceCaptures { get; set; } = new List<VoiceCapture>();
}
