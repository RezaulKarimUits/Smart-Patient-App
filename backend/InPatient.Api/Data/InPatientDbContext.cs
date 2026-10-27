using System;
using System.Collections.Generic;
using InPatient.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Data;

public partial class InPatientDbContext : DbContext
{
    public InPatientDbContext(DbContextOptions<InPatientDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Admission> Admissions { get; set; }

    public virtual DbSet<AdmissionWardStay> AdmissionWardStays { get; set; }

    public virtual DbSet<AiSummary> AiSummaries { get; set; }

    public virtual DbSet<Alert> Alerts { get; set; }

    public virtual DbSet<AppUser> AppUsers { get; set; }

    public virtual DbSet<Bed> Beds { get; set; }

    public virtual DbSet<BedEvent> BedEvents { get; set; }

    public virtual DbSet<CareTask> CareTasks { get; set; }

    public virtual DbSet<ClinicalNote> ClinicalNotes { get; set; }

    public virtual DbSet<ConsultantWard> ConsultantWards { get; set; }

    public virtual DbSet<DoseTask> DoseTasks { get; set; }

    public virtual DbSet<Hospital> Hospitals { get; set; }

    public virtual DbSet<LabResult> LabResults { get; set; }

    public virtual DbSet<NurseShift> NurseShifts { get; set; }

    public virtual DbSet<NurseShiftBed> NurseShiftBeds { get; set; }

    public virtual DbSet<Patient> Patients { get; set; }

    public virtual DbSet<Prescription> Prescriptions { get; set; }

    public virtual DbSet<PrescriptionItem> PrescriptionItems { get; set; }

    public virtual DbSet<StoredFile> StoredFiles { get; set; }

    public virtual DbSet<VitalSign> VitalSigns { get; set; }

    public virtual DbSet<VoiceCapture> VoiceCaptures { get; set; }

    public virtual DbSet<VwPatientWardJourney> VwPatientWardJourneys { get; set; }

    public virtual DbSet<VwWardHistory> VwWardHistories { get; set; }

    public virtual DbSet<VwWardOccupancyNow> VwWardOccupancyNows { get; set; }

    public virtual DbSet<Ward> Wards { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Admission>(entity =>
        {
            entity.ToTable("Admission");

            entity.HasIndex(e => new { e.HospitalId, e.Status }, "IX_Admission_Hospital_Status");

            entity.HasIndex(e => e.PatientId, "IX_Admission_Patient");

            entity.HasIndex(e => e.WristbandToken, "UQ_Admission_WristbandToken").IsUnique();

            entity.HasIndex(e => e.CurrentBedId, "UX_Admission_ActiveBed")
                .IsUnique()
                .HasFilter("([Status]=N'Active' AND [CurrentBedId] IS NOT NULL)");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Admission_CreatedAt");
            entity.Property(e => e.ProvisionalDiagnosis).HasMaxLength(300);
            entity.Property(e => e.Status)
                .HasMaxLength(12)
                .HasDefaultValue("Active", "DF_Admission_Status");
            entity.Property(e => e.WristbandToken)
                .HasMaxLength(64)
                .IsUnicode(false);

            entity.HasOne(d => d.ConsultantUser).WithMany(p => p.AdmissionConsultantUsers)
                .HasForeignKey(d => d.ConsultantUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Admission_Consultant");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.AdmissionCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_Admission_CreatedBy");

            entity.HasOne(d => d.CurrentBed).WithOne(p => p.Admission)
                .HasForeignKey<Admission>(d => d.CurrentBedId)
                .HasConstraintName("FK_Admission_Bed");

            entity.HasOne(d => d.Hospital).WithMany(p => p.Admissions)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Admission_Hospital");

            entity.HasOne(d => d.Patient).WithMany(p => p.Admissions)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Admission_Patient");
        });

        modelBuilder.Entity<AdmissionWardStay>(entity =>
        {
            entity.ToTable("AdmissionWardStay");

            entity.HasIndex(e => new { e.AdmissionId, e.StartAt }, "IX_AdmissionWardStay_Admission");

            entity.HasIndex(e => new { e.WardId, e.StartAt, e.EndAt }, "IX_AdmissionWardStay_Ward");

            entity.Property(e => e.Reason).HasMaxLength(10);

            entity.HasOne(d => d.Admission).WithMany(p => p.AdmissionWardStays)
                .HasForeignKey(d => d.AdmissionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AdmissionWardStay_Admission");

            entity.HasOne(d => d.Bed).WithMany(p => p.AdmissionWardStays)
                .HasForeignKey(d => d.BedId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AdmissionWardStay_Bed");

            entity.HasOne(d => d.Hospital).WithMany(p => p.AdmissionWardStays)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AdmissionWardStay_Hospital");

            entity.HasOne(d => d.Ward).WithMany(p => p.AdmissionWardStays)
                .HasForeignKey(d => d.WardId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AdmissionWardStay_Ward");
        });

        modelBuilder.Entity<AiSummary>(entity =>
        {
            entity.ToTable("AiSummary");

            entity.HasIndex(e => new { e.AdmissionId, e.Kind, e.CreatedAt }, "IX_AiSummary_Admission_Kind").IsDescending(false, false, true);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_AiSummary_CreatedAt");
            entity.Property(e => e.Kind).HasMaxLength(12);

            entity.HasOne(d => d.Admission).WithMany(p => p.AiSummaries)
                .HasForeignKey(d => d.AdmissionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AiSummary_Admission");

            entity.HasOne(d => d.Hospital).WithMany(p => p.AiSummaries)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AiSummary_Hospital");
        });

        modelBuilder.Entity<Alert>(entity =>
        {
            entity.ToTable("Alert");

            entity.HasIndex(e => new { e.AdmissionId, e.Status, e.AlertType }, "IX_Alert_Admission_Status");

            entity.HasIndex(e => new { e.HospitalId, e.Status, e.Severity }, "IX_Alert_Hospital_Status");

            entity.Property(e => e.AiExplanation).HasMaxLength(1000);
            entity.Property(e => e.AlertType).HasMaxLength(12);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Alert_CreatedAt");
            entity.Property(e => e.RuleCode).HasMaxLength(40);
            entity.Property(e => e.RuleDetail).HasMaxLength(500);
            entity.Property(e => e.Severity).HasMaxLength(10);
            entity.Property(e => e.Status)
                .HasMaxLength(14)
                .HasDefaultValue("Open", "DF_Alert_Status");

            entity.HasOne(d => d.AckByNavigation).WithMany(p => p.Alerts)
                .HasForeignKey(d => d.AckBy)
                .HasConstraintName("FK_Alert_AckBy");

            entity.HasOne(d => d.Admission).WithMany(p => p.Alerts)
                .HasForeignKey(d => d.AdmissionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Alert_Admission");

            entity.HasOne(d => d.DoseTask).WithMany(p => p.Alerts)
                .HasForeignKey(d => d.DoseTaskId)
                .HasConstraintName("FK_Alert_DoseTask");

            entity.HasOne(d => d.Hospital).WithMany(p => p.Alerts)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Alert_Hospital");
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("AppUser");

            entity.HasIndex(e => new { e.HospitalId, e.Role }, "IX_AppUser_Hospital_Role");

            entity.HasIndex(e => e.Phone, "IX_AppUser_Phone");

            entity.HasIndex(e => new { e.HospitalId, e.Phone }, "UX_AppUser_Hospital_Phone")
                .IsUnique()
                .HasFilter("([HospitalId] IS NOT NULL)");

            entity.HasIndex(e => e.Phone, "UX_AppUser_SuperAdmin_Phone")
                .IsUnique()
                .HasFilter("([HospitalId] IS NULL)");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_AppUser_CreatedAt");
            entity.Property(e => e.Designation).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_AppUser_IsActive");
            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.PasswordHash).HasMaxLength(100);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Role).HasMaxLength(20);

            entity.HasOne(d => d.Hospital).WithMany(p => p.AppUsers)
                .HasForeignKey(d => d.HospitalId)
                .HasConstraintName("FK_AppUser_Hospital");

            entity.HasOne(d => d.Patient).WithMany(p => p.AppUsers)
                .HasForeignKey(d => d.PatientId)
                .HasConstraintName("FK_AppUser_Patient");
        });

        modelBuilder.Entity<Bed>(entity =>
        {
            entity.ToTable("Bed");

            entity.HasIndex(e => new { e.HospitalId, e.Status }, "IX_Bed_Hospital_Status");

            entity.HasIndex(e => e.QrToken, "UQ_Bed_QrToken").IsUnique();

            entity.HasIndex(e => new { e.WardId, e.BedNumber }, "UQ_Bed_Ward_Number").IsUnique();

            entity.Property(e => e.BedNumber).HasMaxLength(10);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Bed_CreatedAt");
            entity.Property(e => e.QrToken)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.Status)
                .HasMaxLength(10)
                .HasDefaultValue("Free", "DF_Bed_Status");
            entity.Property(e => e.StatusChangedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Bed_StatusChangedAt");

            entity.HasOne(d => d.Hospital).WithMany(p => p.Beds)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Bed_Hospital");

            entity.HasOne(d => d.Ward).WithMany(p => p.Beds)
                .HasForeignKey(d => d.WardId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Bed_Ward");
        });

        modelBuilder.Entity<BedEvent>(entity =>
        {
            entity.ToTable("BedEvent");

            entity.HasIndex(e => new { e.BedId, e.At }, "IX_BedEvent_Bed_At");

            entity.HasIndex(e => new { e.HospitalId, e.At }, "IX_BedEvent_Hospital_At");

            entity.Property(e => e.EventType).HasMaxLength(16);
            entity.Property(e => e.Note).HasMaxLength(200);

            entity.HasOne(d => d.Admission).WithMany(p => p.BedEvents)
                .HasForeignKey(d => d.AdmissionId)
                .HasConstraintName("FK_BedEvent_Admission");

            entity.HasOne(d => d.Bed).WithMany(p => p.BedEvents)
                .HasForeignKey(d => d.BedId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_BedEvent_Bed");

            entity.HasOne(d => d.ByUser).WithMany(p => p.BedEvents)
                .HasForeignKey(d => d.ByUserId)
                .HasConstraintName("FK_BedEvent_User");

            entity.HasOne(d => d.Hospital).WithMany(p => p.BedEvents)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_BedEvent_Hospital");
        });

        modelBuilder.Entity<CareTask>(entity =>
        {
            entity.ToTable("CareTask");

            entity.HasIndex(e => new { e.AdmissionId, e.Status }, "IX_CareTask_Admission_Status");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_CareTask_CreatedAt");
            entity.Property(e => e.Detail).HasMaxLength(300);
            entity.Property(e => e.Status)
                .HasMaxLength(10)
                .HasDefaultValue("Open", "DF_CareTask_Status");
            entity.Property(e => e.TaskType).HasMaxLength(20);

            entity.HasOne(d => d.Admission).WithMany(p => p.CareTasks)
                .HasForeignKey(d => d.AdmissionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CareTask_Admission");

            entity.HasOne(d => d.DoneByNavigation).WithMany(p => p.CareTasks)
                .HasForeignKey(d => d.DoneBy)
                .HasConstraintName("FK_CareTask_DoneBy");

            entity.HasOne(d => d.Hospital).WithMany(p => p.CareTasks)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CareTask_Hospital");
        });

        modelBuilder.Entity<ClinicalNote>(entity =>
        {
            entity.ToTable("ClinicalNote");

            entity.HasIndex(e => new { e.AdmissionId, e.CreatedAt }, "IX_ClinicalNote_Admission").IsDescending(false, true);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ClinicalNote_CreatedAt");
            entity.Property(e => e.NoteType).HasMaxLength(16);
            entity.Property(e => e.Source).HasMaxLength(10);

            entity.HasOne(d => d.Admission).WithMany(p => p.ClinicalNotes)
                .HasForeignKey(d => d.AdmissionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ClinicalNote_Admission");

            entity.HasOne(d => d.AudioFile).WithMany(p => p.ClinicalNotes)
                .HasForeignKey(d => d.AudioFileId)
                .HasConstraintName("FK_ClinicalNote_Audio");

            entity.HasOne(d => d.AuthorUser).WithMany(p => p.ClinicalNoteAuthorUsers)
                .HasForeignKey(d => d.AuthorUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ClinicalNote_Author");

            entity.HasOne(d => d.Hospital).WithMany(p => p.ClinicalNotes)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ClinicalNote_Hospital");

            entity.HasOne(d => d.SignedOffByNavigation).WithMany(p => p.ClinicalNoteSignedOffByNavigations)
                .HasForeignKey(d => d.SignedOffBy)
                .HasConstraintName("FK_ClinicalNote_SignedOffBy");
        });

        modelBuilder.Entity<ConsultantWard>(entity =>
        {
            entity.HasKey(e => new { e.ConsultantUserId, e.WardId });

            entity.ToTable("ConsultantWard");

            entity.HasIndex(e => e.WardId, "IX_ConsultantWard_Ward");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ConsultantWard_CreatedAt");

            entity.HasOne(d => d.ConsultantUser).WithMany(p => p.ConsultantWards)
                .HasForeignKey(d => d.ConsultantUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ConsultantWard_User");

            entity.HasOne(d => d.Hospital).WithMany(p => p.ConsultantWards)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ConsultantWard_Hospital");

            entity.HasOne(d => d.Ward).WithMany(p => p.ConsultantWards)
                .HasForeignKey(d => d.WardId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ConsultantWard_Ward");
        });

        modelBuilder.Entity<DoseTask>(entity =>
        {
            entity.ToTable("DoseTask");

            entity.HasIndex(e => new { e.AdmissionId, e.DueAt }, "IX_DoseTask_Admission_DueAt");

            entity.HasIndex(e => e.PrescriptionItemId, "IX_DoseTask_Item");

            entity.HasIndex(e => new { e.Status, e.DueAt }, "IX_DoseTask_Status_DueAt");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_DoseTask_CreatedAt");
            entity.Property(e => e.Reason).HasMaxLength(300);
            entity.Property(e => e.Status)
                .HasMaxLength(10)
                .HasDefaultValue("Scheduled", "DF_DoseTask_Status");

            entity.HasOne(d => d.Admission).WithMany(p => p.DoseTasks)
                .HasForeignKey(d => d.AdmissionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DoseTask_Admission");

            entity.HasOne(d => d.DoneByUser).WithMany(p => p.DoseTasks)
                .HasForeignKey(d => d.DoneByUserId)
                .HasConstraintName("FK_DoseTask_DoneBy");

            entity.HasOne(d => d.Hospital).WithMany(p => p.DoseTasks)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DoseTask_Hospital");

            entity.HasOne(d => d.PrescriptionItem).WithMany(p => p.DoseTasks)
                .HasForeignKey(d => d.PrescriptionItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DoseTask_Item");
        });

        modelBuilder.Entity<Hospital>(entity =>
        {
            entity.ToTable("Hospital");

            entity.HasIndex(e => e.Code, "UQ_Hospital_Code").IsUnique();

            entity.Property(e => e.Address).HasMaxLength(400);
            entity.Property(e => e.Code).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Hospital_CreatedAt");
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Hospital_IsActive");
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Phone).HasMaxLength(30);
        });

        modelBuilder.Entity<LabResult>(entity =>
        {
            entity.ToTable("LabResult");

            entity.HasIndex(e => new { e.AdmissionId, e.TestName, e.ResultAt }, "IX_LabResult_Admission_Test");

            entity.HasIndex(e => new { e.HospitalId, e.SignedOffAt }, "IX_LabResult_Pending");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_LabResult_CreatedAt");
            entity.Property(e => e.Flag)
                .HasMaxLength(10)
                .HasDefaultValue("Normal", "DF_LabResult_Flag");
            entity.Property(e => e.NumericValue).HasColumnType("decimal(12, 3)");
            entity.Property(e => e.RefHigh).HasColumnType("decimal(12, 3)");
            entity.Property(e => e.RefLow).HasColumnType("decimal(12, 3)");
            entity.Property(e => e.Source).HasMaxLength(10);
            entity.Property(e => e.TestName).HasMaxLength(80);
            entity.Property(e => e.TextValue).HasMaxLength(200);
            entity.Property(e => e.Unit).HasMaxLength(30);

            entity.HasOne(d => d.Admission).WithMany(p => p.LabResults)
                .HasForeignKey(d => d.AdmissionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LabResult_Admission");

            entity.HasOne(d => d.Hospital).WithMany(p => p.LabResults)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LabResult_Hospital");

            entity.HasOne(d => d.RecordedByNavigation).WithMany(p => p.LabResultRecordedByNavigations)
                .HasForeignKey(d => d.RecordedBy)
                .HasConstraintName("FK_LabResult_RecordedBy");

            entity.HasOne(d => d.SignedOffByNavigation).WithMany(p => p.LabResultSignedOffByNavigations)
                .HasForeignKey(d => d.SignedOffBy)
                .HasConstraintName("FK_LabResult_SignedOffBy");

            entity.HasOne(d => d.StoredFile).WithMany(p => p.LabResults)
                .HasForeignKey(d => d.StoredFileId)
                .HasConstraintName("FK_LabResult_File");
        });

        modelBuilder.Entity<NurseShift>(entity =>
        {
            entity.ToTable("NurseShift");

            entity.HasIndex(e => new { e.WardId, e.ShiftDate, e.ShiftType }, "IX_NurseShift_Ward_Date");

            entity.HasIndex(e => new { e.NurseUserId, e.ShiftDate, e.ShiftType }, "UQ_NurseShift_Nurse_Date_Type").IsUnique();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_NurseShift_CreatedAt");
            entity.Property(e => e.ShiftType).HasMaxLength(10);

            entity.HasOne(d => d.Hospital).WithMany(p => p.NurseShifts)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_NurseShift_Hospital");

            entity.HasOne(d => d.NurseUser).WithMany(p => p.NurseShifts)
                .HasForeignKey(d => d.NurseUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_NurseShift_User");

            entity.HasOne(d => d.Ward).WithMany(p => p.NurseShifts)
                .HasForeignKey(d => d.WardId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_NurseShift_Ward");
        });

        modelBuilder.Entity<NurseShiftBed>(entity =>
        {
            entity.HasKey(e => new { e.NurseShiftId, e.BedId });

            entity.ToTable("NurseShiftBed");

            entity.HasIndex(e => e.BedId, "IX_NurseShiftBed_Bed");

            entity.HasOne(d => d.Bed).WithMany(p => p.NurseShiftBeds)
                .HasForeignKey(d => d.BedId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_NurseShiftBed_Bed");

            entity.HasOne(d => d.Hospital).WithMany(p => p.NurseShiftBeds)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_NurseShiftBed_Hospital");

            entity.HasOne(d => d.NurseShift).WithMany(p => p.NurseShiftBeds)
                .HasForeignKey(d => d.NurseShiftId)
                .HasConstraintName("FK_NurseShiftBed_Shift");
        });

        modelBuilder.Entity<Patient>(entity =>
        {
            entity.ToTable("Patient");

            entity.HasIndex(e => new { e.HospitalId, e.Name }, "IX_Patient_Hospital_Name");

            entity.HasIndex(e => new { e.HospitalId, e.Phone }, "IX_Patient_Hospital_Phone");

            entity.HasIndex(e => new { e.HospitalId, e.Mrn }, "UQ_Patient_Hospital_MRN").IsUnique();

            entity.Property(e => e.BloodGroup).HasMaxLength(5);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Patient_CreatedAt");
            entity.Property(e => e.Guardian).HasMaxLength(150);
            entity.Property(e => e.Mrn)
                .HasMaxLength(20)
                .HasColumnName("MRN");
            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Sex).HasMaxLength(1);

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Patients)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_Patient_CreatedBy");

            entity.HasOne(d => d.Hospital).WithMany(p => p.Patients)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Patient_Hospital");
        });

        modelBuilder.Entity<Prescription>(entity =>
        {
            entity.ToTable("Prescription");

            entity.HasIndex(e => new { e.AdmissionId, e.Status }, "IX_Prescription_Admission");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Prescription_CreatedAt");
            entity.Property(e => e.GeneralInstructions).HasMaxLength(1000);
            entity.Property(e => e.Source).HasMaxLength(10);
            entity.Property(e => e.Status)
                .HasMaxLength(10)
                .HasDefaultValue("Draft", "DF_Prescription_Status");

            entity.HasOne(d => d.Admission).WithMany(p => p.Prescriptions)
                .HasForeignKey(d => d.AdmissionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Prescription_Admission");

            entity.HasOne(d => d.ConsultantUser).WithMany(p => p.Prescriptions)
                .HasForeignKey(d => d.ConsultantUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Prescription_Consultant");

            entity.HasOne(d => d.Hospital).WithMany(p => p.Prescriptions)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Prescription_Hospital");
        });

        modelBuilder.Entity<PrescriptionItem>(entity =>
        {
            entity.ToTable("PrescriptionItem");

            entity.HasIndex(e => e.PrescriptionId, "IX_PrescriptionItem_Prescription");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_PrescriptionItem_CreatedAt");
            entity.Property(e => e.Dose).HasMaxLength(40);
            entity.Property(e => e.DrugName).HasMaxLength(120);
            entity.Property(e => e.Form).HasMaxLength(30);
            entity.Property(e => e.Frequency).HasMaxLength(6);
            entity.Property(e => e.Instructions).HasMaxLength(300);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_PrescriptionItem_IsActive");
            entity.Property(e => e.Route).HasMaxLength(20);
            entity.Property(e => e.Strength).HasMaxLength(40);

            entity.HasOne(d => d.Hospital).WithMany(p => p.PrescriptionItems)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PrescriptionItem_Hospital");

            entity.HasOne(d => d.Prescription).WithMany(p => p.PrescriptionItems)
                .HasForeignKey(d => d.PrescriptionId)
                .HasConstraintName("FK_PrescriptionItem_Prescription");
        });

        modelBuilder.Entity<StoredFile>(entity =>
        {
            entity.ToTable("StoredFile");

            entity.HasIndex(e => new { e.AdmissionId, e.Kind }, "IX_StoredFile_Admission");

            entity.Property(e => e.BlobPath).HasMaxLength(400);
            entity.Property(e => e.Caption).HasMaxLength(300);
            entity.Property(e => e.ContentType).HasMaxLength(100);
            entity.Property(e => e.Kind).HasMaxLength(16);
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_StoredFile_UploadedAt");

            entity.HasOne(d => d.Admission).WithMany(p => p.StoredFiles)
                .HasForeignKey(d => d.AdmissionId)
                .HasConstraintName("FK_StoredFile_Admission");

            entity.HasOne(d => d.Hospital).WithMany(p => p.StoredFiles)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StoredFile_Hospital");

            entity.HasOne(d => d.Prescription).WithMany(p => p.StoredFiles)
                .HasForeignKey(d => d.PrescriptionId)
                .HasConstraintName("FK_StoredFile_Prescription");

            entity.HasOne(d => d.UploadedByNavigation).WithMany(p => p.StoredFiles)
                .HasForeignKey(d => d.UploadedBy)
                .HasConstraintName("FK_StoredFile_User");
        });

        modelBuilder.Entity<VitalSign>(entity =>
        {
            entity.ToTable("VitalSign");

            entity.HasIndex(e => new { e.AdmissionId, e.RecordedAt }, "IX_VitalSign_Admission_At").IsDescending(false, true);

            entity.HasIndex(e => new { e.HospitalId, e.SignedOffAt }, "IX_VitalSign_Pending");

            entity.Property(e => e.Consciousness)
                .HasMaxLength(12)
                .HasDefaultValue("Alert", "DF_VitalSign_Consciousness");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_VitalSign_CreatedAt");
            entity.Property(e => e.News2Band).HasMaxLength(10);
            entity.Property(e => e.Source).HasMaxLength(10);
            entity.Property(e => e.TemperatureC).HasColumnType("decimal(4, 1)");

            entity.HasOne(d => d.Admission).WithMany(p => p.VitalSigns)
                .HasForeignKey(d => d.AdmissionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VitalSign_Admission");

            entity.HasOne(d => d.Hospital).WithMany(p => p.VitalSigns)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VitalSign_Hospital");

            entity.HasOne(d => d.RecordedByNavigation).WithMany(p => p.VitalSignRecordedByNavigations)
                .HasForeignKey(d => d.RecordedBy)
                .HasConstraintName("FK_VitalSign_RecordedBy");

            entity.HasOne(d => d.SignedOffByNavigation).WithMany(p => p.VitalSignSignedOffByNavigations)
                .HasForeignKey(d => d.SignedOffBy)
                .HasConstraintName("FK_VitalSign_SignedOffBy");
        });

        modelBuilder.Entity<VoiceCapture>(entity =>
        {
            entity.ToTable("VoiceCapture");

            entity.HasIndex(e => new { e.AdmissionId, e.CreatedAt }, "IX_VoiceCapture_Admission").IsDescending(false, true);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_VoiceCapture_CreatedAt");
            entity.Property(e => e.Purpose).HasMaxLength(14);
            entity.Property(e => e.Status)
                .HasMaxLength(10)
                .HasDefaultValue("Drafted", "DF_VoiceCapture_Status");

            entity.HasOne(d => d.Admission).WithMany(p => p.VoiceCaptures)
                .HasForeignKey(d => d.AdmissionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VoiceCapture_Admission");

            entity.HasOne(d => d.AudioFile).WithMany(p => p.VoiceCaptures)
                .HasForeignKey(d => d.AudioFileId)
                .HasConstraintName("FK_VoiceCapture_Audio");

            entity.HasOne(d => d.Hospital).WithMany(p => p.VoiceCaptures)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VoiceCapture_Hospital");

            entity.HasOne(d => d.User).WithMany(p => p.VoiceCaptures)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VoiceCapture_User");
        });

        modelBuilder.Entity<VwPatientWardJourney>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_PatientWardJourney");

            entity.Property(e => e.AdmissionStatus).HasMaxLength(12);
            entity.Property(e => e.BedNumber).HasMaxLength(10);
            entity.Property(e => e.DurationHours).HasColumnType("decimal(9, 1)");
            entity.Property(e => e.Mrn)
                .HasMaxLength(20)
                .HasColumnName("MRN");
            entity.Property(e => e.PatientName).HasMaxLength(150);
            entity.Property(e => e.Reason).HasMaxLength(10);
            entity.Property(e => e.WardName).HasMaxLength(100);
        });

        modelBuilder.Entity<VwWardHistory>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_WardHistory");

            entity.Property(e => e.AvgLengthOfStayDays).HasColumnType("decimal(9, 2)");
            entity.Property(e => e.OccupiedBedDays).HasColumnType("decimal(9, 2)");
            entity.Property(e => e.WardName).HasMaxLength(100);
        });

        modelBuilder.Entity<VwWardOccupancyNow>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_WardOccupancyNow");

            entity.Property(e => e.OccupancyPct).HasColumnType("decimal(5, 1)");
            entity.Property(e => e.WardCode).HasMaxLength(10);
            entity.Property(e => e.WardName).HasMaxLength(100);
            entity.Property(e => e.WardType).HasMaxLength(20);
        });

        modelBuilder.Entity<Ward>(entity =>
        {
            entity.ToTable("Ward");

            entity.HasIndex(e => new { e.HospitalId, e.Code }, "UQ_Ward_Hospital_Code").IsUnique();

            entity.Property(e => e.Code).HasMaxLength(10);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Ward_CreatedAt");
            entity.Property(e => e.Floor).HasMaxLength(20);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Ward_IsActive");
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Type).HasMaxLength(20);

            entity.HasOne(d => d.Hospital).WithMany(p => p.Wards)
                .HasForeignKey(d => d.HospitalId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Ward_Hospital");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
