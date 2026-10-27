using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using InPatient.Api.Infrastructure;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;

namespace InPatient.Api.Services.Ai;

public sealed class AiOptions
{
    public string ApiKey { get; set; } = "";
    /// <summary>
    /// OpenAI-compatible endpoint. Empty = OpenAI. For Groq: https://api.groq.com/openai/v1
    /// (models: ChatModel openai/gpt-oss-120b, VisionModel qwen/qwen3.8-27b, TranscriptionModel whisper-large-v3).
    /// </summary>
    public string BaseUrl { get; set; } = "";
    public string ChatModel { get; set; } = "gpt-4o-mini";
    /// <summary>Model for requests with an image (report photos). Empty = ChatModel.</summary>
    public string VisionModel { get; set; } = "";
    public string TranscriptionModel { get; set; } = "gpt-4o-transcribe";
    public string TranscriptionLanguage { get; set; } = "bn";
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Ai:ApiKey, falling back to the OPENAI_API_KEY environment variable.</summary>
    public string? ResolveKey() => !string.IsNullOrWhiteSpace(ApiKey) ? ApiKey : Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    public OpenAIClientOptions ClientOptions(TimeSpan timeout)
    {
        var o = new OpenAIClientOptions { NetworkTimeout = timeout };
        if (!string.IsNullOrWhiteSpace(BaseUrl)) o.Endpoint = new Uri(BaseUrl.TrimEnd('/') + "/");
        return o;
    }
}

/// <summary>A strict JSON-schema request. The schema must follow OpenAI strict rules (all properties required, additionalProperties false).</summary>
public record AiJsonRequest(string SchemaName, string JsonSchema, string SystemPrompt, string UserPrompt, BinaryData? Image = null, string? ImageMediaType = null);

public interface IStructuredAi
{
    bool IsConfigured { get; }
    string Model { get; }
    /// <summary>Returns the parsed object; retries once on invalid JSON; 30 s timeout. Throws AiNotConfiguredException with no key.</summary>
    Task<T> GenerateAsync<T>(AiJsonRequest request, CancellationToken ct = default);
}

public interface ISpeechToText
{
    bool IsConfigured { get; }
    string Model { get; }
    Task<string> TranscribeAsync(BinaryData audio, string fileName, string hint, CancellationToken ct = default);
}

