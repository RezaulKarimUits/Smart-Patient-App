namespace InPatient.Api.Dtos;

public record VitalsInput(int? Systolic, int? Diastolic, int? Pulse, int? RespRate, decimal? Temperature, string? TemperatureUnit, int? SpO2,
    bool? OnOxygen, string? Consciousness, DateTime? RecordedAt);

public record VitalDto(int Id, DateTime RecordedAt, int? Systolic, int? Diastolic, int? Pulse, int? RespRate, decimal? TemperatureC, int? SpO2,
    bool OnOxygen, string Consciousness, int? News2Score, string? News2Band, string Source, string? RecordedByName, string? SignedOffByName, DateTime? SignedOffAt);

public record LabInput(string TestName, decimal? Value, string? TextValue, string? Unit, decimal? RefLow, decimal? RefHigh, DateTime? ResultAt);

public record LabDto(int Id, string TestName, decimal? Value, string? TextValue, string? Unit, decimal? RefLow, decimal? RefHigh, string Flag,
    DateTime ResultAt, string Source, int? StoredFileId, string? RecordedByName, string? SignedOffByName, DateTime? SignedOffAt);

public record LabCellDto(int Id, decimal? Value, string? TextValue, string Flag, DateTime ResultAt);

public record LabRowDto(string TestName, string? Unit, decimal? RefLow, decimal? RefHigh, IReadOnlyList<LabCellDto?> Cells, string? Trend);

public record LabTableDto(IReadOnlyList<DateOnly> Dates, IReadOnlyList<LabRowDto> Rows);

public record LabsResponse(IReadOnlyList<LabDto> Results, LabTableDto Table);

public record NoteInput(string NoteType, string Text);

public record NoteDto(int Id, string NoteType, string Text, string Source, DateTime CreatedAt, int AuthorUserId, string AuthorName, string? AuthorRole,
    string? SignedOffByName, DateTime? SignedOffAt, int? AudioFileId);

public record MonitoringDto(int? News2Score, string? News2Band, string Flag, IReadOnlyList<InPatient.Api.Services.AlertDto> NewAlerts, string? VitalsSuggestion);

public record VitalsSaveResponse(VitalDto Vital, MonitoringDto Monitoring);

public record LabsSaveResponse(IReadOnlyList<LabDto> Results, IReadOnlyList<string> Notes, MonitoringDto Monitoring);

public record TimelineItemDto(string Type, DateTime At, string Title, string? Detail, string? Actor, string? Source, string? Severity, int RefId,
    bool? SignedOff, string? SignedOffByName);

public record PendingEntryDto(string Kind, int Id, DateTime At, string Summary, string? ByName, string Source, string? Flag);

public record PendingPatientDto(int AdmissionId, string PatientName, string? BedNumber, string? WardName, string Flag, IReadOnlyList<PendingEntryDto> Entries);

public record SignOffRequest(IReadOnlyList<int>? VitalIds, IReadOnlyList<int>? LabIds, IReadOnlyList<int>? NoteIds);

public record SignOffResponse(int Vitals, int Labs, int Notes, string SignedOffByName);

public record CareTaskDto(int Id, int AdmissionId, string TaskType, string Detail, DateTime DueAt, string Status, bool IsOverdue);

public record NurseBedDto(int BedId, string BedNumber, int WardId, string WardName, string BedStatus, BedOccupantDto? Occupant,
    int DueDoses, int OverdueDoses, DateTime? NextDoseAt, int OpenAlerts, IReadOnlyList<CareTaskDto> CareTasks, DateTime? LastVitalsAt);

public record NurseHomeDto(string ShiftDate, string Shift, DateTime ShiftStart, DateTime ShiftEnd, IReadOnlyList<NurseBedDto> Beds);

public record PriorityPatientDto(AdmissionHeaderDto Patient, IReadOnlyList<InPatient.Api.Services.AlertDto> ActiveAlerts, int PendingSignOff, int Rank, string Reason);
