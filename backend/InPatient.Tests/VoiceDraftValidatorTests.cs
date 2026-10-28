using InPatient.Api.Services.Ai;
using InPatient.Api.Services.Rules;

namespace InPatient.Tests;

public class VoiceDraftValidatorTests
{
    private static readonly ActiveItem[] Items =
    [
        new(101, "Ceftriaxone", "Inj", "1 g", "1 g", "IV", "Q12H"),
        new(102, "Omeprazole", "Cap", "20 mg", "20 mg", "PO", "BD"),
        new(103, "Paracetamol", "Tab", "500 mg", "500 mg", "PO", "PRN"),
    ];

    private static NurseVitalsDraft Vitals(int? sys = null, int? dia = null, int? pulse = null, int? rr = null, decimal? temp = null, string? unit = null, int? spo2 = null)
        => new(sys, dia, pulse, rr, temp, unit, spo2, null, null);

    private static NurseDraft Draft(NurseVitalsDraft? v = null, NurseMedicationDraft[]? meds = null, NurseLabDraft[]? labs = null, string? note = null)
        => new(v ?? Vitals(), meds ?? [], labs ?? [], note, null);

    [Theory]
    [InlineData("102", null, "38.9")]
    [InlineData("102", "F", "38.9")]
    [InlineData("98.6", null, "37.0")]
    [InlineData("38.5", null, "38.5")]
    [InlineData("38.5", "C", "38.5")]
    public void Temperature_is_stored_in_celsius(string value, string? unit, string expected)
    {
        var r = VoiceDraftValidator.ValidateNurse(Draft(Vitals(temp: decimal.Parse(value), unit: unit)), Items);
        Assert.False(r.HasBlockers);
        Assert.Equal(decimal.Parse(expected), r.Draft.Vitals.Temperature);
        Assert.Equal("C", r.Draft.Vitals.TemperatureUnit);
    }

    [Theory]
    [InlineData("60")] [InlineData("120")] [InlineData("44.5")]
    public void Ambiguous_temperature_is_a_blocker(string value)
        => Assert.Contains(VoiceDraftValidator.ValidateNurse(Draft(Vitals(temp: decimal.Parse(value))), Items).Issues, i => i.Field == "temperature" && i.Level == Issue.Blocker);

    [Theory]
    [InlineData(300, 90, null, null, "systolic")]
    [InlineData(120, 130, null, null, "diastolic")]
    [InlineData(null, null, 250, null, "pulse")]
    [InlineData(null, null, null, 105, "spo2")]
    public void Out_of_range_vitals_are_blockers(int? sys, int? dia, int? pulse, int? spo2, string field)
    {
        var r = VoiceDraftValidator.ValidateNurse(Draft(Vitals(sys, dia, pulse, spo2: spo2)), Items);
        Assert.Contains(r.Issues, i => i.Field == field && i.Level == Issue.Blocker);
    }

    [Fact]
    public void Demo_entry_validates_cleanly_and_requires_wristband()
    {
        var r = VoiceDraftValidator.ValidateNurse(Draft(Vitals(100, 60, 118, 24, 102, "F", 93),
            [new NurseMedicationDraft(101, "Ceftriaxone", "1 gram", "IV", null, "given", null)]), Items);
        Assert.False(r.HasBlockers);
        Assert.True(r.RequiresWristband);
        Assert.Equal(38.9m, r.Draft.Vitals.Temperature);
    }

    [Fact]
    public void Item_id_from_another_admission_is_rejected()
    {
        var r = VoiceDraftValidator.ValidateNurse(Draft(meds: [new NurseMedicationDraft(999, "Meropenem", "1 g", "IV", null, "given", null)]), Items);
        Assert.True(r.HasBlockers);
        Assert.Null(r.Draft.Medications[0].PrescriptionItemId);
    }

    [Theory]
    [InlineData("seftriaxone", 101)]
    [InlineData("Inj. Ceftriaxon", 101)]
    [InlineData("omeprazol", 102)]
    [InlineData("Napa", 103)]
    public void Misspelled_drug_names_match_active_items(string spoken, int expectedId)
    {
        var r = VoiceDraftValidator.ValidateNurse(Draft(meds: [new NurseMedicationDraft(null, spoken, null, null, null, "given", null)]), Items);
        Assert.Equal(expectedId, r.Draft.Medications[0].PrescriptionItemId);
        Assert.False(r.HasBlockers);
    }

