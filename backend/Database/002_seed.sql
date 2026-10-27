/* =====================================================================
   InPatient — 002_seed.sql  (idempotent demo data, fictional people)
   - Reference data (hospitals, wards, beds, users, mappings, shifts) is MERGE / IF NOT EXISTS.
   - Clinical history for a hospital is generated only if that hospital has no admissions yet.
     All clinical times are relative to "now", so reset the DB (run-db -Reset) before a demo.
   - Every user's password is Demo@123 (BCrypt, cost 11).
   - Sample report images / blobs are created by the API's dev seeder (SQL cannot upload blobs).
   - Alerts (except the seeded missed dose) are raised by the API's coded monitoring rules on startup.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;

DECLARE @now DATETIME2 = SYSUTCDATETIME();
DECLARE @todayLocal DATE = CAST(DATEADD(HOUR, 6, @now) AS DATE);
DECLARE @pwd NVARCHAR(100) = N'$2a$11$iCtpEic1hT8O1bMIOGJYyOQQ/DW.NPMkk.zIpLVATl3pNWKVACZUS';

BEGIN TRANSACTION;

/* ---------------- Hospitals ---------------- */
MERGE dbo.Hospital AS t
USING (VALUES
    (N'DCGH', N'Dhaka Care General Hospital', N'House 12, Road 7, Dhanmondi, Dhaka 1205', N'+880 2 5810 0001'),
    (N'CCH',  N'Chattogram City Hospital',    N'45 CDA Avenue, GEC Circle, Chattogram 4000', N'+880 31 265 0002')
) AS s(Code, Name, Address, Phone)
ON t.Code = s.Code
WHEN NOT MATCHED THEN INSERT (Name, Code, Address, Phone) VALUES (s.Name, s.Code, s.Address, s.Phone);

DECLARE @h1 INT = (SELECT Id FROM dbo.Hospital WHERE Code = N'DCGH');
DECLARE @h2 INT = (SELECT Id FROM dbo.Hospital WHERE Code = N'CCH');

/* ---------------- Wards ---------------- */
MERGE dbo.Ward AS t
USING (VALUES
    (@h1, N'Medicine Ward',    N'M',   N'Medicine',    N'3rd floor'),
    (@h1, N'Surgery Ward',     N'S',   N'Surgery',     N'4th floor'),
    (@h1, N'Paediatrics Ward', N'P',   N'Paediatrics', N'2nd floor'),
    (@h1, N'Intensive Care Unit', N'ICU', N'ICU',      N'5th floor'),
    (@h2, N'Medicine Ward',    N'M',   N'Medicine',    N'2nd floor'),
    (@h2, N'Gynae & Obstetrics Ward', N'G', N'Gynae',  N'3rd floor')
) AS s(HospitalId, Name, Code, Type, Floor)
ON t.HospitalId = s.HospitalId AND t.Code = s.Code
WHEN NOT MATCHED THEN INSERT (HospitalId, Name, Code, Type, Floor) VALUES (s.HospitalId, s.Name, s.Code, s.Type, s.Floor);

/* ---------------- Beds ---------------- */
DECLARE @BedPlan TABLE (HospitalId INT, WardCode NVARCHAR(10), BedCount INT);
INSERT @BedPlan VALUES (@h1, N'M', 12), (@h1, N'S', 10), (@h1, N'P', 8), (@h1, N'ICU', 4), (@h2, N'M', 8), (@h2, N'G', 6);

;WITH N AS (SELECT TOP (20) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects)
INSERT dbo.Bed (HospitalId, WardId, BedNumber, QrToken, Status, StatusChangedAt)
SELECT w.HospitalId, w.Id, w.Code + N'-' + RIGHT(N'0' + CAST(N.n AS NVARCHAR(3)), 2),
       LOWER(REPLACE(CONVERT(VARCHAR(36), NEWID()), '-', '')), N'Free', DATEADD(DAY, -10, @now)
FROM @BedPlan bp
JOIN dbo.Ward w ON w.HospitalId = bp.HospitalId AND w.Code = bp.WardCode
JOIN N ON N.n <= bp.BedCount
WHERE NOT EXISTS (SELECT 1 FROM dbo.Bed b WHERE b.WardId = w.Id AND b.BedNumber = w.Code + N'-' + RIGHT(N'0' + CAST(N.n AS NVARCHAR(3)), 2));

/* ---------------- Staff users ---------------- */
-- Ward1/Ward2 = ward codes (consultant mapping or nurse ward). NurseSlot 1 = odd beds, 2 = even beds.
CREATE TABLE #Staff (HospitalId INT NULL, Phone NVARCHAR(20), Name NVARCHAR(150), Role NVARCHAR(20), Designation NVARCHAR(100),
                     Ward1 NVARCHAR(10) NULL, Ward2 NVARCHAR(10) NULL, NurseSlot INT NULL);
INSERT #Staff VALUES
 (NULL, N'01800000000', N'Vendor Super Admin', N'SuperAdmin', N'InPatient platform', NULL, NULL, NULL),
 -- Dhaka Care General Hospital
 (@h1, N'01800000001', N'Farhana Rahman',     N'HospitalAdmin', N'Hospital administrator', NULL, NULL, NULL),
 (@h1, N'01800000002', N'Sohel Rana',         N'Receptionist',  N'Front desk', NULL, NULL, NULL),
 (@h1, N'01800000003', N'Nadia Islam',        N'Receptionist',  N'Front desk', NULL, NULL, NULL),
 (@h1, N'01800000011', N'Shampa Rani Das',    N'Nurse', N'Senior staff nurse', N'M', NULL, 1),
 (@h1, N'01800000012', N'Ayesha Siddika',     N'Nurse', N'Staff nurse',        N'M', NULL, 2),
 (@h1, N'01800000013', N'Kohinoor Akter',     N'Nurse', N'Senior staff nurse', N'S', NULL, 1),
 (@h1, N'01800000014', N'Rupa Das',           N'Nurse', N'Staff nurse',        N'S', NULL, 2),
 (@h1, N'01800000015', N'Morjina Khatun',     N'Nurse', N'Senior staff nurse', N'P', NULL, 1),
 (@h1, N'01800000016', N'Lovely Akter',       N'Nurse', N'Staff nurse',        N'P', NULL, 2),
 (@h1, N'01800000017', N'Sabina Yasmin',      N'Nurse', N'ICU nurse',          N'ICU', NULL, 1),
 (@h1, N'01800000018', N'Mahmuda Haque',      N'Nurse', N'ICU nurse',          N'ICU', NULL, 2),
 (@h1, N'01800000021', N'Dr. Ahsan Habib',    N'Consultant', N'Consultant, Medicine & Critical Care', N'M', N'ICU', NULL),
 (@h1, N'01800000022', N'Dr. Nasreen Akter',  N'Consultant', N'Consultant, General Surgery',          N'S', NULL, NULL),
 (@h1, N'01800000023', N'Dr. Kamrul Islam',   N'Consultant', N'Consultant, Paediatrics',              N'P', NULL, NULL),
 (@h1, N'01800000024', N'Dr. Shirin Sultana', N'Consultant', N'Consultant, Internal Medicine',        N'M', NULL, NULL),
 -- Chattogram City Hospital
 (@h2, N'01800000101', N'Mahbub Alam',        N'HospitalAdmin', N'Hospital administrator', NULL, NULL, NULL),
 (@h2, N'01800000102', N'Rubina Yasmin',      N'Receptionist',  N'Front desk', NULL, NULL, NULL),
 (@h2, N'01800000103', N'Imran Hossain',      N'Receptionist',  N'Front desk', NULL, NULL, NULL),
 (@h2, N'01800000111', N'Popy Rani Dey',      N'Nurse', N'Senior staff nurse', N'M', NULL, 1),
 (@h2, N'01800000112', N'Shirin Akter',       N'Nurse', N'Staff nurse',        N'M', NULL, 2),
 (@h2, N'01800000113', N'Nasrin Sultana',     N'Nurse', N'Senior midwife',     N'G', NULL, 1),
 (@h2, N'01800000114', N'Joba Rani',          N'Nurse', N'Staff nurse',        N'G', NULL, 2),
 (@h2, N'01800000121', N'Dr. Tanvir Hossain', N'Consultant', N'Consultant, Medicine',        N'M', NULL, NULL),
 (@h2, N'01800000122', N'Dr. Rumana Chowdhury', N'Consultant', N'Consultant, Obstetrics & Gynaecology', N'G', NULL, NULL),
 (@h2, N'01800000123', N'Dr. Mizanur Rahman', N'Consultant', N'Consultant, Medicine & Obstetric Medicine', N'M', N'G', NULL);

MERGE dbo.AppUser AS t
USING #Staff AS s
ON ((t.HospitalId = s.HospitalId) OR (t.HospitalId IS NULL AND s.HospitalId IS NULL)) AND t.Phone = s.Phone
WHEN NOT MATCHED THEN INSERT (HospitalId, Name, Phone, PasswordHash, Role, Designation)
                      VALUES (s.HospitalId, s.Name, s.Phone, @pwd, s.Role, s.Designation);

/* Consultant ↔ ward mapping */
INSERT dbo.ConsultantWard (ConsultantUserId, WardId, HospitalId)
SELECT u.Id, w.Id, w.HospitalId
FROM #Staff s
JOIN dbo.AppUser u ON u.HospitalId = s.HospitalId AND u.Phone = s.Phone
CROSS APPLY (VALUES (s.Ward1), (s.Ward2)) AS wc(Code)
JOIN dbo.Ward w ON w.HospitalId = s.HospitalId AND w.Code = wc.Code
WHERE s.Role = N'Consultant'
  AND NOT EXISTS (SELECT 1 FROM dbo.ConsultantWard cw WHERE cw.ConsultantUserId = u.Id AND cw.WardId = w.Id);

