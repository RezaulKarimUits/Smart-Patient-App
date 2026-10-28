namespace InPatient.Api.Services.Ai;

/// <summary>Strict JSON schemas (OpenAI structured outputs): every property required, nullable where it may be missing.</summary>
public static class AiSchemas
{
    public const string NurseEntry = """
    {
      "type": "object", "additionalProperties": false,
      "required": ["vitals", "medications", "labResults", "note", "clarificationQuestion"],
      "properties": {
        "vitals": {
          "type": "object", "additionalProperties": false,
          "required": ["systolic", "diastolic", "pulse", "respRate", "temperature", "temperatureUnit", "spo2", "onOxygen", "consciousness"],
          "properties": {
            "systolic": { "type": ["integer", "null"] },
            "diastolic": { "type": ["integer", "null"] },
            "pulse": { "type": ["integer", "null"] },
            "respRate": { "type": ["integer", "null"] },
            "temperature": { "type": ["number", "null"] },
            "temperatureUnit": { "type": ["string", "null"], "enum": ["F", "C", null] },
            "spo2": { "type": ["integer", "null"] },
            "onOxygen": { "type": ["boolean", "null"] },
            "consciousness": { "type": ["string", "null"], "enum": ["Alert", "Confusion", "Voice", "Pain", "Unresponsive", null] }
          }
        },
        "medications": {
          "type": "array",
          "items": {
            "type": "object", "additionalProperties": false,
            "required": ["prescriptionItemId", "drugName", "dose", "route", "time", "status", "reason"],
            "properties": {
              "prescriptionItemId": { "type": ["integer", "null"] },
              "drugName": { "type": "string" },
              "dose": { "type": ["string", "null"] },
              "route": { "type": ["string", "null"] },
              "time": { "type": ["string", "null"], "description": "HH:mm 24-hour Asia/Dhaka" },
              "status": { "type": "string", "enum": ["given", "delayed", "refused", "withheld"] },
              "reason": { "type": ["string", "null"] }
            }
          }
        },
        "labResults": {
          "type": "array",
          "items": {
            "type": "object", "additionalProperties": false,
            "required": ["testName", "value", "unit"],
            "properties": {
              "testName": { "type": "string" },
              "value": { "type": ["number", "null"] },
              "unit": { "type": ["string", "null"] }
            }
          }
        },
        "note": { "type": ["string", "null"] },
        "clarificationQuestion": { "type": ["string", "null"] }
      }
    }
    """;

    public const string Prescription = """
    {
      "type": "object", "additionalProperties": false,
      "required": ["items", "generalInstructions", "clarificationQuestion"],
      "properties": {
        "items": {
          "type": "array",
          "items": {
            "type": "object", "additionalProperties": false,
            "required": ["drugName", "form", "strength", "dose", "route", "frequency", "durationDays", "instructions"],
            "properties": {
              "drugName": { "type": "string" },
              "form": { "type": ["string", "null"], "description": "Tab, Cap, Inj, Syrup, Inf, Neb, Supp, Cream, Drop, Sachet" },
              "strength": { "type": ["string", "null"] },
              "dose": { "type": ["string", "null"] },
              "route": { "type": ["string", "null"], "description": "PO, IV, IM, SC, Neb, PR, SL, Topical, Inhaled" },
              "frequency": { "type": "string", "enum": ["OD", "BD", "TDS", "QID", "Q4H", "Q6H", "Q8H", "Q12H", "STAT", "PRN"] },
              "durationDays": { "type": ["integer", "null"] },
              "instructions": { "type": ["string", "null"] }
            }
          }
        },
        "generalInstructions": { "type": ["string", "null"] },
        "clarificationQuestion": { "type": ["string", "null"] }
      }
    }
    """;

    public const string RoundNote = """
    {
      "type": "object", "additionalProperties": false,
      "required": ["note", "plan", "clarificationQuestion"],
      "properties": {
        "note": { "type": "string" },
        "plan": { "type": ["string", "null"] },
        "clarificationQuestion": { "type": ["string", "null"] }
      }
    }
    """;

    public const string ReportExtraction = """
    {
      "type": "object", "additionalProperties": false,
      "required": ["results", "reportDate", "clarificationQuestion"],
      "properties": {
        "results": {
          "type": "array",
          "items": {
            "type": "object", "additionalProperties": false,
            "required": ["testName", "value", "unit", "refLow", "refHigh"],
            "properties": {
              "testName": { "type": "string" },
              "value": { "type": ["number", "null"] },
              "unit": { "type": ["string", "null"] },
              "refLow": { "type": ["number", "null"] },
              "refHigh": { "type": ["number", "null"] }
            }
          }
        },
        "reportDate": { "type": ["string", "null"], "description": "yyyy-MM-dd if printed on the report" },
        "clarificationQuestion": { "type": ["string", "null"] }
      }
    }
    """;

