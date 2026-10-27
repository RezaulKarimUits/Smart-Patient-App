using InPatient.Api.Data;
using InPatient.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Hubs;

public static class HubGroups
{
    public static string Hospital(int id) => $"hospital:{id}";
    public static string Ward(int id) => $"ward:{id}";
    public static string User(int id) => $"user:{id}";
}

/// <summary>Realtime channel at /hubs/ward. Clients join hospital + user groups automatically and wards on request.</summary>
[Authorize]
public class WardHub(InPatientDbContext db) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (int.TryParse(user?.FindFirst(AppClaims.HospitalId)?.Value, out var hid))
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Hospital(hid));
        if (int.TryParse(user?.FindFirst(AppClaims.Sub)?.Value, out var uid))
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.User(uid));
        await base.OnConnectedAsync();
    }

    public async Task JoinWard(int wardId)
    {
        // Tenant filter applies (scoped ICurrentUser reads the hub's HttpContext user).
        if (!await db.Wards.AnyAsync(w => w.Id == wardId)) throw new HubException("Ward not found");
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Ward(wardId));
    }

    public Task LeaveWard(int wardId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Ward(wardId));
}

/// <summary>Event names pushed to clients.</summary>
public static class HubEvents
{
    public const string BedChanged = "bedChanged";
    public const string AlertRaised = "alertRaised";
    public const string AlertUpdated = "alertUpdated";
    public const string DoseDue = "doseDue";
    public const string DoseMissed = "doseMissed";
    public const string TasksChanged = "tasksChanged";
    public const string AdmissionUpdated = "admissionUpdated";
}

public interface INotifier
{
    Task ToHospitalAsync(int hospitalId, string evt, object payload);
    Task ToWardAsync(int wardId, string evt, object payload);
    Task ToUsersAsync(IEnumerable<int> userIds, string evt, object payload);
}

public sealed class SignalRNotifier(IHubContext<WardHub> hub, ILogger<SignalRNotifier> log) : INotifier
{
    public Task ToHospitalAsync(int hospitalId, string evt, object payload) => Send(HubGroups.Hospital(hospitalId), evt, payload);
    public Task ToWardAsync(int wardId, string evt, object payload) => Send(HubGroups.Ward(wardId), evt, payload);

    public async Task ToUsersAsync(IEnumerable<int> userIds, string evt, object payload)
    {
        var groups = userIds.Distinct().Select(HubGroups.User).ToList();
        if (groups.Count == 0) return;
        try { await hub.Clients.Groups(groups).SendAsync(evt, payload); }
        catch (Exception ex) { log.LogWarning(ex, "SignalR send {Event} failed", evt); }
    }

    private async Task Send(string group, string evt, object payload)
    {
        try { await hub.Clients.Group(group).SendAsync(evt, payload); }
        catch (Exception ex) { log.LogWarning(ex, "SignalR send {Event} to {Group} failed", evt, group); }
    }
}
