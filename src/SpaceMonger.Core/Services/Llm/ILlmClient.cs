namespace SpaceMonger.Core.Services.Llm;

public interface ILlmClient
{
    Task<string> SendAnalysisAsync(
        string systemPrompt,
        string fileMetadataJson,
        string apiKey,
        CancellationToken cancellationToken);

    Task<string> SendChatAsync(
        string systemPrompt,
        List<(string role, string content)> messages,
        string apiKey,
        CancellationToken cancellationToken);

    Task<string> StreamChatAsync(
        string systemPrompt,
        List<(string role, string content)> messages,
        string apiKey,
        Action<string> onToken,
        CancellationToken cancellationToken);

    Task<bool> ValidateApiKeyAsync(string apiKey);
}
