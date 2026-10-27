/* =====================================================================
   InPatient — 001_schema.sql  (database-first source of truth)
   All timestamps are UTC (datetime2). Status columns use CHECK constraints.
   Every tenant-owned table carries HospitalId.
   Re-running on an existing schema is a no-op (guarded with NOEXEC).
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
IF OBJECT_ID(N'dbo.Hospital', N'U') IS NOT NULL
BEGIN
    PRINT 'Schema already exists - skipping 001_schema.sql (use run-db -Reset to recreate).';
    SET NOEXEC ON;
END
GO

/* ---------- Tenancy & identity ---------- */
CREATE TABLE dbo.Hospital (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Hospital PRIMARY KEY,
    Name        NVARCHAR(200) NOT NULL,
    Code        NVARCHAR(20)  NOT NULL CONSTRAINT UQ_Hospital_Code UNIQUE,
    Address     NVARCHAR(400) NULL,
    Phone       NVARCHAR(30)  NULL,
    IsActive    BIT NOT NULL CONSTRAINT DF_Hospital_IsActive DEFAULT (1),
    CreatedAt   DATETIME2 NOT NULL CONSTRAINT DF_Hospital_CreatedAt DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE dbo.Patient (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Patient PRIMARY KEY,
    HospitalId  INT NOT NULL CONSTRAINT FK_Patient_Hospital REFERENCES dbo.Hospital(Id),
    MRN         NVARCHAR(20)  NOT NULL,
    Name        NVARCHAR(150) NOT NULL,
    DateOfBirth DATE NOT NULL,
    Sex         NVARCHAR(1) NOT NULL CONSTRAINT CK_Patient_Sex CHECK (Sex IN (N'M', N'F', N'O')),
    Phone       NVARCHAR(20)  NULL,
    Guardian    NVARCHAR(150) NULL,
    BloodGroup  NVARCHAR(5)   NULL CONSTRAINT CK_Patient_BloodGroup CHECK (BloodGroup IN (N'A+',N'A-',N'B+',N'B-',N'AB+',N'AB-',N'O+',N'O-')),
    CreatedAt   DATETIME2 NOT NULL CONSTRAINT DF_Patient_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy   INT NULL,
    CONSTRAINT UQ_Patient_Hospital_MRN UNIQUE (HospitalId, MRN)
);
CREATE INDEX IX_Patient_Hospital_Name ON dbo.Patient(HospitalId, Name);
CREATE INDEX IX_Patient_Hospital_Phone ON dbo.Patient(HospitalId, Phone);

CREATE TABLE dbo.AppUser (
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppUser PRIMARY KEY,
    HospitalId   INT NULL CONSTRAINT FK_AppUser_Hospital REFERENCES dbo.Hospital(Id),   -- NULL only for SuperAdmin
    Name         NVARCHAR(150) NOT NULL,
    Phone        NVARCHAR(20)  NOT NULL,
    PasswordHash NVARCHAR(100) NOT NULL,
    Role         NVARCHAR(20)  NOT NULL CONSTRAINT CK_AppUser_Role CHECK (Role IN (N'SuperAdmin',N'HospitalAdmin',N'Receptionist',N'Nurse',N'Consultant',N'Patient')),
    Designation  NVARCHAR(100) NULL,
    IsActive     BIT NOT NULL CONSTRAINT DF_AppUser_IsActive DEFAULT (1),
    PatientId    INT NULL CONSTRAINT FK_AppUser_Patient REFERENCES dbo.Patient(Id),
    CreatedAt    DATETIME2 NOT NULL CONSTRAINT DF_AppUser_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_AppUser_Tenant CHECK ((Role = N'SuperAdmin' AND HospitalId IS NULL) OR (Role <> N'SuperAdmin' AND HospitalId IS NOT NULL)),
    CONSTRAINT CK_AppUser_PatientLink CHECK ((Role = N'Patient' AND PatientId IS NOT NULL) OR (Role <> N'Patient' AND PatientId IS NULL))
);
CREATE UNIQUE INDEX UX_AppUser_Hospital_Phone ON dbo.AppUser(HospitalId, Phone) WHERE HospitalId IS NOT NULL;
CREATE UNIQUE INDEX UX_AppUser_SuperAdmin_Phone ON dbo.AppUser(Phone) WHERE HospitalId IS NULL;
CREATE INDEX IX_AppUser_Phone ON dbo.AppUser(Phone);
CREATE INDEX IX_AppUser_Hospital_Role ON dbo.AppUser(HospitalId, Role);

ALTER TABLE dbo.Patient ADD CONSTRAINT FK_Patient_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.AppUser(Id);

/* ---------- Wards & beds ---------- */
CREATE TABLE dbo.Ward (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Ward PRIMARY KEY,
    HospitalId  INT NOT NULL CONSTRAINT FK_Ward_Hospital REFERENCES dbo.Hospital(Id),
    Name        NVARCHAR(100) NOT NULL,
    Code        NVARCHAR(10)  NOT NULL,
    Type        NVARCHAR(20)  NOT NULL CONSTRAINT CK_Ward_Type CHECK (Type IN (N'Medicine',N'Surgery',N'Paediatrics',N'ICU',N'Gynae',N'Other')),
    Floor       NVARCHAR(20)  NULL,
    IsActive    BIT NOT NULL CONSTRAINT DF_Ward_IsActive DEFAULT (1),
    CreatedAt   DATETIME2 NOT NULL CONSTRAINT DF_Ward_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_Ward_Hospital_Code UNIQUE (HospitalId, Code)
);

CREATE TABLE dbo.Bed (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Bed PRIMARY KEY,
    HospitalId      INT NOT NULL CONSTRAINT FK_Bed_Hospital REFERENCES dbo.Hospital(Id),
    WardId          INT NOT NULL CONSTRAINT FK_Bed_Ward REFERENCES dbo.Ward(Id),
    BedNumber       NVARCHAR(10) NOT NULL,
    QrToken         VARCHAR(64)  NOT NULL CONSTRAINT UQ_Bed_QrToken UNIQUE,
    Status          NVARCHAR(10) NOT NULL CONSTRAINT DF_Bed_Status DEFAULT (N'Free')
                    CONSTRAINT CK_Bed_Status CHECK (Status IN (N'Free',N'Reserved',N'Occupied',N'Cleaning',N'Blocked')),
    StatusChangedAt DATETIME2 NOT NULL CONSTRAINT DF_Bed_StatusChangedAt DEFAULT (SYSUTCDATETIME()),
    CreatedAt       DATETIME2 NOT NULL CONSTRAINT DF_Bed_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_Bed_Ward_Number UNIQUE (WardId, BedNumber)
);
CREATE INDEX IX_Bed_Hospital_Status ON dbo.Bed(HospitalId, Status);

CREATE TABLE dbo.ConsultantWard (
    ConsultantUserId INT NOT NULL CONSTRAINT FK_ConsultantWard_User REFERENCES dbo.AppUser(Id),
    WardId           INT NOT NULL CONSTRAINT FK_ConsultantWard_Ward REFERENCES dbo.Ward(Id),
    HospitalId       INT NOT NULL CONSTRAINT FK_ConsultantWard_Hospital REFERENCES dbo.Hospital(Id),
    CreatedAt        DATETIME2 NOT NULL CONSTRAINT DF_ConsultantWard_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_ConsultantWard PRIMARY KEY (ConsultantUserId, WardId)
);
CREATE INDEX IX_ConsultantWard_Ward ON dbo.ConsultantWard(WardId);

CREATE TABLE dbo.NurseShift (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_NurseShift PRIMARY KEY,
    HospitalId  INT NOT NULL CONSTRAINT FK_NurseShift_Hospital REFERENCES dbo.Hospital(Id),
    NurseUserId INT NOT NULL CONSTRAINT FK_NurseShift_User REFERENCES dbo.AppUser(Id),
    WardId      INT NOT NULL CONSTRAINT FK_NurseShift_Ward REFERENCES dbo.Ward(Id),
    ShiftDate   DATE NOT NULL,                       -- Asia/Dhaka calendar date the shift starts on
    ShiftType   NVARCHAR(10) NOT NULL CONSTRAINT CK_NurseShift_Type CHECK (ShiftType IN (N'Morning',N'Evening',N'Night')),
    CreatedAt   DATETIME2 NOT NULL CONSTRAINT DF_NurseShift_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_NurseShift_Nurse_Date_Type UNIQUE (NurseUserId, ShiftDate, ShiftType)
);
CREATE INDEX IX_NurseShift_Ward_Date ON dbo.NurseShift(WardId, ShiftDate, ShiftType);

CREATE TABLE dbo.NurseShiftBed (
    NurseShiftId INT NOT NULL CONSTRAINT FK_NurseShiftBed_Shift REFERENCES dbo.NurseShift(Id) ON DELETE CASCADE,
    BedId        INT NOT NULL CONSTRAINT FK_NurseShiftBed_Bed REFERENCES dbo.Bed(Id),
    HospitalId   INT NOT NULL CONSTRAINT FK_NurseShiftBed_Hospital REFERENCES dbo.Hospital(Id),
    CONSTRAINT PK_NurseShiftBed PRIMARY KEY (NurseShiftId, BedId)
);
CREATE INDEX IX_NurseShiftBed_Bed ON dbo.NurseShiftBed(BedId);

/* ---------- Admissions ---------- */
CREATE TABLE dbo.Admission (
    Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Admission PRIMARY KEY,
    HospitalId           INT NOT NULL CONSTRAINT FK_Admission_Hospital REFERENCES dbo.Hospital(Id),
    PatientId            INT NOT NULL CONSTRAINT FK_Admission_Patient REFERENCES dbo.Patient(Id),
    CurrentBedId         INT NULL CONSTRAINT FK_Admission_Bed REFERENCES dbo.Bed(Id),
    ConsultantUserId     INT NOT NULL CONSTRAINT FK_Admission_Consultant REFERENCES dbo.AppUser(Id),
    WristbandToken       VARCHAR(64) NOT NULL CONSTRAINT UQ_Admission_WristbandToken UNIQUE,
    AdmittedAt           DATETIME2 NOT NULL,
    BedConfirmedAt       DATETIME2 NULL,            -- set when nurse scans bed + wristband (bed -> Occupied)
    ProvisionalDiagnosis NVARCHAR(300) NULL,
    Status               NVARCHAR(12) NOT NULL CONSTRAINT DF_Admission_Status DEFAULT (N'Active')
                         CONSTRAINT CK_Admission_Status CHECK (Status IN (N'Active',N'Discharged')),
    DischargedAt         DATETIME2 NULL,
    DischargeSummary     NVARCHAR(MAX) NULL,
    CreatedAt            DATETIME2 NOT NULL CONSTRAINT DF_Admission_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy            INT NULL CONSTRAINT FK_Admission_CreatedBy REFERENCES dbo.AppUser(Id),
    CONSTRAINT CK_Admission_Discharge CHECK ((Status = N'Discharged' AND DischargedAt IS NOT NULL) OR (Status = N'Active' AND DischargedAt IS NULL))
);
CREATE UNIQUE INDEX UX_Admission_ActiveBed ON dbo.Admission(CurrentBedId) WHERE Status = N'Active' AND CurrentBedId IS NOT NULL;
CREATE INDEX IX_Admission_Hospital_Status ON dbo.Admission(HospitalId, Status) INCLUDE (CurrentBedId, ConsultantUserId);
CREATE INDEX IX_Admission_Patient ON dbo.Admission(PatientId);

CREATE TABLE dbo.AdmissionWardStay (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdmissionWardStay PRIMARY KEY,
    HospitalId  INT NOT NULL CONSTRAINT FK_AdmissionWardStay_Hospital REFERENCES dbo.Hospital(Id),
    AdmissionId INT NOT NULL CONSTRAINT FK_AdmissionWardStay_Admission REFERENCES dbo.Admission(Id),
    WardId      INT NOT NULL CONSTRAINT FK_AdmissionWardStay_Ward REFERENCES dbo.Ward(Id),
    BedId       INT NOT NULL CONSTRAINT FK_AdmissionWardStay_Bed REFERENCES dbo.Bed(Id),
    StartAt     DATETIME2 NOT NULL,
    EndAt       DATETIME2 NULL,
    Reason      NVARCHAR(10) NOT NULL CONSTRAINT CK_AdmissionWardStay_Reason CHECK (Reason IN (N'Admitted',N'Transfer')),
    CONSTRAINT CK_AdmissionWardStay_Range CHECK (EndAt IS NULL OR EndAt >= StartAt)
);
CREATE INDEX IX_AdmissionWardStay_Admission ON dbo.AdmissionWardStay(AdmissionId, StartAt);
CREATE INDEX IX_AdmissionWardStay_Ward ON dbo.AdmissionWardStay(WardId, StartAt, EndAt);

CREATE TABLE dbo.BedEvent (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BedEvent PRIMARY KEY,
    HospitalId  INT NOT NULL CONSTRAINT FK_BedEvent_Hospital REFERENCES dbo.Hospital(Id),
    BedId       INT NOT NULL CONSTRAINT FK_BedEvent_Bed REFERENCES dbo.Bed(Id),
    AdmissionId INT NULL CONSTRAINT FK_BedEvent_Admission REFERENCES dbo.Admission(Id),
    EventType   NVARCHAR(16) NOT NULL CONSTRAINT CK_BedEvent_Type CHECK (EventType IN (N'Reserved',N'Admitted',N'TransferredIn',N'TransferredOut',N'Discharged',N'Cleaned',N'Blocked',N'Unblocked',N'Released')),
    ByUserId    INT NULL CONSTRAINT FK_BedEvent_User REFERENCES dbo.AppUser(Id),
    At          DATETIME2 NOT NULL,
    Note        NVARCHAR(200) NULL
);
CREATE INDEX IX_BedEvent_Bed_At ON dbo.BedEvent(BedId, At);
CREATE INDEX IX_BedEvent_Hospital_At ON dbo.BedEvent(HospitalId, At);

/* ---------- Files (blob metadata) ---------- */
CREATE TABLE dbo.StoredFile (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StoredFile PRIMARY KEY,
    HospitalId     INT NOT NULL CONSTRAINT FK_StoredFile_Hospital REFERENCES dbo.Hospital(Id),
    AdmissionId    INT NULL CONSTRAINT FK_StoredFile_Admission REFERENCES dbo.Admission(Id),
    PrescriptionId INT NULL,
    Kind           NVARCHAR(16) NOT NULL CONSTRAINT CK_StoredFile_Kind CHECK (Kind IN (N'Audio',N'Prescription',N'TestReport',N'Progress',N'Other')),
    Caption        NVARCHAR(300) NULL,
    BlobPath       NVARCHAR(400) NOT NULL,
    ContentType    NVARCHAR(100) NOT NULL,
    SizeBytes      BIGINT NOT NULL,
    UploadedBy     INT NULL CONSTRAINT FK_StoredFile_User REFERENCES dbo.AppUser(Id),
    UploadedAt     DATETIME2 NOT NULL CONSTRAINT DF_StoredFile_UploadedAt DEFAULT (SYSUTCDATETIME())
);
CREATE INDEX IX_StoredFile_Admission ON dbo.StoredFile(AdmissionId, Kind);

/* ---------- Prescriptions & medication ---------- */
CREATE TABLE dbo.Prescription (
    Id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Prescription PRIMARY KEY,
    HospitalId          INT NOT NULL CONSTRAINT FK_Prescription_Hospital REFERENCES dbo.Hospital(Id),
    AdmissionId         INT NOT NULL CONSTRAINT FK_Prescription_Admission REFERENCES dbo.Admission(Id),
    ConsultantUserId    INT NOT NULL CONSTRAINT FK_Prescription_Consultant REFERENCES dbo.AppUser(Id),
    Source              NVARCHAR(10) NOT NULL CONSTRAINT CK_Prescription_Source CHECK (Source IN (N'Form',N'Voice')),
    Status              NVARCHAR(10) NOT NULL CONSTRAINT DF_Prescription_Status DEFAULT (N'Draft')
                        CONSTRAINT CK_Prescription_Status CHECK (Status IN (N'Draft',N'Signed')),
    SignedAt            DATETIME2 NULL,
    GeneralInstructions NVARCHAR(1000) NULL,
    CreatedAt           DATETIME2 NOT NULL CONSTRAINT DF_Prescription_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_Prescription_Signed CHECK ((Status = N'Signed' AND SignedAt IS NOT NULL) OR (Status = N'Draft' AND SignedAt IS NULL))
);
CREATE INDEX IX_Prescription_Admission ON dbo.Prescription(AdmissionId, Status);

ALTER TABLE dbo.StoredFile ADD CONSTRAINT FK_StoredFile_Prescription FOREIGN KEY (PrescriptionId) REFERENCES dbo.Prescription(Id);

CREATE TABLE dbo.PrescriptionItem (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PrescriptionItem PRIMARY KEY,
    HospitalId     INT NOT NULL CONSTRAINT FK_PrescriptionItem_Hospital REFERENCES dbo.Hospital(Id),
    PrescriptionId INT NOT NULL CONSTRAINT FK_PrescriptionItem_Prescription REFERENCES dbo.Prescription(Id) ON DELETE CASCADE,
    DrugName       NVARCHAR(120) NOT NULL,
    Form           NVARCHAR(30)  NULL,        -- Tab / Cap / Inj / Syrup / Inf / Neb ...
    Strength       NVARCHAR(40)  NULL,
    Dose           NVARCHAR(40)  NOT NULL,
    Route          NVARCHAR(20)  NOT NULL,    -- PO / IV / IM / SC / Neb / PR / Topical
    Frequency      NVARCHAR(6)   NOT NULL CONSTRAINT CK_PrescriptionItem_Frequency CHECK (Frequency IN (N'OD',N'BD',N'TDS',N'QID',N'Q4H',N'Q6H',N'Q8H',N'Q12H',N'STAT',N'PRN')),
    StartAt        DATETIME2 NOT NULL,
    DurationDays   INT NULL CONSTRAINT CK_PrescriptionItem_Duration CHECK (DurationDays IS NULL OR DurationDays BETWEEN 1 AND 90),
    Instructions   NVARCHAR(300) NULL,
    IsActive       BIT NOT NULL CONSTRAINT DF_PrescriptionItem_IsActive DEFAULT (1),
    StoppedAt      DATETIME2 NULL,
    CreatedAt      DATETIME2 NOT NULL CONSTRAINT DF_PrescriptionItem_CreatedAt DEFAULT (SYSUTCDATETIME())
);
CREATE INDEX IX_PrescriptionItem_Prescription ON dbo.PrescriptionItem(PrescriptionId);

CREATE TABLE dbo.DoseTask (
    Id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DoseTask PRIMARY KEY,
    HospitalId         INT NOT NULL CONSTRAINT FK_DoseTask_Hospital REFERENCES dbo.Hospital(Id),
    PrescriptionItemId INT NOT NULL CONSTRAINT FK_DoseTask_Item REFERENCES dbo.PrescriptionItem(Id),
    AdmissionId        INT NOT NULL CONSTRAINT FK_DoseTask_Admission REFERENCES dbo.Admission(Id),
    DueAt              DATETIME2 NOT NULL,
    Status             NVARCHAR(10) NOT NULL CONSTRAINT DF_DoseTask_Status DEFAULT (N'Scheduled')
                       CONSTRAINT CK_DoseTask_Status CHECK (Status IN (N'Scheduled',N'Due',N'Given',N'Delayed',N'Refused',N'Withheld',N'Missed')),
    DoneByUserId       INT NULL CONSTRAINT FK_DoseTask_DoneBy REFERENCES dbo.AppUser(Id),
    DoneAt             DATETIME2 NULL,
    Reason             NVARCHAR(300) NULL,
    EscalatedAt        DATETIME2 NULL,
    CreatedAt          DATETIME2 NOT NULL CONSTRAINT DF_DoseTask_CreatedAt DEFAULT (SYSUTCDATETIME())
);
CREATE INDEX IX_DoseTask_Status_DueAt ON dbo.DoseTask(Status, DueAt) INCLUDE (AdmissionId, HospitalId);
CREATE INDEX IX_DoseTask_Admission_DueAt ON dbo.DoseTask(AdmissionId, DueAt);
CREATE INDEX IX_DoseTask_Item ON dbo.DoseTask(PrescriptionItemId);

/* ---------- Clinical records ---------- */
CREATE TABLE dbo.VitalSign (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_VitalSign PRIMARY KEY,
    HospitalId    INT NOT NULL CONSTRAINT FK_VitalSign_Hospital REFERENCES dbo.Hospital(Id),
    AdmissionId   INT NOT NULL CONSTRAINT FK_VitalSign_Admission REFERENCES dbo.Admission(Id),
    RecordedAt    DATETIME2 NOT NULL,
    RecordedBy    INT NULL CONSTRAINT FK_VitalSign_RecordedBy REFERENCES dbo.AppUser(Id),
    Source        NVARCHAR(10) NOT NULL CONSTRAINT CK_VitalSign_Source CHECK (Source IN (N'Form',N'Voice',N'Device')),
    Systolic      INT NULL CONSTRAINT CK_VitalSign_Sys CHECK (Systolic BETWEEN 40 AND 300),
    Diastolic     INT NULL CONSTRAINT CK_VitalSign_Dia CHECK (Diastolic BETWEEN 20 AND 200),
    Pulse         INT NULL CONSTRAINT CK_VitalSign_Pulse CHECK (Pulse BETWEEN 20 AND 250),
    RespRate      INT NULL CONSTRAINT CK_VitalSign_RR CHECK (RespRate BETWEEN 4 AND 70),
    TemperatureC  DECIMAL(4,1) NULL CONSTRAINT CK_VitalSign_Temp CHECK (TemperatureC BETWEEN 30.0 AND 44.0),
    SpO2          INT NULL CONSTRAINT CK_VitalSign_SpO2 CHECK (SpO2 BETWEEN 50 AND 100),
    OnOxygen      BIT NOT NULL CONSTRAINT DF_VitalSign_OnOxygen DEFAULT (0),
    Consciousness NVARCHAR(12) NOT NULL CONSTRAINT DF_VitalSign_Consciousness DEFAULT (N'Alert')
                  CONSTRAINT CK_VitalSign_Consciousness CHECK (Consciousness IN (N'Alert',N'Confusion',N'Voice',N'Pain',N'Unresponsive')),
    News2Score    INT NULL,
    News2Band     NVARCHAR(10) NULL CONSTRAINT CK_VitalSign_Band CHECK (News2Band IN (N'Low',N'LowMedium',N'Medium',N'High')),
    SignedOffBy   INT NULL CONSTRAINT FK_VitalSign_SignedOffBy REFERENCES dbo.AppUser(Id),
    SignedOffAt   DATETIME2 NULL,
    CreatedAt     DATETIME2 NOT NULL CONSTRAINT DF_VitalSign_CreatedAt DEFAULT (SYSUTCDATETIME())
);
CREATE INDEX IX_VitalSign_Admission_At ON dbo.VitalSign(AdmissionId, RecordedAt DESC);
CREATE INDEX IX_VitalSign_Pending ON dbo.VitalSign(HospitalId, SignedOffAt) INCLUDE (AdmissionId);

CREATE TABLE dbo.LabResult (
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LabResult PRIMARY KEY,
    HospitalId   INT NOT NULL CONSTRAINT FK_LabResult_Hospital REFERENCES dbo.Hospital(Id),
    AdmissionId  INT NOT NULL CONSTRAINT FK_LabResult_Admission REFERENCES dbo.Admission(Id),
    StoredFileId INT NULL CONSTRAINT FK_LabResult_File REFERENCES dbo.StoredFile(Id),
    TestName     NVARCHAR(80) NOT NULL,      -- canonical name (Haemoglobin, Platelets, Creatinine, Potassium, Sodium, Glucose, ...)
    NumericValue DECIMAL(12,3) NULL,
    TextValue    NVARCHAR(200) NULL,
    Unit         NVARCHAR(30) NULL,
    RefLow       DECIMAL(12,3) NULL,
    RefHigh      DECIMAL(12,3) NULL,
    Flag         NVARCHAR(10) NOT NULL CONSTRAINT DF_LabResult_Flag DEFAULT (N'Normal')
                 CONSTRAINT CK_LabResult_Flag CHECK (Flag IN (N'Low',N'Normal',N'High',N'Critical')),
    ResultAt     DATETIME2 NOT NULL,
    Source       NVARCHAR(10) NOT NULL CONSTRAINT CK_LabResult_Source CHECK (Source IN (N'Form',N'Photo',N'Voice')),
    RecordedBy   INT NULL CONSTRAINT FK_LabResult_RecordedBy REFERENCES dbo.AppUser(Id),
    SignedOffBy  INT NULL CONSTRAINT FK_LabResult_SignedOffBy REFERENCES dbo.AppUser(Id),
    SignedOffAt  DATETIME2 NULL,
    CreatedAt    DATETIME2 NOT NULL CONSTRAINT DF_LabResult_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_LabResult_Value CHECK (NumericValue IS NOT NULL OR TextValue IS NOT NULL)
);
CREATE INDEX IX_LabResult_Admission_Test ON dbo.LabResult(AdmissionId, TestName, ResultAt);
CREATE INDEX IX_LabResult_Pending ON dbo.LabResult(HospitalId, SignedOffAt) INCLUDE (AdmissionId);

CREATE TABLE dbo.ClinicalNote (
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ClinicalNote PRIMARY KEY,
    HospitalId   INT NOT NULL CONSTRAINT FK_ClinicalNote_Hospital REFERENCES dbo.Hospital(Id),
    AdmissionId  INT NOT NULL CONSTRAINT FK_ClinicalNote_Admission REFERENCES dbo.Admission(Id),
    AuthorUserId INT NOT NULL CONSTRAINT FK_ClinicalNote_Author REFERENCES dbo.AppUser(Id),
    NoteType     NVARCHAR(16) NOT NULL CONSTRAINT CK_ClinicalNote_Type CHECK (NoteType IN (N'Nursing',N'ConsultantRound',N'Handover')),
    Text         NVARCHAR(MAX) NOT NULL,
    Source       NVARCHAR(10) NOT NULL CONSTRAINT CK_ClinicalNote_Source CHECK (Source IN (N'Form',N'Voice')),
    AudioFileId  INT NULL CONSTRAINT FK_ClinicalNote_Audio REFERENCES dbo.StoredFile(Id),
    SignedOffBy  INT NULL CONSTRAINT FK_ClinicalNote_SignedOffBy REFERENCES dbo.AppUser(Id),
    SignedOffAt  DATETIME2 NULL,
    CreatedAt    DATETIME2 NOT NULL CONSTRAINT DF_ClinicalNote_CreatedAt DEFAULT (SYSUTCDATETIME())
);
CREATE INDEX IX_ClinicalNote_Admission ON dbo.ClinicalNote(AdmissionId, CreatedAt DESC);

/* Nursing tasks other than doses (e.g. "re-check vitals in 1 hour" for critical patients) */
CREATE TABLE dbo.CareTask (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CareTask PRIMARY KEY,
    HospitalId  INT NOT NULL CONSTRAINT FK_CareTask_Hospital REFERENCES dbo.Hospital(Id),
    AdmissionId INT NOT NULL CONSTRAINT FK_CareTask_Admission REFERENCES dbo.Admission(Id),
    TaskType    NVARCHAR(20) NOT NULL CONSTRAINT CK_CareTask_Type CHECK (TaskType IN (N'VitalsRecheck')),
    Detail      NVARCHAR(300) NOT NULL,
    DueAt       DATETIME2 NOT NULL,
    Status      NVARCHAR(10) NOT NULL CONSTRAINT DF_CareTask_Status DEFAULT (N'Open')
                CONSTRAINT CK_CareTask_Status CHECK (Status IN (N'Open',N'Done',N'Cancelled')),
    DoneBy      INT NULL CONSTRAINT FK_CareTask_DoneBy REFERENCES dbo.AppUser(Id),
    DoneAt      DATETIME2 NULL,
    CreatedAt   DATETIME2 NOT NULL CONSTRAINT DF_CareTask_CreatedAt DEFAULT (SYSUTCDATETIME())
);
CREATE INDEX IX_CareTask_Admission_Status ON dbo.CareTask(AdmissionId, Status);

/* ---------- Alerts, voice, AI cache ---------- */
CREATE TABLE dbo.Alert (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Alert PRIMARY KEY,
    HospitalId      INT NOT NULL CONSTRAINT FK_Alert_Hospital REFERENCES dbo.Hospital(Id),
    AdmissionId     INT NOT NULL CONSTRAINT FK_Alert_Admission REFERENCES dbo.Admission(Id),
    Severity        NVARCHAR(10) NOT NULL CONSTRAINT CK_Alert_Severity CHECK (Severity IN (N'Info',N'Warning',N'Critical')),
    AlertType       NVARCHAR(12) NOT NULL CONSTRAINT CK_Alert_Type CHECK (AlertType IN (N'News2',N'CriticalLab',N'Trend',N'MissedDose')),
    RuleCode        NVARCHAR(40) NOT NULL,     -- e.g. NEWS2_HIGH, TREND_HB_DROP, CRIT_K; used for de-duplication
    RuleDetail      NVARCHAR(500) NOT NULL,
    AiExplanation   NVARCHAR(1000) NULL,
    Status          NVARCHAR(14) NOT NULL CONSTRAINT DF_Alert_Status DEFAULT (N'Open')
                    CONSTRAINT CK_Alert_Status CHECK (Status IN (N'Open',N'Acknowledged',N'Resolved')),
    EscalationLevel TINYINT NOT NULL CONSTRAINT DF_Alert_Escalation DEFAULT (0)
                    CONSTRAINT CK_Alert_Escalation CHECK (EscalationLevel BETWEEN 0 AND 2),
    DoseTaskId      INT NULL CONSTRAINT FK_Alert_DoseTask REFERENCES dbo.DoseTask(Id),
    CreatedAt       DATETIME2 NOT NULL CONSTRAINT DF_Alert_CreatedAt DEFAULT (SYSUTCDATETIME()),
    LastEscalatedAt DATETIME2 NULL,
    AckBy           INT NULL CONSTRAINT FK_Alert_AckBy REFERENCES dbo.AppUser(Id),
    AckAt           DATETIME2 NULL,
    ResolvedAt      DATETIME2 NULL
);
CREATE INDEX IX_Alert_Admission_Status ON dbo.Alert(AdmissionId, Status, AlertType);
CREATE INDEX IX_Alert_Hospital_Status ON dbo.Alert(HospitalId, Status, Severity);

CREATE TABLE dbo.VoiceCapture (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_VoiceCapture PRIMARY KEY,
    HospitalId  INT NOT NULL CONSTRAINT FK_VoiceCapture_Hospital REFERENCES dbo.Hospital(Id),
    AdmissionId INT NOT NULL CONSTRAINT FK_VoiceCapture_Admission REFERENCES dbo.Admission(Id),
    UserId      INT NOT NULL CONSTRAINT FK_VoiceCapture_User REFERENCES dbo.AppUser(Id),
    Purpose     NVARCHAR(14) NOT NULL CONSTRAINT CK_VoiceCapture_Purpose CHECK (Purpose IN (N'NurseEntry',N'Prescription',N'RoundNote')),
    AudioFileId INT NULL CONSTRAINT FK_VoiceCapture_Audio REFERENCES dbo.StoredFile(Id),
    Transcript  NVARCHAR(MAX) NULL,
    DraftJson   NVARCHAR(MAX) NULL,
    FinalJson   NVARCHAR(MAX) NULL,            -- what the user actually confirmed (audit of edits)
    Status      NVARCHAR(10) NOT NULL CONSTRAINT DF_VoiceCapture_Status DEFAULT (N'Drafted')
                CONSTRAINT CK_VoiceCapture_Status CHECK (Status IN (N'Drafted',N'Confirmed',N'Discarded')),
    CreatedAt   DATETIME2 NOT NULL CONSTRAINT DF_VoiceCapture_CreatedAt DEFAULT (SYSUTCDATETIME()),
    ConfirmedAt DATETIME2 NULL
);
CREATE INDEX IX_VoiceCapture_Admission ON dbo.VoiceCapture(AdmissionId, CreatedAt DESC);

CREATE TABLE dbo.AiSummary (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AiSummary PRIMARY KEY,
    HospitalId  INT NOT NULL CONSTRAINT FK_AiSummary_Hospital REFERENCES dbo.Hospital(Id),
    AdmissionId INT NOT NULL CONSTRAINT FK_AiSummary_Admission REFERENCES dbo.Admission(Id),
    Kind        NVARCHAR(12) NOT NULL CONSTRAINT CK_AiSummary_Kind CHECK (Kind IN (N'Progress24h',N'Handover',N'Discharge')),
    Content     NVARCHAR(MAX) NOT NULL,
    CreatedAt   DATETIME2 NOT NULL CONSTRAINT DF_AiSummary_CreatedAt DEFAULT (SYSUTCDATETIME())
);
CREATE INDEX IX_AiSummary_Admission_Kind ON dbo.AiSummary(AdmissionId, Kind, CreatedAt DESC);
GO

SET NOEXEC OFF;
GO
