using Microsoft.VisualBasic.FileIO;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Cleanup;

public class CleanupService : ICleanupService
{
    public async Task<List<CleanupAction>> ExecuteCleanupAsync(
        List<CleanupRecommendation> accepted,
        DeletionMode mode,
        IProgress<CleanupProgress> progress,
        CancellationToken cancellationToken)
    {
        var results = new List<CleanupAction>();

        for (int i = 0; i < accepted.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var recommendation = accepted[i];

            var action = new CleanupAction
            {
                Recommendation = recommendation,
                ActionType = mode,
                Timestamp = DateTime.Now
            };

            progress.Report(new CleanupProgress(
                recommendation.TargetPath,
                i,
                accepted.Count));

            try
            {
                bool isDirectory = Directory.Exists(recommendation.TargetPath);

                await Task.Run(() =>
                {
                    switch (mode)
                    {
                        case DeletionMode.PermanentDelete:
                            if (isDirectory)
                            {
                                Directory.Delete(recommendation.TargetPath, recursive: true);
                            }
                            else
                            {
                                File.Delete(recommendation.TargetPath);
                            }
                            break;

                        case DeletionMode.MoveToRecycleBin:
                            if (isDirectory)
                            {
                                FileSystem.DeleteDirectory(
                                    recommendation.TargetPath,
                                    UIOption.OnlyErrorDialogs,
                                    RecycleOption.SendToRecycleBin);
                            }
                            else
                            {
                                FileSystem.DeleteFile(
                                    recommendation.TargetPath,
                                    UIOption.OnlyErrorDialogs,
                                    RecycleOption.SendToRecycleBin);
                            }
                            break;
                    }
                }, cancellationToken).ConfigureAwait(false);

                action.Result = CleanupResult.Success;
                action.ActualSizeFreed = recommendation.Size;
            }
            catch (FileNotFoundException)
            {
                action.Result = CleanupResult.AlreadyRemoved;
            }
            catch (DirectoryNotFoundException)
            {
                action.Result = CleanupResult.AlreadyRemoved;
            }
            catch (IOException ex)
            {
                action.Result = CleanupResult.Skipped;
                action.FailureReason = ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                action.Result = CleanupResult.Skipped;
                action.FailureReason = ex.Message;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                action.Result = CleanupResult.Failed;
                action.FailureReason = ex.Message;
            }

            results.Add(action);
        }

        return results;
    }
}
