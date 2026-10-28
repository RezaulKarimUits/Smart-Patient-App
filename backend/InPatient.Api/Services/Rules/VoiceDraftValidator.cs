using System.Text.RegularExpressions;
using InPatient.Api.Domain;
using InPatient.Api.Services.Ai;

namespace InPatient.Api.Services.Rules;

public record ActiveItem(int Id, string DrugName, string? Form, string? Strength, string Dose, string Route, string Frequency);

public record NurseValidation(NurseDraft Draft, IReadOnlyList<Issue> Issues, bool RequiresWristband)
{
    public bool HasBlockers => Issues.Any(i => i.Level == Issue.Blocker);
}

public record PrescriptionValidation(PrescriptionDraft Draft, IReadOnlyList<Issue> Issues)
{
    public bool HasBlockers => Issues.Any(i => i.Level == Issue.Blocker);
}

/// <summary>
/// Validates AI (or user-edited) drafts in code before anything can be confirmed:
/// vital ranges, °F → °C, medicine item ids must belong to this admission's active prescription, lab plausibility.
/// </summary>
public static partial class VoiceDraftValidator
{
    public static readonly string[] MedStatuses = ["given", "delayed", "refused", "withheld"];

    public static NurseValidation ValidateNurse(NurseDraft draft, IReadOnlyList<ActiveItem> activeItems)
    {
        var issues = new List<Issue>();
        var v = draft.Vitals ?? new NurseVitalsDraft(null, null, null, null, null, null, null, null, null);

        issues.AddRange(ClinicalValidation.CheckVitals(v.Systolic, v.Diastolic, v.Pulse, v.RespRate, v.Spo2, v.Consciousness));
        var (tempC, tIssue) = ClinicalValidation.NormalizeTemperature(v.Temperature, v.TemperatureUnit);
        if (tIssue is not null) issues.Add(tIssue);
        else if (v.Temperature is not null && !string.Equals(v.TemperatureUnit, "C", StringComparison.OrdinalIgnoreCase) && tempC != v.Temperature)
            issues.Add(Issue.Warn("temperature", $"Converted {v.Temperature} °F to {tempC} °C"));
        var vitals = v with { Temperature = tIssue is null ? tempC : v.Temperature, TemperatureUnit = tIssue is null && tempC is not null ? "C" : v.TemperatureUnit };

        var meds = new List<NurseMedicationDraft>();
        var itemIds = activeItems.Select(i => i.Id).ToHashSet();
        for (var i = 0; i < (draft.Medications?.Count ?? 0); i++)
        {
            var m = draft.Medications![i];
            var field = $"medications[{i}]";
            var status = (m.Status ?? "").Trim().ToLowerInvariant();
            if (!MedStatuses.Contains(status)) issues.Add(Issue.Block(field, $"{m.DrugName}: status must be given, delayed, refused or withheld"));

            var id = m.PrescriptionItemId;
            if (id is not null && !itemIds.Contains(id.Value))
            {
                issues.Add(Issue.Block(field, $"{m.DrugName}: item #{id} is not on this patient's active prescription"));
                id = null;
            }
            if (id is null && MatchDrug(m.DrugName, activeItems) is { } match)
            {
                id = match.Id;
                issues.Add(Issue.Warn(field, $"\"{m.DrugName}\" matched to {match.DrugName} {match.Dose} {match.Route} {match.Frequency}"));
            }
            if (id is null && !issues.Any(x => x.Field == field && x.Level == Issue.Blocker))
                issues.Add(Issue.Block(field, $"{m.DrugName} is not on the active prescription — choose the medicine or remove it"));

            var item = activeItems.FirstOrDefault(a => a.Id == id);
            if (item is not null && !string.IsNullOrWhiteSpace(m.Dose) && !SameDose(m.Dose, item.Dose) && !SameDose(m.Dose, item.Strength))
                issues.Add(Issue.Warn(field, $"Spoken dose \"{m.Dose}\" differs from prescribed {item.Dose}"));

            string? time = null;
            if (!string.IsNullOrWhiteSpace(m.Time))
            {
                if (TimeRegex().Match(m.Time.Trim()) is { Success: true } tm && int.Parse(tm.Groups[1].Value) < 24 && int.Parse(tm.Groups[2].Value) < 60)
                    time = $"{int.Parse(tm.Groups[1].Value):00}:{tm.Groups[2].Value}";
                else issues.Add(Issue.Warn(field, $"Time \"{m.Time}\" not understood — using the nearest due dose"));
            }
            if (status is "delayed" or "refused" or "withheld" && string.IsNullOrWhiteSpace(m.Reason))
                issues.Add(Issue.Block(field, $"{m.DrugName}: give a reason for {status}"));

            meds.Add(m with { PrescriptionItemId = id, Status = status, Time = time, DrugName = item?.DrugName ?? m.DrugName.Trim(),
                Dose = m.Dose ?? item?.Dose, Route = m.Route ?? item?.Route });
        }

        var labs = new List<NurseLabDraft>();
        for (var i = 0; i < (draft.LabResults?.Count ?? 0); i++)
        {
            var l = draft.LabResults![i];
            var field = $"labResults[{i}]";
            if (string.IsNullOrWhiteSpace(l.TestName)) { issues.Add(Issue.Block(field, "Test name is missing")); continue; }
            if (l.Value is null) { issues.Add(Issue.Block(field, $"{l.TestName}: value is missing")); labs.Add(l); continue; }
            var n = LabCatalog.Normalize(l.TestName, l.Value, l.Unit);
            if (n.Note is not null) issues.Add(Issue.Warn(field, $"{n.TestName}: {n.Note}"));
            if (ClinicalValidation.CheckLab(field, n.TestName, n.Value) is { } li) issues.Add(li);
            labs.Add(new NurseLabDraft(n.TestName, n.Value, n.Unit));
        }

        var anyVital = vitals.Systolic != null || vitals.Diastolic != null || vitals.Pulse != null || vitals.RespRate != null || vitals.Temperature != null || vitals.Spo2 != null;
        if (!anyVital && meds.Count == 0 && labs.Count == 0 && string.IsNullOrWhiteSpace(draft.Note))
            issues.Add(Issue.Block("draft", "Nothing to save — no vitals, medicines, results or note were recognised"));
        if (vitals.Systolic is not null ^ vitals.Diastolic is not null)
            issues.Add(Issue.Warn("vitals", "Only one blood pressure value was recognised"));

        var normalized = new NurseDraft(vitals, meds, labs, string.IsNullOrWhiteSpace(draft.Note) ? null : draft.Note.Trim(), draft.ClarificationQuestion);
        return new NurseValidation(normalized, issues, meds.Count > 0);
    }

