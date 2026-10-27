using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class AppUser
{
    public int Id { get; set; }

    public int? HospitalId { get; set; }

    public string Name { get; set; } = null!;

    public string Phone { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string Role { get; set; } = null!;

    public string? Designation { get; set; }

    public bool IsActive { get; set; }

    public int? PatientId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Admission> AdmissionConsultantUsers { get; set; } = new List<Admission>();

    public virtual ICollection<Admission> AdmissionCreatedByNavigations { get; set; } = new List<Admission>();

    public virtual ICollection<Alert> Alerts { get; set; } = new List<Alert>();

    public virtual ICollection<BedEvent> BedEvents { get; set; } = new List<BedEvent>();

    public virtual ICollection<CareTask> CareTasks { get; set; } = new List<CareTask>();

    public virtual ICollection<ClinicalNote> ClinicalNoteAuthorUsers { get; set; } = new List<ClinicalNote>();

    public virtual ICollection<ClinicalNote> ClinicalNoteSignedOffByNavigations { get; set; } = new List<ClinicalNote>();

    public virtual ICollection<ConsultantWard> ConsultantWards { get; set; } = new List<ConsultantWard>();

    public virtual ICollection<DoseTask> DoseTasks { get; set; } = new List<DoseTask>();

    public virtual Hospital? Hospital { get; set; }

    public virtual ICollection<LabResult> LabResultRecordedByNavigations { get; set; } = new List<LabResult>();

    public virtual ICollection<LabResult> LabResultSignedOffByNavigations { get; set; } = new List<LabResult>();

    public virtual ICollection<NurseShift> NurseShifts { get; set; } = new List<NurseShift>();

    public virtual Patient? Patient { get; set; }

    public virtual ICollection<Patient> Patients { get; set; } = new List<Patient>();

    public virtual ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();

    public virtual ICollection<StoredFile> StoredFiles { get; set; } = new List<StoredFile>();

    public virtual ICollection<VitalSign> VitalSignRecordedByNavigations { get; set; } = new List<VitalSign>();

    public virtual ICollection<VitalSign> VitalSignSignedOffByNavigations { get; set; } = new List<VitalSign>();

    public virtual ICollection<VoiceCapture> VoiceCaptures { get; set; } = new List<VoiceCapture>();
}
