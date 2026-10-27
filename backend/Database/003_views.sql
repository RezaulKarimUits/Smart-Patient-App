/* =====================================================================
   InPatient — 003_views.sql  (reporting views; re-runnable)
   Days are Asia/Dhaka calendar days (UTC+6, no DST).
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

CREATE OR ALTER VIEW dbo.vw_WardOccupancyNow
AS
SELECT  w.HospitalId,
        w.Id   AS WardId,
        w.Name AS WardName,
        w.Code AS WardCode,
        w.Type AS WardType,
        COUNT(b.Id)                                                AS TotalBeds,
        SUM(CASE WHEN b.Status = N'Free'     THEN 1 ELSE 0 END)    AS FreeBeds,
        SUM(CASE WHEN b.Status = N'Reserved' THEN 1 ELSE 0 END)    AS ReservedBeds,
        SUM(CASE WHEN b.Status = N'Occupied' THEN 1 ELSE 0 END)    AS OccupiedBeds,
        SUM(CASE WHEN b.Status = N'Cleaning' THEN 1 ELSE 0 END)    AS CleaningBeds,
        SUM(CASE WHEN b.Status = N'Blocked'  THEN 1 ELSE 0 END)    AS BlockedBeds,
        CAST(CASE WHEN COUNT(b.Id) = 0 THEN 0
             ELSE 100.0 * SUM(CASE WHEN b.Status IN (N'Occupied', N'Reserved') THEN 1 ELSE 0 END) / COUNT(b.Id) END AS DECIMAL(5,1)) AS OccupancyPct
FROM dbo.Ward w
LEFT JOIN dbo.Bed b ON b.WardId = w.Id
WHERE w.IsActive = 1
GROUP BY w.HospitalId, w.Id, w.Name, w.Code, w.Type;
GO

/* One row per ward stay with ordering inside the admission (patient ward journey). */
CREATE OR ALTER VIEW dbo.vw_PatientWardJourney
AS
SELECT  s.HospitalId,
        s.Id            AS StayId,
        s.AdmissionId,
        a.PatientId,
        p.MRN,
        p.Name          AS PatientName,
        s.WardId,
        w.Name          AS WardName,
        s.BedId,
        b.BedNumber,
        s.Reason,
        s.StartAt,
        s.EndAt,
        CAST(DATEDIFF(MINUTE, s.StartAt, COALESCE(s.EndAt, SYSUTCDATETIME())) / 60.0 AS DECIMAL(9,1)) AS DurationHours,
        ROW_NUMBER() OVER (PARTITION BY s.AdmissionId ORDER BY s.StartAt, s.Id) AS Sequence,
        a.Status        AS AdmissionStatus
FROM dbo.AdmissionWardStay s
JOIN dbo.Admission a ON a.Id = s.AdmissionId
JOIN dbo.Patient   p ON p.Id = a.PatientId
JOIN dbo.Ward      w ON w.Id = s.WardId
JOIN dbo.Bed       b ON b.Id = s.BedId;
GO

