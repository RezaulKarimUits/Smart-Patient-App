using InPatient.Api.Data;
using InPatient.Api.Data.Entities;
using InPatient.Api.Domain;
using InPatient.Api.Infrastructure;
using InPatient.Api.Services.Rules;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace InPatient.Api.Services;

/// <summary>
/// Development seeding that SQL cannot do: generates sample report/prescription/progress PNGs, uploads them to blob storage
/// (Azurite) and links them as StoredFiles; writes demo images to wwwroot/samples; runs the coded monitoring rules over the
/// seeded admissions so their alerts exist. Idempotent.
/// </summary>
public sealed class DevSeeder(IServiceScopeFactory scopes, IWebHostEnvironment env, IConfiguration config, ILogger<DevSeeder> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!env.IsDevelopment()) return;
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        try { WriteSampleImages(); } catch (Exception ex) { log.LogWarning("Sample images not written: {Message}", ex.Message); }

        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<CurrentUser>().RunAsSystem();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<InPatientDbContext>();
        try
        {
            if (!await db.Database.CanConnectAsync(ct)) { log.LogWarning("Database not reachable - dev seeding skipped"); return; }
        }
        catch (Exception ex) { log.LogWarning("Database not reachable - dev seeding skipped: {Message}", ex.Message); return; }

        if (config.GetValue("Dev:SeedBlobs", true))
        {
            try { await SeedBlobsAsync(sp, ct); }
            catch (ApiException ex) { log.LogWarning("Blob seeding skipped: {Message}. Start Azurite and restart the API to add sample images.", ex.Message); }
            catch (Exception ex) { log.LogWarning(ex, "Blob seeding failed"); }
        }
        if (config.GetValue("Dev:RunMonitoringOnStartup", true))
        {
            var ids = await db.Admissions.Where(a => a.Status == AdmissionStatus.Active).Select(a => a.Id).ToListAsync(ct);
            var monitoring = sp.GetRequiredService<MonitoringService>();
            var before = await db.Alerts.CountAsync(ct);
            foreach (var id in ids) await monitoring.RunAsync(id, ct);
            log.LogInformation("Monitoring evaluated for {Count} active admissions; {New} new alert(s)", ids.Count, await db.Alerts.CountAsync(ct) - before);
        }
    }

    private record SeedFile(string Mrn, string Kind, string Caption, Func<Admission, Patient, string, byte[]> Render, string[] LinkTests);

    private async Task SeedBlobsAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<InPatientDbContext>();
        var store = sp.GetRequiredService<IFileStore>();
        var files = sp.GetRequiredService<FileService>();
        foreach (var h in await db.Hospitals.Select(h => h.Id).ToListAsync(ct)) await store.EnsureContainerAsync(h, ct);

        var plan = new List<SeedFile>
        {
            new("DCG-24002", FileKinds.TestReport, "CBC report (seed)", (a, p, hosp) => LabReportFor(db, a, p, hosp, "Complete Blood Count", [LabCatalog.Haemoglobin, LabCatalog.Wbc, LabCatalog.Platelets]), [LabCatalog.Haemoglobin]),
            new("DCG-24003", FileKinds.TestReport, "CBC report (seed)", (a, p, hosp) => LabReportFor(db, a, p, hosp, "Complete Blood Count", [LabCatalog.Platelets, LabCatalog.Haematocrit]), [LabCatalog.Platelets, LabCatalog.Haematocrit]),
            new("DCG-24012", FileKinds.TestReport, "Renal function report (seed)", (a, p, hosp) => LabReportFor(db, a, p, hosp, "Renal Function Test", [LabCatalog.Creatinine]), [LabCatalog.Creatinine]),
            new("DCG-24004", FileKinds.Prescription, "Admission prescription (seed)", (a, p, hosp) => PrescriptionImage(db, a, p, hosp), []),
            new("DCG-24013", FileKinds.Progress, "Left foot wound, day 4 after debridement (seed)", (_, _, _) => WoundImage("Left foot ulcer - day 4 post debridement"), []),
            new("DCG-24023", FileKinds.Progress, "Portable chest X-ray photo (seed)", (_, _, _) => ChestXrayImage(), []),
        };

        var added = 0;
        foreach (var item in plan)
        {
            var adm = await db.Admissions.Include(a => a.Patient).Include(a => a.Hospital).Include(a => a.CurrentBed).ThenInclude(b => b!.Ward)
                .FirstOrDefaultAsync(a => a.Patient.Mrn == item.Mrn, ct);
            if (adm is null) continue;
            var existing = await db.StoredFiles.FirstOrDefaultAsync(f => f.AdmissionId == adm.Id && f.Caption == item.Caption, ct);
            if (existing is not null && await store.ExistsAsync(adm.HospitalId, existing.BlobPath, ct)) continue;

            var png = item.Render(adm, adm.Patient, adm.Hospital.Name);
            int? prescriptionId = item.Kind == FileKinds.Prescription
                ? await db.Prescriptions.Where(p => p.AdmissionId == adm.Id).OrderBy(p => p.Id).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct) : null;
            StoredFile file;
            if (existing is not null)
            {
                await store.UploadAsync(adm.HospitalId, existing.BlobPath, new MemoryStream(png), "image/png", ct);
                file = existing;
            }
            else file = await files.SaveAsync(adm, new MemoryStream(png), "image/png", "png", png.Length, item.Kind, item.Caption, prescriptionId, ct);

            foreach (var test in item.LinkTests)
            {
                var latest = await db.LabResults.Where(l => l.AdmissionId == adm.Id && l.TestName == test).OrderByDescending(l => l.ResultAt).FirstOrDefaultAsync(ct);
                if (latest is not null) latest.StoredFileId = file.Id;
            }
            await db.SaveChangesAsync(ct);
            added++;
        }
        log.LogInformation("Blob seeding: {Added} sample file(s) uploaded", added);
    }

    private static byte[] LabReportFor(InPatientDbContext db, Admission a, Patient p, string hospital, string title, string[] tests)
    {
        var rows = db.LabResults.Where(l => l.AdmissionId == a.Id && tests.Contains(l.TestName)).AsEnumerable()
            .GroupBy(l => l.TestName).Select(g => g.OrderByDescending(l => l.ResultAt).First()).OrderBy(l => Array.IndexOf(tests, l.TestName)).ToList();
        var at = rows.Count > 0 ? rows.Max(r => r.ResultAt) : DateTime.UtcNow;
        return LabReport(hospital, p.Name, p.Mrn, $"{Dhaka.AgeYears(p.DateOfBirth, at)} y / {p.Sex}", $"{a.CurrentBed?.Ward.Name} · {a.CurrentBed?.BedNumber}",
            Dhaka.ToLocal(at), title, rows.Select(r => (r.TestName, LabCatalog.Format(r.TestName, r.NumericValue ?? 0), r.Unit ?? "",
                $"{LabCatalog.Format(r.TestName, r.RefLow ?? 0)} - {LabCatalog.Format(r.TestName, r.RefHigh ?? 0)}", r.Flag == LabFlags.Normal ? "" : r.Flag.ToUpperInvariant())).ToList());
    }

    private static byte[] PrescriptionImage(InPatientDbContext db, Admission a, Patient p, string hospital)
    {
        var lines = db.PrescriptionItems.Where(i => i.Prescription.AdmissionId == a.Id).OrderBy(i => i.Id).AsEnumerable()
            .Select(i => $"{i.Form}. {i.DrugName} {i.Dose} {i.Route} - {i.Frequency}{(i.DurationDays is { } d ? $" x {d} days" : "")}{(i.Instructions is null ? "" : $" ({i.Instructions})")}").ToList();
        var consultant = db.AppUsers.Where(u => u.Id == a.ConsultantUserId).Select(u => u.Name).First();
        return Prescription(hospital, p.Name, $"{Dhaka.AgeYears(p.DateOfBirth, DateTime.UtcNow)} y / {p.Sex}", a.ProvisionalDiagnosis ?? "", lines, consultant, Dhaka.ToLocal(a.AdmittedAt));
    }

    /// <summary>Demo images in wwwroot/samples to photograph from a screen (report extraction) or attach (prescription).</summary>
    private void WriteSampleImages()
    {
        var dir = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "samples");
        Directory.CreateDirectory(dir);
        var report = Path.Combine(dir, "sample-lab-report.png");
        if (!File.Exists(report))
            File.WriteAllBytes(report, LabReport("Dhaka Care General Hospital", "Karim Uddin", "DCG-24026", "54 y / M", "Medicine Ward · M-05",
                Dhaka.ToLocal(DateTime.UtcNow), "Complete Blood Count & Biochemistry",
                [("Haemoglobin", "10.2", "g/dL", "12.0 - 16.0", "LOW"), ("WBC", "14.6", "x10^9/L", "4.0 - 11.0", "HIGH"), ("Platelets", "182,000", "/cumm", "150,000 - 450,000", ""),
                 ("S. Creatinine", "1.3", "mg/dL", "0.6 - 1.2", "HIGH"), ("Sodium", "134", "mmol/L", "135 - 145", "LOW"), ("Potassium", "4.4", "mmol/L", "3.5 - 5.1", ""),
                 ("CRP", "96", "mg/L", "0 - 5", "HIGH")]));
        var rx = Path.Combine(dir, "sample-prescription.png");
        if (!File.Exists(rx))
            File.WriteAllBytes(rx, Prescription("Dhaka Care General Hospital", "Karim Uddin", "54 y / M", "Sepsis, source to be determined",
                ["Inj. Ceftriaxone 1 g IV - 12 hourly x 5 days", "Tab. Paracetamol 500 mg - if fever", "Cap. Omeprazole 20 mg 1+0+1 - before meal"],
                "Dr. Ahsan Habib", Dhaka.ToLocal(DateTime.UtcNow)));
        var wound = Path.Combine(dir, "sample-progress-photo.png");
        if (!File.Exists(wound)) File.WriteAllBytes(wound, WoundImage("Wound check - day 2"));
    }

    // ---------- drawing ----------

    private static readonly SKColor Teal = new(15, 118, 110);
    private static SKFont Font(float size, bool bold = false, string family = "Arial", bool italic = false)
        => new(SKTypeface.FromFamilyName(family, bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
            italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright) ?? SKTypeface.Default, size);

    private static byte[] Encode(SKSurface surface)
    {
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    public static byte[] LabReport(string hospital, string patient, string mrn, string ageSex, string wardBed, DateTime localDate, string title,
        IReadOnlyList<(string Test, string Result, string Unit, string Reference, string Flag)> rows)
    {
        const int w = 1240, h = 1500;
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        var c = surface.Canvas;
        c.Clear(SKColors.White);
        using var fill = new SKPaint { IsAntialias = true };
        using var text = new SKPaint { IsAntialias = true, Color = new SKColor(20, 30, 30) };
        using var grey = new SKPaint { IsAntialias = true, Color = new SKColor(110, 120, 120) };
        using var line = new SKPaint { IsAntialias = true, Color = new SKColor(200, 210, 210), StrokeWidth = 2, Style = SKPaintStyle.Stroke };

        fill.Color = Teal; c.DrawRect(0, 0, w, 150, fill);
        using var white = new SKPaint { IsAntialias = true, Color = SKColors.White };
        c.DrawText(hospital, 60, 75, SKTextAlign.Left, Font(44, true), white);
        c.DrawText("Department of Laboratory Medicine", 60, 120, SKTextAlign.Left, Font(26), white);
        c.DrawText(title.ToUpperInvariant(), w / 2f, 230, SKTextAlign.Center, Font(36, true), text);

        var f = Font(26);
        c.DrawText($"Patient: {patient}", 60, 300, SKTextAlign.Left, f, text);
        c.DrawText($"MRN: {mrn}", 760, 300, SKTextAlign.Left, f, text);
        c.DrawText($"Age/Sex: {ageSex}", 60, 345, SKTextAlign.Left, f, text);
        c.DrawText($"Location: {wardBed}", 760, 345, SKTextAlign.Left, f, text);
        c.DrawText($"Collected: {localDate:dd MMM yyyy HH:mm}", 60, 390, SKTextAlign.Left, f, text);
        c.DrawText($"Reported: {localDate.AddHours(1):dd MMM yyyy HH:mm}", 760, 390, SKTextAlign.Left, f, text);
        c.DrawLine(60, 420, w - 60, 420, line);

        var hf = Font(26, true);
        float[] cols = [60, 470, 690, 860, 1100];
        string[] heads = ["Test", "Result", "Unit", "Reference range", "Flag"];
        for (var i = 0; i < heads.Length; i++) c.DrawText(heads[i], cols[i], 470, SKTextAlign.Left, hf, text);
        c.DrawLine(60, 490, w - 60, 490, line);
        var y = 545f;
        using var red = new SKPaint { IsAntialias = true, Color = new SKColor(200, 30, 30) };
        foreach (var r in rows)
        {
            c.DrawText(r.Test, cols[0], y, SKTextAlign.Left, f, text);
            c.DrawText(r.Result, cols[1], y, SKTextAlign.Left, Font(28, true), string.IsNullOrEmpty(r.Flag) ? text : red);
            c.DrawText(r.Unit, cols[2], y, SKTextAlign.Left, f, text);
            c.DrawText(r.Reference, cols[3], y, SKTextAlign.Left, Font(22), grey);
            c.DrawText(r.Flag, cols[4], y, SKTextAlign.Left, Font(24, true), red);
            y += 62;
        }
        c.DrawLine(60, y, w - 60, y, line);
        c.DrawText("Electronically verified. Sample report generated for the InPatient demo - fictional patient.", 60, h - 80, SKTextAlign.Left, Font(22), grey);
        return Encode(surface);
    }

    public static byte[] Prescription(string hospital, string patient, string ageSex, string diagnosis, IReadOnlyList<string> lines, string doctor, DateTime localDate)
    {
        const int w = 1240, h = 1500;
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        var c = surface.Canvas;
        c.Clear(new SKColor(255, 253, 246));
        using var ink = new SKPaint { IsAntialias = true, Color = new SKColor(25, 45, 110) };
        using var text = new SKPaint { IsAntialias = true, Color = new SKColor(20, 30, 30) };
        using var teal = new SKPaint { IsAntialias = true, Color = Teal };
        using var rule = new SKPaint { IsAntialias = true, Color = Teal, StrokeWidth = 3, Style = SKPaintStyle.Stroke };
        c.DrawText(hospital, 60, 90, SKTextAlign.Left, Font(44, true), teal);
        c.DrawText(doctor, 60, 140, SKTextAlign.Left, Font(28, true), text);
        c.DrawText("In-patient prescription", 60, 180, SKTextAlign.Left, Font(24), text);
        c.DrawLine(60, 205, w - 60, 205, rule);
        var f = Font(28);
        c.DrawText($"Name: {patient}", 60, 260, SKTextAlign.Left, f, text);
        c.DrawText($"Age/Sex: {ageSex}", 760, 260, SKTextAlign.Left, f, text);
        c.DrawText($"Date: {localDate:dd/MM/yyyy}", 760, 305, SKTextAlign.Left, f, text);
        c.DrawText($"Dx: {diagnosis}", 60, 305, SKTextAlign.Left, f, text);
        c.DrawText("Rx", 60, 420, SKTextAlign.Left, Font(80, true, "Times New Roman", italic: true), ink);
        var hand = Font(36, false, "Segoe Print");
        var y = 520f;
        var n = 1;
        foreach (var l in lines) { c.DrawText($"{n++}. {l}", 90, y, SKTextAlign.Left, hand, ink); y += 80; }
        c.DrawText("~ signature ~", w - 360, h - 220, SKTextAlign.Left, Font(36, false, "Segoe Print"), ink);
        c.DrawText(doctor, w - 360, h - 170, SKTextAlign.Left, Font(24), text);
        return Encode(surface);
    }

    public static byte[] WoundImage(string caption)
    {
        const int w = 1200, h = 900;
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        var c = surface.Canvas;
        using (var bg = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h),
                   [new SKColor(196, 140, 108), new SKColor(168, 112, 84)], SKShaderTileMode.Clamp) })
            c.DrawRect(0, 0, w, h, bg);
        using var halo = new SKPaint { IsAntialias = true, Color = new SKColor(205, 110, 100, 160), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 40) };
        c.DrawOval(new SKRect(330, 250, 870, 650), halo);
        using var bed = new SKPaint { IsAntialias = true, Color = new SKColor(178, 48, 52) };
        c.DrawOval(new SKRect(420, 320, 780, 580), bed);
        using var slough = new SKPaint { IsAntialias = true, Color = new SKColor(226, 200, 120, 200) };
        c.DrawOval(new SKRect(520, 400, 640, 470), slough);
        using var ruler = new SKPaint { Color = SKColors.White };
        c.DrawRect(80, h - 140, 520, 50, ruler);
        using var tick = new SKPaint { Color = SKColors.Black, StrokeWidth = 2 };
        for (var i = 0; i <= 10; i++) c.DrawLine(80 + i * 52, h - 140, 80 + i * 52, h - (i % 5 == 0 ? 100 : 115), tick);
        using var label = new SKPaint { IsAntialias = true, Color = SKColors.White };
        c.DrawText(caption, 80, 80, SKTextAlign.Left, Font(36, true), label);
        c.DrawText("0        5 cm      10", 80, h - 50, SKTextAlign.Left, Font(26), label);
        c.DrawText("Illustration for demo", w - 80, h - 50, SKTextAlign.Right, Font(22), label);
        return Encode(surface);
    }

    public static byte[] ChestXrayImage()
    {
        const int w = 1000, h = 1100;
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        var c = surface.Canvas;
        c.Clear(new SKColor(12, 12, 14));
        using var lung = new SKPaint { IsAntialias = true, Color = new SKColor(70, 70, 76), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 25) };
        c.DrawOval(new SKRect(170, 220, 470, 900), lung);
        c.DrawOval(new SKRect(530, 220, 830, 900), lung);
        using var haze = new SKPaint { IsAntialias = true, Color = new SKColor(150, 150, 155, 120), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 45) };
        c.DrawOval(new SKRect(560, 620, 820, 880), haze); // right lower zone shadowing (patient's left on film)
        using var bone = new SKPaint { IsAntialias = true, Color = new SKColor(200, 200, 205, 170), Style = SKPaintStyle.Stroke, StrokeWidth = 14 };
        for (var i = 0; i < 9; i++)
        {
            var y = 260 + i * 70;
            using var left = new SKPath(); left.MoveTo(480, y); left.QuadTo(220, y - 40, 170, y + 60); c.DrawPath(left, bone);
            using var right = new SKPath(); right.MoveTo(520, y); right.QuadTo(780, y - 40, 830, y + 60); c.DrawPath(right, bone);
        }
        using var spine = new SKPaint { Color = new SKColor(185, 185, 190, 200) };
        c.DrawRect(475, 160, 50, 820, spine);
        using var heart = new SKPaint { IsAntialias = true, Color = new SKColor(190, 190, 195, 150), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 20) };
        c.DrawOval(new SKRect(430, 560, 700, 860), heart);
        using var label = new SKPaint { IsAntialias = true, Color = SKColors.White };
        c.DrawText("PORTABLE AP CHEST", 40, 60, SKTextAlign.Left, Font(30, true), label);
        c.DrawText("R", 60, 140, SKTextAlign.Left, Font(48, true), label);
        c.DrawText("Illustration for demo", w - 40, h - 40, SKTextAlign.Right, Font(22), label);
        return Encode(surface);
    }
}
