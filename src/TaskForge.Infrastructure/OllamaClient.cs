using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using TaskForge.Application;

namespace TaskForge.Infrastructure;

public sealed class OllamaClient : ILocalLlmClient
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaClient(
        HttpClient httpClient,
        string model)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _httpClient = httpClient;
        _model = model;
    }

    public async Task<T> CompleteStructuredAsync<T>(
        LocalLlmRequest request,
        CancellationToken cancellationToken)
        where T : class
    {
        var schema = JsonOptions.GetJsonSchemaAsNode(typeof(T));
        var schemaJson = schema.ToJsonString(JsonOptions);

        var payload = new
        {
            model = _model,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = request.SystemPrompt
                },
                new
                {
                    role = "user",
                    content =
                        request.UserPrompt
                        + "\n\nReturn only JSON matching this JSON schema:\n"
                        + schemaJson
                }
            },
            stream = false,
            format = schema,
            options = new
            {
                temperature = 0
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(
            "api/chat",
            payload,
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(
                cancellationToken);

            throw new HttpRequestException(
                $"Ollama returned {(int)response.StatusCode}: {error}");
        }

        var envelope = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
            JsonOptions,
            cancellationToken);

        if (envelope?.Message?.Content is null)
        {
            throw new InvalidOperationException(
                "Ollama response did not contain message.content.");
        }

        return JsonSerializer.Deserialize<T>(
                   envelope.Message.Content,
                   JsonOptions)
               ?? throw new InvalidOperationException(
                   $"Ollama returned an empty {typeof(T).Name} payload.");
    }

    private sealed record OllamaChatResponse(OllamaMessage? Message);

    private sealed record OllamaMessage(
        string? Role,
        string? Content);
}
