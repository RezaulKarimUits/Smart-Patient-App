using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class Hospital
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string Code { get; set; } = null!;

    public string? Address { get; set; }

    public string? Phone { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<AdmissionWardStay> AdmissionWardStays { get; set; } = new List<AdmissionWardStay>();

    public virtual ICollection<Admission> Admissions { get; set; } = new List<Admission>();

    public virtual ICollection<AiSummary> AiSummaries { get; set; } = new List<AiSummary>();

    public virtual ICollection<Alert> Alerts { get; set; } = new List<Alert>();

    public virtual ICollection<AppUser> AppUsers { get; set; } = new List<AppUser>();

    public virtual ICollection<BedEvent> BedEvents { get; set; } = new List<BedEvent>();

    public virtual ICollection<Bed> Beds { get; set; } = new List<Bed>();

    public virtual ICollection<CareTask> CareTasks { get; set; } = new List<CareTask>();

    public virtual ICollection<ClinicalNote> ClinicalNotes { get; set; } = new List<ClinicalNote>();

    public virtual ICollection<ConsultantWard> ConsultantWards { get; set; } = new List<ConsultantWard>();

    public virtual ICollection<DoseTask> DoseTasks { get; set; } = new List<DoseTask>();

    public virtual ICollection<LabResult> LabResults { get; set; } = new List<LabResult>();

    public virtual ICollection<NurseShiftBed> NurseShiftBeds { get; set; } = new List<NurseShiftBed>();

    public virtual ICollection<NurseShift> NurseShifts { get; set; } = new List<NurseShift>();

    public virtual ICollection<Patient> Patients { get; set; } = new List<Patient>();

    public virtual ICollection<PrescriptionItem> PrescriptionItems { get; set; } = new List<PrescriptionItem>();

    public virtual ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();

    public virtual ICollection<StoredFile> StoredFiles { get; set; } = new List<StoredFile>();

    public virtual ICollection<VitalSign> VitalSigns { get; set; } = new List<VitalSign>();

    public virtual ICollection<VoiceCapture> VoiceCaptures { get; set; } = new List<VoiceCapture>();

    public virtual ICollection<Ward> Wards { get; set; } = new List<Ward>();
}
