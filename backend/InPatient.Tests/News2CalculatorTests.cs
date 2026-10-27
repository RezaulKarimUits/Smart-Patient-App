using InPatient.Api.Domain;
using InPatient.Api.Services.Rules;

namespace InPatient.Tests;

public class News2CalculatorTests
{
    // A fully normal observation set: every parameter scores 0.
    private static News2Input Normal(int rr = 16, int spo2 = 97, bool o2 = false, int sbp = 120, int pulse = 70, string c = "Alert", decimal temp = 37.0m)
        => new(rr, spo2, o2, sbp, pulse, c, temp);

    [Theory]
    [InlineData(8, 3)] [InlineData(9, 1)] [InlineData(11, 1)] [InlineData(12, 0)] [InlineData(20, 0)]
    [InlineData(21, 2)] [InlineData(24, 2)] [InlineData(25, 3)]
    public void Respiration_rate_edges(int rr, int points) => Assert.Equal(points, News2Calculator.RespRate(rr));

    [Theory]
    [InlineData(91, 3)] [InlineData(92, 2)] [InlineData(93, 2)] [InlineData(94, 1)] [InlineData(95, 1)] [InlineData(96, 0)] [InlineData(100, 0)]
    public void SpO2_scale1_edges(int spo2, int points) => Assert.Equal(points, News2Calculator.SpO2(spo2));

    [Theory]
    [InlineData(90, 3)] [InlineData(91, 2)] [InlineData(100, 2)] [InlineData(101, 1)] [InlineData(110, 1)] [InlineData(111, 0)] [InlineData(219, 0)] [InlineData(220, 3)]
    public void Systolic_edges(int sbp, int points) => Assert.Equal(points, News2Calculator.Systolic(sbp));

    [Theory]
    [InlineData(40, 3)] [InlineData(41, 1)] [InlineData(50, 1)] [InlineData(51, 0)] [InlineData(90, 0)] [InlineData(91, 1)] [InlineData(110, 1)]
    [InlineData(111, 2)] [InlineData(130, 2)] [InlineData(131, 3)]
    public void Pulse_edges(int pulse, int points) => Assert.Equal(points, News2Calculator.Pulse(pulse));

    [Theory]
    [InlineData("35.0", 3)] [InlineData("35.1", 1)] [InlineData("36.0", 1)] [InlineData("36.1", 0)] [InlineData("38.0", 0)]
    [InlineData("38.1", 1)] [InlineData("39.0", 1)] [InlineData("39.1", 2)]
    public void Temperature_edges(string t, int points) => Assert.Equal(points, News2Calculator.Temperature(decimal.Parse(t)));

    [Fact]
    public void Oxygen_adds_two_and_reduced_consciousness_adds_three()
    {
        Assert.Equal(2, News2Calculator.Calculate(Normal(o2: true))!.Score);
        foreach (var c in new[] { "Confusion", "Voice", "Pain", "Unresponsive" })
            Assert.Equal(3, News2Calculator.Calculate(Normal(c: c))!.Score);
    }

    [Fact]
    public void All_normal_is_zero_low()
    {
        var r = News2Calculator.Calculate(Normal())!;
        Assert.Equal(0, r.Score);
        Assert.Equal(News2Bands.Low, r.Band);
    }

    [Fact]
    public void Band_low_up_to_four()
    {
        // RR 21 (2) + SpO2 95 (1) + pulse 95 (1) = 4
        var r = News2Calculator.Calculate(Normal(rr: 21, spo2: 95, pulse: 95))!;
        Assert.Equal(4, r.Score);
        Assert.Equal(News2Bands.Low, r.Band);
    }

    [Fact]
    public void Single_three_with_low_total_is_low_medium()
    {
        var r = News2Calculator.Calculate(Normal(rr: 26))!; // RR ≥25 scores 3, total 3
        Assert.Equal(3, r.Score);
        Assert.True(r.AnySingleThree);
        Assert.Equal(News2Bands.LowMedium, r.Band);
    }

    [Theory]
    [InlineData(5)] [InlineData(6)]
    public void Five_or_six_is_medium(int target)
    {
        // RR 22 (2) + SpO2 93 (2) + SBP 105 (1) = 5 ; + pulse 95 (1) = 6
        var r = News2Calculator.Calculate(Normal(rr: 22, spo2: 93, sbp: 105, pulse: target == 6 ? 95 : 70))!;
        Assert.Equal(target, r.Score);
        Assert.Equal(News2Bands.Medium, r.Band);
    }

    [Fact]
    public void Seven_or_more_is_high()
    {
        var r = News2Calculator.Calculate(Normal(rr: 22, spo2: 93, sbp: 100, pulse: 70))!; // 2 + 2 + 2 = 6
        Assert.Equal(News2Bands.Medium, r.Band);
        r = News2Calculator.Calculate(Normal(rr: 22, spo2: 93, sbp: 100, pulse: 95))!;     // + 1 = 7
        Assert.Equal(7, r.Score);
        Assert.Equal(News2Bands.High, r.Band);
    }

    [Fact]
    public void Demo_script_vitals_score_nine_high()
    {
        // BP 100/60, pulse 118, temp 102 °F (38.9 °C), SpO2 93, RR 24, on air, alert
        var r = News2Calculator.Calculate(new News2Input(24, 93, false, 100, 118, "Alert", 38.9m))!;
        Assert.Equal(9, r.Score);
        Assert.Equal(News2Bands.High, r.Band);
    }

    [Fact]
    public void Incomplete_set_is_not_scored()
    {
        Assert.Null(News2Calculator.Calculate(new News2Input(null, 97, false, 120, 70, "Alert", 37m)));
        Assert.Null(News2Calculator.Calculate(new News2Input(16, 97, false, 120, 70, "Alert", null)));
    }

    [Theory]
    [InlineData(15, false)] [InlineData(16, true)] [InlineData(70, true)]
    public void Not_used_under_16(int age, bool applies) => Assert.Equal(applies, News2Calculator.AppliesToAge(age));
}