    public const string AlertExplanation = """
    {
      "type": "object", "additionalProperties": false,
      "required": ["explanation"],
      "properties": { "explanation": { "type": "string" } }
    }
    """;

    public const string Progress = """
    {
      "type": "object", "additionalProperties": false,
      "required": ["overallTrajectory", "summary", "parameterComments", "watchFor"],
      "properties": {
        "overallTrajectory": { "type": "string", "enum": ["improving", "worsening", "stable", "mixed"] },
        "summary": { "type": "string" },
        "parameterComments": {
          "type": "array",
          "items": {
            "type": "object", "additionalProperties": false,
            "required": ["name", "comment"],
            "properties": { "name": { "type": "string" }, "comment": { "type": "string" } }
          }
        },
        "watchFor": { "type": "array", "items": { "type": "string" } }
      }
    }
    """;

    public const string Handover = """
    {
      "type": "object", "additionalProperties": false,
      "required": ["patients"],
      "properties": {
        "patients": {
          "type": "array",
          "items": {
            "type": "object", "additionalProperties": false,
            "required": ["admissionId", "summary", "priorities"],
            "properties": {
              "admissionId": { "type": "integer" },
              "summary": { "type": "string" },
              "priorities": { "type": "array", "items": { "type": "string" } }
            }
          }
        }
      }
    }
    """;

    public const string Discharge = """
    {
      "type": "object", "additionalProperties": false,
      "required": ["presentingProblem", "hospitalCourse", "keyResults", "conditionAtDischarge", "dischargeMedications", "followUp", "returnPrecautions"],
      "properties": {
        "presentingProblem": { "type": "string" },
        "hospitalCourse": { "type": "string" },
        "keyResults": { "type": "string" },
        "conditionAtDischarge": { "type": "string" },
        "dischargeMedications": { "type": "array", "items": { "type": "string" } },
        "followUp": { "type": "string" },
        "returnPrecautions": { "type": "array", "items": { "type": "string" } }
      }
    }
    """;
}

public static class AiPrompts
{
    public const string Safety = "You support clinical staff; you never diagnose, never recommend treatment changes and never replace clinical judgement.";

    public const string NurseEntry = """
    You turn a hospital nurse's spoken bedside report into a structured draft that the nurse will review before anything is saved.
    The report is spoken in English. Numbers may be digits or English number words ("one forty" = 140, "ninety" = 90).
    Rules:
    - Extract ONLY what was said. Never invent, estimate or carry over values. Anything not mentioned is null (or an empty array).
    - Blood pressure "140 by 90", "140/90", "140 over 90" -> systolic 140, diastolic 90.
    - Temperature: report the number as spoken and set temperatureUnit: values 93-108 are "F", values 34-42 are "C". If the unit was spoken, use it.
    - SpO2 / oxygen saturation / "saturation" / "sats" -> spo2 (integer %). "on oxygen", "on O2", "on 2 litres" -> onOxygen true; "room air" -> false; otherwise null.
    - Consciousness: only if stated (alert/confused/responds to voice/responds to pain/unresponsive), otherwise null.
    - Medicines: "given", "gave", "administered", "done" -> "given"; "will give later", "postponed", "not yet given" -> "delayed"; "held", "withheld", "not given because ..." with a clinical reason (NPO, low BP, doctor said hold) -> "withheld"; "refused", "patient declined", "won't take it" -> "refused". Put the stated reason in reason, else null.
    - Match each medicine to the ACTIVE PRESCRIPTION list provided, allowing for mis-heard or misspelled names and common brand names ("ceftriaxon" -> Ceftriaxone; "omeprazol" -> Omeprazole; "Napa" -> Paracetamol). Use that item's id as prescriptionItemId. If no active item matches, prescriptionItemId is null and drugName is what was said.
    - Times: "at 10", "10 o'clock" -> the 10:00 or 22:00 nearest to the current time given; "10 pm" -> 22:00; return "HH:mm" 24-hour. If no time was said, time is null.
    - Lab results spoken (e.g. "Hb 9.8", "platelets 65 thousand", "creatinine 1.4") -> labResults with testName, value and unit if said. "thousand" = x1000, "lakh" = 100000.
    - note: a concise English nursing note of other observations that were said (symptoms, intake/output, events). null if nothing else was said.
    - clarificationQuestion: ONLY if something essential is ambiguous (e.g. which medicine, a number that could be two values), ask one short question in English. Otherwise null.
    - Write every text field in English.
    """;

