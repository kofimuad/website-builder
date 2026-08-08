using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WebsiteBuilder.Core.Generation;
using WebsiteBuilder.Web.Generation;

namespace WebsiteBuilder.Tests;

/// <summary>
/// Exercises the Gemini call against a stub transport. There is no live request here — the point
/// is the request we send and what we make of the reply, both of which are ours to get right.
/// </summary>
public class GeminiCompletionTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (GeminiJsonCompletion Completion, StubHandler Handler) Build(
        string body, HttpStatusCode status = HttpStatusCode.OK, GeminiOptions? options = null)
    {
        var handler = new StubHandler(status, body);
        var settings = options ?? new GeminiOptions { ApiKey = "test-key", Model = "gemini-3.6-flash" };

        var http = new HttpClient(handler) { BaseAddress = new Uri(settings.BaseUrl) };

        return (new GeminiJsonCompletion(http, Options.Create(settings)), handler);
    }

    private static string Reply(string json, long promptTokens = 1200, long outputTokens = 400) =>
        $$"""
        {
          "candidates": [
            { "content": { "role": "model", "parts": [ { "text": {{JsonSerializer.Serialize(json)}} } ] },
              "finishReason": "STOP" }
          ],
          "usageMetadata": { "promptTokenCount": {{promptTokens}}, "candidatesTokenCount": {{outputTokens}} }
        }
        """;

    [Fact]
    public async Task The_models_json_is_returned_with_its_token_usage()
    {
        var (completion, _) = Build(Reply("""{"heroHeadline":"Fast, tidy plumbing"}"""));

        var result = await completion.CompleteAsync("system", "user", SiteGenerationSchema.Build());

        Assert.Contains("Fast, tidy plumbing", result.Json);
        Assert.Equal(1200, result.InputTokens);
        Assert.Equal(400, result.OutputTokens);
    }

    [Fact]
    public async Task The_cost_is_worked_out_from_the_configured_prices()
    {
        var options = new GeminiOptions
        {
            ApiKey = "test-key",
            InputPricePerMillion = 1.50m,
            OutputPricePerMillion = 7.50m,
        };

        var (completion, _) = Build(Reply("{}", promptTokens: 1_000_000, outputTokens: 1_000_000), options: options);

        var result = await completion.CompleteAsync("system", "user", SiteGenerationSchema.Build());

        Assert.Equal(9.00m, result.EstimatedCostUsd);
    }

    [Fact]
    public async Task The_request_asks_for_json_constrained_to_the_site_schema()
    {
        var (completion, handler) = Build(Reply("{}"));

        await completion.CompleteAsync("the system prompt", "the user prompt", SiteGenerationSchema.Build());

        using var sent = JsonDocument.Parse(handler.LastBody!);
        var config = sent.RootElement.GetProperty("generationConfig");

        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
        Assert.Equal(8000, config.GetProperty("maxOutputTokens").GetInt32());

        // The schema goes across unchanged — no translation layer to be quietly wrong.
        var schema = config.GetProperty("responseSchema");
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.True(schema.GetProperty("properties").TryGetProperty("palette", out _));

        Assert.Equal(
            "the system prompt",
            sent.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal(
            "the user prompt",
            sent.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task The_model_id_is_in_the_path_so_switching_model_needs_no_code()
    {
        var (completion, handler) = Build(Reply("{}"),
            options: new GeminiOptions { ApiKey = "k", Model = "gemini-3.5-flash-lite" });

        await completion.CompleteAsync("system", "user", SiteGenerationSchema.Build());

        Assert.Contains("models/gemini-3.5-flash-lite:generateContent", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task An_error_response_carries_googles_own_explanation()
    {
        // Google says exactly which field it disliked. Swallowing that leaves a generic site and
        // no way to find out why.
        const string error =
            """{"error":{"code":400,"message":"Invalid JSON payload received. Unknown name \"foo\".","status":"INVALID_ARGUMENT"}}""";

        var (completion, _) = Build(error, HttpStatusCode.BadRequest);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => completion.CompleteAsync("system", "user", SiteGenerationSchema.Build()));

        Assert.Contains("400", exception.Message);
        Assert.Contains("Unknown name", exception.Message);
    }

    [Fact]
    public async Task A_truncated_response_says_so_rather_than_returning_half_a_site()
    {
        const string truncated =
            """
            {
              "candidates": [ { "content": { "parts": [] }, "finishReason": "MAX_TOKENS" } ],
              "usageMetadata": { "promptTokenCount": 1200, "candidatesTokenCount": 8000 }
            }
            """;

        var (completion, _) = Build(truncated);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => completion.CompleteAsync("system", "user", SiteGenerationSchema.Build()));

        Assert.Contains("MAX_TOKENS", exception.Message);
        Assert.Contains("MaxOutputTokens", exception.Message);
    }

    [Fact]
    public async Task A_filtered_response_is_an_error_not_an_empty_site()
    {
        const string blocked =
            """{"candidates":[{"finishReason":"SAFETY"}],"usageMetadata":{"promptTokenCount":900}}""";

        var (completion, _) = Build(blocked);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => completion.CompleteAsync("system", "user", SiteGenerationSchema.Build()));

        Assert.Contains("SAFETY", exception.Message);
    }

    [Fact]
    public async Task Text_split_across_parts_is_joined_back_together()
    {
        const string split =
            """
            {
              "candidates": [ { "content": { "parts": [ { "text": "{\"a\":" }, { "text": "1}" } ] },
                "finishReason": "STOP" } ],
              "usageMetadata": { "promptTokenCount": 10, "candidatesTokenCount": 5 }
            }
            """;

        var (completion, _) = Build(split);

        var result = await completion.CompleteAsync("system", "user", SiteGenerationSchema.Build());

        Assert.Equal("""{"a":1}""", result.Json);
    }

    [Fact]
    public async Task Missing_usage_metadata_does_not_lose_a_good_response()
    {
        const string noUsage =
            """{"candidates":[{"content":{"parts":[{"text":"{}"}]},"finishReason":"STOP"}]}""";

        var (completion, _) = Build(noUsage);

        var result = await completion.CompleteAsync("system", "user", SiteGenerationSchema.Build());

        Assert.Equal("{}", result.Json);
        Assert.Equal(0, result.InputTokens);
        Assert.Equal(0m, result.EstimatedCostUsd);
    }
}
