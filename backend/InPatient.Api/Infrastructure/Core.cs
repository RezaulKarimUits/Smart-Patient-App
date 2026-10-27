using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;

namespace InPatient.Api.Infrastructure;

/// <summary>Throw from services; rendered as <c>{ "error": message }</c> with the given status code.</summary>
public class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public static ApiException NotFound(string what = "Not found") => new(StatusCodes.Status404NotFound, what);
    public static ApiException BadRequest(string msg) => new(StatusCodes.Status400BadRequest, msg);
    public static ApiException Conflict(string msg) => new(StatusCodes.Status409Conflict, msg);
    public static ApiException Forbidden(string msg = "Forbidden") => new(StatusCodes.Status403Forbidden, msg);
    public static ApiException Unprocessable(string msg) => new(StatusCodes.Status422UnprocessableEntity, msg);
}

public class AiNotConfiguredException() : ApiException(StatusCodes.Status503ServiceUnavailable, "AI not configured");

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        int status;
        string message;
        switch (ex)
        {
            case ApiException api:
                status = api.StatusCode; message = api.Message; break;
            case BadHttpRequestException bad:
                status = bad.StatusCode; message = bad.Message; break;
            case OperationCanceledException when ctx.RequestAborted.IsCancellationRequested:
                status = 499; message = "Request cancelled"; break;
            case TimeoutException:
                status = StatusCodes.Status504GatewayTimeout; message = "Upstream timed out"; break;
            default:
                logger.LogError(ex, "Unhandled error on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
                status = StatusCodes.Status500InternalServerError;
                message = env.IsDevelopment() ? $"Server error: {ex.GetBaseException().Message}" : "Server error";
                break;
        }
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new { error = message }, ct);
        return true;
    }
}

/// <summary>Application clock. In Development it can be advanced (POST /api/dev/advance-time) to demo escalations.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
    TimeSpan Offset { get; }
    void Advance(TimeSpan by);
    void Reset();
}

public sealed class AppClock : IClock
{
    private long _offsetTicks;
    public DateTime UtcNow => DateTime.SpecifyKind(DateTime.UtcNow.AddTicks(Interlocked.Read(ref _offsetTicks)), DateTimeKind.Utc);
    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));
    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);
    public void Reset() => Interlocked.Exchange(ref _offsetTicks, 0);
}

public static class Tokens
{
    /// <summary>Random URL-safe token (no personal data) for bed and wristband QR codes.</summary>
    public static string NewQrToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}

/// <summary>SQL Server datetime2 comes back as Kind=Unspecified; every stored time is UTC, so always emit ISO-8601 with 'Z'.</summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dt = reader.GetDateTime();
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        writer.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"));
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = Create();

    public static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        o.Converters.Add(new UtcDateTimeConverter());
        return o;
    }
}

/// <summary>
/// Turns ASP.NET model-validation failures (missing required fields, wrong JSON types) into one short, readable
/// <c>{ error }</c> message, e.g. "Item 1: drug name is required", instead of a ProblemDetails blob.
/// </summary>
public static class ValidationMessages
{
    public static string Describe(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary state)
    {
        var parts = state.Where(kv => kv.Value is { Errors.Count: > 0 })
            .Select(kv => Describe(kv.Key, kv.Value!.Errors[0]))
            .Distinct().ToList();
        const string generic = "Some details are missing or in the wrong format";
        var specific = parts.Where(p => p != generic).Take(3).ToList();
        return specific.Count > 0 ? string.Join(". ", specific) : generic;
    }

    private static string Describe(string key, Microsoft.AspNetCore.Mvc.ModelBinding.ModelError error)
    {
        // Keys look like "Items[0].DrugName", "$.items[0].pulse" (JSON errors) or "req" (the whole body parameter).
        var path = key.StartsWith('$') ? key.TrimStart('$', '.') : key;
        if (path.Length == 0 || (!path.Contains('.') && !path.Contains('[') && char.IsLower(path[0]) && !key.StartsWith('$')))
            return "Some details are missing or in the wrong format";

        var item = System.Text.RegularExpressions.Regex.Matches(path, @"\[(\d+)\]");
        var prefix = item.Count > 0 ? $"Item {int.Parse(item[^1].Groups[1].Value) + 1}: " : "";
        var field = Humanize(path[(path.LastIndexOf('.') + 1)..].Split('[')[0]);
        var msg = error.ErrorMessage ?? "";

        if (msg.Contains("is required", StringComparison.OrdinalIgnoreCase)) return $"{prefix}{field} is required";
        if (error.Exception is not null || msg.Contains("could not be converted") || msg.Contains("JSON")) return $"{prefix}{field} has an invalid value";
        return prefix + (string.IsNullOrWhiteSpace(msg) ? $"{field} is invalid" : msg.TrimEnd('.'));
    }

    private static string Humanize(string name)
    {
        if (name.Length == 0) return "A field";
        if (name.Equals("spo2", StringComparison.OrdinalIgnoreCase)) return "SpO2";
        var spaced = System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z0-9])([A-Z])", " $1").ToLowerInvariant();
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
