using InPatient.Api.Infrastructure;
using InPatient.Api.Services;

namespace InPatient.Api.Jobs;

/// <summary>Runs a unit of work periodically in a fresh DI scope as the "system" user (bypasses tenant filters).</summary>
public abstract class PeriodicJob(IServiceScopeFactory scopes, IConfiguration config, ILogger logger) : BackgroundService
{
    protected abstract Task RunOnceAsync(IServiceProvider sp, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = Math.Max(5, config.GetValue("Jobs:IntervalSeconds", 30));
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // let startup seeding finish
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                scope.ServiceProvider.GetRequiredService<CurrentUser>().RunAsSystem();
                await RunOnceAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "{Job} failed", GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>Scheduled → Due 15 min before the dose; pushes reminders to the bed's nurses.</summary>
public sealed class DoseReminderJob(IServiceScopeFactory s, IConfiguration c, ILogger<DoseReminderJob> l) : PeriodicJob(s, c, l)
{
    protected override Task RunOnceAsync(IServiceProvider sp, CancellationToken ct) => sp.GetRequiredService<MedicationService>().ProcessRemindersAsync(ct);
}

/// <summary>Unrecorded 30 min after due → Missed + alert to the ward's nurses and the consultant.</summary>
public sealed class MissedDoseJob(IServiceScopeFactory s, IConfiguration c, ILogger<MissedDoseJob> l) : PeriodicJob(s, c, l)
{
    protected override Task RunOnceAsync(IServiceProvider sp, CancellationToken ct) => sp.GetRequiredService<MedicationService>().ProcessMissedAsync(ct);
}

/// <summary>Unacknowledged alerts: level 0 → 1 (consultant) after 10 min Critical / 30 min Warning → 2 (duty doctor placeholder).</summary>
public sealed class AlertEscalationJob(IServiceScopeFactory s, IConfiguration c, ILogger<AlertEscalationJob> l) : PeriodicJob(s, c, l)
{
    protected override Task RunOnceAsync(IServiceProvider sp, CancellationToken ct) => sp.GetRequiredService<AlertService>().EscalateDueAsync(ct);
}
