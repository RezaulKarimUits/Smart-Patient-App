using InPatient.Api.Domain;
using InPatient.Api.Services.Rules;

namespace InPatient.Tests;

public class MonitoringRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);
    private static LabPoint Lab(string test, decimal v, double hoursAgo) => new(Now.AddHours(-hoursAgo), test, v, null);
    private static VitalPoint Vital(int score, double hoursAgo, string band = "Low") => new(Now.AddHours(-hoursAgo), score, band, null);

    [Fact]
    public void Haemoglobin_drop_of_two_within_48h_is_trend_alert()
    {
        var hits = MonitoringRules.Evaluate([], [Lab("Haemoglobin", 11.2m, 40), Lab("Haemoglobin", 9.8m, 20), Lab("Haemoglobin", 8.1m, 2)]);
        var hit = Assert.Single(hits, h => h.RuleCode == "TREND_HB_DROP");
        Assert.Equal(AlertTypes.Trend, hit.AlertType);
        Assert.Contains("11.2", hit.Detail);
        Assert.Contains("8.1", hit.Detail);
    }

    [Fact]
    public void Haemoglobin_drop_outside_48h_is_ignored()
        => Assert.DoesNotContain(MonitoringRules.Evaluate([], [Lab("Haemoglobin", 11.2m, 60), Lab("Haemoglobin", 9.0m, 1)]), h => h.RuleCode == "TREND_HB_DROP");

    [Fact]
    public void Small_haemoglobin_drop_is_ignored()
        => Assert.Empty(MonitoringRules.Evaluate([], [Lab("Haemoglobin", 11.2m, 20), Lab("Haemoglobin", 9.3m, 1)]));

    [Fact]
    public void Platelet_fall_over_half_is_trend_alert()
    {
        var hits = MonitoringRules.Evaluate([], [Lab("Platelets", 110000, 44), Lab("Platelets", 65000, 22), Lab("Platelets", 28000, 3)]);
        Assert.Contains(hits, h => h.RuleCode == "TREND_PLT_DROP");
        Assert.DoesNotContain(hits, h => h.AlertType == AlertTypes.CriticalLab); // 28k is not < 20k
    }

    [Fact]
    public void Platelet_fall_of_exactly_half_is_not_alerted()
        => Assert.Empty(MonitoringRules.Evaluate([], [Lab("Platelets", 100000, 20), Lab("Platelets", 50000, 1)]));

    [Fact]
    public void Creatinine_rise_of_point_three_within_48h()
    {
        Assert.Contains(MonitoringRules.Evaluate([], [Lab("Creatinine", 1.0m, 46), Lab("Creatinine", 1.4m, 24), Lab("Creatinine", 1.9m, 4)]), h => h.RuleCode == "TREND_CREAT_RISE");
        Assert.Contains(MonitoringRules.Evaluate([], [Lab("Creatinine", 1.0m, 10), Lab("Creatinine", 1.3m, 1)]), h => h.RuleCode == "TREND_CREAT_RISE");
        Assert.Empty(MonitoringRules.Evaluate([], [Lab("Creatinine", 1.0m, 10), Lab("Creatinine", 1.2m, 1)]));
    }

    [Fact]
    public void News2_rise_of_three_within_24h()
    {
        var hits = MonitoringRules.Evaluate([Vital(2, 20), Vital(4, 10), Vital(6, 1, "Medium")], []);
        Assert.Contains(hits, h => h.RuleCode == "TREND_NEWS2_RISE");
        Assert.Contains(hits, h => h.AlertType == AlertTypes.News2 && h.Severity == AlertSeverity.Warning);
    }

    [Fact]
    public void News2_rise_older_than_24h_is_ignored()
        => Assert.DoesNotContain(MonitoringRules.Evaluate([Vital(1, 30), Vital(4, 1)], []), h => h.RuleCode == "TREND_NEWS2_RISE");

    [Theory]
    [InlineData(4, "Low", null)]
    [InlineData(3, "LowMedium", "Warning")]
    [InlineData(5, "Medium", "Warning")]
    [InlineData(6, "Medium", "Warning")]
    [InlineData(7, "High", "Critical")]
    [InlineData(9, "High", "Critical")]
    public void News2_thresholds(int score, string band, string? severity)
    {
        var hit = MonitoringRules.Evaluate([Vital(score, 0.5, band)], []).SingleOrDefault(h => h.AlertType == AlertTypes.News2);
        Assert.Equal(severity, hit?.Severity);
    }

    [Theory]
    [InlineData("Potassium", "2.4", true)] [InlineData("Potassium", "2.5", false)] [InlineData("Potassium", "6.0", false)] [InlineData("Potassium", "6.1", true)]
    [InlineData("Sodium", "119", true)] [InlineData("Sodium", "120", false)] [InlineData("Sodium", "161", true)]
    [InlineData("Haemoglobin", "6.9", true)] [InlineData("Haemoglobin", "7.0", false)]
    [InlineData("Platelets", "19999", true)] [InlineData("Platelets", "20000", false)]
    [InlineData("Glucose", "2.9", true)] [InlineData("Glucose", "3.0", false)] [InlineData("Glucose", "25.1", true)]
    public void Critical_lab_thresholds(string test, string value, bool critical)
    {
        var hits = MonitoringRules.Evaluate([], [Lab(test, decimal.Parse(value), 1)]);
        Assert.Equal(critical, hits.Any(h => h.AlertType == AlertTypes.CriticalLab && h.Severity == AlertSeverity.Critical));
    }

    [Fact]
    public void Stable_patient_raises_nothing()
        => Assert.Empty(MonitoringRules.Evaluate([Vital(1, 12), Vital(0, 6), Vital(1, 1)], [Lab("Haemoglobin", 13m, 30), Lab("Haemoglobin", 12.6m, 2), Lab("Creatinine", 0.9m, 30), Lab("Creatinine", 0.9m, 2)]));

    [Fact]
    public void Lab_names_and_units_are_normalised()
    {
        Assert.Equal(("Platelets", 65000m), N("platelet count", 65, "x10^3/uL"));
        Assert.Equal(("Haemoglobin", 9.8m), N("Hb", 98, "g/L"));
        Assert.Equal(("Creatinine", 1.5m), N("S. Creatinine", 133, "umol/L"));
        Assert.Equal(("Glucose", 10m), N("RBS", 180, "mg/dL"));
        static (string, decimal) N(string n, decimal v, string u) { var r = LabCatalog.Normalize(n, v, u); return (r.TestName, r.Value!.Value); }
    }
}
