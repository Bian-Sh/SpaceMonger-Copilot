namespace SpaceMonger.Core.Services.Copilot;

public interface ISkillPromptProvider
{
    string GetPrompt(string skillId, string fallback);
    string? GetRawContent(string skillId);
}
