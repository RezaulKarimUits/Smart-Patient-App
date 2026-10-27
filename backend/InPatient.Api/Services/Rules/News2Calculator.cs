using InPatient.Api.Domain;

namespace InPatient.Api.Services.Rules;

public record News2Input(int? RespRate, int? SpO2, bool OnOxygen, int? Systolic, int? Pulse, string? Consciousness, decimal? TemperatureC);

public record News2Part(string Parameter, string Value, int Points);

public record News2Result(int Score, string Band, bool AnySingleThree, IReadOnlyList<News2Part> Parts);

/// <summary>
/// NEWS2 (Royal College of Physicians), SpO2 Scale 1. Pure and unit-tested; mirrored in 002_seed.sql.
/// Requires the full set (RR, SpO2, systolic, pulse, temperature); consciousness defaults to Alert and air to no oxygen.
/// Bands: 0–4 Low (any single 3 → Low-medium), 5–6 Medium, 7+ High. Not used under 16 years.
/// </summary>
public static class News2Calculator
{
    public const int MinimumAge = 16;

    public static bool AppliesToAge(int ageYears) => ageYears >= MinimumAge;

    public static News2Result? Calculate(News2Input v)
    {
        if (v.RespRate is null || v.SpO2 is null || v.Systolic is null || v.Pulse is null || v.TemperatureC is null) return null;

        var consciousness = string.IsNullOrWhiteSpace(v.Consciousness) ? Consciousness.Alert : v.Consciousness;
        var parts = new List<News2Part>
        {
            new("Respiration rate", $"{v.RespRate}/min", RespRate(v.RespRate.Value)),
            new("SpO2", $"{v.SpO2}%", SpO2(v.SpO2.Value)),
            new("Oxygen", v.OnOxygen ? "On oxygen" : "Air", v.OnOxygen ? 2 : 0),
            new("Systolic BP", $"{v.Systolic} mmHg", Systolic(v.Systolic.Value)),
            new("Pulse", $"{v.Pulse}/min", Pulse(v.Pulse.Value)),
            new("Consciousness", consciousness, consciousness == Consciousness.Alert ? 0 : 3),
            new("Temperature", $"{v.TemperatureC:0.0} °C", Temperature(v.TemperatureC.Value)),
        };
        var score = parts.Sum(p => p.Points);
        var anyThree = parts.Any(p => p.Points == 3);
        return new News2Result(score, Band(score, anyThree), anyThree, parts);
    }

    public static string Band(int score, bool anySingleThree) => score switch
    {
        >= 7 => News2Bands.High,
        >= 5 => News2Bands.Medium,
        _ when anySingleThree => News2Bands.LowMedium,
        _ => News2Bands.Low
    };

    public static int RespRate(int rr) => rr switch { <= 8 => 3, <= 11 => 1, <= 20 => 0, <= 24 => 2, _ => 3 };
    public static int SpO2(int s) => s switch { <= 91 => 3, <= 93 => 2, <= 95 => 1, _ => 0 };
    public static int Systolic(int sbp) => sbp switch { <= 90 => 3, <= 100 => 2, <= 110 => 1, <= 219 => 0, _ => 3 };
    public static int Pulse(int p) => p switch { <= 40 => 3, <= 50 => 1, <= 90 => 0, <= 110 => 1, <= 130 => 2, _ => 3 };
    public static int Temperature(decimal t) => t switch { <= 35.0m => 3, <= 36.0m => 1, <= 38.0m => 0, <= 39.0m => 1, _ => 2 };

    public static string Describe(News2Result r)
        => $"NEWS2 {r.Score} ({BandLabel(r.Band)})" + (r.Parts.Any(p => p.Points > 0)
            ? ": " + string.Join(", ", r.Parts.Where(p => p.Points > 0).Select(p => $"{p.Parameter} {p.Value} (+{p.Points})"))
            : "");

    public static string BandLabel(string band) => band switch
    {
        News2Bands.LowMedium => "Low-medium",
        _ => band
    };
}
