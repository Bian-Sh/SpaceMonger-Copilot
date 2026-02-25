using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Cleanup;

public interface ICleanupService
{
    Task<List<CleanupAction>> ExecuteCleanupAsync(
        List<CleanupRecommendation> accepted,
        DeletionMode mode,
        IProgress<CleanupProgress> progress,
        CancellationToken cancellationToken);
}
