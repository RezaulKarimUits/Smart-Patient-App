using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using System.Security.Cryptography;
using System.Text;
using InPatient.Api.Data;
using InPatient.Api.Data.Entities;
using InPatient.Api.Domain;
using InPatient.Api.Dtos;
using InPatient.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InPatient.Api.Services;

public sealed class StorageOptions
{
    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";
    /// <summary>
    /// Optional container-level SAS URL (https://account.blob.core.windows.net/container?sp=...). When set, all hospitals
    /// share that one private container under a "hospital-{id}/" prefix, and downloads go through short-lived signed API
    /// links, because a container SAS cannot mint per-blob SAS URLs. Keep it in user-secrets, never in source control.
    /// </summary>
    public string ContainerSasUrl { get; set; } = "";
    /// <summary>Host phones use to reach blob storage (e.g. http://192.168.0.10:10000). Empty in Development = derive from the API request host.</summary>
    public string PublicBlobEndpoint { get; set; } = "";
    public int SasMinutes { get; set; } = 10;
}

/// <summary>
/// Blob storage behind an interface. With a connection string: one private container per hospital (hospital-{id}).
/// With a container SAS URL: one shared private container, blobs prefixed "hospital-{id}/".
/// </summary>
public interface IFileStore
{
    /// <summary>True when the store can mint per-blob read SAS URLs (needs the account key).</summary>
    bool SupportsReadSas { get; }
    Task UploadAsync(int hospitalId, string blobPath, Stream content, string contentType, CancellationToken ct = default);
    Task<BinaryData> DownloadAsync(int hospitalId, string blobPath, CancellationToken ct = default);
    Task<bool> ExistsAsync(int hospitalId, string blobPath, CancellationToken ct = default);
    Uri GetReadSasUri(int hospitalId, string blobPath, DateTimeOffset expiresOn);
    Task EnsureContainerAsync(int hospitalId, CancellationToken ct = default);
}

public sealed class AzureBlobFileStore : IFileStore
{
    private readonly ILogger<AzureBlobFileStore> log;
    private readonly BlobServiceClient? _service;
    private readonly BlobContainerClient? _shared;

    public AzureBlobFileStore(IOptions<StorageOptions> options, ILogger<AzureBlobFileStore> log)
    {
        this.log = log;
        var clientOptions = new BlobClientOptions
        {
            // Fail fast when storage is unreachable instead of retrying for minutes.
            Retry = { MaxRetries = 2, Delay = TimeSpan.FromMilliseconds(300), NetworkTimeout = TimeSpan.FromSeconds(20) }
        };
        var sasUrl = options.Value.ContainerSasUrl?.Trim();
        if (!string.IsNullOrEmpty(sasUrl)) _shared = new BlobContainerClient(new Uri(sasUrl), clientOptions);
        else _service = new BlobServiceClient(options.Value.ConnectionString, clientOptions);
    }

    public static string ContainerName(int hospitalId) => $"hospital-{hospitalId}";

    public bool SupportsReadSas => _shared is null;

    private BlobContainerClient Container(int hospitalId) => _shared ?? _service!.GetBlobContainerClient(ContainerName(hospitalId));

    /// <summary>In shared-container mode each hospital's blobs live under its own "hospital-{id}/" prefix.</summary>
    private BlobClient Blob(int hospitalId, string blobPath)
        => Container(hospitalId).GetBlobClient(_shared is null ? blobPath : $"{ContainerName(hospitalId)}/{blobPath}");

    public async Task EnsureContainerAsync(int hospitalId, CancellationToken ct = default)
    {
        if (_shared is not null) return; // provisioned in Azure; a container SAS cannot create containers
        await Guard(() => Container(hospitalId).CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct));
    }

    public async Task UploadAsync(int hospitalId, string blobPath, Stream content, string contentType, CancellationToken ct = default)
    {
        await EnsureContainerAsync(hospitalId, ct);
        await Guard(() => Blob(hospitalId, blobPath)
            .UploadAsync(content, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct));
    }

    public async Task<BinaryData> DownloadAsync(int hospitalId, string blobPath, CancellationToken ct = default)
    {
        var res = await Guard(() => Blob(hospitalId, blobPath).DownloadContentAsync(ct));
        return res.Value.Content;
    }

    public async Task<bool> ExistsAsync(int hospitalId, string blobPath, CancellationToken ct = default)
        => (await Guard(() => Blob(hospitalId, blobPath).ExistsAsync(ct))).Value;

    public Uri GetReadSasUri(int hospitalId, string blobPath, DateTimeOffset expiresOn)
    {
        var blob = Blob(hospitalId, blobPath);
        if (!SupportsReadSas || !blob.CanGenerateSasUri) throw new ApiException(503, "Storage account key not available for SAS");
        return blob.GenerateSasUri(BlobSasPermissions.Read, expiresOn);
    }

    private async Task<T> Guard<T>(Func<Task<T>> op)
    {
        try { return await op(); }
        catch (RequestFailedException ex) when (ex.Status == 404) { throw ApiException.NotFound("File not found in storage"); }
        catch (Exception ex) when (ex is RequestFailedException or HttpRequestException or AggregateException)
        {
            log.LogWarning("Blob storage unavailable: {Message}", ex.Message);
            throw new ApiException(503, "File storage unavailable (is Azurite running / is the container SAS valid?)");
        }
    }
}

