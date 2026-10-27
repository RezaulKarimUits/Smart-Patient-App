using System.Text.Json;
using InPatient.Api.Domain;
using InPatient.Api.Infrastructure;
using InPatient.Api.Services.Ai;
using InPatient.Api.Services.Rules;

namespace InPatient.Api.Services;

public record EvalFieldResult(string Field, string Expected, string Actual, bool Correct);
public record EvalSampleResult(string Id, string Language, string Purpose, string Transcript, int Fields, int Correct, double Accuracy,
    IReadOnlyList<EvalFieldResult> Mismatches, string? Error);
public record VoiceEvalReport(DateTime RanAt, string Model, int Samples, int Fields, int Correct, double Accuracy,
    IReadOnlyDictionary<string, double> AccuracyByLanguage, IReadOnlyDictionary<string, double> AccuracyByPurpose, IReadOnlyList<EvalSampleResult> Results);

/// <summary>Runs the structuring step (same prompts + validator as production) on Data/voice-samples.json and scores field-level accuracy.</summary>
public sealed class VoiceEvalService(IStructuredAi ai, VoiceService voice, IClock clock, IWebHostEnvironment env, ILogger<VoiceEvalService> log)
{
    public async Task<VoiceEvalReport> RunAsync(CancellationToken ct)
    {
        if (!ai.IsConfigured) throw new AiNotConfiguredException();
        var path = Path.Combine(env.ContentRootPath, "Data", "voice-samples.json");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
        var ctxEl = doc.RootElement.GetProperty("context");
        var items = ctxEl.GetProperty("activeItems").Deserialize<List<ActiveItem>>(Json.Options)!;
        var local = Dhaka.ToLocal(clock.UtcNow).Date.Add(TimeOnly.Parse(ctxEl.GetProperty("time").GetString()!).ToTimeSpan());
        var prompt = VoiceService.BuildPrompt(Dhaka.ToUtc(local), ctxEl.GetProperty("age").GetInt32(), ctxEl.GetProperty("sex").GetString()!,
            ctxEl.GetProperty("bed").GetString(), ctxEl.GetProperty("ward").GetString(), items);
        var vctx = new VoiceService.VoiceContext(items, prompt);

        var results = new List<EvalSampleResult>();
        foreach (var s in doc.RootElement.GetProperty("samples").EnumerateArray())
        {
            var id = s.GetProperty("id").GetString()!;
            var lang = s.GetProperty("language").GetString()!;
            var purpose = s.GetProperty("purpose").GetString()!;
            var transcript = s.GetProperty("transcript").GetString()!;
            var expected = s.GetProperty("expected");
            try
            {
                var fields = purpose == VoicePurpose.NurseEntry
                    ? ScoreNurse(VoiceDraftValidator.ValidateNurse(await voice.StructureNurseAsync(transcript, vctx, ct), items).Draft, expected)
                    : ScorePrescription(VoiceDraftValidator.ValidatePrescription(await voice.StructurePrescriptionAsync(transcript, vctx, ct)).Draft, expected);
                var correct = fields.Count(f => f.Correct);
                results.Add(new EvalSampleResult(id, lang, purpose, transcript, fields.Count, correct, Ratio(correct, fields.Count), fields.Where(f => !f.Correct).ToList(), null));
            }
            catch (ApiException ex)
            {
                log.LogWarning("Voice eval sample {Id} failed: {Message}", id, ex.Message);
                results.Add(new EvalSampleResult(id, lang, purpose, transcript, 1, 0, 0, [], ex.Message));
            }
        }
        var total = results.Sum(r => r.Fields); var ok = results.Sum(r => r.Correct);
        return new VoiceEvalReport(clock.UtcNow, ai.Model, results.Count, total, ok, Ratio(ok, total),
            results.GroupBy(r => r.Language).ToDictionary(g => g.Key, g => Ratio(g.Sum(r => r.Correct), g.Sum(r => r.Fields))),
            results.GroupBy(r => r.Purpose).ToDictionary(g => g.Key, g => Ratio(g.Sum(r => r.Correct), g.Sum(r => r.Fields))),
            results);
    }

    private static double Ratio(int a, int b) => b == 0 ? 0 : Math.Round(100.0 * a / b, 1);

