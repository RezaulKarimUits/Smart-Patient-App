using InPatient.Api.Services;
using InPatient.Api.Domain;

namespace InPatient.Api.Services.Rules;

public record VitalPoint(DateTime At, int? Score, string? Band, News2Result? Detail);

public record LabPoint(DateTime At, string TestName, decimal Value, string? Unit);

/// <summary>
/// Coded monitoring rules (the AI only explains their results):
/// - NEWS2 5–6 or any single 3 → Warning; ≥7 → Critical.
/// - Critical labs (see LabCatalog.IsCritical).
/// - Trends: Hb ↓ ≥2 g/dL within 48 h; platelets ↓ &gt;50% within 48 h; creatinine ↑ ≥0.3 mg/dL within 48 h; NEWS2 ↑ ≥3 within 24 h.
/// </summary>
public static class MonitoringRules
{
    public static readonly TimeSpan LabWindow = TimeSpan.FromHours(48);
    public static readonly TimeSpan News2Window = TimeSpan.FromHours(24);

    public static List<RuleHit> Evaluate(IReadOnlyList<VitalPoint> vitals, IReadOnlyList<LabPoint> labs)
    {
        var hits = new List<RuleHit>();
        hits.AddRange(EvaluateNews2(vitals));
        hits.AddRange(EvaluateLabs(labs));
        return hits;
    }

    public static IEnumerable<RuleHit> EvaluateNews2(IReadOnlyList<VitalPoint> vitals)
    {
        var scored = vitals.Where(v => v.Score is not null).OrderBy(v => v.At).ToList();
        if (scored.Count == 0) yield break;
        var latest = scored[^1];
        var score = latest.Score!.Value;
        var desc = latest.Detail is null ? $"NEWS2 {score}" : News2Calculator.Describe(latest.Detail);
        var at = $" at {Dhaka.FormatTime(latest.At)}";

        if (score >= 7)
            yield return new RuleHit(AlertTypes.News2, "NEWS2_HIGH", AlertSeverity.Critical, desc + at + ". Urgent clinical review required.", latest.At);
        else if (score >= 5)
            yield return new RuleHit(AlertTypes.News2, "NEWS2_MEDIUM", AlertSeverity.Warning, desc + at + ". Urgent ward-based review.", latest.At);
        else if (latest.Band == News2Bands.LowMedium)
            yield return new RuleHit(AlertTypes.News2, "NEWS2_SINGLE3", AlertSeverity.Warning, desc + at + ". A single parameter scores 3.", latest.At);

        var window = scored.Where(v => v.At >= latest.At - News2Window && v.At < latest.At).ToList();
        if (window.Count > 0)
        {
            var low = window.MinBy(v => v.Score)!;
            if (score - low.Score!.Value >= 3)
                yield return new RuleHit(AlertTypes.Trend, "TREND_NEWS2_RISE", AlertSeverity.Warning,
                    $"NEWS2 rose from {low.Score} to {score} within {Hours(latest.At - low.At)} (≥3 in 24 h).", latest.At);
        }
    }

    public static IEnumerable<RuleHit> EvaluateLabs(IReadOnlyList<LabPoint> labs)
    {
        foreach (var g in labs.GroupBy(l => l.TestName))
        {
            var series = g.OrderBy(l => l.At).ToList();
            var latest = series[^1];
            var unit = latest.Unit ?? LabCatalog.Find(latest.TestName)?.Unit ?? "";
            var fmt = (decimal v) => LabCatalog.Format(latest.TestName, v);

            if (LabCatalog.IsCritical(latest.TestName, latest.Value))
                yield return new RuleHit(AlertTypes.CriticalLab, "CRIT_" + Code(latest.TestName), AlertSeverity.Critical,
                    $"Critical {latest.TestName} {fmt(latest.Value)} {unit} at {Dhaka.FormatDateTime(latest.At)}.", latest.At);

            var prior = series.Where(l => l.At >= latest.At - LabWindow && l.At < latest.At).ToList();
            if (prior.Count == 0) continue;

            switch (latest.TestName)
            {
                case LabCatalog.Haemoglobin:
                {
                    var high = prior.MaxBy(l => l.Value)!;
                    var drop = high.Value - latest.Value;
                    if (drop >= 2.0m)
                        yield return new RuleHit(AlertTypes.Trend, "TREND_HB_DROP", AlertSeverity.Warning,
                            $"Haemoglobin fell {Series(series, latest, fmt)} g/dL (−{drop:0.0} g/dL in {Hours(latest.At - high.At)}).", latest.At);
                    break;
                }
                case LabCatalog.Platelets:
                {
                    var high = prior.MaxBy(l => l.Value)!;
                    if (high.Value > 0 && latest.Value < high.Value * 0.5m)
                    {
                        var pct = (int)Math.Round((1 - latest.Value / high.Value) * 100);
                        yield return new RuleHit(AlertTypes.Trend, "TREND_PLT_DROP", AlertSeverity.Warning,
                            $"Platelets fell {Series(series, latest, fmt)} /cumm (−{pct}% in {Hours(latest.At - high.At)}).", latest.At);
                    }
                    break;
                }
                case LabCatalog.Creatinine:
                {
                    var low = prior.MinBy(l => l.Value)!;
                    var rise = latest.Value - low.Value;
                    if (rise >= 0.3m)
                        yield return new RuleHit(AlertTypes.Trend, "TREND_CREAT_RISE", AlertSeverity.Warning,
                            $"Creatinine rose {Series(series, latest, fmt)} mg/dL (+{rise:0.0#} mg/dL in {Hours(latest.At - low.At)}).", latest.At);
                    break;
                }
            }
        }
    }

    private static string Series(List<LabPoint> series, LabPoint latest, Func<decimal, string> fmt)
        => string.Join(" → ", series.Where(l => l.At >= latest.At - LabWindow).Select(l => fmt(l.Value)));

    private static string Hours(TimeSpan t) => t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min" : $"{Math.Round(t.TotalHours)} h";

    private static string Code(string testName) => testName switch
    {
        LabCatalog.Potassium => "K",
        LabCatalog.Sodium => "NA",
        LabCatalog.Haemoglobin => "HB",
        LabCatalog.Platelets => "PLT",
        LabCatalog.Glucose => "GLU",
        _ => testName.ToUpperInvariant()
    };
}
