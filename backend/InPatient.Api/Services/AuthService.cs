using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using InPatient.Api.Data;
using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace InPatient.Api.Services;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "InPatient";
    public string Audience { get; set; } = "InPatient";
    public string Key { get; set; } = "";
    public int ExpiryHours { get; set; } = 12;
}

public sealed class AuthService(InPatientDbContext db, Microsoft.Extensions.Options.IOptions<JwtOptions> jwt, IClock clock)
{
    public async Task<LoginResponse> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Phone) || string.IsNullOrWhiteSpace(req.Password))
            throw ApiException.BadRequest("Phone and password are required");

        // Anonymous endpoint: no tenant context yet, so look across hospitals explicitly.
        var phone = req.Phone.Trim();
        var q = db.AppUsers.IgnoreQueryFilters().Include(u => u.Hospital).Where(u => u.Phone == phone && u.IsActive);
        if (!string.IsNullOrWhiteSpace(req.HospitalCode))
        {
            var code = req.HospitalCode.Trim();
            q = q.Where(u => u.Hospital == null || u.Hospital.Code == code);
        }
        var candidates = await q.ToListAsync(ct);
        var matches = candidates.Where(u => BCrypt.Net.BCrypt.Verify(req.Password, u.PasswordHash)).ToList();
        if (matches.Count == 0) throw new ApiException(401, "Invalid phone or password");
        if (matches.Count > 1) throw ApiException.BadRequest("This phone is registered at more than one hospital - enter the hospital code");

        var user = matches[0];
        if (user.Hospital is { IsActive: false }) throw ApiException.Forbidden("Hospital is inactive");

        var expires = clock.UtcNow.AddHours(jwt.Value.ExpiryHours);
        return new LoginResponse(CreateToken(user.Id, user.Name, user.Role, user.HospitalId, user.PatientId, expires), expires,
            new UserDto(user.Id, user.Name, user.Phone, user.Role, user.Designation, user.HospitalId, user.Hospital?.Name, user.Hospital?.Code, user.PatientId));
    }

    public async Task<UserDto> MeAsync(int userId, CancellationToken ct)
    {
        var u = await db.AppUsers.Include(x => x.Hospital).FirstOrDefaultAsync(x => x.Id == userId, ct) ?? throw ApiException.NotFound("User not found");
        return new UserDto(u.Id, u.Name, u.Phone, u.Role, u.Designation, u.HospitalId, u.Hospital?.Name, u.Hospital?.Code, u.PatientId);
    }

    public string CreateToken(int userId, string name, string role, int? hospitalId, int? patientId, DateTime expiresUtc)
    {
        var claims = new List<Claim>
        {
            new(AppClaims.Sub, userId.ToString()),
            new(AppClaims.Role, role),
            new(AppClaims.Name, name),
        };
        if (hospitalId is not null) claims.Add(new Claim(AppClaims.HospitalId, hospitalId.Value.ToString()));
        if (patientId is not null) claims.Add(new Claim(AppClaims.PatientId, patientId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.Key));
        var token = new JwtSecurityToken(jwt.Value.Issuer, jwt.Value.Audience, claims,
            notBefore: clock.UtcNow.AddMinutes(-1), expires: expiresUtc,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
