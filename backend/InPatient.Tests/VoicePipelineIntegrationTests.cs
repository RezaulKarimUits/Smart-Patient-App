using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using InPatient.Api.Services.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InPatient.Tests;

/// <summary>
/// End-to-end API test of the demo flow on a throw-away database (InPatientDb_Test, created from the SQL scripts):
/// admit → scan → voice prescription → sign → voice nurse entry → wristband check → NEWS2 High → critical alert.
/// The model is replaced by a test double that returns what the real model would for the demo phrases, so the
/// pipeline (validation, confirm, scheduling, monitoring) is exercised without an API key. Skipped if LocalDB/sqlcmd is unavailable.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string Db = "InPatientDb_Test";
    public WebApplicationFactory<Program>? Factory { get; private set; }
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Database/run-db.ps1"));
        try
        {
            var psi = new ProcessStartInfo("powershell", $"-ExecutionPolicy Bypass -File \"{script}\" -Database {Db} -Reset")
                { RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            var err = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (p.ExitCode != 0) { SkipReason = "Database scripts failed: " + err; return; }
        }
        catch (Exception ex) { SkipReason = "Cannot run database scripts: " + ex.Message; return; }

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Default", $"Server=(localdb)\\MSSQLLocalDB;Database={Db};Trusted_Connection=True;TrustServerCertificate=True");
            b.UseSetting("Jwt:Key", "test-signing-key-0123456789-0123456789-abcdef");
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IStructuredAi>(); s.AddSingleton<IStructuredAi, ScriptedAi>();
                s.RemoveAll<ISpeechToText>(); s.AddSingleton<ISpeechToText, ScriptedSpeech>();
            });
        });
    }

    public Task DisposeAsync() { Factory?.Dispose(); return Task.CompletedTask; }
}

/// <summary>Test double for the model: returns the structured output for the two demo-script phrases.</summary>
public sealed class ScriptedAi : IStructuredAi
{
    public bool IsConfigured => true;
    public string Model => "scripted-test-double";

    public Task<T> GenerateAsync<T>(AiJsonRequest r, CancellationToken ct = default)
    {
        object result = r.SchemaName switch
        {
            "prescription" => new PrescriptionDraft([
                new("Ceftriaxone", "Inj", "1 g", "1 g", "IV", "Q12H", 5, null),
                new("Paracetamol", "Tab", "500 mg", "500 mg", null, "PRN", null, "if fever"),
                new("Omeprazole", null, "20 mg", null, null, "BD", null, "before meal")], null, null),
            "nurse_entry" => new NurseDraft(new NurseVitalsDraft(100, 60, 118, 24, 102, "F", 93, null, null),
                [new NurseMedicationDraft(ItemId(r.UserPrompt, "Ceftriaxone"), "Ceftriaxone", "1 gram", "IV", null, "given", null)], [], null, null),
            "alert_explanation" => new AlertExplanationDraft("NEWS2 rose to 9 with RR 24, SpO2 93% and pulse 118."),
            _ => throw new InvalidOperationException("Unexpected schema " + r.SchemaName)
        };
        return Task.FromResult((T)result);
    }

    private static int? ItemId(string prompt, string drug)
        => Regex.Match(prompt, $@"id (\d+): \S* ?{drug}") is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;
}

public sealed class ScriptedSpeech : ISpeechToText
{
    public bool IsConfigured => true;
    public string Model => "scripted";
    public Task<string> TranscribeAsync(BinaryData audio, string fileName, string hint, CancellationToken ct = default) => Task.FromResult("scripted transcript");
}

public class VoicePipelineIntegrationTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    private async Task<HttpClient> As(string phone)
    {
        var c = fx.Factory!.CreateClient();
        var res = await c.PostAsJsonAsync("/api/auth/login", new { phone, password = "Demo@123" });
        res.EnsureSuccessStatusCode();
        var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [Fact]
    public async Task Demo_flow_voice_prescription_and_nurse_entry()
    {
        if (fx.Factory is null) return; // LocalDB/sqlcmd not available on this machine: nothing to run against
        var rec = await As("01800000002");
        var nurse = await As("01800000011");
        var cons = await As("01800000021");

        // Reception admits to M-05
        var board = await Ok(await rec.GetAsync("/api/board"));
        var med = board.GetProperty("wards").EnumerateArray().First(w => w.GetProperty("code").GetString() == "M");
        var m05 = med.GetProperty("beds").EnumerateArray().First(b => b.GetProperty("bedNumber").GetString() == "M-05");
        Assert.Equal("Free", m05.GetProperty("status").GetString());
        var patient = await Ok(await rec.PostAsJsonAsync("/api/patients", new { name = "Integration Patient", dateOfBirth = "1972-03-01", sex = "M" }));
        var admit = await Ok(await rec.PostAsJsonAsync("/api/admissions", new
        {
            patientId = patient.GetProperty("id").GetInt32(), bedId = m05.GetProperty("id").GetInt32(), consultantUserId = await ConsultantId(rec, "Ahsan"),
            provisionalDiagnosis = "Fever"
        }));
        var admissionId = admit.GetProperty("admission").GetProperty("id").GetInt32();
        var wristband = admit.GetProperty("wristband").GetProperty("token").GetString()!;

        // Nurse scans bed then wristband
        var qr = await Ok(await rec.GetAsync($"/api/beds/{m05.GetProperty("id").GetInt32()}/qr"));
        await Ok(await nurse.PostAsJsonAsync("/api/scan/confirm-bed", new { bedToken = qr.GetProperty("token").GetString(), wristbandToken = wristband }));

        // Consultant: voice prescription → draft (dose never invented → blocker) → edit → confirm → Draft prescription → sign
        var draft = await Ok(await cons.PostAsync("/api/voice/prescription", Form(admissionId)));
        Assert.Contains(draft.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("level").GetString() == "blocker");
        var captureId = draft.GetProperty("captureId").GetInt32();
        var items = draft.GetProperty("draft").GetProperty("items").Deserialize<List<Dictionary<string, object?>>>(J)!;
        var blocked = await cons.PostAsJsonAsync($"/api/voice/{captureId}/confirm", new { draft = draft.GetProperty("draft") });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        items[2]["dose"] = "20 mg";
        items[2]["route"] = "PO";
        var rx = await Ok(await cons.PostAsJsonAsync($"/api/voice/{captureId}/confirm", new { draft = new { items, generalInstructions = (string?)null, clarificationQuestion = (string?)null } }));
        var prescription = rx.GetProperty("prescription");
        Assert.Equal("Draft", prescription.GetProperty("status").GetString());
        Assert.Equal("Voice", prescription.GetProperty("source").GetString());
        var signed = await Ok(await cons.PostAsync($"/api/prescriptions/{prescription.GetProperty("id").GetInt32()}/sign", null));
        Assert.True(signed.GetProperty("doseTasksCreated").GetInt32() >= 10);

        // Nurse: voice entry → draft → confirm needs the wristband
        var entry = await Ok(await nurse.PostAsync("/api/voice/nurse-entry", Form(admissionId)));
        Assert.True(entry.GetProperty("requiresWristband").GetBoolean());
        Assert.Equal(38.9m, entry.GetProperty("draft").GetProperty("vitals").GetProperty("temperature").GetDecimal());
        var entryId = entry.GetProperty("captureId").GetInt32();
        var noBand = await nurse.PostAsJsonAsync($"/api/voice/{entryId}/confirm", new { draft = entry.GetProperty("draft") });
        Assert.Equal(HttpStatusCode.BadRequest, noBand.StatusCode);
        var wrong = await nurse.PostAsJsonAsync($"/api/voice/{entryId}/confirm", new { draft = entry.GetProperty("draft"), wristbandToken = "0000deadbeef" });
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        Assert.Contains("Wrong patient", await wrong.Content.ReadAsStringAsync());

        var saved = await Ok(await nurse.PostAsJsonAsync($"/api/voice/{entryId}/confirm", new { draft = entry.GetProperty("draft"), wristbandToken = wristband }));
        var n = saved.GetProperty("nurse");
        Assert.Equal(9, n.GetProperty("news2Score").GetInt32());
        Assert.Equal("High", n.GetProperty("news2Band").GetString());
        Assert.Equal("Given", n.GetProperty("doses")[0].GetProperty("status").GetString());
        Assert.Equal("Critical", saved.GetProperty("monitoring").GetProperty("flag").GetString());

        // Consultant: patient is first on the priority list with a critical alert
        var priority = await Ok(await cons.GetAsync("/api/consultant/patients"));
        Assert.Equal(admissionId, priority[0].GetProperty("patient").GetProperty("id").GetInt32());
        var alerts = await Ok(await cons.GetAsync($"/api/alerts?admissionId={admissionId}"));
        Assert.Contains(alerts.EnumerateArray(), a => a.GetProperty("severity").GetString() == "Critical");

        // A confirmed capture cannot be confirmed twice
        var again = await nurse.PostAsJsonAsync($"/api/voice/{entryId}/confirm", new { draft = entry.GetProperty("draft"), wristbandToken = wristband });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    private static MultipartFormDataContent Form(int admissionId)
        => new() { { new StringContent(admissionId.ToString()), "admissionId" }, { new StringContent("scripted transcript"), "transcript" } };

    private static async Task<int> ConsultantId(HttpClient c, string name)
    {
        var board = await Ok(await c.GetAsync("/api/wards"));
        var ward = board.EnumerateArray().First(w => w.GetProperty("code").GetString() == "M");
        return ward.GetProperty("consultants").EnumerateArray().First(x => x.GetProperty("name").GetString()!.Contains(name)).GetProperty("id").GetInt32();
    }
}
