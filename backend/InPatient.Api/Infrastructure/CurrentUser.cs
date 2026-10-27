using System.Security.Claims;
using InPatient.Api.Domain;

namespace InPatient.Api.Infrastructure;

public static class AppClaims
{
    public const string Sub = "sub";
    public const string Role = "role";
    public const string HospitalId = "hospital_id";
    public const string PatientId = "patient_id";
    public const string Name = "name";
}

/// <summary>Who is calling. Drives EF global query filters (tenant isolation) and audit fields.</summary>
public interface ICurrentUser
{
    int? UserId { get; }
    int? HospitalId { get; }
    string? Role { get; }
    int? PatientId { get; }
    bool IsSuperAdmin { get; }
    /// <summary>Background jobs / dev seeding run as "system" and see every hospital.</summary>
    bool IsSystem { get; }
    bool BypassTenantFilter { get; }
    int RequireUserId();
    int RequireHospitalId();
}

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private bool _system;
    private int? _overrideHospital;
    private int? _overrideUser;

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
    private string? Claim(string type) => Principal?.FindFirst(type)?.Value;

    public int? UserId => _overrideUser ?? (int.TryParse(Claim(AppClaims.Sub), out var v) ? v : null);
    public int? HospitalId => _overrideHospital ?? (int.TryParse(Claim(AppClaims.HospitalId), out var v) ? v : null);
    public string? Role => _system ? "System" : Claim(AppClaims.Role);
    public int? PatientId => int.TryParse(Claim(AppClaims.PatientId), out var v) ? v : null;
    public bool IsSuperAdmin => Role == Roles.SuperAdmin;
    public bool IsSystem => _system;
    public bool BypassTenantFilter => _system || IsSuperAdmin;

    public int RequireUserId() => UserId ?? throw new ApiException(401, "Not signed in");
    public int RequireHospitalId() => HospitalId ?? throw ApiException.BadRequest("This action needs a hospital context");

    /// <summary>Use inside background scopes: bypasses tenant filters. Optionally pins a hospital for inserts.</summary>
    public void RunAsSystem(int? hospitalId = null, int? userId = null)
    {
        _system = true;
        _overrideHospital = hospitalId;
        _overrideUser = userId;
    }
}

/// <summary>Fixed identity, used in tests and tooling.</summary>
public sealed class FixedCurrentUser(int? userId, int? hospitalId, string role, int? patientId = null) : ICurrentUser
{
    public int? UserId { get; } = userId;
    public int? HospitalId { get; } = hospitalId;
    public string? Role { get; } = role;
    public int? PatientId { get; } = patientId;
    public bool IsSuperAdmin => Role == Roles.SuperAdmin;
    public bool IsSystem => Role == "System";
    public bool BypassTenantFilter => IsSystem || IsSuperAdmin;
    public int RequireUserId() => UserId ?? throw new ApiException(401, "Not signed in");
    public int RequireHospitalId() => HospitalId ?? throw ApiException.BadRequest("This action needs a hospital context");
}
