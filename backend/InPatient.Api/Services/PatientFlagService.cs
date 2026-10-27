using InPatient.Api.Data;
using InPatient.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Services;

public record PatientStatusInfo(int AdmissionId, int? News2Score, string? News2Band, DateTime? News2At, string Flag, int ActiveAlerts,
    string? TopSeverity, bool HasCriticalAlert, int TrendAlerts, DateTime? LatestAlertAt);

/// <summary>
/// Derives the Stable / Watch / Critical flag shown everywhere:
/// Critical = latest NEWS2 ≥ 7 or an active Critical alert; Watch = NEWS2 5–6 / any single 3 or an active Warning alert.
/// Missed-dose alerts don't change the clinical flag.
/// </summary>
public sealed class PatientFlagService(InPatientDbContext db)
{
    public static string ComputeFlag(int? news2Score, string? news2Band, IEnumerable<string> activeClinicalSeverities)
    {
        var sev = activeClinicalSeverities.ToList();
        if (news2Score >= 7 || sev.Contains(AlertSeverity.Critical)) return PatientFlag.Critical;
        if (news2Score >= 5 || news2Band == News2Bands.LowMedium || sev.Contains(AlertSeverity.Warning)) return PatientFlag.Watch;
        return PatientFlag.Stable;
    }

    public async Task<Dictionary<int, PatientStatusInfo>> GetAsync(IReadOnlyCollection<int> admissionIds, CancellationToken ct = default)
    {
        if (admissionIds.Count == 0) return new();
        var ids = admissionIds.Distinct().ToList();

        var latest = await db.VitalSigns
            .Where(v => ids.Contains(v.AdmissionId) && v.News2Score != null)
            .GroupBy(v => v.AdmissionId)
            .Select(g => g.OrderByDescending(v => v.RecordedAt).Select(v => new { v.AdmissionId, v.News2Score, v.News2Band, v.RecordedAt }).First())
            .ToListAsync(ct);
        var latestMap = latest.ToDictionary(x => x.AdmissionId);

        var alerts = await db.Alerts
            .Where(a => ids.Contains(a.AdmissionId) && AlertStatus.Active.Contains(a.Status))
            .Select(a => new { a.AdmissionId, a.Severity, a.AlertType, a.CreatedAt })
            .ToListAsync(ct);
        var alertMap = alerts.GroupBy(a => a.AdmissionId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new Dictionary<int, PatientStatusInfo>();
        foreach (var id in ids)
        {
            latestMap.TryGetValue(id, out var v);
            var list = alertMap.TryGetValue(id, out var l) ? l : [];
            var clinical = list.Where(a => a.AlertType != AlertTypes.MissedDose).Select(a => a.Severity).ToList();
            var top = list.OrderByDescending(a => AlertSeverity.Rank(a.Severity)).Select(a => a.Severity).FirstOrDefault();
            result[id] = new PatientStatusInfo(id, v?.News2Score, v?.News2Band, v?.RecordedAt,
                ComputeFlag(v?.News2Score, v?.News2Band, clinical), list.Count, top,
                list.Any(a => a.Severity == AlertSeverity.Critical),
                list.Count(a => a.AlertType == AlertTypes.Trend),
                list.Count == 0 ? null : list.Max(a => a.CreatedAt));
        }
        return result;
    }
}
