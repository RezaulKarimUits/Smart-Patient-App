using InPatient.Api.Domain;

namespace InPatient.Api.Services.Rules;

public record LabDefinition(string Name, string Unit, decimal? RefLow, decimal? RefHigh, string[] Aliases);

public record NormalizedLab(string TestName, decimal? Value, string? TextValue, string? Unit, decimal? RefLow, decimal? RefHigh, string Flag, string? Note);

/// <summary>
/// Canonical lab names/units so trends compare like with like, plus coded critical thresholds:
/// K &lt;2.5 or &gt;6.0; Na &lt;120 or &gt;160; Hb &lt;7 g/dL; platelets &lt;20,000; glucose &lt;3.0 or &gt;25 mmol/L.
/// </summary>
public static class LabCatalog
{
    public const string Haemoglobin = "Haemoglobin", Platelets = "Platelets", Creatinine = "Creatinine", Potassium = "Potassium",
        Sodium = "Sodium", Glucose = "Glucose", Wbc = "WBC", Haematocrit = "Haematocrit", Crp = "CRP", Urea = "Urea";

    public static readonly LabDefinition[] Definitions =
    [
        new(Haemoglobin, "g/dL", 12.0m, 16.0m, ["hb", "hgb", "hemoglobin", "haemoglobin", "hemoglobin (hb)", "haemoglobin (hb)", "হিমোগ্লোবিন"]),
        new(Platelets, "/cumm", 150000m, 450000m, ["platelet", "platelets", "platelet count", "plt", "plt count", "প্লেটলেট"]),
        new(Wbc, "x10^9/L", 4.0m, 11.0m, ["wbc", "tc", "total count", "total wbc count", "white blood cells", "white cell count", "leucocyte count", "tlc"]),
        new(Haematocrit, "%", 36m, 46m, ["hct", "haematocrit", "hematocrit", "pcv"]),
        new(Creatinine, "mg/dL", 0.6m, 1.2m, ["creatinine", "s. creatinine", "s.creatinine", "serum creatinine", "creat", "cr", "ক্রিয়েটিনিন"]),
        new(Urea, "mg/dL", 15m, 40m, ["urea", "blood urea", "s. urea", "bun"]),
        new(Potassium, "mmol/L", 3.5m, 5.1m, ["potassium", "k", "k+", "s. potassium", "serum potassium"]),
        new(Sodium, "mmol/L", 135m, 145m, ["sodium", "na", "na+", "s. sodium", "serum sodium"]),
        new(Glucose, "mmol/L", 3.9m, 7.8m, ["glucose", "rbs", "random blood sugar", "blood sugar", "blood glucose", "fbs", "cbg", "sugar"]),
        new(Crp, "mg/L", 0m, 5m, ["crp", "c-reactive protein", "c reactive protein"]),
    ];

    public static LabDefinition? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = name.Trim().ToLowerInvariant();
        return Definitions.FirstOrDefault(d => d.Name.Equals(n, StringComparison.OrdinalIgnoreCase) || d.Aliases.Contains(n));
    }

    /// <summary>Canonical name, unit conversion to the canonical unit and flag. Unknown tests are kept as typed.</summary>
    public static NormalizedLab Normalize(string testName, decimal? value, string? unit, decimal? refLow = null, decimal? refHigh = null, string? textValue = null)
    {
        var def = Find(testName);
        if (def is null || value is null)
        {
            var name = def?.Name ?? testName.Trim();
            return new NormalizedLab(name, value, textValue, unit ?? def?.Unit, refLow ?? def?.RefLow, refHigh ?? def?.RefHigh,
                value is null ? LabFlags.Normal : Flag(name, value.Value, refLow ?? def?.RefLow, refHigh ?? def?.RefHigh), null);
        }

        var v = value.Value;
        var u = (unit ?? "").Trim().ToLowerInvariant();
        string? note = null;
        var refsFromReport = refLow is not null || refHigh is not null;
        switch (def.Name)
        {
            case Haemoglobin when u is "g/l" || v > 30:
                v /= 10; note = "converted g/L → g/dL"; refsFromReport = false; break;
            case Platelets when v < 10:
                v *= 100000; note = "converted lakh → /cumm"; refsFromReport = false; break;
            case Platelets when v < 2000:
                v *= 1000; note = "converted ×10³ → /cumm"; refsFromReport = false; break;
            case Creatinine when u.Contains("mol") || v > 20:
                v = Math.Round(v / 88.4m, 2); note = "converted µmol/L → mg/dL"; refsFromReport = false; break;
            case Glucose when u.Contains("mg") || v > 40:
                v = Math.Round(v / 18m, 1); note = "converted mg/dL → mmol/L"; refsFromReport = false; break;
            case Wbc when v > 1000:
                v = Math.Round(v / 1000m, 1); note = "converted /cumm → ×10⁹/L"; refsFromReport = false; break;
        }
        var low = refsFromReport ? refLow : def.RefLow;
        var high = refsFromReport ? refHigh : def.RefHigh;
        return new NormalizedLab(def.Name, v, textValue, def.Unit, low, high, Flag(def.Name, v, low, high), note);
    }

    public static bool IsCritical(string testName, decimal v) => testName switch
    {
        Potassium => v < 2.5m || v > 6.0m,
        Sodium => v < 120m || v > 160m,
        Haemoglobin => v < 7m,
        Platelets => v < 20000m,
        Glucose => v < 3.0m || v > 25m,
        _ => false
    };

    public static string Flag(string testName, decimal v, decimal? low, decimal? high)
    {
        if (IsCritical(testName, v)) return LabFlags.Critical;
        if (low is not null && v < low) return LabFlags.Low;
        if (high is not null && v > high) return LabFlags.High;
        return LabFlags.Normal;
    }

    /// <summary>Plausibility limits for typed/voice/photo values (blockers outside these).</summary>
    public static (decimal Min, decimal Max)? Plausible(string testName) => testName switch
    {
        Haemoglobin => (1m, 25m),
        Platelets => (1000m, 2000000m),
        Wbc => (0.1m, 300m),
        Haematocrit => (5m, 80m),
        Creatinine => (0.1m, 25m),
        Urea => (1m, 400m),
        Potassium => (1m, 10m),
        Sodium => (90m, 200m),
        Glucose => (0.5m, 60m),
        Crp => (0m, 600m),
        _ => null
    };

    public static string Format(string testName, decimal v) => testName == Platelets ? v.ToString("#,0") : v.ToString("0.##");
}
