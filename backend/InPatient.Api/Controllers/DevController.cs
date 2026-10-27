using InPatient.Api.Data;
using InPatient.Api.Domain;
using InPatient.Api.Infrastructure;
using InPatient.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Controllers;

/// <summary>Hides an endpoint (404) outside Development.</summary>
public sealed class DevelopmentOnlyAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var env = context.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        if (!env.IsDevelopment()) context.Result = new NotFoundObjectResult(new { error = "Not found" });
    }
    public void OnResourceExecuted(ResourceExecutedContext context) { }
}

public record AdvanceTimeRequest(int Minutes);

[ApiController]
[Route("api/dev")]
[DevelopmentOnly]
[Authorize]
public partial class DevController(IClock clock, IServiceScopeFactory scopes, InPatientDbContext db, ICurrentUser me) : ControllerBase
{
    [HttpGet("clock")]
    public object Clock() => new { utcNow = clock.UtcNow, dhaka = Dhaka.FormatDateTime(clock.UtcNow), offsetMinutes = clock.Offset.TotalMinutes };

    /// <summary>Moves the app clock forward (demo of reminders, missed doses and escalation), then runs the jobs once.</summary>
    [HttpPost("advance-time")]
    public async Task<object> AdvanceTime(AdvanceTimeRequest req, CancellationToken ct)
    {
        if (req.Minutes is < 1 or > 24 * 60) throw ApiException.BadRequest("Minutes must be 1-1440");
        clock.Advance(TimeSpan.FromMinutes(req.Minutes));
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<CurrentUser>().RunAsSystem();
        var meds = scope.ServiceProvider.GetRequiredService<MedicationService>();
        var reminded = await meds.ProcessRemindersAsync(ct);
        var missed = await meds.ProcessMissedAsync(ct);
        var escalated = await scope.ServiceProvider.GetRequiredService<AlertService>().EscalateDueAsync(ct);
        return new { utcNow = clock.UtcNow, dhaka = Dhaka.FormatDateTime(clock.UtcNow), offsetMinutes = clock.Offset.TotalMinutes, reminded, missed, escalated };
    }

    [HttpPost("reset-clock")]
    public object ResetClock() { clock.Reset(); return Clock(); }

    /// <summary>Bed and wristband tokens of my hospital, so the app's dev "simulate scan" works on an emulator without a camera.</summary>
    [HttpGet("scan-tokens")]
    public async Task<object> ScanTokens(CancellationToken ct)
    {
        me.RequireHospitalId();
        var beds = await db.Beds.OrderBy(b => b.WardId).ThenBy(b => b.BedNumber)
            .Select(b => new { kind = "bed", label = b.Ward.Code + " · " + b.BedNumber + " (" + b.Status + ")", token = b.QrToken }).ToListAsync(ct);
        var bands = await db.Admissions.Where(a => a.Status == AdmissionStatus.Active).OrderBy(a => a.CurrentBed!.BedNumber)
            .Select(a => new { kind = "wristband", label = (a.CurrentBed != null ? a.CurrentBed.BedNumber : "-") + " · " + a.Patient.Name, token = a.WristbandToken }).ToListAsync(ct);
        return new { beds, wristbands = bands };
    }
}