public sealed class FileService(InPatientDbContext db, IFileStore store, IClock clock, ICurrentUser me, AdmissionService admissions,
    IOptions<StorageOptions> options, IHttpContextAccessor http, IHostEnvironment env, FileLinkSigner links)
{
    public const long MaxImageBytes = 10 * 1024 * 1024;
    public const long MaxAudioBytes = 20 * 1024 * 1024;

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "jpg", ["image/jpg"] = "jpg", ["image/png"] = "png", ["image/webp"] = "webp", ["image/heic"] = "heic", ["image/heif"] = "heif",
    };
    private static readonly Dictionary<string, string> AudioExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["m4a"] = "audio/mp4", ["mp3"] = "audio/mpeg", ["mp4"] = "audio/mp4", ["wav"] = "audio/wav", ["webm"] = "audio/webm", ["ogg"] = "audio/ogg",
    };

    public static string BlobPath(int admissionId, string kind, string ext) => $"admissions/{admissionId}/{kind.ToLowerInvariant()}/{Guid.NewGuid():N}.{ext}";

    /// <summary>Validates and stores an image/PDF upload for an admission.</summary>
    public async Task<StoredFile> UploadAsync(int admissionId, IFormFile file, string kind, string? caption, int? prescriptionId, CancellationToken ct)
    {
        if (!FileKinds.All.Contains(kind) || kind == FileKinds.Audio) throw ApiException.BadRequest("Kind must be Prescription, TestReport, Progress or Other");
        if (file is null || file.Length == 0) throw ApiException.BadRequest("File is empty");
        if (file.Length > MaxImageBytes) throw ApiException.BadRequest("File is larger than 10 MB");

        var contentType = (file.ContentType ?? "").ToLowerInvariant();
        string ext;
        if (ImageTypes.TryGetValue(contentType, out var e)) ext = e;
        else if (contentType == "application/pdf" && kind is FileKinds.TestReport or FileKinds.Other or FileKinds.Prescription) ext = "pdf";
        else
        {
            // Some phones send application/octet-stream - fall back to the file extension.
            var fx = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();
            (ext, contentType) = fx switch
            {
                "jpg" or "jpeg" => ("jpg", "image/jpeg"),
                "png" => ("png", "image/png"),
                "webp" => ("webp", "image/webp"),
                "heic" => ("heic", "image/heic"),
                _ => throw ApiException.BadRequest("Only JPEG, PNG, WebP, HEIC images (or PDF reports) are accepted")
            };
        }

        var admission = await admissions.GetAccessibleAsync(admissionId, ct);
        await using var stream = file.OpenReadStream();
        return await SaveAsync(admission, stream, contentType, ext, file.Length, kind, caption, prescriptionId, ct);
    }

    /// <summary>Validates and stores a voice recording (m4a/mp3/mp4/wav/webm/ogg, ≤ 20 MB). Returns the file and its bytes.</summary>
    public async Task<(StoredFile File, BinaryData Bytes, string FileName)> UploadAudioAsync(Admission admission, IFormFile audio, CancellationToken ct)
    {
        if (audio is null || audio.Length == 0) throw ApiException.BadRequest("Audio file is empty");
        if (audio.Length > MaxAudioBytes) throw ApiException.BadRequest("Audio is larger than 20 MB");
        var ext = Path.GetExtension(audio.FileName).TrimStart('.').ToLowerInvariant();
        if (!AudioExt.TryGetValue(ext, out var contentType))
            throw ApiException.BadRequest("Audio must be m4a, mp3, mp4, wav, webm or ogg");

        using var ms = new MemoryStream();
        await audio.CopyToAsync(ms, ct);
        var bytes = BinaryData.FromBytes(ms.ToArray());
        var stored = await SaveAsync(admission, bytes.ToStream(), contentType, ext, audio.Length, FileKinds.Audio, "Voice capture", null, ct);
        return (stored, bytes, $"capture.{ext}");
    }

    public async Task<StoredFile> SaveAsync(Admission admission, Stream content, string contentType, string ext, long size, string kind, string? caption,
        int? prescriptionId, CancellationToken ct)
    {
        if (prescriptionId is not null && !await db.Prescriptions.AnyAsync(p => p.Id == prescriptionId && p.AdmissionId == admission.Id, ct))
            throw ApiException.BadRequest("Prescription does not belong to this admission");
        var path = BlobPath(admission.Id, kind, ext);
        await store.UploadAsync(admission.HospitalId, path, content, contentType, ct);
        var f = new StoredFile
        {
            HospitalId = admission.HospitalId, AdmissionId = admission.Id, PrescriptionId = prescriptionId, Kind = kind,
            Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(), BlobPath = path, ContentType = contentType, SizeBytes = size,
            UploadedBy = me.UserId, UploadedAt = clock.UtcNow
        };
        db.StoredFiles.Add(f);
        await db.SaveChangesAsync(ct);
        return f;
    }

    public async Task<List<FileDto>> ListAsync(int admissionId, string? kind, CancellationToken ct)
    {
        await admissions.GetAccessibleAsync(admissionId, ct);
        var q = db.StoredFiles.Where(f => f.AdmissionId == admissionId && f.Kind != FileKinds.Audio);
        if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(f => f.Kind == kind);
        // Patients see test reports only once a consultant has signed off the values taken from them.
        if (me.Role == Roles.Patient)
            q = q.Where(f => f.Kind != FileKinds.TestReport || f.LabResults.Any(l => l.SignedOffAt != null));
        var files = await q.OrderByDescending(f => f.UploadedAt).Include(f => f.UploadedByNavigation).ToListAsync(ct);
        return files.Select(ToDto).ToList();
    }

    public async Task<FileDto> GetAsync(int fileId, CancellationToken ct)
    {
        var f = await db.StoredFiles.Include(x => x.UploadedByNavigation).FirstOrDefaultAsync(x => x.Id == fileId, ct) ?? throw ApiException.NotFound("File not found");
        if (f.AdmissionId is not null) await admissions.GetAccessibleAsync(f.AdmissionId.Value, ct);
        else if (me.Role == Roles.Patient) throw ApiException.Forbidden();
        return ToDto(f);
    }

    public Task<BinaryData> ReadAsync(StoredFile f, CancellationToken ct) => store.DownloadAsync(f.HospitalId, f.BlobPath, ct);

    /// <summary>Metadata plus a read-only SAS URL valid for at most 10 minutes.</summary>
    public FileDto ToDto(StoredFile f)
    {
        string? url = null;
        DateTimeOffset? expires = null;
        try
        {
            expires = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.Value.SasMinutes, 1, 10));
            url = store.SupportsReadSas
                ? PublicUrl(store.GetReadSasUri(f.HospitalId, f.BlobPath, expires.Value)).ToString()
                : SignedContentUrl(f.Id, expires.Value);
        }
        catch (ApiException) { url = null; expires = null; }
        return new FileDto(f.Id, f.Kind, f.Caption, f.ContentType, f.SizeBytes, f.UploadedAt, f.UploadedByNavigation?.Name, f.AdmissionId, f.PrescriptionId,
            url, expires?.UtcDateTime);
    }

    /// <summary>
    /// Short-lived link to GET /api/files/{id}/content, HMAC-signed so an image view can load it without a bearer header.
    /// Used with a container SAS, which must never reach the phone (it is long-lived and writable).
    /// </summary>
    private string? SignedContentUrl(int fileId, DateTimeOffset expiresOn)
    {
        var req = http.HttpContext?.Request;
        if (req is null) return null;
        var exp = expiresOn.ToUnixTimeSeconds();
        return $"{req.Scheme}://{req.Host}/api/files/{fileId}/content?exp={exp}&sig={links.Sign(fileId, exp)}";
    }

    /// <summary>Serves a file for a valid, unexpired signed link (no user context: the signature is the authorization).</summary>
    public async Task<(BinaryData Bytes, string ContentType)> ReadSignedAsync(int fileId, long exp, string? sig, CancellationToken ct)
    {
        if (!links.Verify(fileId, exp, sig)) throw new ApiException(403, "Link is invalid or has expired");
        var f = await db.StoredFiles.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Id == fileId, ct)
            ?? throw ApiException.NotFound("File not found");
        return (await store.DownloadAsync(f.HospitalId, f.BlobPath, ct), f.ContentType);
    }

    /// <summary>Rewrites the blob host so a phone can reach Azurite on the dev machine (the SAS signature doesn't cover the host).</summary>
    private Uri PublicUrl(Uri sas)
    {
        var configured = options.Value.PublicBlobEndpoint;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var pub = new Uri(configured);
            return new UriBuilder(sas) { Scheme = pub.Scheme, Host = pub.Host, Port = pub.Port }.Uri;
        }
        var reqHost = http.HttpContext?.Request.Host.Host;
        if (env.IsDevelopment() && sas.IsLoopback && !string.IsNullOrEmpty(reqHost) && reqHost is not ("localhost" or "127.0.0.1"))
            return new UriBuilder(sas) { Host = reqHost }.Uri;
        return sas;
    }
}

/// <summary>HMAC-SHA256 signer for short-lived file download links (key derived from Jwt:Key).</summary>
public sealed class FileLinkSigner(IOptions<JwtOptions> jwt)
{
    private readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes("file-link:" + jwt.Value.Key));

    public string Sign(int fileId, long exp)
        => Base64Url(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{fileId}:{exp}")));

    public bool Verify(int fileId, long exp, string? sig)
    {
        if (string.IsNullOrEmpty(sig)) return false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (exp < now || exp > now + 11 * 60) return false; // links never live longer than ~10 minutes
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(fileId, exp)), Encoding.ASCII.GetBytes(sig));
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
