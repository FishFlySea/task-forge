using System.Net;
using System.Text;
using System.Text.Json;
using TaskForge.Application;
using TaskForge.Core;
using TaskForge.Infrastructure;

namespace TaskForge.Infrastructure.Tests;

public sealed class OllamaClientTests
{
    [Fact]
    public async Task Structured_completion_sends_schema_and_deserializes_result()
    {
        var plan = new PlanResult
        {
            Summary = "Find bindings",
            SearchTerms = ["PropertyBinding"],
            LikelyAreas = ["Services"],
            AcceptanceCriteria = ["Bindings are removed"]
        };

        var resultJson = JsonSerializer.Serialize(
            plan,
            new JsonSerializerOptions(
                JsonSerializerDefaults.Web));

        var responseJson = JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    role = "assistant",
                    content = resultJson
                }
            });

        var handler = new StubHandler(
            responseJson);

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(
                "http://localhost:11434/")
        };

        var sut = new OllamaClient(
            httpClient,
            "test-model");

        var result =
            await sut.CompleteStructuredAsync<PlanResult>(
                new LocalLlmRequest(
                    "system",
                    "user"),
                CancellationToken.None);

        Assert.Equal(
            plan.Summary,
            result.Summary);

        Assert.NotNull(
            handler.LastRequestBody);

        using var request =
            JsonDocument.Parse(
                handler.LastRequestBody);

        var root = request.RootElement;

        Assert.Equal(
            "test-model",
            root.GetProperty("model").GetString());

        Assert.False(
            root.GetProperty("stream").GetBoolean());

        Assert.Equal(
            JsonValueKind.Object,
            root.GetProperty("format").ValueKind);
    }

    private sealed class StubHandler(
        string responseJson) : HttpMessageHandler
    {
        private readonly string _responseJson = responseJson;

        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestBody =
                await request.Content!.ReadAsStringAsync(
                    cancellationToken);

            return new HttpResponseMessage(
                HttpStatusCode.OK)
            {
                Content = new StringContent(
                    _responseJson,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
