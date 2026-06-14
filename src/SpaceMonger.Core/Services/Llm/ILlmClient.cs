namespace SpaceMonger.Core.Services.Llm;

public interface ILlmClient
{
    Task<string> SendAnalysisAsync(
        string systemPrompt,
        string fileMetadataJson,
        string apiKey,
        string? baseUrl,
        string? modelName,
        bool enableThinking,
        CancellationToken cancellationToken);

    Task<string> SendChatAsync(
        string systemPrompt,
        List<(string role, string content)> messages,
        string apiKey,
        string? baseUrl,
        CancellationToken cancellationToken);

    Task<string> StreamChatAsync(
        string systemPrompt,
        List<(string role, string content)> messages,
        string apiKey,
        string? baseUrl,
        Action<string> onToken,
        CancellationToken cancellationToken);

    Task<bool> ValidateApiKeyAsync(string apiKey, string? baseUrl);
}
