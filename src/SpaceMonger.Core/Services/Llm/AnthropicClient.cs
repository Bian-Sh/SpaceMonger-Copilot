using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpaceMonger.Core.Services.Llm;

public class AnthropicClient : ILlmClient
{
    private const string ApiBaseUrl = "https://api.anthropic.com";
    private const string ApiVersion = "2023-06-01";
    private const string Model = "claude-sonnet-4-20250514";
    private const int AnalysisMaxTokens = 8192;
    private const int ChatMaxTokens = 4096;
    private const int ValidationMaxTokens = 10;
    private static readonly TimeSpan AnalysisTimeout = TimeSpan.FromSeconds(120);

    private readonly HttpClient _httpClient;

    public AnthropicClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("Anthropic");
        _httpClient.BaseAddress = new Uri(ApiBaseUrl);
    }

    public async Task<string> SendAnalysisAsync(
        string systemPrompt,
        string fileMetadataJson,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(AnalysisTimeout);

        var requestBody = BuildRequestBody(
            systemPrompt,
            [new("user", fileMetadataJson)],
            AnalysisMaxTokens);

        return await SendRequestWithRetryAsync(requestBody, apiKey, timeoutCts.Token).ConfigureAwait(false);
    }

    public async Task<string> SendChatAsync(
        string systemPrompt,
        List<(string role, string content)> messages,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var requestBody = BuildRequestBody(systemPrompt, messages, ChatMaxTokens);

        return await SendRequestWithRetryAsync(requestBody, apiKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ValidateApiKeyAsync(string apiKey)
    {
        var requestBody = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = ValidationMaxTokens,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = "Hello"
                }
            }
        };

        using var request = CreateRequest(requestBody.ToJsonString(), apiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return false;
        }

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new HttpRequestException(
            $"API key validation failed with status {(int)response.StatusCode}: {errorBody}");
    }

    private static JsonObject BuildRequestBody(
        string systemPrompt,
        List<(string role, string content)> messages,
        int maxTokens)
    {
        var messagesArray = new JsonArray();
        foreach (var (role, content) in messages)
        {
            messagesArray.Add(new JsonObject
            {
                ["role"] = role,
                ["content"] = content
            });
        }

        return new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = maxTokens,
            ["system"] = systemPrompt,
            ["messages"] = messagesArray
        };
    }

    private async Task<string> SendRequestWithRetryAsync(
        JsonObject requestBody,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var jsonBody = requestBody.ToJsonString();

        using var request = CreateRequest(jsonBody, apiKey);

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return await ParseResponseTextAsync(response, cancellationToken).ConfigureAwait(false);
        }

        // Handle rate limiting (429) and overloaded (529) with a single retry.
        if (response.StatusCode == HttpStatusCode.TooManyRequests
            || (int)response.StatusCode == 529)
        {
            var retryAfter = GetRetryAfterDelay(response);
            await Task.Delay(retryAfter, cancellationToken).ConfigureAwait(false);

            using var retryRequest = CreateRequest(jsonBody, apiKey);

            using var retryResponse = await _httpClient.SendAsync(
                retryRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (retryResponse.IsSuccessStatusCode)
            {
                return await ParseResponseTextAsync(retryResponse, cancellationToken).ConfigureAwait(false);
            }

            var retryStatusCode = (int)retryResponse.StatusCode;
            var retryBody = await retryResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            throw new HttpRequestException(
                $"Anthropic API request failed after retry with status {retryStatusCode}: {retryBody}");
        }

        // Handle other error statuses.
        var statusCode = (int)response.StatusCode;
        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new HttpRequestException(
                "Invalid API key. Please check your Anthropic API key and try again.");
        }

        if (statusCode >= 500)
        {
            throw new HttpRequestException(
                $"Anthropic API server error ({statusCode}). The service is temporarily unavailable. Please try again later.");
        }

        throw new HttpRequestException(
            $"Anthropic API request failed with status {statusCode}: {errorBody}");
    }

    private HttpRequestMessage CreateRequest(string jsonBody, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        return request;
    }

    private static async Task<string> ParseResponseTextAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (root.TryGetProperty("content", out var contentArray)
            && contentArray.GetArrayLength() > 0)
        {
            var firstBlock = contentArray[0];
            if (firstBlock.TryGetProperty("text", out var textElement))
            {
                return textElement.GetString()
                    ?? throw new InvalidOperationException("Response content[0].text was null.");
            }
        }

        throw new InvalidOperationException(
            "Unexpected response format: could not extract content[0].text from response.");
    }

    private static TimeSpan GetRetryAfterDelay(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1);
        }

        // Default retry delay if no retry-after header is provided.
        return TimeSpan.FromSeconds(5);
    }
}
