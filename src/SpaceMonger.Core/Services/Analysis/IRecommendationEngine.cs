using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Analysis;

public interface IRecommendationEngine
{
    Task<List<CleanupRecommendation>> AnalyzeAsync(ScanSession session, string apiKey, string? baseUrl, CancellationToken cancellationToken, FileEntry? focusEntry = null);
}