/* Nurse shifts for yesterday, today and tomorrow (all three shifts) — re-running the seed tops these up. */
INSERT dbo.NurseShift (HospitalId, NurseUserId, WardId, ShiftDate, ShiftType)
SELECT u.HospitalId, u.Id, w.Id, d.ShiftDate, st.ShiftType
FROM #Staff s
JOIN dbo.AppUser u ON u.HospitalId = s.HospitalId AND u.Phone = s.Phone
JOIN dbo.Ward w ON w.HospitalId = s.HospitalId AND w.Code = s.Ward1
CROSS JOIN (VALUES (DATEADD(DAY, -1, @todayLocal)), (@todayLocal), (DATEADD(DAY, 1, @todayLocal))) AS d(ShiftDate)
CROSS JOIN (VALUES (N'Morning'), (N'Evening'), (N'Night')) AS st(ShiftType)
WHERE s.Role = N'Nurse'
  AND NOT EXISTS (SELECT 1 FROM dbo.NurseShift ns WHERE ns.NurseUserId = u.Id AND ns.ShiftDate = d.ShiftDate AND ns.ShiftType = st.ShiftType);

INSERT dbo.NurseShiftBed (NurseShiftId, BedId, HospitalId)
SELECT ns.Id, b.Id, b.HospitalId
FROM dbo.NurseShift ns
JOIN dbo.AppUser u ON u.Id = ns.NurseUserId
JOIN #Staff s ON s.HospitalId = u.HospitalId AND s.Phone = u.Phone
JOIN dbo.Bed b ON b.WardId = ns.WardId
WHERE ((CAST(RIGHT(b.BedNumber, 2) AS INT) % 2 = 1 AND s.NurseSlot = 1)
    OR (CAST(RIGHT(b.BedNumber, 2) AS INT) % 2 = 0 AND s.NurseSlot = 2))
  AND NOT EXISTS (SELECT 1 FROM dbo.NurseShiftBed x WHERE x.NurseShiftId = ns.Id AND x.BedId = b.Id);

/* =====================================================================
   Clinical history (only for hospitals that have no admissions yet)
   ===================================================================== */
CREATE TABLE #Adm (
    Seq INT IDENTITY(1,1) PRIMARY KEY,
    HospitalId INT, MRN NVARCHAR(20), Name NVARCHAR(150), DOB DATE, Sex NVARCHAR(1), Phone NVARCHAR(20), Guardian NVARCHAR(150), Blood NVARCHAR(5),
    WardCode NVARCHAR(10), BedNo NVARCHAR(10), ConsultantPhone NVARCHAR(20), AdmitHoursAgo INT, Diagnosis NVARCHAR(300), Profile NVARCHAR(12),
    FromWard NVARCHAR(10) NULL, FromBed NVARCHAR(10) NULL, TransferHoursAgo INT NULL, FromConsultantPhone NVARCHAR(20) NULL,
    DischargedHoursAgo INT NULL, LoginPhone NVARCHAR(20) NULL,
    Token VARCHAR(64) NOT NULL DEFAULT (LOWER(REPLACE(CONVERT(VARCHAR(36), NEWID()), '-', ''))),
    PatientId INT NULL, AdmissionId INT NULL, AdmittedAt DATETIME2 NULL, TransferAt DATETIME2 NULL, DischargedAt DATETIME2 NULL, EndAt DATETIME2 NULL
);

INSERT #Adm (HospitalId, MRN, Name, DOB, Sex, Phone, Guardian, Blood, WardCode, BedNo, ConsultantPhone, AdmitHoursAgo, Diagnosis, Profile,
             FromWard, FromBed, TransferHoursAgo, FromConsultantPhone, DischargedHoursAgo, LoginPhone)