    public static PrescriptionValidation ValidatePrescription(PrescriptionDraft draft)
    {
        var issues = new List<Issue>();
        var items = new List<PrescriptionItemDraft>();
        if (draft.Items is null || draft.Items.Count == 0) issues.Add(Issue.Block("items", "No medicines were recognised"));
        for (var i = 0; i < (draft.Items?.Count ?? 0); i++)
        {
            var it = draft.Items![i];
            var field = $"items[{i}]";
            var name = string.IsNullOrWhiteSpace(it.DrugName) ? $"Item {i + 1}" : it.DrugName.Trim();
            if (string.IsNullOrWhiteSpace(it.DrugName)) issues.Add(Issue.Block(field, "Drug name is missing"));
            if (!Frequencies.All.Contains(it.Frequency)) issues.Add(Issue.Block(field, $"{name}: frequency must be one of {string.Join(", ", Frequencies.All)}"));

            var dose = string.IsNullOrWhiteSpace(it.Dose) ? null : it.Dose.Trim();
            if (dose is null && !string.IsNullOrWhiteSpace(it.Strength))
            {
                dose = it.Strength.Trim();
                issues.Add(Issue.Warn(field, $"{name}: dose taken as the stated strength {dose} — confirm"));
            }
            if (dose is null) issues.Add(Issue.Block(field, $"{name}: dose was not said — enter it (never assumed)"));

            var route = string.IsNullOrWhiteSpace(it.Route) ? null : it.Route.Trim();
            if (route is null && (it.Form ?? "").Trim().ToLowerInvariant() is "tab" or "tablet" or "cap" or "capsule" or "syrup" or "syp" or "sachet" or "suspension")
            {
                route = "PO";
                issues.Add(Issue.Warn(field, $"{name}: route set to PO from the form"));
            }
            if (route is null) issues.Add(Issue.Block(field, $"{name}: route was not said — choose IV, IM, SC…"));
            if (it.DurationDays is null && it.Frequency is not (Frequencies.STAT or Frequencies.PRN))
                issues.Add(Issue.Warn(field, $"{name}: no duration — will be scheduled for {DoseScheduler.DefaultDurationDays} days"));
            if (it.DurationDays is < 1 or > 90) issues.Add(Issue.Block(field, $"{name}: duration must be 1–90 days"));
            items.Add(it with { DrugName = name, Dose = dose, Route = route });
        }
        return new PrescriptionValidation(draft with { Items = items }, issues);
    }

