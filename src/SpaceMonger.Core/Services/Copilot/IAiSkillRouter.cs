using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Copilot;

public interface IAiSkillRouter
{
    AiSkillRoutingResult Route(string userMessage, FileEntry? linkedEntry, FileEntry? currentViewRoot, bool hasExistingRecommendations);
}
