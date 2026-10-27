using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class Admission
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int PatientId { get; set; }

    public int? CurrentBedId { get; set; }

    public int ConsultantUserId { get; set; }

    public string WristbandToken { get; set; } = null!;

    public DateTime AdmittedAt { get; set; }

    public DateTime? BedConfirmedAt { get; set; }

    public string? ProvisionalDiagnosis { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? DischargedAt { get; set; }

    public string? DischargeSummary { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    public virtual ICollection<AdmissionWardStay> AdmissionWardStays { get; set; } = new List<AdmissionWardStay>();

    public virtual ICollection<AiSummary> AiSummaries { get; set; } = new List<AiSummary>();

    public virtual ICollection<Alert> Alerts { get; set; } = new List<Alert>();

    public virtual ICollection<BedEvent> BedEvents { get; set; } = new List<BedEvent>();

    public virtual ICollection<CareTask> CareTasks { get; set; } = new List<CareTask>();

    public virtual ICollection<ClinicalNote> ClinicalNotes { get; set; } = new List<ClinicalNote>();

    public virtual AppUser ConsultantUser { get; set; } = null!;

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual Bed? CurrentBed { get; set; }

    public virtual ICollection<DoseTask> DoseTasks { get; set; } = new List<DoseTask>();

    public virtual Hospital Hospital { get; set; } = null!;

    public virtual ICollection<LabResult> LabResults { get; set; } = new List<LabResult>();

    public virtual Patient Patient { get; set; } = null!;

    public virtual ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();

    public virtual ICollection<StoredFile> StoredFiles { get; set; } = new List<StoredFile>();

    public virtual ICollection<VitalSign> VitalSigns { get; set; } = new List<VitalSign>();

    public virtual ICollection<VoiceCapture> VoiceCaptures { get; set; } = new List<VoiceCapture>();
}
