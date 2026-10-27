namespace InPatient.Api.Dtos;

public record PatientDto(int Id, string Mrn, string Name, DateOnly DateOfBirth, int AgeYears, string Sex, string? Phone, string? Guardian, string? BloodGroup,
    int? ActiveAdmissionId);

public record RegisterPatientRequest(string Name, DateOnly DateOfBirth, string Sex, string? Phone, string? Guardian, string? BloodGroup);

public record AdmitRequest(int PatientId, int BedId, int ConsultantUserId, string? ProvisionalDiagnosis);

public record AdmissionHeaderDto(int Id, int PatientId, string PatientName, string Mrn, int AgeYears, string Sex, string? BloodGroup,
    int? BedId, string? BedNumber, int? WardId, string? WardName, string? WardType, int ConsultantUserId, string ConsultantName,
    DateTime AdmittedAt, string? ProvisionalDiagnosis, string Status, bool BedConfirmed, DateTime? DischargedAt,
    int? News2Score, string? News2Band, bool News2Applicable, string Flag, int ActiveAlerts, int DayOfStay);

public record WristbandDto(int AdmissionId, string PatientName, string Mrn, DateOnly DateOfBirth, int AgeYears, string Sex, string? BloodGroup,
    string HospitalName, string? WardName, string? BedNumber, string ConsultantName, DateTime AdmittedAt, string Token, string QrPngBase64);

public record AdmitResponse(AdmissionHeaderDto Admission, WristbandDto Wristband);