public sealed class OpenAiStructuredAi(IOptions<AiOptions> options, ILogger<OpenAiStructuredAi> log) : IStructuredAi
{
    private readonly string? _key = options.Value.ResolveKey();
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_key);
    public string Model => options.Value.ChatModel;

    private ChatClient Client(string model) => new(model, new ApiKeyCredential(_key!),
        options.Value.ClientOptions(TimeSpan.FromSeconds(options.Value.TimeoutSeconds)));

    public async Task<T> GenerateAsync<T>(AiJsonRequest req, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new AiNotConfiguredException();
        var model = req.Image is not null && !string.IsNullOrWhiteSpace(options.Value.VisionModel) ? options.Value.VisionModel : Model;
        var client = Client(model);

        var user = new List<ChatMessageContentPart> { ChatMessageContentPart.CreateTextPart(req.UserPrompt) };
        if (req.Image is not null)
            user.Add(ChatMessageContentPart.CreateImagePart(req.Image, req.ImageMediaType ?? "image/jpeg", ChatImageDetailLevel.High));
        var messages = new List<ChatMessage> { new SystemChatMessage(req.SystemPrompt), new UserChatMessage(user) };
        var opts = new ChatCompletionOptions
        {
            Temperature = 0.1f,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(req.SchemaName, BinaryData.FromString(req.JsonSchema), jsonSchemaIsStrict: true),
        };

        Exception? last = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            var sw = Stopwatch.StartNew();
            ChatCompletion completion;
            try
            {
                completion = (await client.CompleteChatAsync(messages, opts, cts.Token)).Value;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                log.LogWarning("AI {Model} {Schema} timed out after {Ms} ms", model, req.SchemaName, sw.ElapsedMilliseconds);
                throw new ApiException(504, "AI timed out — try again");
            }
            catch (ClientResultException ex) when (ex.Status == 400 && attempt == 1 && ex.Message.Contains("json_validate_failed"))
            {
                // Groq returns 400 when the model's output fails its schema check: treat as invalid JSON and retry once.
                log.LogWarning("AI {Model} {Schema} output failed provider schema validation (attempt {Attempt}); retrying", model, req.SchemaName, attempt);
                last = new JsonException("json_validate_failed");
                continue;
            }
            catch (ClientResultException ex)
            {
                log.LogWarning("AI {Model} {Schema} failed: HTTP {Status} {Reason}", model, req.SchemaName, ex.Status, ProviderReason(ex));
                throw new ApiException(502, ex.Status switch
                {
                    401 => "AI key rejected",
                    429 => "AI is busy (rate limit) — try again in a minute",
                    _ => $"AI request failed ({ex.Status})",
                });
            }
            sw.Stop();
            log.LogInformation("AI chat model={Model} schema={Schema} latencyMs={Ms} inputTokens={In} outputTokens={Out} attempt={Attempt}",
                model, req.SchemaName, sw.ElapsedMilliseconds, completion.Usage?.InputTokenCount, completion.Usage?.OutputTokenCount, attempt);

            if (!string.IsNullOrEmpty(completion.Refusal)) throw new ApiException(422, "AI declined to process this input");
            var text = completion.Content.Count > 0 ? completion.Content[0].Text : null;
            try
            {
                var value = JsonSerializer.Deserialize<T>(text ?? "", Json.Options);
                if (value is not null) return value;
                last = new JsonException("Empty JSON");
            }
            catch (JsonException ex) { last = ex; }
            log.LogWarning("AI returned invalid JSON for {Schema} (attempt {Attempt})", req.SchemaName, attempt);
        }
        throw new ApiException(502, $"AI returned an invalid response ({last?.GetType().Name})");
    }

    /// <summary>The provider's error code/message, without the failed generation (which may echo clinical text).</summary>
    internal static string ProviderReason(ClientResultException ex)
    {
        var m = ex.Message;
        var cut = m.IndexOf("failed_generation", StringComparison.Ordinal);
        if (cut > 0) m = m[..cut];
        m = m.Replace('\n', ' ').Replace('\r', ' ');
        return m.Length > 300 ? m[..300] : m;
    }
}

public sealed class OpenAiSpeechToText(IOptions<AiOptions> options, ILogger<OpenAiSpeechToText> log) : ISpeechToText
{
    private readonly string? _key = options.Value.ResolveKey();
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_key);
    public string Model => options.Value.TranscriptionModel;

    public async Task<string> TranscribeAsync(BinaryData audio, string fileName, string hint, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new AiNotConfiguredException();
        var client = new AudioClient(Model, new ApiKeyCredential(_key!),
            options.Value.ClientOptions(TimeSpan.FromSeconds(Math.Max(30, options.Value.TimeoutSeconds))));
        var opts = new AudioTranscriptionOptions
        {
            // Whisper only reads ~224 tokens of prompt (Groq rejects longer), so keep the vocabulary hint short.
            Prompt = hint.Length > 600 ? hint[..600] : hint,
            ResponseFormat = AudioTranscriptionFormat.Simple,
            Temperature = 0f,
        };
        if (!string.IsNullOrWhiteSpace(options.Value.TranscriptionLanguage)) opts.Language = options.Value.TranscriptionLanguage;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, options.Value.TimeoutSeconds)));
        var sw = Stopwatch.StartNew();
        try
        {
            using var stream = audio.ToStream();
            var result = (await client.TranscribeAudioAsync(stream, fileName, opts, cts.Token)).Value;
            log.LogInformation("AI transcription model={Model} latencyMs={Ms} audioBytes={Bytes} chars={Chars}", Model, sw.ElapsedMilliseconds, audio.ToMemory().Length, result.Text?.Length ?? 0);
            return result.Text?.Trim() ?? "";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(504, "Transcription timed out — try a shorter recording");
        }
        catch (ClientResultException ex)
        {
            log.LogWarning("AI transcription failed: HTTP {Status} {Reason}", ex.Status, OpenAiStructuredAi.ProviderReason(ex));
            throw new ApiException(502, ex.Status switch
            {
                401 => "AI key rejected",
                429 => "AI is busy (rate limit) — try again in a minute",
                _ => $"Transcription failed ({ex.Status})",
            });
        }
    }
}