VALUES
 -- ===== Dhaka Care General Hospital: active =====
 (@h1, N'DCG-24001', N'Abdul Karim',      '1962-04-12', N'M', N'01711000001', N'Rahima Begum (wife)',     N'B+', N'M', N'M-01', N'01800000024', 72, N'Uncontrolled type 2 diabetes with urinary tract infection', N'stable', NULL, NULL, NULL, NULL, NULL, N'01800000031'),
 (@h1, N'DCG-24002', N'Rafiqul Islam',    '1958-09-03', N'M', N'01711000002', N'Shafiq Islam (son)',      N'O+', N'M', N'M-02', N'01800000021', 60, N'Melaena - suspected bleeding peptic ulcer', N'hb', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24003', N'Nusrat Jahan',     '1997-01-21', N'F', N'01711000003', N'Jahangir Alam (father)',  N'A+', N'M', N'M-03', N'01800000024', 70, N'Dengue fever (NS1 positive)', N'dengue', NULL, NULL, NULL, NULL, NULL, N'01800000032'),
 (@h1, N'DCG-24004', N'Jahanara Begum',   '1950-11-30', N'F', N'01711000004', N'Kamal Hossain (son)',     N'A-', N'M', N'M-04', N'01800000021', 96, N'Acute exacerbation of COPD', N'copd', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24005', N'Mohammad Selim',   '1971-06-18', N'M', N'01711000005', N'Shirina Akter (wife)',    N'B+', N'M', N'M-07', N'01800000021', 84, N'Community-acquired pneumonia', N'news', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24006', N'Shahana Parvin',   '1985-02-07', N'F', N'01711000006', N'Masud Rana (husband)',    N'O-', N'M', N'M-08', N'01800000024', 100, N'Enteric fever', N'stable', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24007', N'Aminul Haque',     '1966-08-25', N'M', N'01711000007', N'Sharmin Haque (daughter)', N'AB+', N'M', N'M-09', N'01800000021', 50, N'Hypertensive urgency', N'stable', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24008', N'Rokeya Khatun',    '1979-12-14', N'F', N'01711000008', N'Abul Hashem (husband)',   N'B-', N'M', N'M-10', N'01800000024', 40, N'Acute gastroenteritis with dehydration', N'recovering', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24009', N'Delwar Hossain',   '1955-03-09', N'M', N'01711000009', N'Nasir Hossain (son)',     N'O+', N'M', N'M-12', N'01800000021', 50, N'Acute ischaemic stroke with left hemiparesis', N'stable', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24010', N'Habibur Rahman',   '1980-07-02', N'M', N'01711000010', N'Salma Rahman (wife)',     N'A+', N'S', N'S-01', N'01800000022', 56, N'Post-op: laparoscopic cholecystectomy', N'postop', NULL, NULL, NULL, NULL, NULL, N'01800000033'),
 (@h1, N'DCG-24011', N'Sumaiya Akter',    '1992-10-19', N'F', N'01711000011', N'Rashed Khan (husband)',   N'B+', N'S', N'S-02', N'01800000022', 44, N'Post-op: open appendicectomy', N'postop', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24012', N'Mostafa Kamal',    '1960-05-27', N'M', N'01711000012', N'Lutfa Kamal (wife)',      N'O+', N'S', N'S-03', N'01800000022', 80, N'Post-op: exploratory laparotomy for perforated duodenal ulcer', N'creat', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24013', N'Fatema Begum',     '1968-01-15', N'F', N'01711000013', N'Rafiq Uddin (husband)',   N'A+', N'S', N'S-04', N'01800000022', 110, N'Diabetic foot ulcer - post debridement', N'postop', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24014', N'Anwar Hossain',    '1975-09-09', N'M', N'01711000014', N'Monira Hossain (wife)',   N'B+', N'S', N'S-05', N'01800000022', 90, N'Fracture shaft of femur - post ORIF', N'postop', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24015', N'Tania Sultana',    '2001-04-04', N'F', N'01711000015', N'Abdus Sobhan (father)',   N'O+', N'S', N'S-06', N'01800000022', 66, N'Acute appendicitis - post appendicectomy', N'postop', N'M', N'M-12', 60, N'01800000024', NULL, NULL),
 (@h1, N'DCG-24016', N'Kabir Ahmed',      '1988-12-01', N'M', N'01711000016', N'Rina Ahmed (wife)',       N'A-', N'S', N'S-07', N'01800000022', 30, N'Post-op: inguinal hernia repair (mesh)', N'postop', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24017', N'Moriom Nesa',      '1956-06-22', N'F', N'01711000017', N'Jalal Uddin (son)',       N'B+', N'S', N'S-08', N'01800000022', 26, N'Post-op: incisional hernia repair', N'postop', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24018', N'Ayaan Rahman',     '2019-03-14', N'M', N'01711000018', N'Sadia Rahman (mother)',   N'O+', N'P', N'P-01', N'01800000023', 50, N'Pneumonia', N'paed', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24019', N'Tasnim Hossain',   '2021-06-02', N'F', N'01711000019', N'Faruk Hossain (father)',  N'A+', N'P', N'P-02', N'01800000023', 40, N'Acute watery diarrhoea with some dehydration', N'paed', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24020', N'Arif Mia',         '2016-01-20', N'M', N'01711000020', N'Hasina Begum (mother)',   N'B+', N'P', N'P-03', N'01800000023', 60, N'Dengue fever', N'paed', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24021', N'Raisa Islam',      '2023-08-11', N'F', N'01711000021', N'Mitu Islam (mother)',     N'O+', N'P', N'P-04', N'01800000023', 30, N'Simple febrile convulsion', N'paed', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24022', N'Samiul Haque',     '2014-05-05', N'M', N'01711000022', N'Enamul Haque (father)',   N'AB+', N'P', N'P-05', N'01800000023', 70, N'Acute severe asthma', N'paed', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24023', N'Golam Mostafa',    '1957-10-10', N'M', N'01711000023', N'Rubel Mostafa (son)',     N'A+', N'ICU', N'ICU-01', N'01800000021', 80, N'Severe pneumonia with type 1 respiratory failure', N'icu_o2', N'M', N'M-05', 30, N'01800000021', NULL, NULL),
 (@h1, N'DCG-24024', N'Nasima Akter',     '1964-02-28', N'F', N'01711000024', N'Ruhul Amin (husband)',    N'B+', N'ICU', N'ICU-02', N'01800000021', 20, N'CKD stage 5 with hyperkalaemia', N'k_high', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h1, N'DCG-24025', N'Shafiqul Alam',    '1952-07-07', N'M', N'01711000025', N'Parveen Alam (wife)',     N'O+', N'ICU', N'ICU-03', N'01800000021', 90, N'Post-laparotomy sepsis (suspected anastomotic leak)', N'icu_postop', N'S', N'S-10', 40, N'01800000022', NULL, NULL),
 -- ===== Dhaka Care General Hospital: discharged =====
 (@h1, N'DCG-23991', N'Moinul Islam',     '1970-03-03', N'M', N'01711000091', N'Rokshana Islam (wife)',   N'A+', N'M', N'M-06', N'01800000024', 96,  N'Acute febrile illness', N'recovering', NULL, NULL, NULL, NULL, 1, NULL),
 (@h1, N'DCG-23992', N'Rehana Parvin',    '1983-08-08', N'F', N'01711000092', N'Selim Reza (husband)',    N'B+', N'S', N'S-09', N'01800000022', 80,  N'Post-op: haemorrhoidectomy', N'postop', NULL, NULL, NULL, NULL, 2, NULL),
 (@h1, N'DCG-23993', N'Kulsum Bibi',      '1949-12-12', N'F', N'01711000093', N'Abdul Malek (son)',       N'O+', N'M', N'M-05', N'01800000021', 170, N'Acute decompensated heart failure', N'recovering', NULL, NULL, NULL, NULL, 110, NULL),
 (@h1, N'DCG-23994', N'Nabil Hasan',      '2018-09-09', N'M', N'01711000094', N'Rumana Hasan (mother)',   N'A+', N'P', N'P-06', N'01800000023', 120, N'Pneumonia', N'paed', NULL, NULL, NULL, NULL, 50, NULL),
 (@h1, N'DCG-23995', N'Jamal Uddin',      '1965-05-15', N'M', N'01711000095', N'Sufia Khatun (wife)',     N'B-', N'S', N'S-10', N'01800000022', 170, N'Post-op: open cholecystectomy', N'postop', NULL, NULL, NULL, NULL, 100, NULL),
 (@h1, N'DCG-23996', N'Bilkis Ara',       '1960-10-01', N'F', N'01711000096', N'Taher Uddin (husband)',   N'O+', N'M', N'M-01', N'01800000024', 150, N'Pre-renal acute kidney injury - resolved', N'recovering', NULL, NULL, NULL, NULL, 85, NULL),
 -- ===== Chattogram City Hospital: active =====
 (@h2, N'CCH-24001', N'Abul Kashem',      '1959-02-02', N'M', N'01811000001', N'Sajib Kashem (son)',      N'A+', N'M', N'M-01', N'01800000121', 70, N'Acute coronary syndrome (NSTEMI)', N'stable', NULL, NULL, NULL, NULL, NULL, N'01800000131'),
 (@h2, N'CCH-24002', N'Parvin Akter',     '1975-07-17', N'F', N'01811000002', N'Nurul Islam (husband)',   N'B+', N'M', N'M-02', N'01800000123', 50, N'Acute pyelonephritis', N'stable', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h2, N'CCH-24003', N'Liton Das',        '1982-11-11', N'M', N'01811000003', N'Mithu Das (wife)',        N'O+', N'M', N'M-03', N'01800000121', 60, N'Dengue fever - recovery phase', N'recovering', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h2, N'CCH-24004', N'Selina Rahman',    '1968-04-24', N'F', N'01811000004', N'Ashraf Rahman (husband)', N'AB-', N'M', N'M-04', N'01800000123', 80, N'Type 2 diabetes with hyperglycaemia', N'stable', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h2, N'CCH-24005', N'Shahidul Islam',   '1950-09-30', N'M', N'01811000005', N'Faisal Islam (son)',      N'B+', N'M', N'M-06', N'01800000121', 40, N'Acute exacerbation of COPD', N'copd', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h2, N'CCH-24006', N'Sharmin Sultana',  '1994-01-08', N'F', N'01811000006', N'Tanvir Ahmed (husband)',  N'O+', N'G', N'G-01', N'01800000122', 50, N'Post caesarean section day 2', N'postop', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h2, N'CCH-24007', N'Mitu Akter',       '1999-05-19', N'F', N'01811000007', N'Sumon Mia (husband)',     N'A+', N'G', N'G-02', N'01800000122', 36, N'Hyperemesis gravidarum', N'recovering', NULL, NULL, NULL, NULL, NULL, NULL),
 (@h2, N'CCH-24008', N'Rina Das',         '1990-12-25', N'F', N'01811000008', N'Pradip Das (husband)',    N'B+', N'G', N'G-03', N'01800000123', 60, N'Pre-eclampsia at 34 weeks', N'stable', N'M', N'M-05', 40, N'01800000121', NULL, NULL),
 -- ===== Chattogram City Hospital: discharged =====
 (@h2, N'CCH-23991', N'Hafiz Ullah',      '1963-06-06', N'M', N'01811000091', N'Jahanara Ullah (wife)',   N'O+', N'M', N'M-07', N'01800000121', 120, N'Acute gastroenteritis', N'recovering', NULL, NULL, NULL, NULL, 30, NULL),
 (@h2, N'CCH-23992', N'Lipi Begum',       '1987-03-13', N'F', N'01811000092', N'Monir Hossain (husband)', N'A+', N'G', N'G-04', N'01800000122', 130, N'Post-op: total abdominal hysterectomy', N'postop', NULL, NULL, NULL, NULL, 2, NULL);

-- Skip hospitals that already have clinical data (idempotency).
DELETE a FROM #Adm a WHERE EXISTS (SELECT 1 FROM dbo.Admission x WHERE x.HospitalId = a.HospitalId);

IF EXISTS (SELECT 1 FROM #Adm)
BEGIN
    UPDATE #Adm SET AdmittedAt   = DATEADD(HOUR, -AdmitHoursAgo, @now),
                    TransferAt   = CASE WHEN TransferHoursAgo IS NULL THEN NULL ELSE DATEADD(HOUR, -TransferHoursAgo, @now) END,
                    DischargedAt = CASE WHEN DischargedHoursAgo IS NULL THEN NULL ELSE DATEADD(HOUR, -DischargedHoursAgo, @now) END;
    UPDATE #Adm SET EndAt = COALESCE(DischargedAt, @now);

    DECLARE @n TABLE (n INT PRIMARY KEY);
    INSERT @n SELECT TOP (400) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 FROM sys.all_objects a CROSS JOIN sys.all_objects b;

    /* ---- Patients ---- */
    INSERT dbo.Patient (HospitalId, MRN, Name, DateOfBirth, Sex, Phone, Guardian, BloodGroup, CreatedAt, CreatedBy)
    SELECT a.HospitalId, a.MRN, a.Name, a.DOB, a.Sex, a.Phone, a.Guardian, a.Blood, a.AdmittedAt,
           (SELECT TOP 1 Id FROM dbo.AppUser u WHERE u.HospitalId = a.HospitalId AND u.Role = N'Receptionist' ORDER BY u.Id)
    FROM #Adm a
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Patient p WHERE p.HospitalId = a.HospitalId AND p.MRN = a.MRN);
    UPDATE a SET PatientId = p.Id FROM #Adm a JOIN dbo.Patient p ON p.HospitalId = a.HospitalId AND p.MRN = a.MRN;

    /* ---- Admissions ---- */
    INSERT dbo.Admission (HospitalId, PatientId, CurrentBedId, ConsultantUserId, WristbandToken, AdmittedAt, BedConfirmedAt,
                          ProvisionalDiagnosis, Status, DischargedAt, DischargeSummary, CreatedAt, CreatedBy)
    SELECT a.HospitalId, a.PatientId,
           CASE WHEN a.DischargedAt IS NULL THEN b.Id END,
           cu.Id, a.Token, a.AdmittedAt, DATEADD(MINUTE, 20, a.AdmittedAt), a.Diagnosis,
           CASE WHEN a.DischargedAt IS NULL THEN N'Active' ELSE N'Discharged' END,
           a.DischargedAt,
           CASE WHEN a.DischargedAt IS NULL THEN NULL ELSE
                N'Admitted with ' + LOWER(LEFT(a.Diagnosis, 1)) + SUBSTRING(a.Diagnosis, 2, 300)
                + N'. Managed with intravenous fluids, antibiotics and supportive care as per ward protocol. Vital signs settled and the patient remained clinically stable for 24 hours before discharge.'
                + N' Discharge medicines: as per prescription. Advice: adequate oral fluids, complete the course of medicines, return immediately if fever, breathlessness, bleeding or reduced urine output. Follow-up in the outpatient clinic after 7 days.' END,
           a.AdmittedAt,
           (SELECT TOP 1 Id FROM dbo.AppUser u WHERE u.HospitalId = a.HospitalId AND u.Role = N'Receptionist' ORDER BY u.Id)
    FROM #Adm a
    JOIN dbo.Ward w ON w.HospitalId = a.HospitalId AND w.Code = a.WardCode
    JOIN dbo.Bed b ON b.WardId = w.Id AND b.BedNumber = a.BedNo
    JOIN dbo.AppUser cu ON cu.HospitalId = a.HospitalId AND cu.Phone = a.ConsultantPhone;
    UPDATE a SET AdmissionId = x.Id FROM #Adm a JOIN dbo.Admission x ON x.WristbandToken = a.Token;

    /* ---- Patient-role logins ---- */
    INSERT dbo.AppUser (HospitalId, Name, Phone, PasswordHash, Role, Designation, PatientId)
    SELECT a.HospitalId, a.Name, a.LoginPhone, @pwd, N'Patient', N'Patient', a.PatientId
    FROM #Adm a
    WHERE a.LoginPhone IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM dbo.AppUser u WHERE u.HospitalId = a.HospitalId AND u.Phone = a.LoginPhone);

    /* ---- Ward stays (first stay; transfer creates a second) ---- */
    INSERT dbo.AdmissionWardStay (HospitalId, AdmissionId, WardId, BedId, StartAt, EndAt, Reason)
    SELECT a.HospitalId, a.AdmissionId, w.Id, b.Id, a.AdmittedAt, COALESCE(a.TransferAt, a.DischargedAt), N'Admitted'
    FROM #Adm a
    JOIN dbo.Ward w ON w.HospitalId = a.HospitalId AND w.Code = COALESCE(a.FromWard, a.WardCode)
    JOIN dbo.Bed b ON b.WardId = w.Id AND b.BedNumber = COALESCE(a.FromBed, a.BedNo);

    INSERT dbo.AdmissionWardStay (HospitalId, AdmissionId, WardId, BedId, StartAt, EndAt, Reason)
    SELECT a.HospitalId, a.AdmissionId, w.Id, b.Id, a.TransferAt, a.DischargedAt, N'Transfer'
    FROM #Adm a
    JOIN dbo.Ward w ON w.HospitalId = a.HospitalId AND w.Code = a.WardCode
    JOIN dbo.Bed b ON b.WardId = w.Id AND b.BedNumber = a.BedNo
    WHERE a.TransferAt IS NOT NULL;

    /* ---- Bed events ---- */
    DECLARE @rec TABLE (HospitalId INT PRIMARY KEY, UserId INT);
    INSERT @rec SELECT HospitalId, MIN(Id) FROM dbo.AppUser WHERE Role = N'Receptionist' GROUP BY HospitalId;

    -- Ward nurse (slot 1) per ward, used as the actor for nursing records.
    CREATE TABLE #WardNurse (WardId INT PRIMARY KEY, NurseId INT, Nurse2Id INT);
    INSERT #WardNurse
    SELECT w.Id,
           (SELECT u.Id FROM #Staff s JOIN dbo.AppUser u ON u.HospitalId = s.HospitalId AND u.Phone = s.Phone WHERE s.Role = N'Nurse' AND s.HospitalId = w.HospitalId AND s.Ward1 = w.Code AND s.NurseSlot = 1),
           (SELECT u.Id FROM #Staff s JOIN dbo.AppUser u ON u.HospitalId = s.HospitalId AND u.Phone = s.Phone WHERE s.Role = N'Nurse' AND s.HospitalId = w.HospitalId AND s.Ward1 = w.Code AND s.NurseSlot = 2)
    FROM dbo.Ward w;

    INSERT dbo.BedEvent (HospitalId, BedId, AdmissionId, EventType, ByUserId, At, Note)
    SELECT s.HospitalId, s.BedId, s.AdmissionId, N'Reserved', r.UserId, s.StartAt, N'Reserved at admission'
    FROM dbo.AdmissionWardStay s JOIN #Adm a ON a.AdmissionId = s.AdmissionId JOIN @rec r ON r.HospitalId = s.HospitalId
    WHERE s.Reason = N'Admitted'
    UNION ALL
    SELECT s.HospitalId, s.BedId, s.AdmissionId, N'Admitted', wn.NurseId, DATEADD(MINUTE, 20, s.StartAt), N'Bed and wristband scanned'
    FROM dbo.AdmissionWardStay s JOIN #Adm a ON a.AdmissionId = s.AdmissionId JOIN #WardNurse wn ON wn.WardId = s.WardId
    WHERE s.Reason = N'Admitted'
    UNION ALL
    SELECT s.HospitalId, s.BedId, s.AdmissionId, N'TransferredIn', wn.NurseId, s.StartAt, N'Transfer'
    FROM dbo.AdmissionWardStay s JOIN #Adm a ON a.AdmissionId = s.AdmissionId JOIN #WardNurse wn ON wn.WardId = s.WardId
    WHERE s.Reason = N'Transfer'
    UNION ALL
    SELECT s.HospitalId, s.BedId, s.AdmissionId, N'TransferredOut', wn.NurseId, s.EndAt, N'Transfer'
    FROM dbo.AdmissionWardStay s JOIN #Adm a ON a.AdmissionId = s.AdmissionId JOIN #WardNurse wn ON wn.WardId = s.WardId
    WHERE s.EndAt IS NOT NULL AND a.TransferAt IS NOT NULL AND s.Reason = N'Admitted'
    UNION ALL
    SELECT s.HospitalId, s.BedId, s.AdmissionId, N'Cleaned', wn.NurseId, DATEADD(MINUTE, 45, s.EndAt), NULL
    FROM dbo.AdmissionWardStay s JOIN #Adm a ON a.AdmissionId = s.AdmissionId JOIN #WardNurse wn ON wn.WardId = s.WardId
    WHERE s.EndAt IS NOT NULL AND a.TransferAt IS NOT NULL AND s.Reason = N'Admitted'
    UNION ALL
    SELECT s.HospitalId, s.BedId, s.AdmissionId, N'Discharged', cu.Id, s.EndAt, N'Discharged home'
    FROM dbo.AdmissionWardStay s JOIN #Adm a ON a.AdmissionId = s.AdmissionId
    JOIN dbo.AppUser cu ON cu.HospitalId = a.HospitalId AND cu.Phone = a.ConsultantPhone
    WHERE a.DischargedAt IS NOT NULL AND s.EndAt = a.DischargedAt
    UNION ALL
    SELECT s.HospitalId, s.BedId, s.AdmissionId, N'Cleaned', wn.NurseId, DATEADD(MINUTE, 60, s.EndAt), NULL
    FROM dbo.AdmissionWardStay s JOIN #Adm a ON a.AdmissionId = s.AdmissionId JOIN #WardNurse wn ON wn.WardId = s.WardId
    WHERE a.DischargedAt IS NOT NULL AND s.EndAt = a.DischargedAt AND a.DischargedHoursAgo >= 3;

    /* ---- Clinical profiles: generic vitals baseline ---- */
    CREATE TABLE #Prof (Profile NVARCHAR(12) PRIMARY KEY, Sys INT, Dia INT, Pulse INT, RR INT, Temp DECIMAL(4,1), SpO2 INT, OnO2 BIT,
                        GenericUntilHoursAgo INT, RoundPlan NVARCHAR(400), NursePlan NVARCHAR(300));
    INSERT #Prof VALUES
     (N'stable',     124, 80, 80, 16, 36.9, 97, 0, 0,  N'Clinically stable. Continue current antibiotics and medicines. Monitor blood sugar and intake-output.', N'Patient comfortable, taking oral diet. IV cannula patent, no complaints overnight.'),
     (N'copd',       128, 78, 86, 18, 36.8, 96, 0, 0,  N'Wheeze improving with nebulisation. Continue nebulised bronchodilators and antibiotics; target SpO2 88-92 percent if retaining CO2.', N'Nebulisation given as charted. Mild cough, sleeping propped up.'),
     (N'recovering', 118, 76, 78, 16, 36.8, 98, 0, 0,  N'Improving. Encourage oral fluids, step down IV medicines, plan discharge if remains stable.', N'Tolerating oral fluids well, ambulating in the ward.'),
     (N'postop',     122, 78, 84, 16, 37.1, 97, 0, 0,  N'Wound healthy, no collection. Continue analgesia and antibiotics, early mobilisation, diet as tolerated.', N'Dressing dry and intact. Pain score 3/10 after analgesia. Passed urine.'),
     (N'hb',         116, 72, 88, 17, 36.9, 97, 0, 24, N'Melaena with falling haemoglobin. Keep NPO for endoscopy, IV PPI, crossmatch 2 units, monitor pulse and BP 2-hourly.', N'Black stool passed twice this shift. Patient feels dizzy on standing.'),
     (N'dengue',     112, 74, 96, 18, 38.1, 98, 0, 24, N'Dengue in critical phase with falling platelets. Strict intake-output, watch for warning signs and bleeding, repeat CBC 12-hourly.', N'Mild gum bleeding while brushing. Encouraging oral fluids, urine output adequate.'),
     (N'news',       122, 78, 84, 17, 37.4, 97, 0, 24, N'Pneumonia on IV antibiotics. Review chest X-ray, consider escalation if respiratory rate keeps rising.', N'Productive cough, mild breathlessness on exertion.'),
     (N'creat',      124, 78, 90, 17, 37.3, 97, 0, 0,  N'Post-laparotomy day 3, urine output borderline. Review creatinine trend, avoid nephrotoxic drugs, maintain fluid balance.', N'Urine output 25-30 mL/hour, drain minimal serous.'),
     (N'icu_o2',     118, 70, 96, 19, 37.4, 95, 1, 0, N'Type 1 respiratory failure on 4 L/min oxygen. Continue meropenem, chest physiotherapy, ABG 6-hourly, prone positioning when awake.', N'On oxygen via face mask, SpO2 93-95 percent. Repositioned 2-hourly.'),
     (N'k_high',     148, 88, 76, 18, 36.8, 97, 0, 0,  N'Hyperkalaemia in CKD 5. Cardiac monitoring, repeat potassium after insulin-dextrose, nephrology for urgent haemodialysis.', N'On cardiac monitor. Urine output poor.'),
     (N'icu_postop', 118, 70, 92, 19, 37.6, 96, 0, 0,  N'Post-laparotomy sepsis improving on meropenem and metronidazole. Continue drain monitoring, early enteral feeding.', N'Drain output reducing, abdomen soft. Afebrile since morning.'),
     (N'paed',       100, 64, 112, 26, 37.4, 97, 0, 0, N'Child improving. Continue current treatment, maintain hydration, mother counselled about danger signs.', N'Child playful, taking breast milk / oral fluids. Mother at bedside.');

    -- Generic 6-hourly vitals throughout each stay (skipping the last 24 h for profiles with scripted deterioration).
    INSERT dbo.VitalSign (HospitalId, AdmissionId, RecordedAt, RecordedBy, Source, Systolic, Diastolic, Pulse, RespRate, TemperatureC, SpO2, OnOxygen, Consciousness, CreatedAt)
    SELECT a.HospitalId, a.AdmissionId, x.At,
           CASE WHEN x.At >= COALESCE(a.TransferAt, '9999-12-31') THEN wnNew.NurseId ELSE wnOld.NurseId END,
           CASE WHEN n.n % 5 = 0 THEN N'Voice' ELSE N'Form' END,
           pr.Sys + ((n.n * 7) % 9) - 4, pr.Dia + ((n.n * 5) % 7) - 3, pr.Pulse + ((n.n * 3) % 7) - 3, pr.RR + ((n.n % 3) - 1),
           CAST(pr.Temp + (((n.n * 3) % 5) - 2) * 0.1 AS DECIMAL(4,1)),
           CASE WHEN pr.SpO2 + (n.n % 3) - 1 > 100 THEN 100 ELSE pr.SpO2 + (n.n % 3) - 1 END,
           pr.OnO2, N'Alert', x.At
    FROM #Adm a
    JOIN #Prof pr ON pr.Profile = a.Profile
    JOIN dbo.Ward wOld ON wOld.HospitalId = a.HospitalId AND wOld.Code = COALESCE(a.FromWard, a.WardCode)
    JOIN #WardNurse wnOld ON wnOld.WardId = wOld.Id
    JOIN dbo.Ward wNew ON wNew.HospitalId = a.HospitalId AND wNew.Code = a.WardCode
    JOIN #WardNurse wnNew ON wnNew.WardId = wNew.Id
    JOIN @n n ON n.n < 60
    CROSS APPLY (SELECT DATEADD(MINUTE, 60 + n.n * 360 + (a.Seq % 4) * 10, a.AdmittedAt) AS At) x
    WHERE x.At < DATEADD(MINUTE, -45, a.EndAt)
      AND x.At < DATEADD(HOUR, -pr.GenericUntilHoursAgo, @now);

    -- Scripted deterioration vitals
    INSERT dbo.VitalSign (HospitalId, AdmissionId, RecordedAt, RecordedBy, Source, Systolic, Diastolic, Pulse, RespRate, TemperatureC, SpO2, OnOxygen, Consciousness, CreatedAt)
    SELECT a.HospitalId, a.AdmissionId, DATEADD(MINUTE, -v.MinAgo, @now), wn.NurseId, v.Src, v.Sys, v.Dia, v.Pulse, v.RR, v.Temp, v.SpO2, 0, N'Alert', DATEADD(MINUTE, -v.MinAgo, @now)
    FROM #Adm a
    JOIN dbo.Ward w ON w.HospitalId = a.HospitalId AND w.Code = a.WardCode
    JOIN #WardNurse wn ON wn.WardId = w.Id
    JOIN (VALUES
        -- Hb drop: rising pulse, softening BP
        (N'hb',     1200, N'Form',  114, 70,  96, 18, 36.9, 97),
        (N'hb',      600, N'Voice', 106, 66, 108, 19, 37.0, 96),
        (N'hb',       90, N'Form',  102, 62, 118, 20, 37.1, 96),
        -- Dengue
        (N'dengue', 1260, N'Form',  110, 72,  98, 18, 38.4, 98),
        (N'dengue',  660, N'Form',  106, 70, 100, 18, 37.9, 98),
        (N'dengue',  150, N'Voice', 104, 70, 104, 19, 37.6, 97),
        -- NEWS2 2 -> 4 -> 6
        (N'news',   1200, N'Form',  118, 76,  95, 18, 37.8, 95),
        (N'news',    600, N'Form',  115, 74,  98, 22, 38.0, 95),
        (N'news',     60, N'Voice', 108, 70, 105, 23, 38.0, 93)
    ) AS v(Profile, MinAgo, Src, Sys, Dia, Pulse, RR, Temp, SpO2) ON v.Profile = a.Profile
    WHERE a.DischargedAt IS NULL;

    -- NEWS2 (Royal College of Physicians, Scale 1) — same table as Services/News2Calculator.cs; not scored under 16 years.
    UPDATE v SET
        News2Score = sc.Total,
        News2Band  = CASE WHEN sc.Total >= 7 THEN N'High' WHEN sc.Total >= 5 THEN N'Medium' WHEN sc.MaxPart = 3 THEN N'LowMedium' ELSE N'Low' END
    FROM dbo.VitalSign v
    JOIN dbo.Admission ad ON ad.Id = v.AdmissionId
    JOIN dbo.Patient p ON p.Id = ad.PatientId
    CROSS APPLY (SELECT
        CASE WHEN v.RespRate <= 8 THEN 3 WHEN v.RespRate <= 11 THEN 1 WHEN v.RespRate <= 20 THEN 0 WHEN v.RespRate <= 24 THEN 2 ELSE 3 END AS RR,
        CASE WHEN v.SpO2 <= 91 THEN 3 WHEN v.SpO2 <= 93 THEN 2 WHEN v.SpO2 <= 95 THEN 1 ELSE 0 END AS Sp,
        CASE WHEN v.OnOxygen = 1 THEN 2 ELSE 0 END AS O2,
        CASE WHEN v.Systolic <= 90 THEN 3 WHEN v.Systolic <= 100 THEN 2 WHEN v.Systolic <= 110 THEN 1 WHEN v.Systolic <= 219 THEN 0 ELSE 3 END AS SBP,
        CASE WHEN v.Pulse <= 40 THEN 3 WHEN v.Pulse <= 50 THEN 1 WHEN v.Pulse <= 90 THEN 0 WHEN v.Pulse <= 110 THEN 1 WHEN v.Pulse <= 130 THEN 2 ELSE 3 END AS HR,
        CASE WHEN v.Consciousness = N'Alert' THEN 0 ELSE 3 END AS CNS,
        CASE WHEN v.TemperatureC <= 35.0 THEN 3 WHEN v.TemperatureC <= 36.0 THEN 1 WHEN v.TemperatureC <= 38.0 THEN 0 WHEN v.TemperatureC <= 39.0 THEN 1 ELSE 2 END AS T
    ) parts
    CROSS APPLY (SELECT parts.RR + parts.Sp + parts.O2 + parts.SBP + parts.HR + parts.CNS + parts.T AS Total,
                        (SELECT MAX(x) FROM (VALUES (parts.RR), (parts.Sp), (parts.O2), (parts.SBP), (parts.HR), (parts.CNS), (parts.T)) m(x)) AS MaxPart) sc
    WHERE ad.HospitalId IN (SELECT DISTINCT HospitalId FROM #Adm)
      AND v.News2Score IS NULL
      AND v.RespRate IS NOT NULL AND v.SpO2 IS NOT NULL AND v.Systolic IS NOT NULL AND v.Pulse IS NOT NULL AND v.TemperatureC IS NOT NULL
      AND DATEDIFF(DAY, p.DateOfBirth, CAST(v.RecordedAt AS DATE)) / 365.25 >= 16;

    /* ---- Lab results ---- */
    CREATE TABLE #Lab (Profile NVARCHAR(12), TestName NVARCHAR(80), Value DECIMAL(12,3), Unit NVARCHAR(30), RefLow DECIMAL(12,3), RefHigh DECIMAL(12,3),
                       AfterAdmitHours INT NULL, HoursAgo INT NULL);
    -- Generic panel (Profile '*'): on admission and again ~48 h later for longer stays.
    INSERT #Lab VALUES
     (N'*', N'Haemoglobin', 12.8, N'g/dL', 12.0, 16.0, 2, NULL),
     (N'*', N'WBC',         8.4,  N'x10^9/L', 4.0, 11.0, 2, NULL),
     (N'*', N'Platelets',   245000, N'/cumm', 150000, 450000, 2, NULL),
     (N'*', N'Sodium',      138,  N'mmol/L', 135, 145, 2, NULL),
     (N'*', N'Potassium',   4.1,  N'mmol/L', 3.5, 5.1, 2, NULL),
     (N'*', N'Creatinine',  0.9,  N'mg/dL', 0.6, 1.2, 2, NULL),
     (N'*', N'Glucose',     6.4,  N'mmol/L', 3.9, 7.8, 2, NULL),
     (N'*', N'Haemoglobin', 12.6, N'g/dL', 12.0, 16.0, 50, NULL),
     (N'*', N'Creatinine',  0.9,  N'mg/dL', 0.6, 1.2, 50, NULL),
     -- Scripted trends
     (N'hb',     N'Haemoglobin', 11.2, N'g/dL', 12.0, 16.0, NULL, 40),
     (N'hb',     N'Haemoglobin', 9.8,  N'g/dL', 12.0, 16.0, NULL, 20),
     (N'hb',     N'Haemoglobin', 8.1,  N'g/dL', 12.0, 16.0, NULL, 2),
     (N'dengue', N'Platelets', 110000, N'/cumm', 150000, 450000, NULL, 44),
     (N'dengue', N'Platelets', 65000,  N'/cumm', 150000, 450000, NULL, 22),
     (N'dengue', N'Platelets', 28000,  N'/cumm', 150000, 450000, NULL, 3),
     (N'dengue', N'Haematocrit', 44, N'%', 36, 46, NULL, 22),
     (N'dengue', N'Haematocrit', 48, N'%', 36, 46, NULL, 3),
     (N'creat',  N'Creatinine', 1.0, N'mg/dL', 0.6, 1.2, NULL, 46),
     (N'creat',  N'Creatinine', 1.4, N'mg/dL', 0.6, 1.2, NULL, 24),
     (N'creat',  N'Creatinine', 1.9, N'mg/dL', 0.6, 1.2, NULL, 4),
     (N'k_high', N'Potassium',  6.4, N'mmol/L', 3.5, 5.1, NULL, 3),
     (N'k_high', N'Creatinine', 6.8, N'mg/dL', 0.6, 1.2, NULL, 18),
     (N'k_high', N'Creatinine', 6.9, N'mg/dL', 0.6, 1.2, NULL, 3),
     (N'icu_o2', N'WBC',        16.2, N'x10^9/L', 4.0, 11.0, NULL, 20),
     (N'icu_o2', N'CRP',        142,  N'mg/L', 0, 5, NULL, 20),
     (N'news',   N'WBC',        13.8, N'x10^9/L', 4.0, 11.0, NULL, 12),
     (N'news',   N'CRP',        86,   N'mg/L', 0, 5, NULL, 12);

    INSERT dbo.LabResult (HospitalId, AdmissionId, TestName, NumericValue, Unit, RefLow, RefHigh, Flag, ResultAt, Source, RecordedBy, CreatedAt)
    SELECT a.HospitalId, a.AdmissionId, l.TestName,
           -- Children get child-appropriate haemoglobin; generic values vary slightly per patient
           CASE WHEN l.Profile = N'*' AND l.TestName = N'Platelets' THEN l.Value + (a.Seq % 5) * 12000
                WHEN l.Profile = N'*' AND l.TestName = N'Glucose' AND a.Diagnosis LIKE N'%diabet%' THEN 13.6
                WHEN l.Profile = N'*' AND l.TestName = N'Haemoglobin' THEN l.Value - (a.Seq % 3) * 0.3
                ELSE l.Value END,
           l.Unit, l.RefLow, l.RefHigh, N'Normal', x.At, N'Form',
           wn.NurseId, x.At
    FROM #Adm a
    JOIN #Lab l ON (l.Profile = a.Profile) OR (l.Profile = N'*' AND NOT EXISTS (SELECT 1 FROM #Lab s WHERE s.Profile = a.Profile AND s.TestName = l.TestName))
    JOIN dbo.Ward w ON w.HospitalId = a.HospitalId AND w.Code = COALESCE(a.FromWard, a.WardCode)
    JOIN #WardNurse wn ON wn.WardId = w.Id
    CROSS APPLY (SELECT CASE WHEN l.HoursAgo IS NOT NULL THEN DATEADD(HOUR, -l.HoursAgo, @now) ELSE DATEADD(HOUR, l.AfterAdmitHours, a.AdmittedAt) END AS At) x
    WHERE x.At < a.EndAt AND x.At >= a.AdmittedAt;

    UPDATE r SET Flag =
        CASE WHEN (r.TestName = N'Potassium'   AND (r.NumericValue < 2.5 OR r.NumericValue > 6.0))
               OR (r.TestName = N'Sodium'      AND (r.NumericValue < 120 OR r.NumericValue > 160))
               OR (r.TestName = N'Haemoglobin' AND r.NumericValue < 7)
               OR (r.TestName = N'Platelets'   AND r.NumericValue < 20000)
               OR (r.TestName = N'Glucose'     AND (r.NumericValue < 3.0 OR r.NumericValue > 25)) THEN N'Critical'
             WHEN r.RefLow IS NOT NULL AND r.NumericValue < r.RefLow THEN N'Low'
             WHEN r.RefHigh IS NOT NULL AND r.NumericValue > r.RefHigh THEN N'High'
             ELSE N'Normal' END
    FROM dbo.LabResult r
    WHERE r.HospitalId IN (SELECT DISTINCT HospitalId FROM #Adm);

    /* ---- Clinical notes ---- */
    INSERT dbo.ClinicalNote (HospitalId, AdmissionId, AuthorUserId, NoteType, Text, Source, CreatedAt)
    -- Nursing admission note
    SELECT a.HospitalId, a.AdmissionId, wn.NurseId, N'Nursing',
           N'Received patient in the ward, bed and wristband checked. Oriented to the ward, IV cannula in situ. ' + pr.NursePlan, N'Form',
           DATEADD(MINUTE, 50, a.AdmittedAt)
    FROM #Adm a JOIN #Prof pr ON pr.Profile = a.Profile
    JOIN dbo.Ward w ON w.HospitalId = a.HospitalId AND w.Code = COALESCE(a.FromWard, a.WardCode)
    JOIN #WardNurse wn ON wn.WardId = w.Id
    UNION ALL
    -- Daily consultant round
    SELECT a.HospitalId, a.AdmissionId, cu.Id, N'ConsultantRound',
           N'Day ' + CAST(n.n + 1 AS NVARCHAR(3)) + N' round. ' + a.Diagnosis + N'. ' + pr.RoundPlan,
           CASE WHEN n.n % 2 = 1 THEN N'Voice' ELSE N'Form' END,
           x.At
    FROM #Adm a JOIN #Prof pr ON pr.Profile = a.Profile
    JOIN dbo.AppUser cu ON cu.HospitalId = a.HospitalId AND cu.Phone = a.ConsultantPhone
    JOIN @n n ON n.n < 10
    CROSS APPLY (SELECT DATEADD(HOUR, 3 + n.n * 24, a.AdmittedAt) AS At) x
    WHERE x.At < a.EndAt
    UNION ALL
    -- Handover note each morning
    SELECT a.HospitalId, a.AdmissionId, wn.Nurse2Id, N'Handover',
           N'Night handover: ' + pr.NursePlan + N' Pending: medicines as charted, vitals 4-6 hourly.', N'Form', x.At
    FROM #Adm a JOIN #Prof pr ON pr.Profile = a.Profile
    JOIN dbo.Ward w ON w.HospitalId = a.HospitalId AND w.Code = a.WardCode
    JOIN #WardNurse wn ON wn.WardId = w.Id
    JOIN @n n ON n.n < 10
    CROSS APPLY (SELECT DATEADD(HOUR, 14 + n.n * 24, a.AdmittedAt) AS At) x
    WHERE x.At < a.EndAt;

    -- Scripted nursing notes for deteriorating patients (recent, pending sign-off)
    INSERT dbo.ClinicalNote (HospitalId, AdmissionId, AuthorUserId, NoteType, Text, Source, CreatedAt)
    SELECT a.HospitalId, a.AdmissionId, wn.NurseId, N'Nursing', v.Txt, N'Voice', DATEADD(MINUTE, -v.MinAgo, @now)
    FROM #Adm a
    JOIN dbo.Ward w ON w.HospitalId = a.HospitalId AND w.Code = a.WardCode
    JOIN #WardNurse wn ON wn.WardId = w.Id
    JOIN (VALUES
        (N'hb',     80,  N'Patient passed black tarry stool again, feels dizzy and thirsty. Pulse 118. Informed duty doctor.'),
        (N'dengue', 140, N'Gum bleeding while brushing, no other bleeding. Oral intake about 2 litres since morning.'),
        (N'news',   55,  N'More breathless than morning, respiratory rate 23, SpO2 93 percent on air. Sitting upright.'),
        (N'creat',  200, N'Urine output only 120 mL in last 6 hours. Catheter flushed, patent.')
    ) AS v(Profile, MinAgo, Txt) ON v.Profile = a.Profile
    WHERE a.DischargedAt IS NULL;

    /* ---- Sign-off: entries older than 24 h were reviewed on the consultant's round ---- */
    UPDATE v SET SignedOffBy = ad.ConsultantUserId, SignedOffAt = DATEADD(HOUR, 3, v.RecordedAt)
    FROM dbo.VitalSign v JOIN dbo.Admission ad ON ad.Id = v.AdmissionId
    WHERE ad.HospitalId IN (SELECT DISTINCT HospitalId FROM #Adm) AND (v.RecordedAt < DATEADD(HOUR, -24, @now) OR ad.Status = N'Discharged');
    UPDATE r SET SignedOffBy = ad.ConsultantUserId, SignedOffAt = DATEADD(HOUR, 3, r.ResultAt)
    FROM dbo.LabResult r JOIN dbo.Admission ad ON ad.Id = r.AdmissionId
    WHERE ad.HospitalId IN (SELECT DISTINCT HospitalId FROM #Adm) AND (r.ResultAt < DATEADD(HOUR, -24, @now) OR ad.Status = N'Discharged');
    UPDATE c SET SignedOffBy = ad.ConsultantUserId, SignedOffAt = DATEADD(HOUR, 3, c.CreatedAt)
    FROM dbo.ClinicalNote c JOIN dbo.Admission ad ON ad.Id = c.AdmissionId
    WHERE ad.HospitalId IN (SELECT DISTINCT HospitalId FROM #Adm) AND c.NoteType <> N'ConsultantRound'
      AND (c.CreatedAt < DATEADD(HOUR, -24, @now) OR ad.Status = N'Discharged');

    /* ---- Prescriptions ---- */
    -- AlignMin: makes a Q-hourly item fall due AlignMin minutes from now (several doses due in the next hour).
    CREATE TABLE #Rx (Profile NVARCHAR(12), Ord INT, DrugName NVARCHAR(120), Form NVARCHAR(30), Strength NVARCHAR(40), Dose NVARCHAR(40), Route NVARCHAR(20),
                      Frequency NVARCHAR(6), DurationDays INT NULL, Instructions NVARCHAR(300) NULL, AlignMin INT NULL);
    INSERT #Rx VALUES
     (N'stable', 1, N'Ceftriaxone', N'Inj', N'1 g', N'1 g', N'IV', N'Q12H', 7, NULL, NULL),
     (N'stable', 2, N'Omeprazole', N'Cap', N'20 mg', N'20 mg', N'PO', N'BD', 14, N'Before meal', NULL),
     (N'stable', 3, N'Paracetamol', N'Tab', N'500 mg', N'500 mg', N'PO', N'PRN', 5, N'If fever (temp > 38 C)', NULL),
     (N'copd', 1, N'Salbutamol + Ipratropium', N'Neb', N'2.5 mg/500 mcg', N'1 respule', N'Neb', N'Q6H', 5, NULL, NULL),
     (N'copd', 2, N'Amoxicillin + Clavulanic acid', N'Tab', N'625 mg', N'625 mg', N'PO', N'TDS', 7, N'After meal', NULL),
     (N'copd', 3, N'Prednisolone', N'Tab', N'5 mg', N'40 mg', N'PO', N'OD', 5, N'After breakfast', NULL),
     (N'recovering', 1, N'Paracetamol', N'Tab', N'500 mg', N'500 mg', N'PO', N'TDS', 5, N'After meal', NULL),
     (N'recovering', 2, N'Omeprazole', N'Cap', N'20 mg', N'20 mg', N'PO', N'OD', 14, N'Before breakfast', NULL),
     (N'postop', 1, N'Ceftriaxone', N'Inj', N'1 g', N'1 g', N'IV', N'Q12H', 5, NULL, NULL),
     (N'postop', 2, N'Ketorolac', N'Inj', N'30 mg', N'30 mg', N'IV', N'Q8H', 3, N'Stop if gastric pain', NULL),
     (N'postop', 3, N'Omeprazole', N'Inj', N'40 mg', N'40 mg', N'IV', N'OD', 5, N'Before breakfast', NULL),
     (N'hb', 1, N'Pantoprazole', N'Inj', N'40 mg', N'40 mg', N'IV', N'Q12H', 5, NULL, 25),
     (N'hb', 2, N'Normal saline 0.9%', N'Inf', N'1000 mL', N'1000 mL', N'IV', N'Q8H', 3, N'Over 8 hours', NULL),
     (N'hb', 3, N'Tranexamic acid', N'Inj', N'500 mg', N'500 mg', N'IV', N'TDS', 3, NULL, NULL),
     (N'dengue', 1, N'Normal saline 0.9%', N'Inf', N'500 mL', N'500 mL', N'IV', N'Q6H', 3, N'Titrate to urine output', 40),
     (N'dengue', 2, N'Paracetamol', N'Tab', N'500 mg', N'500 mg', N'PO', N'QID', 5, N'Avoid NSAIDs', NULL),
     (N'dengue', 3, N'Omeprazole', N'Cap', N'20 mg', N'20 mg', N'PO', N'OD', 7, N'Before breakfast', NULL),
     (N'news', 1, N'Ceftriaxone', N'Inj', N'1 g', N'1 g', N'IV', N'Q12H', 7, NULL, 35),
     (N'news', 2, N'Azithromycin', N'Tab', N'500 mg', N'500 mg', N'PO', N'OD', 5, N'One hour before meal', NULL),
     (N'news', 3, N'Salbutamol', N'Neb', N'2.5 mg', N'2.5 mg', N'Neb', N'Q6H', 5, NULL, NULL),
     (N'creat', 1, N'Meropenem', N'Inj', N'1 g', N'1 g', N'IV', N'Q8H', 7, N'Renal dose review', 50),
     (N'creat', 2, N'Omeprazole', N'Inj', N'40 mg', N'40 mg', N'IV', N'BD', 5, N'Before meal', NULL),
     (N'creat', 3, N'Paracetamol', N'Inj', N'1 g', N'1 g', N'IV', N'Q8H', 3, NULL, NULL),
     (N'icu_o2', 1, N'Meropenem', N'Inj', N'1 g', N'1 g', N'IV', N'Q8H', 7, NULL, NULL),
     (N'icu_o2', 2, N'Salbutamol + Ipratropium', N'Neb', N'2.5 mg/500 mcg', N'1 respule', N'Neb', N'Q6H', 5, NULL, 20),
     (N'icu_o2', 3, N'Enoxaparin', N'Inj', N'40 mg', N'40 mg', N'SC', N'OD', 7, NULL, NULL),
     (N'k_high', 1, N'Calcium gluconate 10%', N'Inj', N'10 mL', N'10 mL', N'IV', N'STAT', NULL, N'Over 10 minutes', NULL),
     (N'k_high', 2, N'Insulin (soluble) + Dextrose 25%', N'Inj', N'10 units + 50 mL', N'10 units', N'IV', N'STAT', NULL, NULL, NULL),
     (N'k_high', 3, N'Sodium bicarbonate', N'Tab', N'600 mg', N'600 mg', N'PO', N'TDS', 7, N'After meal', NULL),
     (N'icu_postop', 1, N'Meropenem', N'Inj', N'1 g', N'1 g', N'IV', N'Q8H', 7, NULL, NULL),
     (N'icu_postop', 2, N'Metronidazole', N'Inf', N'500 mg', N'500 mg', N'IV', N'Q8H', 7, NULL, 55),
     (N'icu_postop', 3, N'Omeprazole', N'Inj', N'40 mg', N'40 mg', N'IV', N'BD', 5, N'Before meal', NULL),
     (N'paed', 1, N'Paracetamol', N'Syrup', N'120 mg/5 mL', N'5 mL', N'PO', N'Q6H', 3, N'If fever', 45),
     (N'paed', 2, N'Zinc', N'Syrup', N'10 mg/5 mL', N'10 mg', N'PO', N'OD', 10, N'After meal', NULL),
     (N'paed', 3, N'ORS', N'Sachet', N'1 sachet', N'50-100 mL', N'PO', N'PRN', 3, N'After each loose stool', NULL);

    INSERT dbo.Prescription (HospitalId, AdmissionId, ConsultantUserId, Source, Status, SignedAt, GeneralInstructions, CreatedAt)
    SELECT a.HospitalId, a.AdmissionId, cu.Id, N'Form', N'Signed', DATEADD(MINUTE, 60, a.AdmittedAt),
           N'Monitor vitals 6-hourly, strict intake-output chart. Inform duty doctor if NEWS2 is 5 or more.', DATEADD(MINUTE, 55, a.AdmittedAt)
    FROM #Adm a
    JOIN dbo.AppUser cu ON cu.HospitalId = a.HospitalId AND cu.Phone = COALESCE(a.FromConsultantPhone, a.ConsultantPhone);

    INSERT dbo.PrescriptionItem (HospitalId, PrescriptionId, DrugName, Form, Strength, Dose, Route, Frequency, StartAt, DurationDays, Instructions, IsActive, StoppedAt, CreatedAt)
    SELECT a.HospitalId, p.Id, r.DrugName, r.Form, r.Strength, r.Dose, r.Route, r.Frequency,
           CASE WHEN r.AlignMin IS NOT NULL AND a.DischargedAt IS NULL THEN
                    DATEADD(HOUR, -h.Hrs * ((DATEDIFF(MINUTE, DATEADD(MINUTE, 60, a.AdmittedAt), DATEADD(MINUTE, r.AlignMin, @now)) / 60) / h.Hrs), DATEADD(MINUTE, r.AlignMin, @now))
                ELSE DATEADD(MINUTE, 60 + (a.Seq % 3) * 15, a.AdmittedAt) END,
           r.DurationDays, r.Instructions,
           CASE WHEN a.DischargedAt IS NULL THEN 1 ELSE 0 END, a.DischargedAt, DATEADD(MINUTE, 55, a.AdmittedAt)
    FROM #Adm a
    JOIN dbo.Prescription p ON p.AdmissionId = a.AdmissionId
    JOIN #Rx r ON r.Profile = a.Profile
    CROSS APPLY (SELECT CASE r.Frequency WHEN N'Q4H' THEN 4 WHEN N'Q6H' THEN 6 WHEN N'Q8H' THEN 8 WHEN N'Q12H' THEN 12 ELSE 24 END AS Hrs) h;

    /* ---- Dose tasks (same schedule rules as Services/DoseScheduler.cs) ---- */
    -- Fixed clock times, Asia/Dhaka minutes after midnight. "ODN" = OD at night (instructions mention night).
    DECLARE @FreqTime TABLE (Frequency NVARCHAR(6), LocalMinute INT);
    INSERT @FreqTime VALUES (N'OD', 480), (N'ODN', 1200), (N'BD', 480), (N'BD', 1200), (N'TDS', 480), (N'TDS', 840), (N'TDS', 1200),
                            (N'QID', 360), (N'QID', 720), (N'QID', 1080), (N'QID', 1440);

    INSERT dbo.DoseTask (HospitalId, PrescriptionItemId, AdmissionId, DueAt, Status, CreatedAt)
    SELECT i.HospitalId, i.Id, p.AdmissionId, x.DueAt, N'Scheduled', i.CreatedAt
    FROM dbo.PrescriptionItem i
    JOIN dbo.Prescription p ON p.Id = i.PrescriptionId
    JOIN #Adm a ON a.AdmissionId = p.AdmissionId
    JOIN @FreqTime ft ON ft.Frequency = CASE WHEN i.Frequency = N'OD' AND i.Instructions LIKE N'%night%' THEN N'ODN' ELSE i.Frequency END
    JOIN @n n ON n.n <= COALESCE(i.DurationDays, 7) + 1
    CROSS APPLY (SELECT DATEADD(MINUTE, ft.LocalMinute - 360, CAST(DATEADD(DAY, n.n, CAST(DATEADD(HOUR, 6, i.StartAt) AS DATE)) AS DATETIME2)) AS DueAt) x
    WHERE x.DueAt >= i.StartAt AND x.DueAt < DATEADD(DAY, COALESCE(i.DurationDays, 7), i.StartAt)
      AND (a.DischargedAt IS NULL OR x.DueAt < a.DischargedAt)
    UNION ALL
    SELECT i.HospitalId, i.Id, p.AdmissionId, x.DueAt, N'Scheduled', i.CreatedAt
    FROM dbo.PrescriptionItem i
    JOIN dbo.Prescription p ON p.Id = i.PrescriptionId
    JOIN #Adm a ON a.AdmissionId = p.AdmissionId
    CROSS APPLY (SELECT CASE i.Frequency WHEN N'Q4H' THEN 4 WHEN N'Q6H' THEN 6 WHEN N'Q8H' THEN 8 WHEN N'Q12H' THEN 12 END AS Hrs) h
    JOIN @n n ON n.n * h.Hrs < COALESCE(i.DurationDays, 7) * 24
    CROSS APPLY (SELECT DATEADD(HOUR, n.n * h.Hrs, i.StartAt) AS DueAt) x
    WHERE h.Hrs IS NOT NULL
      AND (a.DischargedAt IS NULL OR x.DueAt < a.DischargedAt)
    UNION ALL
    SELECT i.HospitalId, i.Id, p.AdmissionId, i.StartAt, N'Scheduled', i.CreatedAt
    FROM dbo.PrescriptionItem i
    JOIN dbo.Prescription p ON p.Id = i.PrescriptionId
    JOIN #Adm a ON a.AdmissionId = p.AdmissionId
    WHERE i.Frequency = N'STAT';

    -- Statuses relative to now: past = Given by the ward nurse; within the window = Due; future = Scheduled.
    UPDATE d SET
        Status = CASE WHEN d.DueAt <= DATEADD(MINUTE, -30, @now) OR ad.Status = N'Discharged' THEN N'Given'
                      WHEN d.DueAt <= DATEADD(MINUTE, 15, @now) THEN N'Due'
                      ELSE N'Scheduled' END,
        DoneAt = CASE WHEN d.DueAt <= DATEADD(MINUTE, -30, @now) OR ad.Status = N'Discharged' THEN DATEADD(MINUTE, 3 + d.Id % 17, d.DueAt) END,
        DoneByUserId = CASE WHEN d.DueAt <= DATEADD(MINUTE, -30, @now) OR ad.Status = N'Discharged'
                            THEN CASE WHEN d.Id % 2 = 0 THEN wn.NurseId ELSE wn.Nurse2Id END END
    FROM dbo.DoseTask d
    JOIN dbo.Admission ad ON ad.Id = d.AdmissionId
    JOIN #Adm a ON a.AdmissionId = ad.Id
    CROSS APPLY (SELECT TOP 1 s.WardId FROM dbo.AdmissionWardStay s WHERE s.AdmissionId = ad.Id AND s.StartAt <= d.DueAt ORDER BY s.StartAt DESC) st
    JOIN #WardNurse wn ON wn.WardId = st.WardId;

    -- One refused dose (realism)
    UPDATE TOP (1) d SET Status = N'Refused', Reason = N'Patient vomiting, refused oral medicine'
    FROM dbo.DoseTask d JOIN #Adm a ON a.AdmissionId = d.AdmissionId
    JOIN dbo.PrescriptionItem i ON i.Id = d.PrescriptionItemId
    WHERE a.MRN = N'DCG-24006' AND i.Route = N'PO' AND d.Status = N'Given' AND d.DueAt > DATEADD(HOUR, -20, @now);

    -- One missed dose with an escalated alert (Jahanara Begum, M-04)
    DECLARE @missed INT = (
        SELECT TOP 1 d.Id FROM dbo.DoseTask d JOIN #Adm a ON a.AdmissionId = d.AdmissionId
        WHERE a.MRN = N'DCG-24004' AND d.Status = N'Given' AND d.DueAt BETWEEN DATEADD(HOUR, -5, @now) AND DATEADD(MINUTE, -60, @now)
        ORDER BY d.DueAt DESC);
    IF @missed IS NOT NULL
    BEGIN
        UPDATE dbo.DoseTask SET Status = N'Missed', DoneAt = NULL, DoneByUserId = NULL, EscalatedAt = DATEADD(MINUTE, 30, DueAt) WHERE Id = @missed;
        INSERT dbo.Alert (HospitalId, AdmissionId, Severity, AlertType, RuleCode, RuleDetail, Status, EscalationLevel, DoseTaskId, CreatedAt, LastEscalatedAt)
        SELECT d.HospitalId, d.AdmissionId, N'Warning', N'MissedDose', N'MISSED_DOSE',
               N'Dose not recorded 30 min after due: ' + i.DrugName + N' ' + i.Dose + N' ' + i.Route + N' due ' + FORMAT(DATEADD(HOUR, 6, d.DueAt), 'HH:mm') + N' (Dhaka).',
               N'Open', 1, d.Id, DATEADD(MINUTE, 30, d.DueAt), DATEADD(MINUTE, 30, d.DueAt)
        FROM dbo.DoseTask d JOIN dbo.PrescriptionItem i ON i.Id = d.PrescriptionItemId WHERE d.Id = @missed;
    END

    /* ---- Bed statuses ---- */
    UPDATE b SET Status = N'Free', StatusChangedAt = DATEADD(HOUR, -1, @now)
    FROM dbo.Bed b WHERE b.HospitalId IN (SELECT DISTINCT HospitalId FROM #Adm);

    -- Last event time per bed
    UPDATE b SET StatusChangedAt = e.LastAt
    FROM dbo.Bed b CROSS APPLY (SELECT MAX(At) AS LastAt FROM dbo.BedEvent be WHERE be.BedId = b.Id) e
    WHERE b.HospitalId IN (SELECT DISTINCT HospitalId FROM #Adm) AND e.LastAt IS NOT NULL;

    UPDATE b SET Status = N'Occupied'
    FROM dbo.Bed b JOIN dbo.Admission ad ON ad.CurrentBedId = b.Id AND ad.Status = N'Active'
    WHERE ad.HospitalId IN (SELECT DISTINCT HospitalId FROM #Adm);

    UPDATE b SET Status = N'Cleaning'
    FROM dbo.Bed b JOIN #Adm a ON a.DischargedHoursAgo IS NOT NULL AND a.DischargedHoursAgo < 3
    JOIN dbo.Ward w ON w.HospitalId = a.HospitalId AND w.Code = a.WardCode AND b.WardId = w.Id AND b.BedNumber = a.BedNo;

    -- Blocked and reserved beds (with matching events)
    INSERT dbo.BedEvent (HospitalId, BedId, AdmissionId, EventType, ByUserId, At, Note)
    SELECT b.HospitalId, b.Id, NULL, v.EventType, adm.Id, DATEADD(MINUTE, -v.MinAgo, @now), v.Note
    FROM (VALUES (N'DCGH', N'M',  N'M-11', N'Blocked',  1800, N'Broken side rail - maintenance requested'),
                 (N'DCGH', N'P',  N'P-07', N'Reserved', 20,   N'Reserved for transfer from emergency'),
                 (N'CCH',  N'M',  N'M-08', N'Blocked',  900,  N'Oxygen outlet faulty'),
                 (N'CCH',  N'G',  N'G-05', N'Reserved', 35,   N'Reserved for elective LSCS tomorrow')) AS v(HCode, WCode, BedNo, EventType, MinAgo, Note)
    JOIN dbo.Hospital h ON h.Code = v.HCode AND h.Id IN (SELECT DISTINCT HospitalId FROM #Adm)
    JOIN dbo.Ward w ON w.HospitalId = h.Id AND w.Code = v.WCode
    JOIN dbo.Bed b ON b.WardId = w.Id AND b.BedNumber = v.BedNo
    CROSS APPLY (SELECT TOP 1 u.Id FROM dbo.AppUser u WHERE u.HospitalId = h.Id AND u.Role = CASE WHEN v.EventType = N'Blocked' THEN N'HospitalAdmin' ELSE N'Receptionist' END ORDER BY u.Id) adm;

    UPDATE b SET Status = e.EventType, StatusChangedAt = e.At
    FROM dbo.Bed b
    JOIN dbo.BedEvent e ON e.BedId = b.Id AND e.AdmissionId IS NULL AND e.EventType IN (N'Blocked', N'Reserved')
    WHERE b.HospitalId IN (SELECT DISTINCT HospitalId FROM #Adm) AND b.Status = N'Free';
END

COMMIT TRANSACTION;

SELECT h.Code, (SELECT COUNT(*) FROM dbo.Admission a WHERE a.HospitalId = h.Id AND a.Status = N'Active') AS ActiveAdmissions,
       (SELECT COUNT(*) FROM dbo.Admission a WHERE a.HospitalId = h.Id AND a.Status = N'Discharged') AS Discharged,
       (SELECT COUNT(*) FROM dbo.AppUser u WHERE u.HospitalId = h.Id) AS Users,
       (SELECT COUNT(*) FROM dbo.DoseTask d WHERE d.HospitalId = h.Id) AS DoseTasks,
       (SELECT COUNT(*) FROM dbo.VitalSign v WHERE v.HospitalId = h.Id) AS Vitals
FROM dbo.Hospital h;