    /// <summary>All six numeric vitals are scored (unexpected values count as invented), plus oxygen/consciousness when expected, medicines and labs.</summary>
    public static List<EvalFieldResult> ScoreNurse(NurseDraft d, JsonElement exp)
    {
        var f = new List<EvalFieldResult>();
        var ev = exp.GetProperty("vitals");
        decimal? E(string name) => ev.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : null;
        void Num(string name, decimal? actual, decimal tol = 0)
        {
            var e = E(name);
            var correct = e is null ? actual is null : actual is not null && Math.Abs(actual.Value - e.Value) <= tol;
            f.Add(new EvalFieldResult($"vitals.{name}", e?.ToString() ?? "null", actual?.ToString() ?? "null", correct));
        }
        Num("systolic", d.Vitals.Systolic); Num("diastolic", d.Vitals.Diastolic); Num("pulse", d.Vitals.Pulse); Num("respRate", d.Vitals.RespRate);
        Num("temperatureC", d.Vitals.Temperature, 0.15m); Num("spo2", d.Vitals.Spo2);
        if (ev.TryGetProperty("onOxygen", out var ox))
            f.Add(new EvalFieldResult("vitals.onOxygen", ox.GetBoolean().ToString(), d.Vitals.OnOxygen?.ToString() ?? "null", d.Vitals.OnOxygen == ox.GetBoolean()));
        if (ev.TryGetProperty("consciousness", out var cs))
            f.Add(new EvalFieldResult("vitals.consciousness", cs.GetString()!, d.Vitals.Consciousness ?? "null", d.Vitals.Consciousness == cs.GetString()));

        var expMeds = exp.GetProperty("medications").EnumerateArray().ToList();
        foreach (var m in expMeds)
        {
            var idE = m.GetProperty("prescriptionItemId").GetInt32();
            var st = m.GetProperty("status").GetString()!;
            var got = d.Medications.FirstOrDefault(x => x.PrescriptionItemId == idE);
            f.Add(new EvalFieldResult($"medication[{idE}].matched", idE.ToString(), got is null ? "missing" : idE.ToString(), got is not null));
            f.Add(new EvalFieldResult($"medication[{idE}].status", st, got?.Status ?? "missing", got?.Status == st));
        }
        foreach (var extra in d.Medications.Where(x => expMeds.All(m => m.GetProperty("prescriptionItemId").GetInt32() != x.PrescriptionItemId)))
            f.Add(new EvalFieldResult("medication.extra", "none", $"{extra.DrugName} ({extra.Status})", false));

        var expLabs = exp.GetProperty("labResults").EnumerateArray().ToList();
        foreach (var l in expLabs)
        {
            var name = l.GetProperty("testName").GetString()!;
            var val = l.GetProperty("value").GetDecimal();
            var got = d.LabResults.FirstOrDefault(x => x.TestName == name);
            f.Add(new EvalFieldResult($"lab[{name}].name", name, got?.TestName ?? "missing", got is not null));
            f.Add(new EvalFieldResult($"lab[{name}].value", val.ToString(), got?.Value?.ToString() ?? "missing", got?.Value is { } gv && Math.Abs(gv - val) <= Math.Max(0.05m, val * 0.01m)));
        }
        foreach (var extra in d.LabResults.Where(x => expLabs.All(l => l.GetProperty("testName").GetString() != x.TestName)))
            f.Add(new EvalFieldResult("lab.extra", "none", $"{extra.TestName} {extra.Value}", false));

        if (exp.TryGetProperty("noteRequired", out var nr) && nr.GetBoolean())
            f.Add(new EvalFieldResult("note", "present", string.IsNullOrWhiteSpace(d.Note) ? "missing" : "present", !string.IsNullOrWhiteSpace(d.Note)));
        return f;
    }

    public static List<EvalFieldResult> ScorePrescription(PrescriptionDraft d, JsonElement exp)
    {
        var f = new List<EvalFieldResult>();
        var used = new HashSet<int>();
        var expItems = exp.GetProperty("items").EnumerateArray().ToList();
        foreach (var e in expItems)
        {
            var name = e.GetProperty("drugName").GetString()!;
            var freq = e.GetProperty("frequency").GetString()!;
            // Prefer an unused item with the same name and frequency (handles the same drug twice, e.g. STAT then OD).
            var idx = Enumerable.Range(0, d.Items.Count).Where(i => !used.Contains(i) && SameDrug(d.Items[i].DrugName, name))
                .OrderByDescending(i => d.Items[i].Frequency == freq).Cast<int?>().FirstOrDefault();
            var got = idx is null ? null : d.Items[idx.Value];
            if (idx is not null) used.Add(idx.Value);
            f.Add(new EvalFieldResult($"{name}.matched", name, got?.DrugName ?? "missing", got is not null));
            f.Add(new EvalFieldResult($"{name}.frequency", freq, got?.Frequency ?? "missing", got?.Frequency == freq));
            if (e.TryGetProperty("durationDays", out var dd))
                f.Add(new EvalFieldResult($"{name}.durationDays", dd.GetInt32().ToString(), got?.DurationDays?.ToString() ?? "null", got?.DurationDays == dd.GetInt32()));
            if (e.TryGetProperty("route", out var r))
                f.Add(new EvalFieldResult($"{name}.route", r.GetString()!, got?.Route ?? "null", string.Equals(got?.Route, r.GetString(), StringComparison.OrdinalIgnoreCase)));
            if (e.TryGetProperty("instructionsContains", out var ic))
                f.Add(new EvalFieldResult($"{name}.instructions", $"~{ic.GetString()}", got?.Instructions ?? "null",
                    got?.Instructions?.Contains(ic.GetString()!, StringComparison.OrdinalIgnoreCase) == true));
            if (e.TryGetProperty("doseIsNull", out var dn) && dn.GetBoolean())
                f.Add(new EvalFieldResult($"{name}.dose", "null (not said)", got?.Dose ?? "null", got is not null && got.Dose is null));
        }
        for (var i = 0; i < d.Items.Count; i++)
            if (!used.Contains(i)) f.Add(new EvalFieldResult("item.extra", "none", d.Items[i].DrugName, false));
        return f;
    }

    private static bool SameDrug(string a, string b)
    {
        var pa = VoiceDraftValidator.Phonetic(a); var pb = VoiceDraftValidator.Phonetic(b);
        return pa.Contains(pb) || pb.Contains(pa) || VoiceDraftValidator.Levenshtein(pa, pb) <= 2;
    }
}