    public const string Prescription = """
    You turn a consultant's spoken prescription (in English) into a structured DRAFT the consultant will review and sign.
    Rules:
    - One item per medicine. drugName is the generic name with correct spelling (e.g. "ceftriaxon" -> Ceftriaxone). Keep form (Tab, Cap, Inj, Syrup, Inf, Neb), strength and dose as said.
    - NEVER invent a dose, strength or route. If the dose was not said, dose is null. Route: Tab/Cap/Syrup -> "PO"; "IV", "IM", "SC", "intravenous" -> IV; if an injection's route was not said, route is null.
    - Frequency: "once daily" / "once a day" -> OD; "twice daily" -> BD; "three times daily" -> TDS; "four times daily" -> QID;
      "every 12 hours" -> Q12H; "every 8 hours" -> Q8H; "every 6 hours" -> Q6H; "every 4 hours" -> Q4H; "now" / "stat" / "immediately" -> STAT;
      "as needed" / "if needed" / "when required" / "PRN" -> PRN (put the condition, e.g. "if fever", in instructions).
      Dose-pattern shorthand: "1+0+1" -> BD; "1+1+1" -> TDS; "1+0+0" -> OD (morning); "0+0+1" -> OD with instructions "at night"; "1+1+1+1" -> QID; "at night" / "at bedtime" -> OD with instructions "at night".
    - "before food" / "before meals" -> instructions "before meal"; "after food" / "after meals" -> "after meal". "for 5 days" -> durationDays 5; "for 1 week" -> 7; "for 2 weeks" -> 14. No duration said -> null.
    - generalInstructions: other orders that were said (e.g. monitoring, diet), else null.
    - clarificationQuestion: only if something essential is ambiguous (e.g. dose missing, unclear drug), one short question in English; else null.
    - Write every text field in English.
    """;

    public const string RoundNote = """
    You turn a consultant's spoken ward-round note (in English) into a concise English clinical note for the record.
    Keep only what was said; do not add findings, diagnoses or plans that were not stated. "note" = assessment/findings as said; "plan" = plan as said or null.
    clarificationQuestion only if something essential is ambiguous, else null.
    """;

    public const string ReportExtraction = """
    You read a photographed laboratory report and extract each result exactly as printed. Do not guess values that are unreadable — omit them and ask a clarificationQuestion.
    testName as printed (e.g. "Haemoglobin", "Platelet count", "S. Creatinine"); value as a number (no thousands separators); unit as printed; refLow/refHigh from the printed reference range or null.
    If the report uses "lakh" or x10^3, keep the number as printed and put the multiplier in unit. Ignore patient identifiers.
    """;
}

// ---------- Draft models (also what the client edits and sends back on confirm) ----------

public record NurseVitalsDraft(int? Systolic, int? Diastolic, int? Pulse, int? RespRate, decimal? Temperature, string? TemperatureUnit, int? Spo2, bool? OnOxygen, string? Consciousness);
public record NurseMedicationDraft(int? PrescriptionItemId, string DrugName, string? Dose, string? Route, string? Time, string Status, string? Reason);
public record NurseLabDraft(string TestName, decimal? Value, string? Unit);
public record NurseDraft(NurseVitalsDraft Vitals, IReadOnlyList<NurseMedicationDraft> Medications, IReadOnlyList<NurseLabDraft> LabResults, string? Note, string? ClarificationQuestion);

public record PrescriptionItemDraft(string DrugName, string? Form, string? Strength, string? Dose, string? Route, string Frequency, int? DurationDays, string? Instructions);
public record PrescriptionDraft(IReadOnlyList<PrescriptionItemDraft> Items, string? GeneralInstructions, string? ClarificationQuestion);

public record RoundNoteDraft(string Note, string? Plan, string? ClarificationQuestion);

public record ReportResultDraft(string TestName, decimal? Value, string? Unit, decimal? RefLow, decimal? RefHigh);
public record ReportDraft(IReadOnlyList<ReportResultDraft> Results, string? ReportDate, string? ClarificationQuestion);

public record AlertExplanationDraft(string Explanation);
public record ParameterComment(string Name, string Comment);
public record ProgressDraft(string OverallTrajectory, string Summary, IReadOnlyList<ParameterComment> ParameterComments, IReadOnlyList<string> WatchFor);
public record HandoverPatientDraft(int AdmissionId, string Summary, IReadOnlyList<string> Priorities);
public record HandoverDraft(IReadOnlyList<HandoverPatientDraft> Patients);
public record DischargeDraft(string PresentingProblem, string HospitalCourse, string KeyResults, string ConditionAtDischarge, IReadOnlyList<string> DischargeMedications,
    string FollowUp, IReadOnlyList<string> ReturnPrecautions);
