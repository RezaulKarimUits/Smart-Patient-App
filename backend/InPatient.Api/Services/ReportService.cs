using InPatient.Api.Data;
using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Infrastructure;
using InPatient.Api.Services.Ai;
using InPatient.Api.Services.Rules;
using Microsoft.EntityFrameworkCore;

namespace InPatient.Api.Services;

public record ExtractedResultDto(string TestName, string RawTestName, decimal? Value, string? Unit, decimal? RefLow, decimal? RefHigh, string Flag, string? Note);

public record ReportExtractResponse(FileDto File, IReadOnlyList<ExtractedResultDto> Results, IReadOnlyList<Issue> Warnings, string? ReportDate, string? ClarificationQuestion);

public record ReportConfirmRequest(int FileId, IReadOnlyList<LabInput> Results);

/// <summary>Report photo → blob → vision model (strict schema) → user reviews → confirm saves LabResults and runs monitoring.</summary>
public sealed class ReportService(InPatientDbContext db, IStructuredAi ai, FileService files, ClinicalService clinical, AdmissionService admissions)
{
    public async Task<ReportExtractResponse> ExtractAsync(int admissionId, IFormFile image, CancellationToken ct)
    {
        if (!ai.IsConfigured) throw new AiNotConfiguredException();
        if (image is null || !(image.ContentType?.StartsWith("image/") ?? false) && !IsImageName(image?.FileName))
            throw ApiException.BadRequest("Send a photo (JPEG/PNG) of the report");
        await admissions.GetActiveAsync(admissionId, ct);

        var stored = await files.UploadAsync(admissionId, image!, FileKinds.TestReport, "Lab report photo", null, ct); // kept for audit
        var bytes = await files.ReadAsync(stored, ct);
        var raw = await ai.GenerateAsync<ReportDraft>(new AiJsonRequest("lab_report", AiSchemas.ReportExtraction, AiPrompts.ReportExtraction + "\n" + AiPrompts.Safety,
            "Extract every result from this laboratory report photo.", bytes, stored.ContentType), ct);

        var issues = new List<Issue>();
        var results = new List<ExtractedResultDto>();
        for (var i = 0; i < raw.Results.Count; i++)
        {
            var r = raw.Results[i];
            var n = LabCatalog.Normalize(r.TestName, r.Value, r.Unit, r.RefLow, r.RefHigh);
            if (r.Value is null) issues.Add(Issue.Block($"results[{i}]", $"{r.TestName}: value unreadable — type it or remove the row"));
            if (ClinicalValidation.CheckLab($"results[{i}]", n.TestName, n.Value) is { } li) issues.Add(li);
            if (LabCatalog.Find(r.TestName) is null) issues.Add(Issue.Warn($"results[{i}]", $"\"{r.TestName}\" is not a known test — check the name"));
            results.Add(new ExtractedResultDto(n.TestName, r.TestName, n.Value, n.Unit, n.RefLow, n.RefHigh, n.Flag, n.Note));
        }
        if (results.Count == 0) issues.Add(Issue.Block("results", "No results could be read — retake the photo in good light"));
        await db.Entry(stored).Reference(f => f.UploadedByNavigation).LoadAsync(ct);
        return new ReportExtractResponse(files.ToDto(stored), results, issues, raw.ReportDate, raw.ClarificationQuestion);
    }

    public async Task<LabsSaveResponse> ConfirmAsync(int admissionId, ReportConfirmRequest req, CancellationToken ct)
    {
        var file = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == req.FileId && f.AdmissionId == admissionId && f.Kind == FileKinds.TestReport, ct)
            ?? throw ApiException.BadRequest("Report photo not found for this admission");
        if (await db.LabResults.AnyAsync(l => l.StoredFileId == file.Id, ct)) throw ApiException.Conflict("Results from this report were already saved");
        return await clinical.RecordLabsAsync(admissionId, req.Results, Sources.Photo, file.Id, ct);
    }

    private static bool IsImageName(string? name) => Path.GetExtension(name ?? "").ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".heic";
}
