using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Agent;

public sealed record AgentContext(
    ScanSession Session,
    FileEntry CurrentViewRoot,
    FileEntry? LinkedEntry,
    CleanupRecommendation? LinkedRecommendation);
