namespace InPatient.Api.Dtos;

public record BedOccupantDto(int AdmissionId, int PatientId, string PatientName, string Mrn, int AgeYears, string Sex,
    string? Diagnosis, string ConsultantName, int? News2Score, string? News2Band, string Flag, int ActiveAlerts, bool AwaitingScan);

public record BedTileDto(int Id, int WardId, string WardName, string BedNumber, string Status, DateTime StatusChangedAt, string? LastNote, BedOccupantDto? Occupant);

public record WardOccupancyDto(int TotalBeds, int Free, int Reserved, int Occupied, int Cleaning, int Blocked, decimal OccupancyPct);

public record WardSummaryDto(int Id, string Name, string Code, string Type, string? Floor, WardOccupancyDto Occupancy, int WatchCount, int CriticalCount,
    IReadOnlyList<StaffBriefDto> Consultants);

public record BoardWardDto(int Id, string Name, string Code, string Type, WardOccupancyDto Occupancy, IReadOnlyList<BedTileDto> Beds);

public record BoardDto(int HospitalId, string HospitalName, DateTime GeneratedAt, IReadOnlyList<BoardWardDto> Wards);

public record StaffBriefDto(int Id, string Name, string? Designation);

public record BedStatusChangeRequest(string Status, string? Note);

public record ReserveBedRequest(string? Note);

public record QrDto(string Token, string PngBase64, string Label);

public record ScanResolveDto(string Kind, BedTileDto? Bed, AdmissionHeaderDto? Admission);

public record ConfirmBedRequest(string BedToken, string WristbandToken);

public record ScanTransferRequest(string WristbandToken, string BedToken, string? Reason);

public record TransferRequest(int BedId, string? Reason);

public record BedEventDto(int Id, int BedId, string BedNumber, int WardId, string WardName, string EventType, DateTime At, string? Note,
    int? AdmissionId, string? PatientName, string? ByUserName);

public record WardHistoryDayDto(DateOnly Day, int AdmissionsIn, int TransfersIn, int TransfersOut, int Discharges, decimal OccupiedBedDays, decimal? AvgLengthOfStayDays);

public record WardHistoryTotalsDto(int AdmissionsIn, int TransfersIn, int TransfersOut, int Discharges, decimal OccupiedBedDays, decimal? AvgLengthOfStayDays,
    decimal AvgOccupancyPct);

public record WardHistoryDto(int WardId, string WardName, DateOnly From, DateOnly To, int TotalBeds, WardOccupancyDto OccupancyNow,
    WardHistoryTotalsDto Totals, IReadOnlyList<WardHistoryDayDto> Days, IReadOnlyList<BedEventDto> Events);

public record JourneyStayDto(int StayId, int Sequence, int WardId, string WardName, int BedId, string BedNumber, string Reason, DateTime StartAt, DateTime? EndAt, decimal DurationHours);

public record PatientJourneyDto(int AdmissionId, string PatientName, string Mrn, string AdmissionStatus, IReadOnlyList<JourneyStayDto> Stays);