/* Per ward and Dhaka day: admissions in, transfers in/out, discharges, occupied bed-days, average LOS of discharges. */
CREATE OR ALTER VIEW dbo.vw_WardHistory
AS
WITH StayLocal AS (
    SELECT  s.HospitalId, s.WardId, s.AdmissionId, s.Reason,
            DATEADD(HOUR, 6, s.StartAt) AS StartLocal,
            DATEADD(HOUR, 6, COALESCE(s.EndAt, SYSUTCDATETIME())) AS EndLocal,
            s.EndAt,
            CASE WHEN s.EndAt IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.AdmissionWardStay n
                                                       WHERE n.AdmissionId = s.AdmissionId AND n.StartAt >= s.EndAt AND n.Id <> s.Id)
                 THEN 1 ELSE 0 END AS EndedByTransfer
    FROM dbo.AdmissionWardStay s
),
N AS (  -- 0..999 day offsets
    SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
),
Bounds AS (
    SELECT w.HospitalId, w.Id AS WardId, w.Name AS WardName,
           CAST(MIN(sl.StartLocal) AS DATE) AS FirstDay,
           CAST(DATEADD(HOUR, 6, SYSUTCDATETIME()) AS DATE) AS LastDay
    FROM dbo.Ward w
    JOIN StayLocal sl ON sl.WardId = w.Id
    GROUP BY w.HospitalId, w.Id, w.Name
),
Days AS (
    SELECT b.HospitalId, b.WardId, b.WardName, DATEADD(DAY, N.n, b.FirstDay) AS Day
    FROM Bounds b
    JOIN N ON DATEADD(DAY, N.n, b.FirstDay) <= b.LastDay
),
Starts AS (
    SELECT s.WardId, CAST(s.StartLocal AS DATE) AS Day,
           SUM(CASE WHEN s.Reason = N'Admitted' THEN 1 ELSE 0 END) AS AdmissionsIn,
           SUM(CASE WHEN s.Reason = N'Transfer' THEN 1 ELSE 0 END) AS TransfersIn
    FROM StayLocal s
    GROUP BY s.WardId, CAST(s.StartLocal AS DATE)
),
Ends AS (
    SELECT s.WardId, CAST(s.EndLocal AS DATE) AS Day,
           SUM(s.EndedByTransfer) AS TransfersOut,
           SUM(CASE WHEN s.EndedByTransfer = 0 AND a.Status = N'Discharged' THEN 1 ELSE 0 END) AS Discharges,
           AVG(CASE WHEN s.EndedByTransfer = 0 AND a.Status = N'Discharged'
                    THEN DATEDIFF(MINUTE, a.AdmittedAt, a.DischargedAt) / 1440.0 END) AS AvgLos
    FROM StayLocal s
    JOIN dbo.Admission a ON a.Id = s.AdmissionId
    WHERE s.EndAt IS NOT NULL
    GROUP BY s.WardId, CAST(s.EndLocal AS DATE)
),
Occ AS (
    SELECT d.WardId, d.Day,
           SUM(DATEDIFF(MINUTE,
                   CASE WHEN s.StartLocal > CAST(d.Day AS DATETIME2) THEN s.StartLocal ELSE CAST(d.Day AS DATETIME2) END,
                   CASE WHEN s.EndLocal < DATEADD(DAY, 1, CAST(d.Day AS DATETIME2)) THEN s.EndLocal ELSE DATEADD(DAY, 1, CAST(d.Day AS DATETIME2)) END)) AS Minutes
    FROM Days d
    JOIN StayLocal s ON s.WardId = d.WardId
                    AND s.StartLocal < DATEADD(DAY, 1, CAST(d.Day AS DATETIME2))
                    AND s.EndLocal > CAST(d.Day AS DATETIME2)
    GROUP BY d.WardId, d.Day
)
SELECT  d.HospitalId,
        d.WardId,
        d.WardName,
        d.Day,
        COALESCE(st.AdmissionsIn, 0) AS AdmissionsIn,
        COALESCE(st.TransfersIn, 0)  AS TransfersIn,
        COALESCE(en.TransfersOut, 0) AS TransfersOut,
        COALESCE(en.Discharges, 0)   AS Discharges,
        CAST(COALESCE(o.Minutes, 0) / 1440.0 AS DECIMAL(9,2)) AS OccupiedBedDays,
        CAST(en.AvgLos AS DECIMAL(9,2)) AS AvgLengthOfStayDays
FROM Days d
LEFT JOIN Starts st ON st.WardId = d.WardId AND st.Day = d.Day
LEFT JOIN Ends   en ON en.WardId = d.WardId AND en.Day = d.Day
LEFT JOIN Occ    o  ON o.WardId  = d.WardId AND o.Day  = d.Day;
GO
