using InPatient.Api.Domain;

namespace InPatient.Api.Services.Rules;

public record Issue(string Level, string Field, string Message)
{
    public const string Warning = "warning", Blocker = "blocker";
    public static Issue Warn(string field, string msg) => new(Warning, field, msg);
    public static Issue Block(string field, string msg) => new(Blocker, field, msg);
}

/// <summary>Ranges and unit rules shared by form entry, voice drafts and report extraction.</summary>
public static class ClinicalValidation
{
    public static readonly (string Field, int Min, int Max)[] VitalRanges =
    [
        ("systolic", 50, 260), ("diastolic", 20, 160), ("pulse", 20, 220), ("respRate", 4, 60), ("spo2", 50, 100)
    ];

    /// <summary>
    /// Temperature to °C. Explicit unit wins; otherwise 93–108 is °F and 34–42 is °C. Anything else is a blocker.
    /// </summary>
    public static (decimal? Celsius, Issue? Issue) NormalizeTemperature(decimal? value, string? unit)
    {
        if (value is null) return (null, null);
        var v = value.Value;
        var u = unit?.Trim().ToUpperInvariant().Replace("°", "");
        decimal c;
        if (u is "F" or "FAHRENHEIT") c = (v - 32m) * 5m / 9m;
        else if (u is "C" or "CELSIUS") c = v;
        else if (v is >= 93m and <= 108m) c = (v - 32m) * 5m / 9m;
        else if (v is >= 34m and <= 42m) c = v;
        else return (null, Issue.Block("temperature", $"Temperature {v} is outside 34–42 °C / 93–108 °F — check the value"));

        c = Math.Round(c, 1);
        if (c is < 30m or > 44m) return (null, Issue.Block("temperature", $"Temperature {c} °C is not plausible"));
        return (c, null);
    }

    public static List<Issue> CheckVitals(int? systolic, int? diastolic, int? pulse, int? respRate, int? spo2, string? consciousness)
    {
        var issues = new List<Issue>();
        var values = new Dictionary<string, int?> { ["systolic"] = systolic, ["diastolic"] = diastolic, ["pulse"] = pulse, ["respRate"] = respRate, ["spo2"] = spo2 };
        foreach (var (field, min, max) in VitalRanges)
        {
            var v = values[field];
            if (v is not null && (v < min || v > max)) issues.Add(Issue.Block(field, $"{Label(field)} {v} is outside {min}–{max}"));
        }
        if (systolic is not null && diastolic is not null && diastolic >= systolic)
            issues.Add(Issue.Block("diastolic", $"Diastolic {diastolic} must be lower than systolic {systolic}"));
        if (consciousness is not null && !Consciousness.All.Contains(consciousness))
            issues.Add(Issue.Block("consciousness", "Consciousness must be Alert, Confusion, Voice, Pain or Unresponsive"));
        if (spo2 is < 90 and >= 50) issues.Add(Issue.Warn("spo2", $"SpO2 {spo2}% is low — confirm the reading"));
        if (systolic is < 90 and >= 50) issues.Add(Issue.Warn("systolic", $"Systolic {systolic} is low — confirm the reading"));
        if (pulse is > 130 and <= 220) issues.Add(Issue.Warn("pulse", $"Pulse {pulse} is high — confirm the reading"));
        return issues;
    }

    public static Issue? CheckLab(string field, string testName, decimal? value)
    {
        if (value is null) return null;
        var range = LabCatalog.Plausible(testName);
        if (range is { } r && (value < r.Min || value > r.Max))
            return Issue.Block(field, $"{testName} {value} is not plausible ({r.Min}–{r.Max})");
        return null;
    }

    public static string Label(string field) => field switch
    {
        "systolic" => "Systolic BP", "diastolic" => "Diastolic BP", "pulse" => "Pulse", "respRate" => "Respiration rate", "spo2" => "SpO2",
        _ => field
    };
}
