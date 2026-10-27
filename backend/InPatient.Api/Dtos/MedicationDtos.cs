namespace InPatient.Api.Dtos;

public record PrescriptionItemInput(string DrugName, string? Form, string? Strength, string Dose, string Route, string Frequency,
    int? DurationDays, string? Instructions, DateTime? StartAt);

public record SavePrescriptionRequest(string? GeneralInstructions, IReadOnlyList<PrescriptionItemInput> Items);

public record PrescriptionItemDto(int Id, string DrugName, string? Form, string? Strength, string Dose, string Route, string Frequency,
    DateTime StartAt, int? DurationDays, string? Instructions, bool IsActive, DateTime? StoppedAt, string Summary);

public record FileDto(int Id, string Kind, string? Caption, string ContentType, long SizeBytes, DateTime UploadedAt, string? UploadedByName,
    int? AdmissionId, int? PrescriptionId, string? Url, DateTime? UrlExpiresAt);

public record PrescriptionDto(int Id, int AdmissionId, int ConsultantUserId, string ConsultantName, string Source, string Status,
    DateTime CreatedAt, DateTime? SignedAt, string? GeneralInstructions, IReadOnlyList<PrescriptionItemDto> Items, IReadOnlyList<FileDto> Files,
    int DoseTasksCreated);

public record DoseTaskDto(int Id, int AdmissionId, int PrescriptionItemId, string DrugName, string? Form, string? Strength, string Dose, string Route,
    string Frequency, string? Instructions, DateTime DueAt, string Status, DateTime? DoneAt, string? DoneByName, string? Reason,
    bool IsOverdue, int MinutesFromDue);

public record RoundPatientDto(int AdmissionId, string PatientName, string? BedNumber, string? WardName, string Flag, IReadOnlyList<DoseTaskDto> Doses);

public record RecordDoseRequest(string WristbandToken, string Status, string? Reason);

public record RecordPrnRequest(string WristbandToken, string? Reason);

public record DoseScheduleDto(int AdmissionId, DateOnly Date, IReadOnlyList<DoseTaskDto> Doses, IReadOnlyList<PrescriptionItemDto> PrnItems);
