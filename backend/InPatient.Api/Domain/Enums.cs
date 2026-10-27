namespace InPatient.Api.Domain;

// String constants mirror the CHECK constraints in Database/001_schema.sql.

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string HospitalAdmin = "HospitalAdmin";
    public const string Receptionist = "Receptionist";
    public const string Nurse = "Nurse";
    public const string Consultant = "Consultant";
    public const string Patient = "Patient";

    // Handy combinations for [Authorize(Roles = ...)]
    public const string Staff = "HospitalAdmin,Receptionist,Nurse,Consultant";
    public const string Clinical = "Nurse,Consultant";
    public const string FrontDesk = "HospitalAdmin,Receptionist";
    public const string BedBoard = "HospitalAdmin,Receptionist,Nurse,Consultant";
    public const string Reviewers = "HospitalAdmin,Consultant";

    public static readonly string[] All = [SuperAdmin, HospitalAdmin, Receptionist, Nurse, Consultant, Patient];
}

public static class BedStatus
{
    public const string Free = "Free";
    public const string Reserved = "Reserved";
    public const string Occupied = "Occupied";
    public const string Cleaning = "Cleaning";
    public const string Blocked = "Blocked";
}

public static class BedEventType
{
    public const string Reserved = "Reserved";
    public const string Admitted = "Admitted";
    public const string TransferredIn = "TransferredIn";
    public const string TransferredOut = "TransferredOut";
    public const string Discharged = "Discharged";
    public const string Cleaned = "Cleaned";
    public const string Blocked = "Blocked";
    public const string Unblocked = "Unblocked";
    public const string Released = "Released";
}

public static class WardTypes
{
    public static readonly string[] All = ["Medicine", "Surgery", "Paediatrics", "ICU", "Gynae", "Other"];
}

public static class ShiftTypes
{
    public const string Morning = "Morning";   // 08:00-14:00 Dhaka
    public const string Evening = "Evening";   // 14:00-20:00
    public const string Night = "Night";       // 20:00-08:00 (belongs to the date it starts on)
    public static readonly string[] All = [Morning, Evening, Night];
}

public static class AdmissionStatus
{
    public const string Active = "Active";
    public const string Discharged = "Discharged";
}

public static class StayReason
{
    public const string Admitted = "Admitted";
    public const string Transfer = "Transfer";
}

public static class Sources
{
    public const string Form = "Form";
    public const string Voice = "Voice";
    public const string Device = "Device";
    public const string Photo = "Photo";
}

public static class PrescriptionStatus
{
    public const string Draft = "Draft";
    public const string Signed = "Signed";
}

public static class Frequencies
{
    public const string OD = "OD", BD = "BD", TDS = "TDS", QID = "QID", Q4H = "Q4H", Q6H = "Q6H", Q8H = "Q8H", Q12H = "Q12H", STAT = "STAT", PRN = "PRN";
    public static readonly string[] All = [OD, BD, TDS, QID, Q4H, Q6H, Q8H, Q12H, STAT, PRN];
}

public static class DoseStatus
{
    public const string Scheduled = "Scheduled";
    public const string Due = "Due";
    public const string Given = "Given";
    public const string Delayed = "Delayed";
    public const string Refused = "Refused";
    public const string Withheld = "Withheld";
    public const string Missed = "Missed";

    /// <summary>Statuses a nurse can record.</summary>
    public static readonly string[] Recordable = [Given, Delayed, Refused, Withheld];
    /// <summary>Tasks still waiting for a nurse action.</summary>
    public static readonly string[] Open = [Scheduled, Due, Delayed];
}

public static class News2Bands
{
    public const string Low = "Low";
    public const string LowMedium = "LowMedium";
    public const string Medium = "Medium";
    public const string High = "High";
}

public static class Consciousness
{
    public const string Alert = "Alert";
    public static readonly string[] All = ["Alert", "Confusion", "Voice", "Pain", "Unresponsive"];
}

public static class LabFlags
{
    public const string Low = "Low", Normal = "Normal", High = "High", Critical = "Critical";
}

public static class NoteTypes
{
    public const string Nursing = "Nursing";
    public const string ConsultantRound = "ConsultantRound";
    public const string Handover = "Handover";
    public static readonly string[] All = [Nursing, ConsultantRound, Handover];
}

public static class FileKinds
{
    public const string Audio = "Audio", Prescription = "Prescription", TestReport = "TestReport", Progress = "Progress", Other = "Other";
    public static readonly string[] All = [Audio, Prescription, TestReport, Progress, Other];
}

public static class AlertSeverity
{
    public const string Info = "Info", Warning = "Warning", Critical = "Critical";
    public static int Rank(string s) => s switch { Critical => 3, Warning => 2, _ => 1 };
}

public static class AlertTypes
{
    public const string News2 = "News2", CriticalLab = "CriticalLab", Trend = "Trend", MissedDose = "MissedDose";
}

public static class AlertStatus
{
    public const string Open = "Open", Acknowledged = "Acknowledged", Resolved = "Resolved";
    public static readonly string[] Active = [Open, Acknowledged];
}

public static class VoicePurpose
{
    public const string NurseEntry = "NurseEntry", Prescription = "Prescription", RoundNote = "RoundNote";
}

public static class VoiceStatus
{
    public const string Drafted = "Drafted", Confirmed = "Confirmed", Discarded = "Discarded";
}

public static class AiSummaryKind
{
    public const string Progress24h = "Progress24h", Handover = "Handover", Discharge = "Discharge";
}

public static class CareTaskStatus
{
    public const string Open = "Open", Done = "Done", Cancelled = "Cancelled";
}

/// <summary>Watch status shown everywhere (bed board, nurse list, consultant list).</summary>
public static class PatientFlag
{
    public const string Stable = "Stable", Watch = "Watch", Critical = "Critical";
}