    // ---------- drug matching ----------

    private static readonly Dictionary<string, string> Brands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["napa"] = "paracetamol", ["ace"] = "paracetamol", ["renova"] = "paracetamol", ["acetaminophen"] = "paracetamol",
        ["seclo"] = "omeprazole", ["losectil"] = "omeprazole", ["omep"] = "omeprazole",
        ["pantonix"] = "pantoprazole", ["ceftron"] = "ceftriaxone", ["axon"] = "ceftriaxone", ["traxon"] = "ceftriaxone",
        ["meronem"] = "meropenem", ["flagyl"] = "metronidazole", ["filmet"] = "metronidazole", ["ventolin"] = "salbutamol",
        ["windel"] = "salbutamol", ["clexane"] = "enoxaparin", ["saline"] = "normal saline", ["ns"] = "normal saline",
    };

    public static ActiveItem? MatchDrug(string spoken, IReadOnlyList<ActiveItem> items)
    {
        if (string.IsNullOrWhiteSpace(spoken) || items.Count == 0) return null;
        var words = spoken.ToLowerInvariant().Split([' ', '.', '-', '/', ','], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("inj" or "tab" or "cap" or "syp" or "syrup" or "inf" or "neb" or "injection" or "tablet")).ToList();
        var candidates = new List<string> { string.Join(" ", words) };
        candidates.AddRange(words);
        candidates.AddRange(words.Where(Brands.ContainsKey).Select(w => Brands[w]));

        ActiveItem? best = null;
        var bestScore = int.MaxValue;
        foreach (var item in items)
        {
            var target = Phonetic(item.DrugName);
            var targetFirst = Phonetic(item.DrugName.Split(' ', '+')[0]);
            foreach (var c in candidates)
            {
                var p = Phonetic(c);
                if (p.Length < 3) continue;
                if (target.StartsWith(p) || p.StartsWith(targetFirst)) return item;
                var d = Math.Min(Levenshtein(p, target), Levenshtein(p, targetFirst));
                var limit = Math.Max(2, targetFirst.Length / 4);
                if (d <= limit && d < bestScore) { best = item; bestScore = d; }
            }
        }
        return best;
    }

    /// <summary>Crude phonetic key for mis-heard or misspelled drug names ("seftriaxone" ≈ "ceftriaxone").</summary>
    public static string Phonetic(string s)
    {
        var x = new string(s.ToLowerInvariant().Where(char.IsLetter).ToArray());
        x = x.Replace("ph", "f").Replace("x", "ks").Replace("ck", "k").Replace("c", "s").Replace("q", "k").Replace("z", "j").Replace("y", "i").Replace("ee", "i").Replace("oo", "u");
        x = x.TrimEnd('e', 'a');
        return x;
    }

    public static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    private static bool SameDose(string? a, string? b)
    {
        if (a is null || b is null) return false;
        static string N(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()).Replace("gram", "g").Replace("gm", "g").Replace("milligram", "mg");
        var na = N(a); var nb = N(b);
        return na == nb || (na.Length > 0 && nb.StartsWith(na)) || (nb.Length > 0 && na.StartsWith(nb)) || (na == "1g" && nb == "1000mg") || (na == "1000mg" && nb == "1g");
    }

    [GeneratedRegex(@"^(\d{1,2})[:.](\d{2})$")]
    private static partial Regex TimeRegex();
}
