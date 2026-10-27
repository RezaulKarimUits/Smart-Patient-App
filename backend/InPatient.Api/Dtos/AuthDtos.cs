namespace InPatient.Api.Dtos;

public record LoginRequest(string Phone, string Password, string? HospitalCode);

public record UserDto(int Id, string Name, string Phone, string Role, string? Designation, int? HospitalId, string? HospitalName, string? HospitalCode, int? PatientId);

public record LoginResponse(string Token, DateTime ExpiresAt, UserDto User);
