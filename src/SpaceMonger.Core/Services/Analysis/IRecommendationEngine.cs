using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Analysis;

public interface IRecommendationEngine
{
    Task<List<CleanupRecommendation>> AnalyzeAsync(ScanSession session, string apiKey, CancellationToken cancellationToken);
}