    [Fact]
    public void Unknown_drug_is_a_blocker()
        => Assert.True(VoiceDraftValidator.ValidateNurse(Draft(meds: [new NurseMedicationDraft(null, "Insulin", null, null, null, "given", null)]), Items).HasBlockers);

    [Theory]
    [InlineData("refused")] [InlineData("withheld")] [InlineData("delayed")]
    public void Not_given_needs_a_reason(string status)
    {
        var r = VoiceDraftValidator.ValidateNurse(Draft(meds: [new NurseMedicationDraft(102, "Omeprazole", null, null, null, status, null)]), Items);
        Assert.True(r.HasBlockers);
        r = VoiceDraftValidator.ValidateNurse(Draft(meds: [new NurseMedicationDraft(102, "Omeprazole", null, null, null, status, "vomiting")]), Items);
        Assert.False(r.HasBlockers);
    }

    [Fact]
    public void Lab_values_are_normalised_and_checked()
    {
        var r = VoiceDraftValidator.ValidateNurse(Draft(labs: [new NurseLabDraft("platelet", 65, null), new NurseLabDraft("Hb", 9.8m, "g/dL")]), Items);
        Assert.False(r.HasBlockers);
        Assert.Equal(65000m, r.Draft.LabResults[0].Value);
        Assert.Equal("Haemoglobin", r.Draft.LabResults[1].TestName);
        Assert.True(VoiceDraftValidator.ValidateNurse(Draft(labs: [new NurseLabDraft("Potassium", 45, null)]), Items).HasBlockers);
    }

    [Fact]
    public void Empty_draft_is_a_blocker() => Assert.True(VoiceDraftValidator.ValidateNurse(Draft(), Items).HasBlockers);

    [Fact]
    public void Prescription_without_dose_is_blocked_never_invented()
    {
        var r = VoiceDraftValidator.ValidatePrescription(new PrescriptionDraft([new PrescriptionItemDraft("Salbutamol", "Neb", null, null, "Neb", "Q6H", 5, null)], null, null));
        Assert.True(r.HasBlockers);
        Assert.Null(r.Draft.Items[0].Dose);
    }

    [Fact]
    public void Prescription_route_inferred_from_tablet_with_warning()
    {
        var r = VoiceDraftValidator.ValidatePrescription(new PrescriptionDraft([new PrescriptionItemDraft("Omeprazole", "Cap", "20 mg", "20 mg", null, "BD", 5, "before meal")], null, null));
        Assert.False(r.HasBlockers);
        Assert.Equal("PO", r.Draft.Items[0].Route);
        Assert.Contains(r.Issues, i => i.Level == Issue.Warning);
    }

    [Fact]
    public void Injection_without_route_is_blocked()
        => Assert.True(VoiceDraftValidator.ValidatePrescription(new PrescriptionDraft([new PrescriptionItemDraft("Ceftriaxone", "Inj", "1 g", "1 g", null, "Q12H", 5, null)], null, null)).HasBlockers);

    [Fact]
    public void Ten_tay_resolves_to_nearest_ten()
    {
        var now = InPatient.Api.Domain.Dhaka.ToUtc(new DateTime(2026, 10, 26, 21, 0, 0));
        var t = InPatient.Api.Services.VoiceService.ResolveTime("22:00", now);
        Assert.Equal(InPatient.Api.Domain.Dhaka.ToUtc(new DateTime(2026, 10, 26, 22, 0, 0)), t);
        var early = InPatient.Api.Services.VoiceService.ResolveTime("23:30", InPatient.Api.Domain.Dhaka.ToUtc(new DateTime(2026, 10, 27, 0, 30, 0)));
        Assert.Equal(InPatient.Api.Domain.Dhaka.ToUtc(new DateTime(2026, 10, 26, 23, 30, 0)), early);
    }
}
